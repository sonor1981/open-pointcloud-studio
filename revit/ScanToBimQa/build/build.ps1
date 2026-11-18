<#
.SYNOPSIS
  Builds and tests the Scan-to-BIM QA add-in on Windows.

.DESCRIPTION
  Runs the core test suite, then builds one package per Revit version against the RevitAPI.dll of that
  version's install folder (C:\Program Files\Autodesk\Revit <version>). Versions that are not installed
  are skipped. Packages land in dist\ScanToBimQa-Revit<version>.zip.

  Requires the .NET 10 SDK (and the .NET Framework 4.8 developer pack for Revit 2023/2024).

.EXAMPLE
  .\build\build.ps1                      # all installed versions
  .\build\build.ps1 -Versions 2026       # only Revit 2026
  .\build\build.ps1 -Versions 2026 -Install   # also copy the package into %AppData%\Autodesk\Revit\Addins\2026
#>
param(
    [string[]] $Versions = @('2023', '2024', '2025', '2026', '2027'),
    [switch] $Install,
    [switch] $SkipTests
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    if (-not $SkipTests) {
        dotnet run --project tests\ScanQa.Core.Tests -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
    }
    foreach ($v in $Versions) {
        $api = Join-Path $env:ProgramW6432 "Autodesk\Revit $v"
        if (-not (Test-Path (Join-Path $api 'RevitAPI.dll'))) { Write-Host "Revit $v not installed - skipped"; continue }
        dotnet build src\ScanQa.Revit -c Release -p:RevitVersion=$v -p:RevitApiDir="$api"
        if ($LASTEXITCODE -ne 0) { throw "Build for Revit $v failed." }
        if ($Install) {
            $dest = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$v"
            New-Item -ItemType Directory -Force -Path $dest | Out-Null
            Copy-Item -Recurse -Force (Join-Path $root "dist\Revit$v\*") $dest
            Write-Host "Installed into $dest"
        }
    }
}
finally { Pop-Location }
