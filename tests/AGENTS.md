# AGENTS.md for Automated Tests

## General Guidelines

- The test project uses .NET 10 and NUnit.
- For the assertion phase of a test, always use FluentAssertions.
- Do not use libraries like Moq or NSubstitute for creating test doubles. Implement test doubles manually.
- Do not write test methods in nested classes. Test classes can have nested types, typically for types that represent test doubles, but test methods should always be placed in a top-level class residing directly in a C# namespace.
- Try to avoid code duplication across several test methods. Refactor code by using, for example, data-driven tests, test fixtures, or factory methods. Honor the DRY and the Single Point of Truth principles.
- Do not add the null-forgiving operator (`!`) after asserting a value with FluentAssertions. `Should()` annotates its subject with `[NotNull]`, so the compiler already treats the expression as non-null afterwards. Assert with `Should().NotBeNull()` and dereference without `!`.
- Do not initialize test fixture members with `= null!` when they are assigned in `[SetUp]` or `[OneTimeSetUp]`. NUnit.Analyzers suppresses the corresponding warning, so declare them non-nullable and leave them uninitialized.
- Keep `null!` only where a test deliberately passes an invalid null to exercise argument validation.

## How to Test Analyzers

- A test compiles a source snippet and asserts the diagnostics the analyzer reports on it. `SuppressorTestHarness` builds the compilation from the assemblies already loaded into the test process, so the tests need no network access and no reference-assembly download. A harness is not a test double; the rule against mocking libraries above does not apply to it.
- Suppressions are observed as an **absence**: `CompilationWithAnalyzers.GetAllDiagnosticsAsync()` removes suppressed diagnostics instead of returning them with `IsSuppressed` set, and `Diagnostic.ProgrammaticSuppressionInfo` is internal to Roslyn. To assert which suppression rule fired, switch the other one off through the compilation's `SpecificDiagnosticOptions` (`ReportDiagnostic.Suppress` on the suppression ID, the API-level equivalent of `dotnet_diagnostic.<id>.severity = none`) and check that the diagnostic reappears. This doubles as coverage for a user's ability to disable a single rule.
- Every rule needs both directions: sources that must trigger the diagnostic and sources that must stay silent. A rule that is only tested on its positive cases is not tested.
- Test snippets must compile cleanly apart from the diagnostics under test. A snippet with unrelated compiler errors makes the analyzer's behavior undefined, so add the Blazor reference assemblies the snippet needs to the test compilation.
- Keep snippets minimal and inline in the test. Move a snippet to a fixture file under the test project only when its size makes the test unreadable.
- Cover the cases where an analyzer must not fire at all: a compilation without any Blazor reference, generated code, and partial or erroneous source as it appears while the user is still typing in the IDE.

## How to Structure Tests

### Three Different Types of Tests

In general, we distinguish between three types of tests:

1. Unit Tests: these are NUnit test methods that completely run in memory when executed, that is, they do not make I/O calls to third-party systems like databases, web services, message brokers, etc. There is one exception to this rule: Unit Tests are allowed to read from and write to the local file system as it is always present on the executing dev machine or within CI job runners. For Chaos.BlazorAnalyzers this means every test is a Unit Test: creating a `CSharpCompilation` and running an analyzer over it happens entirely in process, and reading a source fixture from disk falls under the file system exception.
2. Integration tests: these are test methods where I/O calls to third-party systems (as mentioned above) are performed. The defining factor is the existence of at least one I/O call that does not involve the local file system. Chaos.BlazorAnalyzers has no third-party system — an analyzer's only input is a compilation — so integration tests are not applicable to this repository. Do not introduce infrastructure for them.
3. An End-to-End test is a modification of an integration test where test doubles are not allowed. Everything has to be in place as it would run in a staging or production environment. The equivalent here would be building a real consumer project against the packaged analyzer and asserting on the build output. This is out of scope for the automated test suite; do not build End-to-End infrastructure for this repository.

### We Follow the Test Pyramid

We follow the test pyramid where Unit Tests are the most important part of our test suite. Integration tests and especially End-to-End tests are more complex to set up and maintain, they also take longer to run, which is why we treat them as a second line of defense. The production code should be primarily tested via Unit Tests (happy path and error/edge cases), integration tests and End-to-End tests should only cover the happy path and check if things work as expected when everything is in place.

An alternative to the Test Pyramid is the Test Diamond which focuses on Integration Tests - we do not follow this approach.

### We Prefer Sociable Unit Tests

In Sociable Unit Tests, the SUT's dependencies are usually not replaced with test doubles. Instead, the actual production code types are used within the test (the target unit can reference other production code units).

When designing Unit Tests, first go for the type that provides the highest-level API for the test scenario. Include all types that the high-level API depends upon. Only leave out types that perform I/O calls and replace these with test doubles to keep the Unit Test isolated and fast.

For an analyzer, the highest-level API is the analyzer itself driven over a compilation. Test through it rather than through the helper types it uses internally, and only add tests for a helper when coverage shows the analyzer-level tests do not reach it.

Use Code Coverage to find out which dependencies are not covered fully by the highest-level API tests and add these as additional tests.

An alternative to this approach is the Solitary Unit Test where each dependency is replaced with a test double. We want to avoid this approach.

### Test Doubles

Analyzers take no injected dependencies, so test doubles are rare in this repository. When one is needed, we use the Test Double types defined in the book Xunit Test Patterns by Gerard Meszaros:

- Dummy/Null Object: An object or value which is passed to the System Under Test which is irrelevant for the test scenario. A Dummy will not be called by the System Under Test, only forwarded to other dependencies. A Null Object will be called by the SUT, but the corresponding methods have no return value, the implementation of the Null Object is empty.
- Stub: an object which is called by the SUT, the corresponding methods have return values and the Stub returns preconfigured data. This data is either hard-coded or can be injected by the test or test fixture.
- Spy: an object which is called by the SUT, the corresponding methods have no return value and the Spy captures information about the calls. This information can then be used in the assertion phase of a test.
- Mock: a combination of a Stub and a Spy.
- Fake: this is a test double that replaces entire system (so called Dependent-On Component, DOC) like a database, a message broker, or a simulation. The SUT typically uses drivers which are redirected to use the Fake instead of the real third-party system. Thus, the SUT is not really aware that a Fake is in place.

Avoid Fakes if possible. They usually do not behave exactly as the third-party system they replace. It is usually better to write custom test doubles which are tailored to the test scenario.
