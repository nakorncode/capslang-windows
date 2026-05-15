$ErrorActionPreference = "Stop"

$ShortcutPath = Join-Path ([Environment]::GetFolderPath("Startup")) "CapsLang.lnk"

if (Test-Path $ShortcutPath) {
    Remove-Item $ShortcutPath
    Write-Host "Removed startup shortcut:"
    Write-Host $ShortcutPath
} else {
    Write-Host "Startup shortcut was not found:"
    Write-Host $ShortcutPath
}

Get-Process CapsLang -ErrorAction SilentlyContinue | Stop-Process
Write-Host "Stopped any running CapsLang process."
