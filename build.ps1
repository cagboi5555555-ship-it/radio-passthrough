<#
  Builds a release: runs the tests, publishes the self-contained single-file app and writes
  dist\RadioPassthrough-Setup-<version>.exe with a SHA-256 checksum next to it.

    powershell -ExecutionPolicy Bypass -File .\build.ps1 [-SkipTests]
#>
param([switch]$SkipTests, [switch]$SkipSelfTest)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

[xml]$project = Get-Content 'src\RadioPassthrough\RadioPassthrough.csproj'
$version = ($project.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw 'No <Version> in RadioPassthrough.csproj' }
Write-Host "Radio Passthrough $version"

if (-not $SkipTests) {
    dotnet test --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
}

if (Test-Path publish) { Remove-Item publish -Recurse -Force }
dotnet publish src\RadioPassthrough -c Release -o publish --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }

New-Item dist -ItemType Directory -Force | Out-Null
$setup = Join-Path dist "RadioPassthrough-Setup-$version.exe"
Copy-Item publish\RadioPassthrough.exe $setup -Force
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "$setup.sha256" -Value "$hash  $(Split-Path $setup -Leaf)" -Encoding ascii
Copy-Item THIRD-PARTY-NOTICES.md dist -Force

# Run the finished file itself: catches packaging problems that tests on the build output can't
# (e.g. a native DLL missing from the single file). The GitHub build runs it too.
if (-not $SkipSelfTest) {
    $result = Join-Path $env:TEMP "radiopassthrough-selftest.txt"
    $p = Start-Process (Resolve-Path $setup) -ArgumentList "--selftest `"$result`"" -PassThru -WindowStyle Hidden
    if (-not $p.WaitForExit(120000)) { $p.Kill(); throw 'Self-test timed out' }
    Get-Content $result | Write-Host
    if ($p.ExitCode -ne 0) { throw "Self-test failed (exit $($p.ExitCode))" }
}

$size = [math]::Round((Get-Item $setup).Length / 1MB, 1)
Write-Host "Built $setup ($size MB)"
Write-Host "SHA-256 $hash"
