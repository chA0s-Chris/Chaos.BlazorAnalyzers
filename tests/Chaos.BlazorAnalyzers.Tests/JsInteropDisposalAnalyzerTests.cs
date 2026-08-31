// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license.See LICENSE in the project root for more information.
namespace Chaos.BlazorAnalyzers.Tests;

using FluentAssertions;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

[TestFixture]
public class JsInteropDisposalAnalyzerTests
{
    [Test]
    public void SupportedDiagnostics_DeclaresKos2001()
    {
        var descriptor = new JsInteropDisposalAnalyzer().SupportedDiagnostics.Should().ContainSingle().Subject;

        descriptor.Id.Should().Be("KOS2001");
        descriptor.DefaultSeverity.Should().Be(DiagnosticSeverity.Warning);
        descriptor.IsEnabledByDefault.Should().BeTrue();
        descriptor.HelpLinkUri.Should().NotBeNullOrWhiteSpace();
    }

    [TestCase("""
              private IJSObjectReference? _module;
              protected override async Task OnAfterRenderAsync(bool firstRender)
                  => _module = await JS.InvokeAsync<IJSObjectReference>("import", "./x.js");
              """, "_module", TestName = "IJSObjectReference from module import")]
    [TestCase("""
              private IJSInProcessObjectReference? _module;
              protected override void OnInitialized()
                  => _module = ((IJSInProcessRuntime)JS).Invoke<IJSInProcessObjectReference>("get");
              """, "_module", TestName = "Derived IJSInProcessObjectReference")]
    [TestCase("""
              private DotNetObjectReference<Component>? _ref;
              protected override void OnInitialized() => _ref = DotNetObjectReference.Create(this);
              """, "_ref", TestName = "DotNetObjectReference")]
    [TestCase("""
              public IJSObjectReference? Module { get; set; }
              protected override async Task OnAfterRenderAsync(bool firstRender)
                  => Module = await JS.InvokeAsync<IJSObjectReference>("import", "./x.js");
              """, "Module", TestName = "Property instead of field")]
    public async Task Kos2001_IsReported_ForUndisposedReference(String body, String expectedMember)
    {
        var diagnostics = await TestHarness.GetAnalyzerDiagnosticsAsync(BuildComponent(body), new JsInteropDisposalAnalyzer());

        var diagnostic = diagnostics.Should().ContainSingle().Subject;
        diagnostic.Id.Should().Be("KOS2001");
        diagnostic.GetMessage().Should().Contain(expectedMember).And.Contain("Component");
    }

    [TestCase("""
              private IJSObjectReference? _module;
              protected override async Task OnAfterRenderAsync(bool firstRender)
                  => _module = await JS.InvokeAsync<IJSObjectReference>("import", "./x.js");
              public async ValueTask DisposeAsync() { if (_module is not null) await _module.DisposeAsync(); }
              """, TestName = "Disposed in DisposeAsync")]
    [TestCase("""
              private IJSObjectReference? _module;
              protected override async Task OnAfterRenderAsync(bool firstRender)
                  => _module = await JS.InvokeAsync<IJSObjectReference>("import", "./x.js");
              public async ValueTask DisposeAsync() => await (_module?.DisposeAsync() ?? default);
              """, TestName = "Disposed through conditional access")]
    [TestCase("""
              private DotNetObjectReference<Component>? _ref;
              protected override void OnInitialized() => _ref = DotNetObjectReference.Create(this);
              public void Dispose() => _ref?.Dispose();
              """, TestName = "DotNetObjectReference disposed")]
    [TestCase("""
              private IJSObjectReference? _module;
              """, TestName = "Declared but never assigned")]
    [TestCase("""
              [Parameter] public IJSObjectReference? Module { get; set; }
              protected override void OnInitialized() => Module = Module;
              """, TestName = "Parameter is owned by the parent")]
    public async Task Kos2001_IsNotReported(String body)
    {
        var diagnostics = await TestHarness.GetAnalyzerDiagnosticsAsync(BuildComponent(body), new JsInteropDisposalAnalyzer());

        diagnostics.Should().BeEmpty();
    }

    /// <remarks>
    /// Razor compiles every .razor file into generated code, so a rule that does not opt into
    /// reporting there never fires for a component declared in markup.
    /// </remarks>
    [Test]
    public async Task Kos2001_IsReported_InGeneratedCode()
    {
        const String body = """
                            private IJSObjectReference? _module;
                            protected override async Task OnAfterRenderAsync(bool firstRender)
                                => _module = await JS.InvokeAsync<IJSObjectReference>("import", "./x.js");
                            """;

        var diagnostics = await TestHarness.GetAnalyzerDiagnosticsAsync(
            BuildComponent(body),
            new JsInteropDisposalAnalyzer(),
            markAsGenerated: true);

        diagnostics.Should().ContainSingle().Which.Id.Should().Be("KOS2001");
    }

    [Test]
    public async Task Kos2001_IsNotReported_ForNonComponent()
    {
        const String source = """
                              public class NotAComponent
                              {
                                  private IJSObjectReference? _module;
                                  public void Set(IJSObjectReference module) => _module = module;
                              }
                              """;

        var diagnostics = await TestHarness.GetAnalyzerDiagnosticsAsync(source, new JsInteropDisposalAnalyzer());

        diagnostics.Should().BeEmpty();
    }

    [Test]
    public async Task Kos2001_IsNotReported_InNonBlazorCompilation()
    {
        const String source = """
                              public class Plain
                              {
                                  private IDisposable? _thing;
                                  public void Set(IDisposable thing) => _thing = thing;
                              }
                              """;

        var diagnostics = await TestHarness.GetAnalyzerDiagnosticsAsync(source, new JsInteropDisposalAnalyzer(), false);

        diagnostics.Should().BeEmpty();
    }

    [Test]
    public async Task Kos2001_IsReportedPerMember_WhenSeveralAreUndisposed()
    {
        const String body = """
                            private IJSObjectReference? _first;
                            private DotNetObjectReference<Component>? _second;

                            protected override async Task OnAfterRenderAsync(bool firstRender)
                            {
                                _first = await JS.InvokeAsync<IJSObjectReference>("import", "./x.js");
                                _second = DotNetObjectReference.Create(this);
                            }
                            """;

        var diagnostics = await TestHarness.GetAnalyzerDiagnosticsAsync(BuildComponent(body), new JsInteropDisposalAnalyzer());

        diagnostics.Should().HaveCount(2);
        diagnostics.Select(diagnostic => diagnostic.GetMessage())
                   .Should().Contain(message => message.Contains("_first"))
                   .And.Contain(message => message.Contains("_second"));
    }

    private static String BuildComponent(String body)
    {
        return $$"""
                 public partial class Component : ComponentBase
                 {
                     [Inject] public IJSRuntime JS { get; set; } = null!;

                     {{body}}
                 }
                 """;
    }
}
