$ErrorActionPreference = "Stop"

$ProjectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$PublishDir = Join-Path $ProjectDir "bin\Release\net8.0-windows\win-x64\publish"
$BundledExePath = Join-Path $ProjectDir "CapsLang.exe"
$ExePath = $BundledExePath

Get-Process CapsLang -ErrorAction SilentlyContinue | Stop-Process

if (-not (Test-Path $BundledExePath)) {
    $ExePath = Join-Path $PublishDir "CapsLang.exe"

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw "CapsLang.exe was not found in this folder, and dotnet SDK was not found. Download a release ZIP, or install .NET 8 SDK to build from source."
    }

    dotnet publish $ProjectDir -c Release -r win-x64 --self-contained false
}

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
