<#
.SYNOPSIS
    Builds portable Dockett: one self-contained Dockett.exe per runtime, zipped with the portable marker.

.DESCRIPTION
    The exe bundles the .NET runtime, so it runs on Windows 10/11 without installing anything.
    Each zip contains Dockett.exe, Dockett.portable (keeps settings in a Data folder beside the exe),
    and README.txt. Output goes to artifacts\.

.EXAMPLE
    ./build/publish-portable.ps1
    ./build/publish-portable.ps1 -Runtime win-x64, win-arm64
#>
param(
    [string[]] $Runtime = @('win-x64'),
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\Dockett\Dockett.csproj'
$artifacts = Join-Path $root 'artifacts'
$version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1

foreach ($rid in $Runtime) {
    $out = Join-Path $artifacts "portable\$rid"
    if (Test-Path $out) { Remove-Item -Recurse -Force $out }

    Write-Host "Publishing Dockett $version for $rid..." -ForegroundColor Cyan
    # No trimming: Avalonia's XAML and the shell COM interop rely on reflection.
    dotnet publish $project `
        --configuration $Configuration `
        --runtime $rid `
        --self-contained true `
        --output $out `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none `
        -p:GenerateDocumentationFile=false
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $rid" }

    # SkiaSharp and HarfBuzz ship native debug symbols (~100 MB) that users don't need.
    Get-ChildItem $out -Filter *.pdb | Remove-Item -Force

    New-Item -ItemType File -Force (Join-Path $out 'Dockett.portable') | Out-Null
    @"
Dockett $version (portable, $rid)

Run Dockett.exe. Nothing needs to be installed.

Settings, tabs, and window layout are saved in the Data folder next to Dockett.exe,
so you can keep Dockett on a USB stick. To store them in your user profile
(%APPDATA%\Dockett) instead, delete Dockett.portable.

https://github.com/hiramatx/dockett
"@ | Set-Content -Encoding utf8 (Join-Path $out 'README.txt')

    $zip = Join-Path $artifacts "Dockett-$version-$rid-portable.zip"
    if (Test-Path $zip) { Remove-Item -Force $zip }
    Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip
    $size = [math]::Round((Get-Item $zip).Length / 1MB, 1)
    Write-Host "Created $zip ($size MB)" -ForegroundColor Green
}
