# Builds both release artifacts into dist\:
#   VolOsd-<version>-setup.exe     installer (per-user, adds Start Menu + uninstaller)
#   VolOsd-<version>-portable.exe  single exe, run from anywhere
# Both are framework-dependent and need the .NET 8 Desktop Runtime.

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"

# Version lives in the csproj so the two artifacts can never disagree.
[xml]$proj = Get-Content "$root\VolOsd\VolOsd.csproj"
$version = ($proj.Project.PropertyGroup.Version | Where-Object { $_ }) | Select-Object -First 1
if (-not $version) { throw "No <Version> found in VolOsd.csproj" }
Write-Host "Building Vol OSD $version" -ForegroundColor Cyan

Remove-Item "$root\publish", "$root\dist" -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path "$root\dist" -Force | Out-Null

Write-Host "Running tests" -ForegroundColor Cyan
dotnet test "$root\VolOsd.Tests" -c Release
if ($LASTEXITCODE -ne 0) { throw "tests failed - not publishing" }

dotnet publish "$root\VolOsd" -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:DebugType=none -o "$root\publish"
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

Copy-Item "$root\publish\VolOsd.exe" "$root\dist\VolOsd-$version-portable.exe"

if (-not (Test-Path $iscc)) { throw "Inno Setup not found at $iscc" }
& $iscc "/DAppVersion=$version" "$root\installer\VolOsd.iss" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "installer build failed" }

Write-Host "`nDone:" -ForegroundColor Green
Get-ChildItem "$root\dist" | ForEach-Object {
    "{0,-34} {1,8:N0} KB" -f $_.Name, ($_.Length / 1KB)
}
