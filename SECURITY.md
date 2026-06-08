# Security Policy

Foreman is a Windows-native watchdog for AI coding agents. It runs in the
system tray at medium integrity (no admin), watches harness processes via WMI,
applies a heuristic command-analysis engine, and exposes a local MCP server on
`http://localhost:54321/mcp` (port configurable). Because it inspects process
command lines and hosts a local HTTP endpoint, it has a real, if small, attack
surface. This document explains how to report problems and what is in scope.

Foreman is alpha software built on a .NET 10 preview SDK, and it is maintained
as a hobby project. Please read the response-window note below before you set
expectations.

## Supported versions

Only the most recent code receives security fixes:

| Version              | Supported |
| -------------------- | --------- |
| Latest GitHub release | Yes      |
| `main` (HEAD)        | Yes       |
| Anything older       | No        |

There are no long-term-support or backport branches. If you are running a build
older than the latest release, the first step is to update. The MCP `ForemanStatus`
tool reports the running version (the `/health` endpoint reports liveness, port,
and session count, but not the version); include the version in a report.

## Reporting a vulnerability

Report privately. Do **not** open a public GitHub issue, discussion, or pull
request for a suspected vulnerability, and do not post details on social media,
until a fix is available and you have coordinated disclosure.

Two private channels, either is fine:

1. **GitHub Security Advisories** — open a draft advisory on the repository
   (Security tab -> "Report a vulnerability"). This is preferred because it
   keeps the discussion attached to the project.
2. **Email** — `jinkaflops@gmail.com`. Put "Foreman security" in the subject
   line. If you want to encrypt, ask in a first short message and a key can be
   arranged.

A useful report includes:

- Affected version or commit (see the version string above).
- A description of the issue and the impact you believe it has.
- Minimal steps to reproduce, including OS build (Windows 10/11 x64) and how
  Foreman was launched (installer vs. build from source).
- Any relevant configuration: the MCP port, whether run-at-login is enabled,
  and any custom harness names or permission profiles in use.

### Response window

This is a single-maintainer hobby project, so this is best-effort, not an SLA:

- Acknowledgement of your report: typically within about a week.
- An initial assessment (in scope / not in scope, rough severity): within about
  two weeks of acknowledgement.
- Fix timeline: depends on severity and complexity, and will be communicated in
  the thread. High-impact issues are prioritized.

If you have not heard back within two weeks, a polite follow-up on the same
channel is welcome — mail can be missed.

Credit: reporters who want acknowledgement will be credited in the release
notes or the advisory. Let me know your preference; anonymous reports are fine
too.

## Scope

Foreman is a local desktop tool. There is no hosted service, no account system,
and no telemetry. Scope is bounded accordingly.

### In scope

- The MCP server and its HTTP/SSE endpoints (`/mcp`, `/health`): unauthenticated
  access from other local processes, request handling flaws, or anything that
  lets an MCP client reach beyond the documented tool set.
- The local HTTP listener itself — for example, unintended binding beyond
  localhost, or behavior that exposes the port to other machines.
- Process and command-line handling in the monitor and heuristic engine:
  crashes, resource exhaustion, or memory-safety issues triggered by hostile or
  malformed process command lines that Foreman reads.
- The false-positive filter or pattern engine mishandling input in a way that
  causes a crash or hang rather than a missed/extra alert.
- Privilege or integrity issues: any path by which Foreman ends up running with
  more privilege than the `asInvoker` (medium integrity) manifest intends, or by
  which a monitored process can influence Foreman's own execution.
- The installer (`installer/foreman.iss`) and the release artifacts: tampering,
  unsafe install paths, or writing outside the per-user install location.
- Handling of files Foreman reads from disk, such as Claude settings and the
  pattern/profile JSON, if a crafted file can cause unsafe behavior.

### Out of scope

- **Detection coverage gaps.** A command that a real attacker could run but that
  Foreman's heuristics do not flag is a missed detection, not a vulnerability.
  Foreman is described as "a smart AV, not a policy enforcer" — it is a
  best-effort watchdog and is trivially bypassable by a determined adversary on
  the same machine. False negatives (and false positives) are bugs or tuning
  requests; please file those as normal issues, and see `data/patterns/*.json`
  for the rule definitions.
- **The agents being monitored.** Vulnerabilities in Claude Code, Codex,
  Gemini CLI, or any other harness belong to those projects, not Foreman.
- **Local attacker already at your privilege level.** Foreman runs at medium
  integrity and binds to localhost. Another process running as the same user can
  already do anything Foreman can. Foreman does not, and cannot, defend the
  machine against a same-user attacker; that is outside its threat model.
- **Roadmap features that do not exist yet.** The elevated ETW sidecar,
  server-initiated SSE push, native toast notifications, and the settings/profile
  editor UI are unbuilt. Reports about how they "could" behave are not actionable
  until the code lands.
- Issues that require physical access, a compromised OS, or disabling Windows
  security features to reproduce.
- Denial of service that requires the reporter to already control the machine
  Foreman runs on.

If you are unsure whether something is in scope, report it privately anyway and
ask. It is better to over-report than to disclose publicly.

## About the detection content

Foreman ships detection patterns under `data/patterns/` (and embedded as
resources in `Foreman.Core/patterns`). These cover categories such as
destructive/dangerous commands, network-borne code execution, privilege
escalation, credential-access tooling, and Windows-specific defense-evasion and
persistence techniques.

These patterns are **descriptive signatures** — compiled regular expressions
that recognize the shape of known-risky command behavior so it can be flagged
for a human. They are not exploits, payloads, or runnable attack tooling, and
they do not execute anything. They exist so a defender can understand what
Foreman watches for. Treat them the way you would any other detection ruleset:
useful for monitoring, not a how-to. Improvements and new rules are welcome via
normal pull requests under the project's GPL-3.0-or-later license.

## A note on the MCP bridge

The MCP server listens on localhost and is currently unauthenticated — any local
process that can reach the port can call the tool set (status, process listing,
behavior metrics, command pre-checks, alert acknowledgement). Keep this in mind
on shared or multi-user machines. If you find a way for that endpoint to be
reached from off the machine, or to do more than its documented tools allow,
that is in scope — report it via the channels above.
