# Foreman FOSS Polish Handover

Date: 2026-06-08

## Current State

- App icon and tray status icons are now checked in under `src/Foreman.App/Resources`.
- The app executable uses `Resources\foreman.ico` as its Windows application icon.
- Every WPF window declares the Foreman icon for the titlebar, taskbar, and Alt-Tab surfaces.
- The Dashboard has a branded header and empty state using the supplied icon.
- No-I/O hang alerts are medium/yellow warnings, not high/red alarms.
- Hang alerts now attribute both the direct spawner process and the owning harness when they differ.
- MCP alert payloads include spawner and owner metadata so another harness/API can audit the event without reconstructing stale process-tree state.
- MCP/profile integration now has integration instructions, validation, audit preference listing, and route selection.

## Verification

Run from `W:\TOOLS\Foreman`:

```powershell
dotnet test .\Foreman.slnx -c Release --verbosity minimal
dotnet build .\src\Foreman.App\Foreman.App.csproj -c Release
```

Latest local result:

- Tests: 54 passed, 0 failed.
- Release app build: succeeded, 0 warnings, 0 errors.
- Running instance after bounce: `Foreman.App` PID 69100.
- Health endpoint: `http://localhost:54321/health` returned `status: ok`.

## Push Status

This checkout originally had no configured Git remote. A private GitHub repository was created at `https://github.com/aXL333/Foreman` and configured as `origin`.

The push is still blocked: GitHub rejected `main` because the current GitHub CLI OAuth token has scopes `gist`, `read:org`, and `repo`, but not `workflow`. Pushing this repository requires `workflow` scope because `.github/workflows/ci.yml` and `.github/workflows/release.yml` are part of the history.

Attempted recovery:

```powershell
gh auth refresh -h github.com -s workflow
```

That command timed out waiting for auth completion. SSH push is also unavailable on this machine: `ssh -o BatchMode=yes -T git@github.com` returned `Permission denied (publickey)`.

To finish the push, refresh GitHub CLI auth with `workflow` scope in an interactive terminal, then run:

```powershell
git push -u origin main
```

## Before Public FOSS Release

- Add real screenshots for the tray menu, dashboard, alert detail, process monitor, and settings windows.
- Decide whether the supplied icon is safe to publish under the project license, or add an explicit asset credit/license note.
- Replace `.NET 10 preview` with a stable SDK target when practical, or document the preview requirement prominently in releases.
- Verify CI/release workflows against a fresh clone and signed installer expectations.
- Review SECURITY.md, CONTRIBUTING.md, funding metadata, and package/license notices from a public-reader perspective.
- Add a first-run setup path for MCP profile integration so users do not need to hand-edit harness config files.
- Add UI for LLM triage preference editing instead of relying only on JSON settings.
- Re-test installer install/uninstall/run-at-login behavior on a clean Windows VM before publishing.
