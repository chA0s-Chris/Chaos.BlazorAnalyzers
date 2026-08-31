// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license.See LICENSE in the project root for more information.
namespace Chaos.BlazorAnalyzers;

using Microsoft.CodeAnalysis;

/// <summary>
/// The catalogue of diagnostics reported by this library. Every rule is declared here so the
/// complete rule set is visible in one place.
/// </summary>
internal static class DiagnosticDescriptors
{
    public static readonly DiagnosticDescriptor UndisposedJsInteropReference = new(
        "KOS2001",
        "Blazor component does not dispose a JS interop reference",
        "Component '{0}' assigns '{1}' but never disposes it",
        "Reliability",
        DiagnosticSeverity.Warning,
        true,
        "A component that creates an IJSObjectReference or a DotNetObjectReference owns it. " +
        "Neither is released by the garbage collector alone, so the component has to implement " +
        "IDisposable or IAsyncDisposable and dispose the reference.",
        HelpLinkBase + "KOS2001.md");
    private const String HelpLinkBase = "https://github.com/chA0s-Chris/Chaos.BlazorAnalyzers/blob/main/docs/rules/";
}
