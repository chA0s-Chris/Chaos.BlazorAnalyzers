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

| ID        | Suppressed when                                                    |
|-----------|--------------------------------------------------------------------|
| `KOS8001` | The member is assigned in `OnInitialized` or `OnInitializedAsync`. |
| `KOS8002` | The member is a property marked `[Inject]`, so DI assigns it.      |
| `KOS8003` | The member is captured by an `@ref`, so the renderer assigns it.   |

Members marked `[Parameter]` or `[CascadingParameter]` are deliberately **not** suppressed: the framework does not guarantee they are supplied.

## Analyzers

| ID                                 | Severity | Description                                                                                   |
|------------------------------------|----------|-----------------------------------------------------------------------------------------------|
| [`KOS2001`](docs/rules/KOS2001.md) | Warning  | A component assigns an `IJSObjectReference` or `DotNetObjectReference` and never disposes it. |

## Configuration

Every rule is configured through `.editorconfig` like any other diagnostic. Suppressions are switched off the same way, which re-enables the underlying compiler warning:

```ini
# turn a suppression off, so CS8618 is reported again for @ref captures
dotnet_diagnostic.KOS8003.severity = none

# escalate an analyzer rule
dotnet_diagnostic.KOS2001.severity = error
```

## Requirements

The analyzers target `netstandard2.0` and run in any Roslyn host. They only activate in compilations that reference ASP.NET Core Blazor; in any other project they report nothing.

## License

MIT. See [LICENSE](LICENSE).
