# Contributing to Foreman

Thanks for taking the time to help. Foreman is alpha software built on a preview SDK, so expect rough edges — bug reports, detection-rule additions, and small focused PRs are all welcome.

## Prerequisites

- **.NET 10 SDK (preview)** — Foreman currently targets `net10.0` on a preview SDK. Install the preview channel; the CI workflow uses `dotnet-quality: preview`.
- **Windows 10/11 x64.** The tray app, WMI monitoring, and P/Invoke code are Windows-only. `Foreman.Core` is platform-agnostic and its tests run anywhere, but the full solution builds and runs only on Windows.
- A working `dotnet` on your `PATH`. No admin/UAC is required — Foreman runs at medium integrity.

## Build and test

The solution file is [`Foreman.slnx`](Foreman.slnx) (the newer XML solution format).

```
dotnet build Foreman.slnx -c Release
dotnet test  Foreman.slnx -c Release
```

CI runs the equivalent on `windows-latest` as separate restore / build / test steps (see [`.github/workflows/ci.yml`](.github/workflows/ci.yml)).

## Project layout

Four projects under `src/`:

| Project | Responsibility |
| --- | --- |
| `Foreman.Core` | Platform-agnostic logic: events/EventBus, models, heuristics (`CommandAnalyzer` + `PatternLibrary`), behavior tracking and escalation, settings, profiles. |
| `Foreman.Monitor` | Windows-only: WMI process create/terminate watcher, `ProcessTreeTracker`, `HarnessClassifier`, `IoPoller`, `HangDetector`, `OrphanDetector`, `ClaudeSettingsReader`. |
| `Foreman.McpServer` | Kestrel host, MCP tool registry, and SSE session manager. |
| `Foreman.App` | The WPF tray app and all its windows. |

Tests live under `tests/`: `Foreman.Core.Tests` and `Foreman.Monitor.Tests` (xUnit).

## Adding a detection rule

Detection rules are plain JSON, grouped by category. The source-of-truth copies live in [`data/patterns/*.json`](data/patterns/); `Foreman.Core/patterns/*.json` are the embedded-resource copies that actually ship. `PatternLibrary` loads every embedded `*.json` at startup and pre-compiles each `pattern` into a timeout-guarded `Regex`. Keep both locations in sync when you add or change a rule.

Each file is `{ "category", "version", "rules": [ ... ] }`. A rule object has these fields:

| Field | Type | Notes |
| --- | --- | --- |
| `id` | string | Stable unique id, prefixed per category (e.g. `del-006`, `net-003`). Tests reference this. |
| `name` | string | Short human-readable label. |
| `severity` | string | One of `info`, `low`, `medium`, `high`, `critical`. Parsed into `ForemanSeverity`; an unknown value falls back to `info`. |
| `description` | string | One line on what the rule matches and why it matters. |
| `pattern` | string | A .NET regex (JSON-escaped). Compiled case-insensitive with a 50 ms match timeout, so keep it anchored and avoid catastrophic backtracking. |
| `platforms` | string[] | Shells/contexts the rule applies to, e.g. `["bash", "sh", "wsl"]` or `["cmd", "powershell"]`. |
| `falsePositiveTags` | string[] | Tags consumed by the false-positive filter to suppress known-safe contexts. Use `[]` if none. |

An optional `guidance` field is also supported — a short string shown to the user explaining what to do about a match.

### Benign example

Use a real category file for your actual rule, but here is the shape, using a placeholder pattern that matches nothing dangerous:

```json
{
  "id": "demo-001",
  "name": "example placeholder rule",
  "severity": "low",
  "description": "matches the literal marker string used in contributor docs",
  "pattern": "\\bfoo-bar-baz\\b",
  "platforms": ["bash", "cmd", "powershell"],
  "falsePositiveTags": []
}
```

### Add a test

Every new rule should come with a test. `tests/Foreman.Core.Tests/Heuristics/CommandAnalyzerTests.cs` drives `CommandAnalyzer.Analyze` through `[Theory]`/`[InlineData]` cases and asserts the expected `Severity` and `RuleId`. Add at least one positive case (a string your pattern should flag) and, where it matters, a negative case in `AllowsNormalCommands` so a benign command does not regress into a false positive. The class uses `PatternLibraryFixture`, which calls `PatternLibrary.Instance.Initialize()` once for the run.

When writing detection content, describe behavior at the category level. Do not paste working attack one-liners into descriptions or commit messages — the rule's `pattern` is enough.

## Coding conventions

- **Nullable reference types are enabled** solution-wide (`<Nullable>enable</Nullable>` in `Directory.Build.props`). Don't suppress warnings to make them go away; fix the nullability.
- **File-scoped namespaces** (`namespace Foreman.Core.Heuristics;`).
- **Implicit usings** are on; `LangVersion` is `latest`. Use collection expressions (`[]`) where the existing code does.
- **Keep `Foreman.Core` platform-agnostic.** No `System.Management`, no P/Invoke, no WPF, no Windows-only APIs in Core — that code belongs in `Foreman.Monitor` or `Foreman.App`. Core must stay testable cross-platform.
- Match the surrounding style; the existing files are the reference.

## Pull requests

- Branch off `main`, keep PRs focused, and write a clear description of what and why.
- Run `dotnet build` and `dotnet test` locally before pushing. **CI must pass** — it runs the build and full test suite on Windows, and a green check is required to merge.
- New behavior (especially detection rules) needs test coverage.
- If your change touches detection categories or the MCP tool surface, mention it explicitly so reviewers can check the safe-framing and the docs.

## Security

If you find a vulnerability, please email **jinkaflops@gmail.com** rather than opening a public issue.

## License

Foreman is licensed under **GPL-3.0-or-later** (see [`LICENSE`](LICENSE)). By contributing, you agree that your contributions are licensed under the same terms.
