// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license.See LICENSE in the project root for more information.
namespace Chaos.BlazorAnalyzers.Tests;

using FluentAssertions;
using NUnit.Framework;

[TestFixture]
public class AnalyzerCompatibilityTests
{
    /// <summary>
    /// The oldest Roslyn the analyzer supports. Referencing a newer compiler than the host is
    /// running makes the host refuse to load the analyzer with CS9057, so this is the floor, not
    /// the newest version available. 3.11 is the highest version the ReSharper and Rider
    /// inspection engine loads, which is the binding constraint rather than any .NET SDK.
    /// </summary>
    private static readonly Version SupportedCompilerFloor = new(3, 11, 0, 0);

    [Test]
    public void AnalyzerAssembly_DoesNotReferenceACompilerNewerThanTheSupportedFloor()
    {
        var compilerReferences = typeof(BlazorLifecycleNullabilitySuppressor).Assembly
                                                                             .GetReferencedAssemblies()
                                                                             .Where(reference => reference.Name?.StartsWith(
                                                                                        "Microsoft.CodeAnalysis", StringComparison.Ordinal) == true)
                                                                             .ToList();

        compilerReferences.Should().NotBeEmpty("the analyzer is built against Roslyn");

        var tooNew = compilerReferences.Where(reference => reference.Version is null || reference.Version > SupportedCompilerFloor)
                                       .Select(reference => $"{reference.Name} {reference.Version}")
                                       .ToList();

        tooNew.Should().BeEmpty(
            "a host running an older compiler refuses to load the analyzer with CS9057; raise " +
            $"{nameof(SupportedCompilerFloor)} only when dropping support for older SDKs is intended");
    }
}
