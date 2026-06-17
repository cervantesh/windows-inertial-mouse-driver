# Windows Inertial Mouse Driver

A Windows KMDF HID mouse filter driver that turns physical mouse movement into
inertial cursor motion. Moving the mouse adds velocity; when the mouse stops,
the cursor keeps moving and slows down by friction until it settles.

The active implementation lives in `src/inertialmouse`. The installer lives in
`src/installer` and is packaged as a single executable.

Project page:

```text
https://cervantesh.github.io/windows-inertial-mouse-driver/
```

Latest release:

```text
https://github.com/cervantesh/windows-inertial-mouse-driver/releases/tag/v1.0.0
```

## Status

This project is for local driver development and testing. The driver uses a test
certificate, so Windows test-signing is required unless the package is signed
through a production driver-signing flow.

## Features

- Kernel-mode HID mouse upper-filter driver.
- Physical relative mouse movement adds to persistent virtual velocity.
- KMDF timer applies friction and emits continued cursor movement.
- Per-device registry configuration seeded by the INF.
- Traditional Windows setup wizard for install, uninstall, and test-signing
  cleanup.
- Small single-file installer bootstrapper with .NET 10 Desktop Runtime support.

## Build

Install the current Visual Studio 2022 + WDK toolchain:

```powershell
.\scripts\install-modern-toolchain.ps1 -IncludeVsCommunity
```

Build the driver:

```powershell
.\scripts\build-inertialmouse.ps1
```

Build the single-file installer:

```powershell
.\scripts\build-installer.ps1
```

The generated installer is:

```text
dist\InertialMouseInstaller.exe
```

## Install For Testing

Run the installer as Administrator:

```text
dist\InertialMouseInstaller.exe
```

The wizard detects connected HID mice, recommends the active mouse, installs the
filter, and registers a normal Windows uninstall entry.

After installation, restart Windows before testing the mouse.

## Uninstall

Use the Windows uninstall entry or run:

```powershell
.\scripts\uninstall-inertialmouse.ps1
```

To also disable test-signing:

```powershell
.\scripts\uninstall-inertialmouse.ps1 -DisableTestSigning
```

## Configuration

The INF seeds these DWORD values under the device `Device Parameters` key:

| Value | Default | Meaning |
| --- | ---: | --- |
| `InertialEnabled` | `1` | Enables or disables the filter. |
| `TimerPeriodMs` | `8` | Friction timer period. |
| `AccelXQ8` / `AccelYQ8` | `256` | Acceleration scale in Q8 fixed point. |
| `FrictionXQ8` / `FrictionYQ8` | `20` | Velocity decay per timer tick. |
| `MaxVelocityXQ8` / `MaxVelocityYQ8` | `10240` | Velocity clamp in Q8 fixed point. |
