$ErrorActionPreference = "Stop"

$ProjectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$PublishDir = Join-Path $ProjectDir "bin\Release\net8.0-windows\win-x64\publish"
$ExePath = Join-Path $PublishDir "CapsLang.exe"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet SDK was not found. Install .NET 8 SDK, then run this script again."
}

Get-Process CapsLang -ErrorAction SilentlyContinue | Stop-Process

dotnet publish $ProjectDir -c Release -r win-x64 --self-contained false

if (-not (Test-Path $ExePath)) {
    throw "Publish completed, but CapsLang.exe was not found at $ExePath"
}

$StartupDir = [Environment]::GetFolderPath("Startup")
$ShortcutPath = Join-Path $StartupDir "CapsLang.lnk"

$Shell = New-Object -ComObject WScript.Shell
$Shortcut = $Shell.CreateShortcut($ShortcutPath)
$Shortcut.TargetPath = $ExePath
$Shortcut.WorkingDirectory = $PublishDir
$Shortcut.Description = "Use CapsLock as input language switcher"
$Shortcut.Save()

Start-Process -FilePath $ExePath -WorkingDirectory $PublishDir

Write-Host "Installed CapsLang startup shortcut:"
Write-Host $ShortcutPath
Write-Host "Started:"
Write-Host $ExePath
