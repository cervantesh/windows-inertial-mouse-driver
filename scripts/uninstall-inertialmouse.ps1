param(
    [string]$PublishedName,
    [switch]$DisableTestSigning,
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

function Find-InertialMousePublishedNames {
    $output = pnputil /enum-drivers | Out-String
    $sections = $output -split "(\r?\n){2,}"
    $names = @()

    foreach ($section in $sections) {
        if ($section -notmatch "inertialmouse\.inf") {
            continue
        }

        $match = [regex]::Match($section, "(?im)^\s*Published Name\s*:\s*(\S+)\s*$")
        if ($match.Success) {
            $names += $match.Groups[1].Value
        }
    }

    return $names | Select-Object -Unique
}

Assert-Administrator

$targets = @()
if ($PublishedName) {
    $targets += $PublishedName
} else {
    $targets += Find-InertialMousePublishedNames
}

if ($targets.Count -eq 0) {
    Write-Host "No installed inertialmouse.inf driver package was found."
} else {
    foreach ($target in $targets) {
        Write-Host "Removing $target..."
        pnputil /delete-driver $target /uninstall /force | Out-Host
    }
}

if ($DisableTestSigning) {
    bcdedit /set testsigning off
    Write-Host "Test-signing was disabled. A reboot is required."
}

if ($Restart) {
    shutdown /r /t 0
} else {
    Write-Host "Reboot Windows if the filter was loaded."
}
