// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license.See LICENSE in the project root for more information.
namespace Chaos.BlazorAnalyzers;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Globalization;

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

    private static Boolean ContainsInvocationOf(SyntaxNode scope, String name)
    {
        foreach (var invocation in scope.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (GetInvokedMethodName(invocation) == name)
            {
                return true;
            }
        }

        return false;
    }

    private static ArgumentSyntax? FindLambdaArgumentAssigning(InvocationExpressionSyntax invocation, String memberName)
    {
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (argument.Expression is LambdaExpressionSyntax lambda &&
                ContainsAssignmentTo(lambda, memberName))
            {
                return argument;
            }
        }

        return null;
    }

    /// <remarks>
    /// A generic component whose type argument is inferred does not get its reference capture
    /// inline. Razor routes the render call through a generated <c>TypeInference.Create*</c> helper
    /// and passes the capture on as an <c>Action&lt;T&gt;</c> parameter; the helper is what calls
    /// <c>AddComponentReferenceCapture</c>, invoking that parameter from inside the capture lambda.
    /// One level of indirection is enough, because that is all the generator emits.
    /// </remarks>
    private static Boolean ForwardsArgumentToReferenceCapture(
        SuppressionAnalysisContext context,
        InvocationExpressionSyntax invocation,
        ArgumentSyntax argument)
    {
        var semanticModel = context.GetSemanticModel(invocation.SyntaxTree);
        if (semanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method)
        {
            return false;
        }

        var parameterName = GetParameterName(invocation, argument, method);
        if (parameterName is null)
        {
            return false;
        }

        foreach (var reference in method.OriginalDefinition.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(context.CancellationToken) is not MethodDeclarationSyntax declaration)
            {
                continue;
            }

            foreach (var candidate in declaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var candidateName = GetInvokedMethodName(candidate);
                if (candidateName is null || !ReferenceCaptureMethods.Contains(candidateName))
                {
                    continue;
                }

                foreach (var captureArgument in candidate.ArgumentList.Arguments)
                {
                    if (captureArgument.Expression is LambdaExpressionSyntax lambda &&
                        ContainsInvocationOf(lambda, parameterName))
                    {
                        return true;
                    }
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

    /// <remarks>
    /// The member name is verified against the type before it is used, so a compiler that reworded
    /// CS8618 ends in no suppression rather than in the wrong one. The invariant culture pins the
    /// message to the neutral resource, which a localized compiler host would otherwise replace.
    /// </remarks>
    private static String? GetMemberNameFromMessage(Diagnostic diagnostic, INamedTypeSymbol typeSymbol)
    {
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);

        var start = message.IndexOf('\'');
        if (start < 0)
        {
            return null;
        }

        var end = message.IndexOf('\'', start + 1);
        if (end < 0)
        {
            return null;
        }

        var memberName = message.Substring(start + 1, end - start - 1);

        foreach (var member in typeSymbol.GetMembers(memberName))
        {
            if (member is IPropertySymbol or IFieldSymbol)
            {
                return memberName;
            }
        }

        return null;
    }

    private static String? GetParameterName(
        InvocationExpressionSyntax invocation,
        ArgumentSyntax argument,
        IMethodSymbol method)
    {
        if (argument.NameColon is not null)
        {
            return argument.NameColon.Name.Identifier.Text;
        }

        var index = invocation.ArgumentList.Arguments.IndexOf(argument);

        return index >= 0 && index < method.Parameters.Length
            ? method.Parameters[index].Name
            : null;
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

        // Find the member (property or field) that has the warning. The compiler anchors CS8618 at
        // the member, unless the type declares a constructor: then it anchors every member's
        // diagnostic at that constructor instead and only the message still names the member.
        var memberName = GetMemberName(node) ?? GetMemberNameFromMessage(diagnostic, typeSymbol);
        if (memberName is null)
        {
            return null;
        }

        // [Inject] is only valid on properties, so a field never reaches this suppression
        if (IsInjectedMember(typeSymbol, memberName, injectAttributeType))
        {
            return SuppressCs8618ForInjectedMember;
        }

        // A component is commonly split across a .razor file and its code-behind, and the Razor
        // generator emits BuildRenderTree into yet another part, so every part has to be scanned.
        var declarations = GetTypeDeclarations(typeSymbol, context.CancellationToken);

        if (IsMemberCapturedByReference(context, declarations, memberName))
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

    private static Boolean IsInjectedMember(
        INamedTypeSymbol typeSymbol,
        String memberName,
        INamedTypeSymbol? injectAttributeType)
    {
        if (injectAttributeType is null)
        {
            return false;
        }

        // Looked up on the type rather than on the diagnostic's syntax, because a
        // constructor-anchored diagnostic points at no member declaration at all.
        foreach (var member in typeSymbol.GetMembers(memberName))
        {
            foreach (var attribute in member.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, injectAttributeType))
                {
                    return true;
                }
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
        SuppressionAnalysisContext context,
        ImmutableArray<TypeDeclarationSyntax> declarations,
        String memberName)
    {
        foreach (var declaration in declarations)
        {
            foreach (var invocation in declaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var argument = FindLambdaArgumentAssigning(invocation, memberName);
                if (argument is null)
                {
                    continue;
                }

                var methodName = GetInvokedMethodName(invocation);
                if (methodName is not null && ReferenceCaptureMethods.Contains(methodName))
                {
                    return true;
                }

                if (ForwardsArgumentToReferenceCapture(context, invocation, argument))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
