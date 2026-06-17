param(
    [switch]$IncludeVsCommunity
)

$ErrorActionPreference = "Stop"

if ($IncludeVsCommunity) {
    winget install `
        --id Microsoft.VisualStudio.2022.Community `
        --source winget `
        --accept-package-agreements `
        --accept-source-agreements `
        --silent `
        --override "--quiet --wait --norestart --add Microsoft.VisualStudio.Workload.NativeDesktop --add Component.Microsoft.Windows.DriverKit --add Component.Microsoft.Windows.DriverKit.BuildTools --add Microsoft.VisualStudio.Component.Windows11SDK.26100 --add Microsoft.VisualStudio.Component.VC.14.44.17.14.x86.x64.Spectre --includeRecommended"
}

winget install `
    --id Microsoft.WindowsWDK.10.0.26100 `
    --exact `
    --source winget `
    --accept-package-agreements `
    --accept-source-agreements `
    --silent

$vsix = "${env:ProgramFiles(x86)}\Windows Kits\10\Vsix\VS2022\WDK.vsix"
$vsixInstaller = "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\Common7\IDE\VSIXInstaller.exe"

if ((Test-Path $vsix) -and (Test-Path $vsixInstaller)) {
    & $vsixInstaller /quiet /admin $vsix
}
