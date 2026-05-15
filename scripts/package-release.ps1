$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$Version = if ($env:GITHUB_REF_NAME) { $env:GITHUB_REF_NAME } else { "local" }
$Runtime = "win-x64"
$ArtifactsDir = Join-Path $RepoRoot "artifacts"
$PackageName = "CapsLang-$Version-$Runtime"
$StageDir = Join-Path $ArtifactsDir $PackageName
$ZipPath = Join-Path $ArtifactsDir "$PackageName.zip"

Remove-Item -LiteralPath $StageDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $ZipPath -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $StageDir | Out-Null

dotnet publish (Join-Path $RepoRoot "CapsLang.csproj") `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $StageDir

Remove-Item -LiteralPath (Join-Path $StageDir "*.pdb") -Force -ErrorAction SilentlyContinue

Copy-Item -LiteralPath (Join-Path $RepoRoot "README.md") -Destination $StageDir
Copy-Item -LiteralPath (Join-Path $RepoRoot "LICENSE") -Destination $StageDir
Copy-Item -LiteralPath (Join-Path $RepoRoot "install-startup.ps1") -Destination $StageDir
Copy-Item -LiteralPath (Join-Path $RepoRoot "uninstall-startup.ps1") -Destination $StageDir
Copy-Item -LiteralPath (Join-Path $RepoRoot "assets") -Destination $StageDir -Recurse

Compress-Archive -Path (Join-Path $StageDir "*") -DestinationPath $ZipPath -Force

Write-Host "Created release package:"
Write-Host $ZipPath
