# Root AGENTS.md

`Chaos.BlazorAnalyzers` is a set of Roslyn analyzers for Blazor, shipped as a NuGet package. The repository contains exactly one production project and its test project. There is no UI, no web application, and no console application.

## Implementation rules

Plans typically have acceptance criteria with check boxes. Check each box when you are finished with the corresponding criterion.

## General Rules for the Code Base

- The analyzer project targets `netstandard2.0`, because analyzers are loaded into the compiler host. The test project targets `net10.0`. Both settings come from `Directory.Build.props` — do not set `TargetFramework` in a project file.
- Package versions are managed centrally in `Directory.Packages.props`. Add a `PackageVersion` there and reference the package without a version.
- `TreatWarningsAsErrors` is on for Release. Run the release build before you consider a change finished.
- FluentAssertions is pinned to the 7.x line for licensing reasons. Do not upgrade it.

### Code Style

For the project's code style, refer to `CODESTYLE.md`.

## Local Development Commands

Run these from the repository root. The scripts and the coverage recipe use the local tools from
`dotnet-tools.json`, so run `dotnet tool restore` once after cloning.

| Purpose                      | Command                                                                                                          |
|------------------------------|------------------------------------------------------------------------------------------------------------------|
| Apply the code style         | `./cleanup_code.sh`                                                                                              |
| Find code issues             | `./inspect_code.sh`                                                                                              |
| Find code issues in a branch | `./inspect_code.sh --base <revision>`                                                                            |
| Complete test suite          | `dotnet test Chaos.BlazorAnalyzers.slnx`                                                                         |
| Test suite with coverage     | `dotnet dotnet-coverage collect -f cobertura -o coverage.cobertura.xml "dotnet test Chaos.BlazorAnalyzers.slnx"` |
| Release parity               | `dotnet build -c Release Chaos.BlazorAnalyzers.slnx`                                                             |

### Code style and inspections

Run `./cleanup_code.sh` when you are finished with a change. It applies ReSharper's `Zorn` profile to the files Git reports as changed, staged, or untracked.

`./inspect_code.sh` runs ReSharper's inspections and reports semantic findings. Only C# files are inspected, because `inspectcode` has no rules for the other file types `cleanup_code.sh` formats. Every argument the script does not own itself is forwarded to `dotnet jb inspectcode`, so `-e=WARNING` narrows the report to warnings and above. Both scripts print `No matching files to process.` and do nothing when no file of a relevant type is affected.

Match the scope to what you are checking:

- **No arguments** — the changed, staged, and untracked files, like `cleanup_code.sh`. Use this while implementing, after `cleanup_code.sh` and before committing.
- **`--base <revision>`** — the C# files added, modified, or renamed in `<revision>...HEAD`, plus the current working-tree changes. Use this to review a committed branch or stack layer, with the trunk or the layer below as the base. `--base=<revision>` works as well.
- **`--all`** — the whole solution. Reserve this for explicit whole-solution audits, analyzer or configuration changes, and broad refactors. It is slow and reports pre-existing findings that have nothing to do with the change under review.

`--base` and `--all` are mutually exclusive. A scoped run that selects no C# files means the inspection is not applicable to that change; it is not a reason to fall back to `--all`.

Findings are advisory: the script reports them and exits 0 either way. A non-zero exit means the inspection itself could not run, for example because an argument was invalid, or because the `--base` revision could not be resolved or shares no history with `HEAD`, as in a shallow clone.

**Formatting is `cleanup_code.sh`'s responsibility.** `inspect_code.sh` does not report formatting deviations, because `inspectcode` ships nearly all of its formatting rules disabled and enabling them would mean changing the shared `Chaos.BlazorAnalyzers.slnx.DotSettings` for everyone working in the solution. Do not reach for the inspection script expecting whitespace, indentation, or brace findings.

Do not use `dotnet format`: it never reads `Chaos.BlazorAnalyzers.slnx.DotSettings` and therefore reports findings that contradict the profile this repository actually enforces.

## Production Code Rules

Read ./src/AGENTS.md for details about the production code.

## Testing Rules

Read ./tests/AGENTS.md for details about how to write tests.

## Plan Rules

Read ./ai-plans/AGENTS.md for details on how to write plans.

## Here is Your Space

If you encounter something worth noting while you are working on this code base, write it down here in this section. Once you are finished, I will discuss it with you, and we can decide where to put your notes.
