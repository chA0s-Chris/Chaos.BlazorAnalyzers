// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license.See LICENSE in the project root for more information.
namespace Chaos.BlazorAnalyzers.Tests;

using FluentAssertions;
using NUnit.Framework;

[TestFixture]
public class BlazorLifecycleNullabilitySuppressorTests
{
    private const String InjectAndLifecycleComponent = """
                                                       public class Component : ComponentBase
                                                       {
                                                           [Inject] public ITestService Injected { get; set; }

                                                           private string _lifecycle;

                                                           protected override void OnInitialized() => _lifecycle = "x";
                                                       }
                                                       """;

    // Mirrors what the Razor source generator emits for <Child @ref="_captured" />, with the
    // field in the code-behind part and BuildRenderTree in the generated part.
    private const String ReferenceCaptureComponent = """
                                                     public partial class Component : ComponentBase
                                                     {
                                                         private ITestService _captured;
                                                     }

                                                     public partial class Component
                                                     {
                                                         protected override void BuildRenderTree(RenderTreeBuilder __builder)
                                                         {
                                                             __builder.AddComponentReferenceCapture(1, (__value) => { _captured = (ITestService)__value; });
                                                         }
                                                     }
                                                     """;

    [Test]
    public void SupportedSuppressions_DeclaresAllRulesForCs8618()
    {
        var suppressor = new BlazorLifecycleNullabilitySuppressor();

        suppressor.SupportedSuppressions.Select(descriptor => descriptor.Id)
                  .Should().BeEquivalentTo("KOS8001", "KOS8002", "KOS8003");

        suppressor.SupportedSuppressions.Should()
                  .OnlyContain(descriptor => descriptor.SuppressedDiagnosticId == "CS8618");

        suppressor.SupportedSuppressions.Should()
                  .OnlyContain(descriptor => !String.IsNullOrWhiteSpace(descriptor.Justification.ToString()));
    }

    [TestCase("[Inject] public ITestService Member { get; set; }", TestName = "Inject attribute")]
    [TestCase("public string Member { get; set; }\nprotected override void OnInitialized() => Member = \"x\";", TestName = "Assigned in OnInitialized")]
    [TestCase("public string Member { get; set; }\nprotected override async Task OnInitializedAsync() { await Task.Yield(); Member = \"x\"; }",
              TestName = "Assigned in OnInitializedAsync")]
    [TestCase("private string Member;\nprotected override void OnInitialized() => Member = \"x\";", TestName = "Field assigned in OnInitialized")]
    [TestCase("public string Member { get; set; }\nprotected override void OnInitialized() => this.Member = \"x\";", TestName = "Assigned via this")]
    [TestCase("public string Member { get; set; }\nprotected override void OnInitialized() { if (true) { Member = \"x\"; } }",
              TestName = "Assigned in nested block")]
    public async Task Cs8618_IsSuppressed(String memberDeclaration)
    {
        var source = $$"""
                       public class Component : ComponentBase
                       {
                           {{memberDeclaration}}
                       }
                       """;

        var diagnostics = await TestHarness.GetCs8618DiagnosticsAsync(source);

        diagnostics.Should().BeEmpty();
    }

    [Test]
    public async Task Cs8618_IsSuppressed_ForComponentInheritingFromIntermediateBase()
    {
        const String source = """
                              public abstract class IntermediateBase : ComponentBase { }

                              public class Component : IntermediateBase
                              {
                                  [Inject] public ITestService Injected { get; set; }
                              }
                              """;

        var diagnostics = await TestHarness.GetCs8618DiagnosticsAsync(source);

        diagnostics.Should().BeEmpty();
    }

    [TestCase("public string Member { get; set; }", TestName = "Never assigned")]
    [TestCase("public string Member { get; set; }\nprotected override void OnParametersSet() => Member = \"x\";", TestName = "Assigned in OnParametersSet")]
    [TestCase("public string Member { get; set; }\npublic void Configure() => Member = \"x\";", TestName = "Assigned in ordinary method")]
    [TestCase("[Parameter] public string Member { get; set; }", TestName = "Parameter attribute")]
    [TestCase("[CascadingParameter] public string Member { get; set; }", TestName = "CascadingParameter attribute")]
    public async Task Cs8618_IsReported_OnComponent(String memberDeclaration)
    {
        var source = $$"""
                       public class Component : ComponentBase
                       {
                           {{memberDeclaration}}
                       }
                       """;

        var diagnostics = await TestHarness.GetCs8618DiagnosticsAsync(source);

        diagnostics.Should().ContainSingle();
        TestHarness.GetMemberName(diagnostics[0]).Should().Be("Member");
    }

    [Test]
    public async Task Cs8618_IsReported_ForInjectAttributeOutsideComponent()
    {
        const String source = """
                              public class NotAComponent
                              {
                                  [Inject] public ITestService Injected { get; set; }
                              }
                              """;

        var diagnostics = await TestHarness.GetCs8618DiagnosticsAsync(source);

        diagnostics.Should().ContainSingle();
        TestHarness.GetMemberName(diagnostics[0]).Should().Be("Injected");
    }

    [Test]
    public async Task Cs8618_IsReported_WhenLifecycleMethodBelongsToAnotherType()
    {
        const String source = """
                              public class Other : ComponentBase
                              {
                                  protected override void OnInitialized() { }
                              }

                              public class Component : ComponentBase
                              {
                                  public string Member { get; set; }
                              }
                              """;

        var diagnostics = await TestHarness.GetCs8618DiagnosticsAsync(source);

        diagnostics.Should().ContainSingle();
        TestHarness.GetMemberName(diagnostics[0]).Should().Be("Member");
    }

    [Test]
    public async Task Cs8618_IsReported_InNonBlazorCompilation()
    {
        const String source = """
                              public class Component
                              {
                                  public string Member { get; set; }
                              }
                              """;

        var diagnostics = await TestHarness.GetCs8618DiagnosticsAsync(source, withBlazorReferences: false);

        diagnostics.Should().ContainSingle();
        TestHarness.GetMemberName(diagnostics[0]).Should().Be("Member");
    }

    [Test]
    public async Task DisablingKos8001_ReportsOnlyTheLifecycleMember()
    {
        var diagnostics = await TestHarness.GetCs8618DiagnosticsAsync(
            InjectAndLifecycleComponent,
            "KOS8001");

        diagnostics.Select(TestHarness.GetMemberName).Should().BeEquivalentTo("_lifecycle");
    }

    [Test]
    public async Task DisablingKos8002_ReportsOnlyTheInjectedMember()
    {
        var diagnostics = await TestHarness.GetCs8618DiagnosticsAsync(
            InjectAndLifecycleComponent,
            "KOS8002");

        diagnostics.Select(TestHarness.GetMemberName).Should().BeEquivalentTo("Injected");
    }

    [Test]
    public async Task BothSuppressionsApply_WhenNeitherIsDisabled()
    {
        var diagnostics = await TestHarness.GetCs8618DiagnosticsAsync(InjectAndLifecycleComponent);

        diagnostics.Should().BeEmpty();
    }

    [Test]
    public async Task Cs8618_IsSuppressed_ForPartialComponentWithLifecycleInOtherPart()
    {
        const String source = """
                              public partial class Component : ComponentBase
                              {
                                  private string _member;
                              }

                              public partial class Component
                              {
                                  protected override void OnInitialized() => _member = "x";
                              }
                              """;

        var diagnostics = await TestHarness.GetCs8618DiagnosticsAsync(source);

        diagnostics.Should().BeEmpty();
    }

    [Test]
    public async Task Cs8618_IsSuppressed_ForReferenceCaptureInGeneratedPart()
    {
        var diagnostics = await TestHarness.GetCs8618DiagnosticsAsync(ReferenceCaptureComponent);

        diagnostics.Should().BeEmpty();
    }

    [Test]
    public async Task DisablingKos8003_ReportsTheCapturedMember()
    {
        var diagnostics = await TestHarness.GetCs8618DiagnosticsAsync(
            ReferenceCaptureComponent,
            "KOS8003");

        diagnostics.Select(TestHarness.GetMemberName).Should().BeEquivalentTo("_captured");
    }

    [Test]
    public async Task Cs8618_IsReported_WhenAssignmentIsNotAReferenceCapture()
    {
        const String source = """
                              public partial class Component : ComponentBase
                              {
                                  private ITestService _captured;
                              }

                              public partial class Component
                              {
                                  protected override void BuildRenderTree(RenderTreeBuilder __builder)
                                  {
                                      __builder.AddContent(0, "text");
                                  }
                              }
                              """;

        var diagnostics = await TestHarness.GetCs8618DiagnosticsAsync(source);

        diagnostics.Should().ContainSingle();
        TestHarness.GetMemberName(diagnostics[0]).Should().Be("_captured");
    }

    /// <remarks>
    /// A type that declares a constructor moves every CS8618 from the member to the constructor,
    /// so the suppressor cannot read the member off the diagnostic's syntax.
    /// </remarks>
    [TestCase("[Inject] public ITestService Member { get; set; }", TestName = "Inject attribute")]
    [TestCase("public string Member { get; set; }\nprotected override void OnInitialized() => Member = \"x\";", TestName = "Assigned in OnInitialized")]
    [TestCase("private string Member;\nprotected override void OnInitialized() => Member = \"x\";", TestName = "Field assigned in OnInitialized")]
    public async Task Cs8618_IsSuppressed_ForComponentWithExplicitConstructor(String memberDeclaration)
    {
        var source = $$"""
                       public class Component : ComponentBase
                       {
                           public Component() { }

                           {{memberDeclaration}}
                       }
                       """;

        var diagnostics = await TestHarness.GetCs8618DiagnosticsAsync(source);

        diagnostics.Should().BeEmpty();
    }

    [Test]
    public async Task Cs8618_IsSuppressed_ForComponentWithSeveralConstructors()
    {
        const String source = """
                              public class Component : ComponentBase
                              {
                                  public Component() { }

                                  public Component(int value) => Value = value;

                                  public int Value { get; }

                                  [Inject] public ITestService Injected { get; set; }
                              }
                              """;

        var diagnostics = await TestHarness.GetCs8618DiagnosticsAsync(source);

        diagnostics.Should().BeEmpty();
    }

    [Test]
    public async Task Cs8618_IsSuppressed_ForReferenceCaptureInComponentWithExplicitConstructor()
    {
        const String source = """
                              public partial class Component : ComponentBase
                              {
                                  public Component() { }

                                  private ITestService _captured;
                              }

                              public partial class Component
                              {
                                  protected override void BuildRenderTree(RenderTreeBuilder __builder)
                                  {
                                      __builder.AddComponentReferenceCapture(1, (__value) => { _captured = (ITestService)__value; });
                                  }
                              }
                              """;

        var diagnostics = await TestHarness.GetCs8618DiagnosticsAsync(source);

        diagnostics.Should().BeEmpty();
    }

    /// <remarks>
    /// Every diagnostic of a constructor-anchored type shares one location, so the uncovered member
    /// has to be told apart from the covered ones rather than the whole type being suppressed.
    /// </remarks>
    [Test]
    public async Task Cs8618_IsReported_ForUncoveredMemberOfComponentWithExplicitConstructor()
    {
        const String source = """
                              public class Component : ComponentBase
                              {
                                  public Component() { }

                                  [Inject] public ITestService Injected { get; set; }

                                  public string Forgotten { get; set; }
                              }
                              """;

        var diagnostics = await TestHarness.GetCs8618DiagnosticsAsync(source);

        diagnostics.Should().ContainSingle();
        TestHarness.GetMemberName(diagnostics[0]).Should().Be("Forgotten");
    }

    [Test]
    public async Task Cs8618_IsReported_ForInjectAttributeOutsideComponentWithExplicitConstructor()
    {
        const String source = """
                              public class NotAComponent
                              {
                                  public NotAComponent() { }

                                  [Inject] public ITestService Injected { get; set; }
                              }
                              """;

        var diagnostics = await TestHarness.GetCs8618DiagnosticsAsync(source);

        diagnostics.Should().ContainSingle();
        TestHarness.GetMemberName(diagnostics[0]).Should().Be("Injected");
    }

    /// <remarks>
    /// Documents a known limitation: assignments are matched by name only, so a local variable
    /// that shadows the member name suppresses the warning.
    /// </remarks>
    [Test]
    public async Task Cs8618_IsSuppressed_ForLocalVariableShadowingTheMemberName()
    {
        const String source = """
                              public class Component : ComponentBase
                              {
                                  public string Member { get; set; }

                                  protected override void OnInitialized()
                                  {
                                      string Member;
                                      Member = "local";
                                      System.Console.WriteLine(Member);
                                  }
                              }
                              """;

        var diagnostics = await TestHarness.GetCs8618DiagnosticsAsync(source);

        diagnostics.Should().BeEmpty();
    }
}
