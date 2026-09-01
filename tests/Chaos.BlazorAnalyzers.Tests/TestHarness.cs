// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license.See LICENSE in the project root for more information.
namespace Chaos.BlazorAnalyzers.Tests;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;

/// <summary>
/// Compiles a C# snippet in memory and runs an analyzer or suppressor over it. Suppressed
/// diagnostics are removed from the result, so a suppression is observed as the absence of the
/// corresponding diagnostic.
/// </summary>
internal static class TestHarness
{
    private const String BlazorPreamble = """
                                          #nullable enable
                                          using Microsoft.AspNetCore.Components;
                                          using Microsoft.AspNetCore.Components.Rendering;
                                          using Microsoft.JSInterop;
                                          using System;
                                          using System.Threading.Tasks;

                                          public interface ITestService;


                                          """;

    private static readonly Lazy<ImmutableArray<MetadataReference>> BlazorReferences =
        new(() => LoadReferences(true));

    private const String PlainPreamble = """
                                         #nullable enable
                                         using System;


                                         """;

    private static readonly Lazy<ImmutableArray<MetadataReference>> PlainReferences =
        new(() => LoadReferences(false));

    /// <summary>
    /// Compiles <paramref name="source"/> and returns the diagnostics <paramref name="analyzer"/>
    /// reports on it.
    /// </summary>
    /// <param name="source">The snippet to compile. A Blazor preamble is prepended.</param>
    /// <param name="analyzer">The analyzer to run.</param>
    /// <param name="withBlazorReferences">Whether the compilation references ASP.NET Core.</param>
    /// <param name="markAsGenerated">Whether the snippet is compiled under a generated-code file name, as Razor output is.</param>
    /// <returns>The reported diagnostics, ordered by source position.</returns>
    public static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(
        String source,
        DiagnosticAnalyzer analyzer,
        Boolean withBlazorReferences = true,
        Boolean markAsGenerated = false)
    {
        var compilation = CreateCompilation(source, withBlazorReferences, markAsGenerated: markAsGenerated);
        var diagnostics = await compilation.WithAnalyzers(ImmutableArray.Create(analyzer)).GetAnalyzerDiagnosticsAsync();

        return diagnostics.OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start).ToImmutableArray();
    }

    /// <summary>
    /// Compiles <paramref name="source"/> and returns the CS8618 diagnostics that survive
    /// suppression by <see cref="BlazorLifecycleNullabilitySuppressor"/>.
    /// </summary>
    /// <param name="source">The snippet to compile. A Blazor preamble is prepended.</param>
    /// <param name="disabledSuppressionId">A suppression ID (for example <c>KOS8001</c>) to switch off, mirroring <c>dotnet_diagnostic.&lt;id&gt;.severity = none</c>.</param>
    /// <param name="withBlazorReferences">Whether the compilation references ASP.NET Core. Pass <c>false</c> to model a non-Blazor project.</param>
    /// <returns>The reported CS8618 diagnostics, ordered by source position.</returns>
    public static async Task<ImmutableArray<Diagnostic>> GetCs8618DiagnosticsAsync(
        String source,
        String? disabledSuppressionId = null,
        Boolean withBlazorReferences = true)
    {
        var compilation = CreateCompilation(source, withBlazorReferences, disabledSuppressionId);
        var diagnostics = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new BlazorLifecycleNullabilitySuppressor()))
                                           .GetAllDiagnosticsAsync();

        return diagnostics.Where(diagnostic => diagnostic.Id == "CS8618")
                          .OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start)
                          .ToImmutableArray();
    }

    /// <summary>
    /// Returns the name of the member a CS8618 diagnostic was reported for.
    /// </summary>
    /// <param name="diagnostic">The CS8618 diagnostic to inspect.</param>
    /// <returns>The member name taken from the diagnostic's message.</returns>
    public static String GetMemberName(Diagnostic diagnostic)
    {
        // CS8618's message is "Non-nullable property 'Name' must contain ...", so the member name
        // is the text between the first pair of single quotes.
        var message = diagnostic.GetMessage();
        var start = message.IndexOf('\'') + 1;
        var end = message.IndexOf('\'', start);
        return message.Substring(start, end - start);
    }

    private static CSharpCompilation CreateCompilation(
        String source,
        Boolean withBlazorReferences,
        String? disabledDiagnosticId = null,
        Boolean markAsGenerated = false)
    {
        var preamble = withBlazorReferences ? BlazorPreamble : PlainPreamble;

        // Roslyn treats a .g.cs file name as generated code, exactly as it does Razor's output
        var path = markAsGenerated ? "Component.g.cs" : "Component.cs";
        var syntaxTree = CSharpSyntaxTree.ParseText(preamble + source, path: path);

        var options = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: NullableContextOptions.Enable,
            specificDiagnosticOptions: disabledDiagnosticId is null
                ? null
                : [new(disabledDiagnosticId, ReportDiagnostic.Suppress)]);

        var references = withBlazorReferences ? BlazorReferences.Value : PlainReferences.Value;
        var compilation = CSharpCompilation.Create("AnalyzerTests", [syntaxTree], references, options);

        // A snippet that does not compile makes the analyzer's behavior meaningless, so fail loudly
        // instead of reporting a misleading absence of diagnostics.
        var compilationErrors = compilation.GetDiagnostics()
                                           .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                                           .ToImmutableArray();

        if (compilationErrors.Length > 0)
        {
            var details = String.Join(Environment.NewLine, compilationErrors.Select(error => error.ToString()));
            throw new InvalidOperationException($"The test snippet does not compile:{Environment.NewLine}{details}");
        }

        return compilation;
    }

    private static ImmutableArray<MetadataReference> LoadReferences(Boolean includeBlazor)
    {
        var trustedAssemblies = (String)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;

        return trustedAssemblies.Split(Path.PathSeparator)
                                .Where(path => !String.IsNullOrEmpty(path))
                                .Where(path => includeBlazor || !Path.GetFileName(path).StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal))
                                .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
                                .ToImmutableArray();
    }
}
