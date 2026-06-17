param(
    [string]$HardwareId,

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [switch]$ListMice,
    [switch]$EnableTestSigning,
    [switch]$NoBuild,
    [switch]$Restart
)

$ErrorActionPreference = "Stop"

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    $adminRole = [Security.Principal.WindowsBuiltInRole]::Administrator

    if (-not $principal.IsInRole($adminRole)) {
        throw "Run this script from an elevated PowerShell window."
    }
}

function Get-MouseHardwareIds {
    $index = 0

    Get-PnpDevice -Class Mouse | ForEach-Object {
        $device = $_
        $property = Get-PnpDeviceProperty `
            -InstanceId $device.InstanceId `
            -KeyName "DEVPKEY_Device_HardwareIds" `
            -ErrorAction SilentlyContinue

        foreach ($id in @($property.Data)) {
            if ($id -like "HID\VID_*&PID_*") {
                $index++
                [PSCustomObject]@{
                    Index = $index
                    Status = $device.Status
                    FriendlyName = $device.FriendlyName
                    HardwareId = $id
                    InstanceId = $device.InstanceId
                }
            }
        }
    }
}

function Select-MouseHardwareId {
    $mice = @(Get-MouseHardwareIds)

    if ($mice.Count -eq 0) {
        throw "No HID mouse hardware IDs were found."
    }

    $mice | Format-Table Index, Status, FriendlyName, HardwareId -AutoSize
    $selection = Read-Host "Type the number of the mouse to filter"
    $selectionNumber = 0

    if (-not [int]::TryParse($selection, [ref]$selectionNumber)) {
        throw "Invalid selection: $selection"
    }

    $selected = $mice | Where-Object { $_.Index -eq $selectionNumber } | Select-Object -First 1
    if (-not $selected) {
        throw "No mouse entry matches selection $selection."
    }

    return $selected.HardwareId
}

function Set-InfHardwareId {
    param(
        [Parameter(Mandatory)]
        [string]$InfPath,

        [Parameter(Mandatory)]
        [string]$TargetHardwareId
    )

    $content = Get-Content -LiteralPath $InfPath -Raw
    $replacement = 'MouseHardwareId="' + $TargetHardwareId + '"'
    $updated = [regex]::Replace($content, 'MouseHardwareId="[^"]+"', $replacement)

    if ($updated -eq $content) {
        throw "Could not find MouseHardwareId in $InfPath."
    }

    Set-Content -LiteralPath $InfPath -Value $updated -NoNewline
}

function Test-TestSigningEnabled {
    $bootConfig = bcdedit /enum "{current}" 2>$null | Out-String
    return ($bootConfig -match "(?im)^\s*testsigning\s+Yes\s*$")
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$infPath = Join-Path $repoRoot "src\inertialmouse\inertialmouse.inf"
$buildScript = Join-Path $repoRoot "scripts\build-inertialmouse.ps1"
$packageDir = Join-Path $repoRoot "src\inertialmouse\x64\$Configuration\inertialmouse"
$packageInf = Join-Path $packageDir "inertialmouse.inf"
$packageCer = Join-Path $packageDir "inertialmouse.cer"
$buildCer = Join-Path $repoRoot "src\inertialmouse\x64\$Configuration\inertialmouse.cer"

if ($ListMice) {
    Get-MouseHardwareIds | Format-Table Index, Status, FriendlyName, HardwareId -AutoSize
    return
}

Assert-Administrator

if (-not $HardwareId) {
    $HardwareId = Select-MouseHardwareId
}

Write-Host "Target mouse HardwareId: $HardwareId"

Set-InfHardwareId -InfPath $infPath -TargetHardwareId $HardwareId

if (-not $NoBuild) {
    & $buildScript -Configuration $Configuration
}

if (-not (Test-Path -LiteralPath $packageInf)) {
    throw "Built package INF was not found: $packageInf"
}

if (-not (Test-Path -LiteralPath $packageCer)) {
    if (Test-Path -LiteralPath $buildCer) {
        $packageCer = $buildCer
    } else {
        throw "Test certificate was not found. Checked:`n$packageCer`n$buildCer"
    }
}

if (-not (Test-TestSigningEnabled)) {
    if (-not $EnableTestSigning) {
        throw "Windows test-signing is not enabled. Re-run with -EnableTestSigning, then reboot after the install."
    }

    bcdedit /set testsigning on
    Write-Host "Test-signing was enabled. A reboot is required before the driver can load."
}

certutil -addstore -f "Root" $packageCer | Out-Host
certutil -addstore -f "TrustedPublisher" $packageCer | Out-Host

pnputil /add-driver $packageInf /install | Out-Host

Write-Host ""
Write-Host "Install command completed. Reboot Windows to load the mouse filter."

if ($Restart) {
    shutdown /r /t 0
}
