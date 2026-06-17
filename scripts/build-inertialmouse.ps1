param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$msbuild = "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\amd64\MSBuild.exe"
$solution = Join-Path $PSScriptRoot "..\src\inertialmouse\inertialmouse.sln"

if (-not (Test-Path $msbuild)) {
    throw "MSBuild x64 for Visual Studio 2022 Community was not found. Run scripts\install-modern-toolchain.ps1 -IncludeVsCommunity first."
}

& $msbuild $solution /p:Configuration=$Configuration /p:Platform=x64 /m
