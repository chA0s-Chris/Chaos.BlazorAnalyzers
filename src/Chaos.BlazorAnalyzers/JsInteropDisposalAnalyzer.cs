// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license.See LICENSE in the project root for more information.
namespace Chaos.BlazorAnalyzers;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Concurrent;
using System.Collections.Immutable;

/// <summary>
/// Reports Blazor components that store an <c>IJSObjectReference</c> or a
/// <c>DotNetObjectReference</c> without ever disposing it.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class JsInteropDisposalAnalyzer : DiagnosticAnalyzer
{
    private const String ComponentBaseTypeName = "Microsoft.AspNetCore.Components.ComponentBase";

    private const String DotNetObjectReferenceTypeName = "Microsoft.JSInterop.DotNetObjectReference`1";

    private static readonly ImmutableArray<String> FrameworkAssignedAttributes =
    [
        "Microsoft.AspNetCore.Components.InjectAttribute",
        "Microsoft.AspNetCore.Components.ParameterAttribute",
        "Microsoft.AspNetCore.Components.CascadingParameterAttribute"
    ];

    private const String JsObjectReferenceTypeName = "Microsoft.JSInterop.IJSObjectReference";

    /// <summary>
    /// Gets the diagnostics this analyzer can report.
    /// </summary>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [DiagnosticDescriptors.UndisposedJsInteropReference];

    /// <summary>
    /// Registers the analysis callbacks.
    /// </summary>
    /// <param name="context">The context used to register the callbacks.</param>
    public override void Initialize(AnalysisContext context)
    {
        // Razor emits component code into files marked as generated, so generated code has to be
        // analyzed or no component declared in a .razor file would ever be inspected. Analyze on its
        // own is not enough: without ReportDiagnostics the findings are computed and then dropped,
        // because their location is inside the generated file.
        context.ConfigureGeneratedCodeAnalysis(
            GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static ISymbol? GetDisposedMember(IInvocationOperation invocation)
    {
        var instance = invocation.Instance;

        // For "_member?.DisposeAsync()" the receiver sits on the enclosing conditional access
        if (instance is IConditionalAccessInstanceOperation)
        {
            var current = invocation.Parent;
            while (current is not null and not IConditionalAccessOperation)
            {
                current = current.Parent;
            }

            instance = (current as IConditionalAccessOperation)?.Operation;
        }

        return instance switch
        {
            IFieldReferenceOperation field => field.Field,
            IPropertyReferenceOperation property => property.Property,
            _ => null
        };
    }

    private static ITypeSymbol? GetMemberType(ISymbol member)
    {
        return member switch
        {
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            _ => null
        };
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

    private static Boolean IsFrameworkAssigned(ISymbol member, ImmutableArray<INamedTypeSymbol> attributeTypes)
    {
        foreach (var attribute in member.GetAttributes())
        {
            foreach (var attributeType in attributeTypes)
            {
                if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static Boolean IsOwnedJsInteropType(
        ITypeSymbol type,
        INamedTypeSymbol jsObjectReferenceType,
        INamedTypeSymbol dotNetObjectReferenceType)
    {
        if (SymbolEqualityComparer.Default.Equals(type, jsObjectReferenceType) ||
            type.AllInterfaces.Contains(jsObjectReferenceType, SymbolEqualityComparer.Default))
        {
            return true;
        }

        return type is INamedTypeSymbol { IsGenericType: true } namedType &&
               SymbolEqualityComparer.Default.Equals(namedType.ConstructedFrom, dotNetObjectReferenceType);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        var compilation = context.Compilation;

        var componentBaseType = compilation.GetTypeByMetadataName(ComponentBaseTypeName);
        var jsObjectReferenceType = compilation.GetTypeByMetadataName(JsObjectReferenceTypeName);
        var dotNetObjectReferenceType = compilation.GetTypeByMetadataName(DotNetObjectReferenceTypeName);

        // Not a Blazor compilation, or JS interop is not referenced: nothing to report
        if (componentBaseType is null || jsObjectReferenceType is null || dotNetObjectReferenceType is null)
        {
            return;
        }

        var attributeTypes = FrameworkAssignedAttributes.Select(compilation.GetTypeByMetadataName)
                                                        .OfType<INamedTypeSymbol>()
                                                        .ToImmutableArray();

        context.RegisterSymbolStartAction(
            symbolStartContext => OnTypeStart(
                symbolStartContext,
                componentBaseType,
                jsObjectReferenceType,
                dotNetObjectReferenceType,
                attributeTypes),
            SymbolKind.NamedType);
    }

    private static void OnTypeStart(
        SymbolStartAnalysisContext context,
        INamedTypeSymbol componentBaseType,
        INamedTypeSymbol jsObjectReferenceType,
        INamedTypeSymbol dotNetObjectReferenceType,
        ImmutableArray<INamedTypeSymbol> attributeTypes)
    {
        if (context.Symbol is not INamedTypeSymbol { TypeKind: TypeKind.Class } typeSymbol ||
            !InheritsFrom(typeSymbol, componentBaseType))
        {
            return;
        }

        var candidates = new List<ISymbol>();
        foreach (var member in typeSymbol.GetMembers())
        {
            if (member is not (IFieldSymbol or IPropertySymbol) || member.IsImplicitlyDeclared)
            {
                continue;
            }

            var memberType = GetMemberType(member);
            if (memberType is null ||
                !IsOwnedJsInteropType(memberType, jsObjectReferenceType, dotNetObjectReferenceType) ||
                IsFrameworkAssigned(member, attributeTypes))
            {
                continue;
            }

            candidates.Add(member);
        }

        if (candidates.Count == 0)
        {
            return;
        }

        // Operation callbacks may run concurrently, so the collected state has to be thread-safe
        var assigned = new ConcurrentDictionary<ISymbol, Boolean>(SymbolEqualityComparer.Default);
        var disposed = new ConcurrentDictionary<ISymbol, Boolean>(SymbolEqualityComparer.Default);

        context.RegisterOperationAction(
            operationContext =>
            {
                var target = ((IAssignmentOperation)operationContext.Operation).Target;
                var member = target switch
                {
                    IFieldReferenceOperation field => (ISymbol)field.Field,
                    IPropertyReferenceOperation property => property.Property,
                    _ => null
                };

                if (member is not null)
                {
                    assigned[member] = true;
                }
            },
            OperationKind.SimpleAssignment);

        context.RegisterOperationAction(
            operationContext =>
            {
                var invocation = (IInvocationOperation)operationContext.Operation;
                if (invocation.TargetMethod.Name is not ("Dispose" or "DisposeAsync"))
                {
                    return;
                }

                var member = GetDisposedMember(invocation);
                if (member is not null)
                {
                    disposed[member] = true;
                }
            },
            OperationKind.Invocation);

        context.RegisterSymbolEndAction(symbolEndContext =>
        {
            foreach (var candidate in candidates)
            {
                if (!assigned.ContainsKey(candidate) || disposed.ContainsKey(candidate))
                {
                    continue;
                }

                symbolEndContext.ReportDiagnostic(Diagnostic.Create(
                                                      DiagnosticDescriptors.UndisposedJsInteropReference,
                                                      candidate.Locations[0],
                                                      typeSymbol.Name,
                                                      candidate.Name));
            }
        });
    }
}
