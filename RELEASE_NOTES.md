# Release v1.0.0

Initial public release of Windows Inertial Mouse Driver.

## Highlights

- KMDF HID mouse upper-filter driver for inertial cursor motion.
- Relative mouse movement contributes to persistent virtual velocity.
- Friction timer slows motion until the cursor settles.
- Registry-backed tuning defaults for acceleration, friction, timer period, and velocity clamp.
- Traditional Windows setup wizard for install and uninstall.
- Single-file installer bootstrapper with .NET 10 Desktop Runtime detection.
- Windows uninstall entry registration after successful install.

## Installer

Primary artifact:

```text
InertialMouseInstaller.exe
```

The installer is intended for local testing and requires Administrator
privileges. Restart Windows after installation.

## Driver Signing Note

This release uses a test-signed package for local experimentation. Windows
test-signing is required unless the driver is signed through a production
driver-signing flow.
