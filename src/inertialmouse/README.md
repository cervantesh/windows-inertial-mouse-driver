# Inertial Mouse Filter

Modern KMDF HID mouse upper-filter driver. Physical mouse deltas are treated as
acceleration. The driver keeps a virtual velocity and applies friction on a
timer until motion decays to zero.

No screen warp is implemented here. Screen bounds, DPI, virtual desktops, and
multi-monitor geometry belong to user-mode APIs, and this project stays
kernel-only.

## Files

- `driver.c`: KMDF entry points, device setup, and internal IOCTL routing.
- `filter.c` / `filter.h`: mouse service callback and friction timer callback.
- `motion.c` / `motion.h`: fixed-point acceleration/friction model.
- `config.c` / `config.h`: registry-backed configuration.
- `inertialmouse.inf`: HID install template and configuration defaults.

## Modern Toolchain

Use Visual Studio Community 2022 plus WDK 10.0.26100. The 28000 WDK package
appears in `winget`, but 26100 is the safer current stable target.

```powershell
..\..\scripts\install-modern-toolchain.ps1 -IncludeVsCommunity
```

After installing the WDK, install the VS2022 WDK extension if the WDK setup did
not do it automatically:

```powershell
& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\Community\Common7\IDE\VSIXInstaller.exe" /quiet /admin "${env:ProgramFiles(x86)}\Windows Kits\10\Vsix\VS2022\WDK.vsix"
```

Then build:

```powershell
..\..\scripts\build-inertialmouse.ps1
```
