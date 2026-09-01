// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license.See LICENSE in the project root for more information.
namespace Chaos.BlazorAnalyzers;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;

/// <summary>
/// Suppresses CS8618 warnings for non-nullable members of Blazor components that the framework
/// assigns after construction: members injected via <c>[Inject]</c>, members captured by an
/// <c>@ref</c> reference capture, and members assigned in a lifecycle method (OnInitialized,
/// OnInitializedAsync).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BlazorLifecycleNullabilitySuppressor : DiagnosticSuppressor
{
    private const String ComponentBaseTypeName = "Microsoft.AspNetCore.Components.ComponentBase";

    private const String InjectAttributeTypeName = "Microsoft.AspNetCore.Components.InjectAttribute";

    private static readonly ImmutableHashSet<String> LifecycleMethods = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "OnInitialized",
        "OnInitializedAsync");

    private static readonly ImmutableHashSet<String> ReferenceCaptureMethods = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "AddComponentReferenceCapture",
        "AddElementReferenceCapture");

    private static readonly SuppressionDescriptor SuppressCs8618ForInjectedMember = new(
        "KOS8002",
        "CS8618",
        "Member is assigned by Blazor dependency injection ([Inject]).");

    private static readonly SuppressionDescriptor SuppressCs8618ForReferenceCapture = new(
        "KOS8003",
        "CS8618",
        "Member is assigned by a Blazor reference capture (@ref).");

    private static readonly SuppressionDescriptor SuppressCs8618InBlazorLifecycle = new(
        "KOS8001",
        "CS8618",
        "Member is assigned in a Blazor lifecycle method (OnInitialized/OnInitializedAsync).");

    /// <summary>
    /// Gets the suppressions this suppressor is able to report. <c>KOS8001</c> covers members
    /// assigned in a lifecycle method, <c>KOS8002</c> covers members marked with <c>[Inject]</c>,
    /// and <c>KOS8003</c> covers members captured by an <c>@ref</c>. All suppress <c>CS8618</c>.
    /// </summary>
    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions =>
        ImmutableArray.Create(SuppressCs8618InBlazorLifecycle, SuppressCs8618ForInjectedMember, SuppressCs8618ForReferenceCapture);

    /// <summary>
    /// Reports a suppression for every <c>CS8618</c> diagnostic whose member is declared on a type
    /// deriving from <c>ComponentBase</c> and is marked with <c>[Inject]</c>, captured by an
    /// <c>@ref</c>, or assigned in one of the Blazor lifecycle methods.
    /// </summary>
    /// <param name="context">The analysis context providing the reported diagnostics to examine.</param>
    public override void ReportSuppressions(SuppressionAnalysisContext context)
    {
        // Resolve the well-known Blazor types once. Without ComponentBase this is not a Blazor
        // compilation and there is nothing to suppress.
        var componentBaseType = context.Compilation.GetTypeByMetadataName(ComponentBaseTypeName);
        if (componentBaseType is null)
        {
            return;
        }

        var injectAttributeType = context.Compilation.GetTypeByMetadataName(InjectAttributeTypeName);

        foreach (var diagnostic in context.ReportedDiagnostics)
        {
            if (diagnostic.Id != "CS8618")
            {
                continue;
            }

            var descriptor = GetSuppression(context, diagnostic, componentBaseType, injectAttributeType);
            if (descriptor is not null)
            {
                context.ReportSuppression(Suppression.Create(descriptor, diagnostic));
            }
        }
    }

    private static Boolean ContainsAssignmentTo(SyntaxNode scope, String memberName)
    {
        foreach (var descendant in scope.DescendantNodes())
        {
            if (descendant is AssignmentExpressionSyntax assignment)
            {
                var target = assignment.Left;

                // Direct assignment: MemberName = value
                if (target is IdentifierNameSyntax identifier &&
                    identifier.Identifier.Text == memberName)
                {
                    return true;
                }

                // this.MemberName = value
                if (target is MemberAccessExpressionSyntax memberAccess &&
                    memberAccess.Expression is ThisExpressionSyntax &&
                    memberAccess.Name.Identifier.Text == memberName)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static String? GetInvokedMethodName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            _ => null
        };
    }

    private static String? GetMemberName(SyntaxNode node)
    {
        return node switch
        {
            PropertyDeclarationSyntax property => property.Identifier.Text,
            VariableDeclaratorSyntax variable => variable.Identifier.Text,
            _ => node.Parent switch
            {
                PropertyDeclarationSyntax property => property.Identifier.Text,
                VariableDeclaratorSyntax variable => variable.Identifier.Text,
                _ => null
            }
        };
    }

    private static SuppressionDescriptor? GetSuppression(
        SuppressionAnalysisContext context,
        Diagnostic diagnostic,
        INamedTypeSymbol componentBaseType,
        INamedTypeSymbol? injectAttributeType)
    {
        var syntaxTree = diagnostic.Location.SourceTree;
        if (syntaxTree is null)
        {
            return null;
        }

        var root = syntaxTree.GetRoot(context.CancellationToken);
        var node = root.FindNode(diagnostic.Location.SourceSpan);

        // Find the member (property or field) that has the warning
        var memberName = GetMemberName(node);
        if (memberName is null)
        {
            return null;
        }

        // Find the containing type
        var typeDeclaration = node.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (typeDeclaration is null)
        {
            return null;
        }

        // Check if this type inherits from ComponentBase
        var semanticModel = context.GetSemanticModel(syntaxTree);
        var typeSymbol = semanticModel.GetDeclaredSymbol(typeDeclaration, context.CancellationToken);
        if (typeSymbol is null || !InheritsFrom(typeSymbol, componentBaseType))
        {
            return null;
        }

        // [Inject] is only valid on properties, so a field never reaches this suppression
        if (IsInjectedProperty(semanticModel, node, injectAttributeType, context.CancellationToken))
        {
            return SuppressCs8618ForInjectedMember;
        }

        // A component is commonly split across a .razor file and its code-behind, and the Razor
        // generator emits BuildRenderTree into yet another part, so every part has to be scanned.
        var declarations = GetTypeDeclarations(typeSymbol, context.CancellationToken);

        if (IsMemberCapturedByReference(declarations, memberName))
        {
            return SuppressCs8618ForReferenceCapture;
        }

        if (IsMemberAssignedInLifecycleMethod(declarations, memberName))
        {
            return SuppressCs8618InBlazorLifecycle;
        }

        return null;
    }

    private static ImmutableArray<TypeDeclarationSyntax> GetTypeDeclarations(
        INamedTypeSymbol typeSymbol,
        CancellationToken cancellationToken)
    {
        var builder = ImmutableArray.CreateBuilder<TypeDeclarationSyntax>();

        foreach (var reference in typeSymbol.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is TypeDeclarationSyntax declaration)
            {
                builder.Add(declaration);
            }
        }

        return builder.ToImmutable();
    }

    private static Boolean InheritsFrom(INamedTypeSymbol typeSymbol, INamedTypeSymbol baseType)
    {
        var current = typeSymbol.BaseType;
        while (current is not null)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }

            current = current.BaseType;
        }

        return false;
    }

    private static Boolean IsInjectedProperty(
        SemanticModel semanticModel,
        SyntaxNode node,
        INamedTypeSymbol? injectAttributeType,
        CancellationToken cancellationToken)
    {
        if (injectAttributeType is null)
        {
            return false;
        }

        var propertyDeclaration = node.FirstAncestorOrSelf<PropertyDeclarationSyntax>();
        if (propertyDeclaration is null)
        {
            return false;
        }

        var propertySymbol = semanticModel.GetDeclaredSymbol(propertyDeclaration, cancellationToken);
        if (propertySymbol is null)
        {
            return false;
        }

        foreach (var attribute in propertySymbol.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, injectAttributeType))
            {
                return true;
            }
        }

        return false;
    }

    private static Boolean IsMemberAssignedInLifecycleMethod(
        ImmutableArray<TypeDeclarationSyntax> declarations,
        String memberName)
    {
        foreach (var declaration in declarations)
        {
            foreach (var member in declaration.Members)
            {
                if (member is not MethodDeclarationSyntax method)
                {
                    continue;
                }

                if (!LifecycleMethods.Contains(method.Identifier.Text))
                {
                    continue;
                }

                if (ContainsAssignmentTo(method, memberName))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static Boolean IsMemberCapturedByReference(
        ImmutableArray<TypeDeclarationSyntax> declarations,
        String memberName)
    {
        foreach (var declaration in declarations)
        {
            foreach (var invocation in declaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var methodName = GetInvokedMethodName(invocation);
                if (methodName is null || !ReferenceCaptureMethods.Contains(methodName))
                {
                    continue;
                }

                foreach (var argument in invocation.ArgumentList.Arguments)
                {
                    if (argument.Expression is LambdaExpressionSyntax lambda &&
                        ContainsAssignmentTo(lambda, memberName))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }
}
