# Foreman FOSS Polish Handover

Date: 2026-06-08

## Current State

- App icon and tray status icons are now checked in under `src/Foreman.App/Resources`.
- The app executable uses `Resources\foreman.ico` as its Windows application icon.
- Every WPF window declares the Foreman icon for the titlebar, taskbar, and Alt-Tab surfaces.
- The Dashboard has a branded header, session metric strip, quick navigation actions, polished row cards, and an empty state using the supplied icon.
- README has a generated branded banner under `docs/assets/foreman-social-preview.png` and a short product-standards section to make the public project quality bar explicit.
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
- Running instance after bounce: `Foreman.App` PID 71592.
- Health endpoint: `http://localhost:54321/health` returned `status: ok`.

## Push Status

This checkout originally had no configured Git remote. A private GitHub repository was created at `https://github.com/aXL333/Foreman` and configured as `origin`.

`main` has been pushed and is tracking `origin/main`.

Note: the first push was rejected because the GitHub CLI OAuth token did not initially have `workflow` scope, which is required because `.github/workflows/ci.yml` and `.github/workflows/release.yml` are part of the history. After the auth refresh attempt, a direct `git push -u origin main` succeeded.

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
