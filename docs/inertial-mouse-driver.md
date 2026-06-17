# Inertial Mouse Driver Plan

The active implementation is `src/inertialmouse`.

This is a clean KMDF HID mouse upper-filter driver. The active code removes the
legacy PS/2 ISR path and keeps the project structured for a current Visual
Studio 2022 + WDK 26100 toolchain.

## Scope

- Kernel-only mouse motion filter.
- Physical relative mouse movement adds to virtual cursor velocity.
- A KMDF timer emits continued relative motion while friction decays velocity.
- Configuration is read from the device registry values seeded by the INF.
- No screen-edge warp, because screen geometry is user-mode state.

## Configuration

The INF seeds these DWORD values under `Device Parameters`:

| Value | Default | Meaning |
| --- | ---: | --- |
| `InertialEnabled` | `1` | Enables or disables the inertial filter. |
| `TimerPeriodMs` | `8` | Friction timer period. |
| `AccelXQ8` / `AccelYQ8` | `256` | Acceleration scale. Q8 fixed point, so 256 means 1.0. |
| `FrictionXQ8` / `FrictionYQ8` | `20` | Velocity decay per timer tick. |
| `MaxVelocityXQ8` / `MaxVelocityYQ8` | `10240` | Velocity clamp. Q8 value for 40.0. |

## Install Toolchain

Use the stable current WDK target:

```powershell
.\scripts\install-modern-toolchain.ps1 -IncludeVsCommunity
```

If needed, install the WDK Visual Studio extension manually:

```powershell
& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\Community\Common7\IDE\VSIXInstaller.exe" /quiet /admin "${env:ProgramFiles(x86)}\Windows Kits\10\Vsix\VS2022\WDK.vsix"
```

Build:

```powershell
.\scripts\build-inertialmouse.ps1
```

Build the installer bundle:

```powershell
.\scripts\build-installer.ps1
```

The generated single-file executable is a small native bootstrapper:

```text
dist\InertialMouseInstaller.exe
```

If the .NET 10 Desktop Runtime is missing, the bootstrapper offers to install it
with:

```powershell
winget install --id Microsoft.DotNet.DesktopRuntime.10 --exact
```

To build the larger self-contained variant:

```powershell
.\scripts\build-installer.ps1 -SelfContained
```

That writes:

```text
dist\InertialMouseInstaller-full.exe
```

## Install For Local Testing

Preferred path: run `dist\InertialMouseInstaller.exe` and use the GUI.

After a successful install, the executable registers `Inertial Mouse Filter
Driver` in Windows uninstall entries. That entry runs the same executable with
the `uninstall` command.

Script path: open PowerShell as Administrator.

List the connected HID mouse hardware IDs:

```powershell
.\scripts\install-inertialmouse.ps1 -ListMice
```

Install using the interactive selector:

```powershell
.\scripts\install-inertialmouse.ps1 -EnableTestSigning
```

Or install directly with a known hardware ID:

```powershell
.\scripts\install-inertialmouse.ps1 -HardwareId "HID\VID_046D&PID_C548&REV_0503&MI_01&Col01" -EnableTestSigning
```

Reboot after the install. If Windows reports that Secure Boot blocks test
signing, disable Secure Boot in firmware settings for local testing.

Uninstall:

```powershell
.\scripts\uninstall-inertialmouse.ps1
```

To also turn test-signing back off:

```powershell
.\scripts\uninstall-inertialmouse.ps1 -DisableTestSigning
```
