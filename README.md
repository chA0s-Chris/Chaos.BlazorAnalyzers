# Chaos.BlazorAnalyzers

Roslyn analyzers and diagnostic suppressors for Blazor.

Blazor assigns a lot of state after a component is constructed — injected services, `@ref` captures, values set in lifecycle methods. The C# compiler cannot see that, so it reports nullability warnings for code that is actually correct. This package suppresses those, and reports Blazor-specific defects the compiler and the .NET analyzers do not catch.

## Installation

```
dotnet add package Chaos.BlazorAnalyzers
```

The package is a development dependency: it contributes no runtime reference and nothing ships with your application.

## Suppressions

All three suppress `CS8618` ("non-nullable member must contain a non-null value when exiting constructor") on types deriving from `ComponentBase`.

| ID                                                                                                | Suppressed when                                                    |
|---------------------------------------------------------------------------------------------------|--------------------------------------------------------------------|
| [`KOS8001`](https://github.com/chA0s-Chris/Chaos.BlazorAnalyzers/blob/main/docs/rules/KOS8001.md) | The member is assigned in `OnInitialized` or `OnInitializedAsync`. |
| [`KOS8002`](https://github.com/chA0s-Chris/Chaos.BlazorAnalyzers/blob/main/docs/rules/KOS8002.md) | The member is a property marked `[Inject]`, so DI assigns it.      |
| [`KOS8003`](https://github.com/chA0s-Chris/Chaos.BlazorAnalyzers/blob/main/docs/rules/KOS8003.md) | The member is captured by an `@ref`, so the renderer assigns it.   |

Members marked `[Parameter]` or `[CascadingParameter]` are deliberately **not** suppressed: the framework does not guarantee they are supplied.

All three work whether or not the component declares a constructor. A component that declares one makes the compiler report every member's `CS8618` against the constructor rather than against the member, and each rule resolves the member it belongs to.

## Analyzers

| ID                                                                                                | Severity | Description                                                                                   |
|---------------------------------------------------------------------------------------------------|----------|-----------------------------------------------------------------------------------------------|
| [`KOS2001`](https://github.com/chA0s-Chris/Chaos.BlazorAnalyzers/blob/main/docs/rules/KOS2001.md) | Warning  | A component assigns an `IJSObjectReference` or `DotNetObjectReference` and never disposes it. |

## Configuration

Every rule is configured through `.editorconfig` like any other diagnostic. Suppressions are switched off the same way, which re-enables the underlying compiler warning:

```ini
# turn a suppression off, so CS8618 is reported again for @ref captures
dotnet_diagnostic.KOS8003.severity = none

# escalate an analyzer rule
dotnet_diagnostic.KOS2001.severity = error
```

## Requirements

The analyzers target `netstandard2.0` and require **Roslyn 3.11 or newer**, so every currently supported .NET SDK works. The floor is set by the ReSharper and Rider inspection engine, which loads no analyzer built against a newer Roslyn — building against 3.11 means `dotnet jb inspectcode` reports these rules too. They only activate in compilations that reference ASP.NET Core Blazor; in any other project they report nothing.

## License

MIT. See [LICENSE](https://github.com/chA0s-Chris/Chaos.BlazorAnalyzers/blob/main/LICENSE).
