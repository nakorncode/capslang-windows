# CapsLang

CapsLang turns `CapsLock` into a dedicated Windows input-language switch key.

It does **not** send `Win+Space`, so fast typing cannot accidentally become
`Win+Space+1`, `Win+Space+D`, or another Windows shortcut.

After switching, it briefly shows the active input language near the text caret.
If Windows cannot expose the caret position for the current app, it shows the
popup near the mouse cursor instead.

## How it works

- Installs a low-level keyboard hook.
- Suppresses `CapsLock` down/up events before Windows toggles uppercase lock.
- Sends `WM_INPUTLANGCHANGEREQUEST` to the foreground window to move to the next
  input language.
- Reads the foreground thread keyboard layout and shows a short `TH` / `EN`
  style popup.
- Forces CapsLock state back to off if it was already enabled.
- Runs as a small tray app with an `Exit` menu.

## Key bindings

- `CapsLock`: switch to the next input language and keep CapsLock off.
- `Shift+CapsLock`: toggle real CapsLock on/off when you intentionally need it.
- `Ctrl+CapsLock`: force CapsLock off without changing input language.
- Tray menu `Turn CapsLock Off`: force CapsLock off with the mouse.

## Install

Open PowerShell in this folder and run:

```powershell
.\install-startup.ps1
```

The script publishes the app, creates a Startup shortcut, and starts it.

## Uninstall

```powershell
.\uninstall-startup.ps1
```

## Notes

- Requires the .NET 8 SDK to build.
- If CapsLock should also work inside elevated administrator apps, run CapsLang
  as administrator too. Normal non-admin apps should work without elevation.
- Keep the PowerToys CapsLock remap disabled while CapsLang is running, otherwise
  both tools may react to the same key.
- Some apps do not expose their text caret through the standard Windows API. In
  those apps the language popup appears near the mouse cursor.
