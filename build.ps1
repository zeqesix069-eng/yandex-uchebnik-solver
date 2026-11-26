$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$outDir = Join-Path $root "build\Release"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path", "User")
$gpp = Get-Command g++ -ErrorAction SilentlyContinue
if (-not $gpp) {
    $gppCandidate = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\BrechtSanders.WinLibs*\mingw64\bin\g++.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($gppCandidate) {
        $env:Path = $gppCandidate.DirectoryName + ";" + $env:Path
        $gpp = $gppCandidate
    }
}
if (-not $gpp) {
    throw "g++.exe not found."
}

$dllPath = Join-Path $outDir "SolverCore.dll"
& g++ -std=c++20 -O2 -shared -static (Join-Path $root "SolverCore\SolverCore.cpp") -lwinhttp -o $dllPath

$msbuild = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe"
& $msbuild (Join-Path $root "YandexSolver\YandexSolver.csproj") /p:Configuration=Release /p:Platform=x64 /nologo /v:minimal

Write-Host "Built: $dllPath"
Write-Host "Built: $(Join-Path $outDir 'YandexSolver.exe')"
