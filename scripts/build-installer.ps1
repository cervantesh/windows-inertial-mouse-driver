param(
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$repoRootPath = [System.IO.Path]::GetFullPath($repoRoot.Path)
$project = Join-Path $repoRootPath "src\installer\InertialMouseInstaller.csproj"
$localDotnet = Join-Path $repoRootPath ".dotnet10\dotnet.exe"
$payloadRoot = Join-Path $repoRootPath "src\installer\Payload"
$publishDir = Join-Path $repoRootPath "src\installer\publish"
$bootstrapBuildDir = Join-Path $repoRootPath "src\bootstrapper\build"
$distRoot = Join-Path $repoRootPath "dist"
$singleFileName = if ($SelfContained) { "InertialMouseInstaller-full.exe" } else { "InertialMouseInstaller.exe" }
$singleFile = Join-Path $distRoot $singleFileName

function Test-DotNet10Sdk {
    param([string]$DotnetPath)

    try {
        $sdks = & $DotnetPath --list-sdks 2>$null
        return ($sdks -match "^10\.")
    } catch {
        return $false
    }
}

function Install-LocalDotNet10Sdk {
    $installDir = Join-Path $repoRootPath ".dotnet10"
    $installScript = Join-Path $env:TEMP "dotnet-install.ps1"

    Write-Host "Installing local .NET 10 SDK into $installDir..."
    Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $installScript
    & $installScript -Channel 10.0 -InstallDir $installDir -Architecture x64
}

if (Test-Path -LiteralPath $localDotnet) {
    $dotnetExe = $localDotnet
} elseif (Test-DotNet10Sdk "dotnet") {
    $dotnetExe = "dotnet"
} else {
    Install-LocalDotNet10Sdk
    $dotnetExe = $localDotnet
}

foreach ($path in @($payloadRoot, $publishDir, $bootstrapBuildDir, $distRoot)) {
    $fullPath = [System.IO.Path]::GetFullPath($path)
    if (-not $fullPath.StartsWith($repoRootPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to write outside the repository: $fullPath"
    }
}

if (Test-Path -LiteralPath $payloadRoot) {
    Remove-Item -LiteralPath $payloadRoot -Recurse -Force
}

if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

if (Test-Path -LiteralPath $bootstrapBuildDir) {
    Remove-Item -LiteralPath $bootstrapBuildDir -Recurse -Force
}

if (Test-Path -LiteralPath (Join-Path $distRoot "InertialMouseInstaller")) {
    Remove-Item -LiteralPath (Join-Path $distRoot "InertialMouseInstaller") -Recurse -Force
}

New-Item -ItemType Directory -Path (Join-Path $payloadRoot "scripts") | Out-Null
New-Item -ItemType Directory -Path (Join-Path $payloadRoot "src\inertialmouse") | Out-Null
New-Item -ItemType Directory -Path (Join-Path $payloadRoot "docs") | Out-Null

Copy-Item -LiteralPath (Join-Path $repoRootPath "scripts\build-inertialmouse.ps1") -Destination (Join-Path $payloadRoot "scripts")
Copy-Item -LiteralPath (Join-Path $repoRootPath "scripts\install-inertialmouse.ps1") -Destination (Join-Path $payloadRoot "scripts")
Copy-Item -LiteralPath (Join-Path $repoRootPath "scripts\install-modern-toolchain.ps1") -Destination (Join-Path $payloadRoot "scripts")
Copy-Item -LiteralPath (Join-Path $repoRootPath "scripts\uninstall-inertialmouse.ps1") -Destination (Join-Path $payloadRoot "scripts")

$driverFiles = @(
    "config.c",
    "config.h",
    "driver.c",
    "filter.c",
    "filter.h",
    "inertialmouse.h",
    "inertialmouse.inf",
    "inertialmouse.sln",
    "inertialmouse.vcxproj",
    "inertialmouse.vcxproj.filters",
    "motion.c",
    "motion.h",
    "README.md"
)

foreach ($file in $driverFiles) {
    Copy-Item `
        -LiteralPath (Join-Path $repoRootPath "src\inertialmouse\$file") `
        -Destination (Join-Path $payloadRoot "src\inertialmouse")
}

Copy-Item `
    -LiteralPath (Join-Path $repoRootPath "docs\inertial-mouse-driver.md") `
    -Destination (Join-Path $payloadRoot "docs")

if (-not (Test-Path -LiteralPath $distRoot)) {
    New-Item -ItemType Directory -Path $distRoot | Out-Null
}

New-Item -ItemType Directory -Path $bootstrapBuildDir | Out-Null

$publishArgs = @(
    "publish",
    $project,
    "-c", "Release",
    "-r", "win-x64",
    "-o", $publishDir,
    "/p:PublishSingleFile=true",
    "/p:DebugType=none",
    "/p:DebugSymbols=false"
)

if ($SelfContained) {
    $publishArgs += "--self-contained"
    $publishArgs += "true"
} else {
    $publishArgs += "--self-contained"
    $publishArgs += "false"
}

& $dotnetExe @publishArgs

$publishedExe = Join-Path $publishDir "InertialMouseInstaller.exe"
if (-not (Test-Path -LiteralPath $publishedExe)) {
    throw "Published installer was not found: $publishedExe"
}

if ($SelfContained) {
    Copy-Item -LiteralPath $publishedExe -Destination $singleFile -Force
} else {
    $managedExe = Join-Path $bootstrapBuildDir "InertialMouseInstaller.Managed.exe"
    Copy-Item -LiteralPath $publishedExe -Destination $managedExe -Force

    $resourceFile = Join-Path $bootstrapBuildDir "bootstrapper.rc"
    $resourceObj = Join-Path $bootstrapBuildDir "bootstrapper.res"
    $bootstrapObj = Join-Path $bootstrapBuildDir "bootstrapper.obj"

@"
#include "..\resource.h"
IDR_MANAGED_EXE BIN "$($managedExe.Replace('\', '\\'))"
"@ | Set-Content -LiteralPath $resourceFile -NoNewline

    $vcTools = Get-ChildItem "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\VC\Tools\MSVC" -Directory |
        Sort-Object Name -Descending |
        Select-Object -First 1

    if (-not $vcTools) {
        throw "Visual C++ tools were not found."
    }

    $sdkRoot = "${env:ProgramFiles(x86)}\Windows Kits\10"
    $sdkVersion = Get-ChildItem (Join-Path $sdkRoot "Include") -Directory |
        Where-Object { $_.Name -match '^10\.' } |
        Sort-Object Name -Descending |
        Select-Object -First 1

    if (-not $sdkVersion) {
        throw "Windows SDK include directory was not found."
    }

    $cl = Join-Path $vcTools.FullName "bin\Hostx64\x64\cl.exe"
    $rc = Join-Path $sdkRoot "bin\$($sdkVersion.Name)\x64\rc.exe"
    $source = Join-Path $repoRootPath "src\bootstrapper\bootstrapper.cpp"

    & $rc `
        /nologo `
        /I (Join-Path $repoRootPath "src\bootstrapper") `
        /fo $resourceObj `
        $resourceFile

    if ($LASTEXITCODE -ne 0) {
        throw "Resource compilation failed."
    }

    & $cl `
        /nologo `
        /std:c++17 `
        /EHsc `
        /O2 `
        /MT `
        /DUNICODE `
        /D_UNICODE `
        /I (Join-Path $vcTools.FullName "include") `
        /I (Join-Path $sdkRoot "Include\$($sdkVersion.Name)\ucrt") `
        /I (Join-Path $sdkRoot "Include\$($sdkVersion.Name)\shared") `
        /I (Join-Path $sdkRoot "Include\$($sdkVersion.Name)\um") `
        /Fo$bootstrapObj `
        $source `
        $resourceObj `
        /link `
        /SUBSYSTEM:WINDOWS `
        /OUT:$singleFile `
        "/LIBPATH:$(Join-Path $vcTools.FullName "lib\x64")" `
        "/LIBPATH:$(Join-Path $sdkRoot "Lib\$($sdkVersion.Name)\ucrt\x64")" `
        "/LIBPATH:$(Join-Path $sdkRoot "Lib\$($sdkVersion.Name)\um\x64")" `
        user32.lib shell32.lib advapi32.lib

    if ($LASTEXITCODE -ne 0) {
        throw "Bootstrapper compilation failed."
    }
}

Write-Host ""
Write-Host "Single-file installer created:"
Write-Host $singleFile
