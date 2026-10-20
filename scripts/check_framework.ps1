$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$runtime = Join-Path $root '.framework-runtime'
$work = Join-Path $root '.framework-build'
New-Item -ItemType Directory -Force $work | Out-Null
python scripts/prepare_native_check.py
if ($LASTEXITCODE -ne 0) { throw 'Unable to generate inline variants' }
$sdk = ((dotnet --list-sdks | Select-Object -Last 1) -split ' ')[0]
$sdkRoot = ((dotnet --list-sdks | Select-Object -Last 1) -split '\[')[1].TrimEnd(']')
$csc = Join-Path $sdkRoot "$sdk/Roslyn/bincore/csc.dll"
$core = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/mscorlib.dll'
$api = Join-Path $runtime 'Streamer.bot.Plugin.Interface.dll'
foreach ($phase in @('fetch', 'save')) {
    dotnet $csc /nologo /noconfig /nostdlib+ /target:library /langversion:7.3 "/reference:$core" "/reference:$api" "/out:$work/$phase.dll" "$work/$phase.cs" "$root/tests/FrameworkCompatibility/ApiShim.cs"
    if ($LASTEXITCODE -ne 0) { throw "$phase failed to compile against the native core/API reference set" }
    dotnet $csc /nologo /noconfig /nostdlib+ /target:library /langversion:7.3 "/reference:$core" "/reference:$api" "/out:$work/$phase-wrapped.dll" "$work/$phase-wrapped.cs" "$work/wrapped-shim.cs"
    if ($LASTEXITCODE -ne 0) { throw "$phase failed after native namespace wrapping" }
}
dotnet build tests/FrameworkCompatibility --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Framework compatibility harness failed to build' }
& "$root/tests/FrameworkCompatibility/bin/Release/net481/FrameworkCompatibility.exe"
if ($LASTEXITCODE -ne 0) { throw 'Framework adapter checks failed' }
