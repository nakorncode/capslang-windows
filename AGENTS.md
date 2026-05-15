# AGENTS.md

Guidance for AI agents working on CapsLang.

## Project

CapsLang is a small Windows tray utility that turns `CapsLock` into a dedicated
input-language switch key without sending `Win+Space`.

Primary goals:

- Keep `CapsLock` from accidentally enabling uppercase lock.
- Avoid Windows shortcut leakage such as `Win+Space+1` or `Win+Space+D`.
- Show the active language near the text insertion caret after switching.
- Language labels must come from the active Windows input language culture and
  must not be hardcoded to Thai/English only.
- Stay lightweight, local-first, and easy to install from a release ZIP.

## Technical Boundaries

- This is a Windows-only .NET WinForms app.
- Keep the implementation dependency-free unless a dependency clearly reduces
  risk or complexity.
- Preserve the low-level keyboard hook behavior in `Program.cs`.
- Do not send `Win+Space` as the switching implementation.
- Prefer Win32/UI Automation APIs over simulated key chords for language
  switching, caret detection, and state management.
- Keyboard-triggered indicators should anchor to the text insertion caret when
  available. Use native UIA3 `TextPattern2` before older UIA or Win32 caret
  APIs. Do not fall back to the mouse pointer for normal typing feedback.

## UX Rules

- `CapsLock`: switch to next input language and keep CapsLock off.
- `Shift+CapsLock`: intentionally toggle real CapsLock.
- `Ctrl+CapsLock`: force CapsLock off without switching language.
- Tray menu must expose runtime toggles for CapsLang enabled/disabled, language
  indicator visibility, indicator placement, and Start with Windows.
- When CapsLang is disabled, let CapsLock pass through normally.
- Persist user settings under `%LOCALAPPDATA%\CapsLang`.
- Tray menu actions may use mouse-position feedback because they are pointer
  initiated.
- Popups must not take focus from the active app.

## Repo Hygiene

- Keep public docs clear for non-developer Windows users.
- Release ZIPs should include the executable, install/uninstall scripts,
  license, and README.
- Build outputs belong in `bin/`, `obj/`, or `artifacts/` and should not be
  committed.
- Do not run broad verification by default. Compile or package when changing
  Win32 interop, release automation, or install behavior.

## Release

- Tag releases as `vX.Y.Z`.
- Use the ToastDeck-style release pattern from the PC-wide
  `windows-release-packaging` skill.
- Keep `scripts/publish-release.ps1` as the canonical local and CI packaging
  entrypoint.
- Write distributable files to `artifacts/release/`.
- Keep latest-download asset names stable:
  - `CapsLang-Portable-win-x64.zip`
  - `CapsLang-SHA256SUMS.txt`
- Pushing a `v*.*.*` tag runs `.github/workflows/release.yml`.
- The workflow creates a self-contained `win-x64` ZIP plus checksum file and
  publishes them to the GitHub Releases tab.
