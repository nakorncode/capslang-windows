param(
    [ValidateSet("win-x64")]
    [string] $Runtime = "win-x64",

    [ValidateSet("Release", "Debug")]
    [string] $Configuration = "Release",

    [string] $ProductVersion
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptDir
$ProjectPath = Join-Path $RepoRoot "CapsLang.csproj"
$PublishDir = Join-Path $RepoRoot "artifacts\publish\$Runtime"
$ReleaseDir = Join-Path $RepoRoot "artifacts\release"
$PortableZipPath = Join-Path $ReleaseDir "CapsLang-Portable-$Runtime.zip"
$ChecksumPath = Join-Path $ReleaseDir "CapsLang-SHA256SUMS.txt"

function Get-ProductVersion {
    if (-not [string]::IsNullOrWhiteSpace($ProductVersion)) {
        return $ProductVersion.TrimStart("v")
    }

    if ($env:GITHUB_REF_TYPE -eq "tag" -and -not [string]::IsNullOrWhiteSpace($env:GITHUB_REF_NAME)) {
        return $env:GITHUB_REF_NAME.TrimStart("v")
    }

    $GitTag = git -C $RepoRoot describe --tags --exact-match 2>$null
    if (-not [string]::IsNullOrWhiteSpace($GitTag)) {
        return $GitTag.TrimStart("v")
    }

    [xml] $ProjectXml = Get-Content $ProjectPath
    return $ProjectXml.Project.PropertyGroup.Version
}

function Get-FileVersion([string] $Version) {
    $Parts = $Version.Split(".")
    while ($Parts.Count -lt 4) {
        $Parts += "0"
    }

    return ($Parts | Select-Object -First 4) -join "."
}

$ResolvedVersion = Get-ProductVersion
$FileVersion = Get-FileVersion $ResolvedVersion

Remove-Item -LiteralPath $PublishDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $PublishDir, $ReleaseDir | Out-Null

Get-ChildItem -Path $ReleaseDir -File -Filter "CapsLang-*" -ErrorAction SilentlyContinue | Remove-Item -Force

dotnet publish $ProjectPath `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    --output $PublishDir `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:Version=$ResolvedVersion `
    -p:FileVersion=$FileVersion `
    -p:AssemblyVersion=$FileVersion `
    -p:InformationalVersion=$ResolvedVersion

$PackageStageDir = Join-Path $ReleaseDir "CapsLang-Portable-$Runtime"
Remove-Item -LiteralPath $PackageStageDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $PackageStageDir | Out-Null

Copy-Item -Path (Join-Path $PublishDir "*") -Destination $PackageStageDir -Recurse
Copy-Item -LiteralPath (Join-Path $RepoRoot "README.md") -Destination $PackageStageDir
Copy-Item -LiteralPath (Join-Path $RepoRoot "LICENSE") -Destination $PackageStageDir
Copy-Item -LiteralPath (Join-Path $RepoRoot "install-startup.ps1") -Destination $PackageStageDir
Copy-Item -LiteralPath (Join-Path $RepoRoot "uninstall-startup.ps1") -Destination $PackageStageDir
Copy-Item -LiteralPath (Join-Path $RepoRoot "assets") -Destination $PackageStageDir -Recurse

Remove-Item -LiteralPath $PortableZipPath -Force -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $PackageStageDir "*") -DestinationPath $PortableZipPath
Remove-Item -LiteralPath $PackageStageDir -Recurse -Force

$Packages = Get-ChildItem -Path $ReleaseDir -File | Where-Object { $_.Extension -in ".zip", ".exe", ".msi" } | Sort-Object Name
$HashLines = foreach ($Package in $Packages) {
    $Hash = Get-FileHash -Path $Package.FullName -Algorithm SHA256
    "$($Hash.Hash.ToLowerInvariant())  $($Package.Name)"
}

Set-Content -Path $ChecksumPath -Value $HashLines -Encoding ASCII

Write-Host "Release assets created:"
Get-ChildItem -Path $ReleaseDir -File | Sort-Object Name | ForEach-Object {
    Write-Host " - $($_.FullName)"
}
