# Foreman

A Windows tray watchdog for AI coding agents. It watches the processes your agents spawn, flags shell commands that look dangerous, tracks each agent's behavior over a session, and exposes an MCP bridge so an agent can query its own oversight.

[![License: GPL-3.0-or-later](https://img.shields.io/badge/License-GPL--3.0--or--later-blue.svg)](LICENSE)

> Status: alpha. Built on a .NET 10 preview SDK. Windows 10/11 x64 only. See [Project status & roadmap](#project-status--roadmap) before you rely on it.

<!-- screenshots: tray menu, dashboard, alert window -->

## Why

AI coding agents are good at starting things and bad at cleaning up. Over a long session a harness will leave orphaned shell processes behind, jam a hook that never returns, and spawn child processes that outlive a harness restart. Once in a while it will also run a command that does real damage — wiping a directory, piping a remote script straight into a shell, reaching for credential-dumping tooling.

Foreman is a watchdog for that class of problem, not a policy enforcer. It runs at medium integrity (no admin, no UAC), sits in the tray, and tells you when an agent is making a mess or about to do something you'd want to stop. Think smart AV for agent sessions, not a sandbox.

## What it does

- Watches the process trees your agents create and notices when a child process is **hung** (silent past a threshold) or **orphaned** (parent harness gone, child still running).
- Flags shell commands by category — destructive/deletion commands, network-borne code execution, privilege escalation, credential-access tooling, and Windows-specific defense-evasion or persistence.
- Tracks per-agent behavior across a session and **escalates** through four levels (Watch → Alert → Alarm → Emergency) as alerts accumulate.
- Classifies known agents automatically (Claude Code, Codex, and others) from process name and command line; you can register custom executable names.
- Exposes an **MCP server** an agent can call to check its own status, pre-flight a command, see whether Foreman has raised an alarm about it, and announce task boundaries.
- Keeps a searchable, exportable event log and an at-a-glance dashboard, with tray notifications for critical alerts.

## Supported agents

Auto-classified by process name and command line:

| ID | Agent | Vendor |
| --- | --- | --- |
| `claude-code` | Claude Code | Anthropic |
| `codex` | Codex CLI | OpenAI |
| `gemini-cli` | Gemini CLI | Google |
| `amazon-q` | Amazon Q Developer | Amazon / AWS |
| `aider` | Aider | Paul Gauthier |
| `github-copilot` | GitHub Copilot CLI | GitHub / Microsoft |
| `cursor` | Cursor | Anysphere |
| `cline` | Cline / Continue / Roo | Community |

Anything else can be added as a custom harness executable name in settings.

## How it works

Four moving parts, kept deliberately separate.

**Tray app.** A WPF tray icon (green / amber / red) is the whole UI surface. Left-click opens the Dashboard (per-agent status chips and a recent-alert feed). Double-click opens the Event Log (filter, search, export). The right-click menu also reaches Process Monitor, Harnesses, Behavior Metrics, Settings, and a "Send test alert" action. A critical alert raises a tray notification; clicking it opens an Alert window that explains why the command is risky and what to do.

**Heuristic engine.** Around 30 compiled-regex rules across five categories, loaded from JSON. Each rule carries an id, name, severity (info / low / medium / high / critical), description, target platforms, and false-positive tags. A false-positive filter suppresses Foreman's own process and known-safe contexts. The rule sources live in [`data/patterns/`](data/patterns/) and are embedded into `Foreman.Core` at build time — read those files if you want the exact coverage; they are the source of truth, not this README.

**Process monitoring.** A WMI watcher subscribes to process create/terminate events. A process-tree tracker maps harness children, an I/O poller samples counters (`GetProcessIoCounters`) to tell "working" from "stuck," and hang/orphan detectors raise events when a child goes silent or loses its parent.

**Behavior escalation.** Each agent session accumulates a profile. Configurable thresholds — medium- and high-alert counts, unique rules fired, distinct categories touched, total alerts, and a set of high-risk rule IDs — drive escalation across four levels:

| Level | Roughly triggered by | Action |
| --- | --- | --- |
| Watch | default | logged only |
| Alert | a high-severity alert, several medium alerts, or multiple categories | tray turns red, notification |
| Alarm | a critical alert, enough unique rules or categories | notification + MCP push to the connected agent |
| Emergency | an emergency-tier rule, or too many alerts across the major categories at once | alarm window auto-opens with Kill / Disable actions |

**MCP bridge.** An embedded ASP.NET Core Kestrel host runs an MCP server (ModelContextProtocol SDK) over HTTP+SSE at `http://localhost:54321/mcp`, plus a `/health` endpoint. The tools exposed to an agent:

| Tool | What it does |
| --- | --- |
| `ForemanStatus` | overall health summary (green/amber/red, active alerts, process count, uptime) |
| `ListMonitoredProcesses` | the agent processes Foreman is tracking |
| `QueryProcessDetail` | details for one PID |
| `ReportSuspiciousCommand` | pre-flight a command line; returns allow / allow_once / escalate / block |
| `ListRecentEvents` | recent events, optionally filtered by minimum severity |
| `AcknowledgeAlert` | acknowledge an alert and suppress further notifications for it |
| `GetBehaviorMetrics` | escalation level and counts for every monitored agent |
| `ResetBehaviorMetrics` | reset one agent's escalation back to Watch (e.g. starting an unrelated task) |
| `ReportTaskStart` | announce a new task so operators can correlate task boundaries with alerts |
| `GetMyPermissions` | the permission profile that applies to the caller |

## Install

### From Releases (recommended)

Download the installer from [GitHub Releases](https://github.com/aXL333/Foreman/releases). It installs per-user to `%LocalAppData%\Foreman` with no admin prompt, and offers an optional run-at-login entry. The released build is a self-contained single-file exe, so no separate .NET runtime is required.

### From source

Prerequisites:

- Windows 10/11 x64
- .NET 10 SDK (preview — the project currently tracks a preview build)

```
dotnet build Foreman.slnx -c Release
dotnet test  Foreman.slnx -c Release
```

The solution uses the newer `.slnx` (XML) format. To run the tray app from a source build:

```
dotnet run --project src/Foreman.App
```

To produce the same kind of single-file exe the release workflow ships:

```
dotnet publish src/Foreman.App/Foreman.App.csproj ^
  -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## Connect your agent

Foreman's MCP server listens on `http://localhost:54321/mcp` while the tray app is running. To connect Claude Code, add this to your `.claude/settings.json`:

```json
{
  "mcpServers": {
    "foreman": {
      "type": "http",
      "url": "http://localhost:54321/mcp"
    }
  }
}
```

Any MCP client that speaks streamable HTTP can connect the same way. Confirm the server is up with a quick `GET http://localhost:54321/health`.

## Configuration

Settings are stored at `%LocalAppData%\Foreman\settings.json` and editable from the tray Settings window. The knobs that matter most:

| Setting | Default | Purpose |
| --- | --- | --- |
| `McpPort` | `54321` | MCP / health server port |
| `HangThresholdMinutes` | `10` | silence before a child is treated as hung |
| `HookJamThresholdMinutes` | `5` | silence before a hook is treated as jammed |
| `IoPollerIntervalSeconds` | `30` | how often I/O counters are sampled |
| `MonitorAllProcesses` | `false` | `false` = harness children only |
| `CustomHarnessExes` | `[]` | extra executable names to treat as agents |
| `DisabledHarnesses` | `[]` | agents to detect but not alert on |

Per-session escalation thresholds (medium-alert count, high-alert count, unique-rule count, category count, total-alert count, and the set of emergency-tier rule IDs) live in the same file. Defaults are in [`src/Foreman.Core/Settings/ForemanSettings.cs`](src/Foreman.Core/Settings/ForemanSettings.cs).

## Project status & roadmap

Honest accounting. This is alpha software on a preview runtime.

Built and working:

- Tray app with dashboard, event log, process monitor, harnesses, behavior-metrics, and settings windows
- Heuristic engine and the five-category pattern library
- WMI process monitoring with hang and orphan detection
- Permission profiles and behavior escalation
- MCP server and the tool set above
- CI build/test and a release workflow that publishes the single-file exe wrapped in an Inno Setup installer

Roadmap (not done yet — don't rely on these):

- An elevated ETW sidecar for pre-execution command capture
- True server-initiated SSE push (the dispatcher is wired, full proactive push is not complete)
- Native Windows toast notifications (currently tray balloons)
- A settings / profile editor UI and a first-run wizard
- Per-caller identification over MCP session metadata (`GetMyPermissions` currently returns a default profile)

## Contributing

Contributions are welcome under the project's license. See [CONTRIBUTING.md](CONTRIBUTING.md) for setup and conventions. Security reports go to jinkaflops@gmail.com — please don't open a public issue for a vulnerability.

## License

GPL-3.0-or-later. See [LICENSE](LICENSE). Contributions are accepted under the same license.

## Support

Foreman is free and GPL. If it has saved you from a mess and you want to chip in, there's a Ko-fi: <https://ko-fi.com/jinkaflops>.
