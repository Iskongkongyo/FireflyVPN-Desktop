# `package-windows.ps1` Windows packaging

The installer wizard uses Simplified Chinese and carries its language definition
in `ChineseSimplified.isl`; no separate Inno Setup language pack is needed on
the build machine.

Installed copies store the install location in the current-user registry so the
client can keep settings, logs, databases, and copied cores in Local AppData.
No `NotStoreConfigHere.txt` marker is included in the installer or application
directory.

## Quick start: x64 portable ZIP

Exit every running Firefly/v2rayN desktop process from its tray icon before
packaging. The script intentionally stops if one is still running, preventing
an old single-instance process from being mistaken for the newly built client
or a portable `guiConfigs` directory from being removed during testing.

```powershell
Set-Location D:\Firefly-Desktop-Push

# Required. Use the real protected API endpoint; do not commit it to source.
$env:FIREFLY_CLIENT_API_URL = 'https://api.example.com'

# Download and verify the pinned sing-box, Xray, and Mihomo cores.
.\scripts\download-cores-windows.ps1 -Architecture x64

# Build the self-contained x64 portable package.
.\package-windows.ps1 -Architecture x64 -PackageFormat portable
```

The ZIP is written to `artifacts\` as `*-win-x64.zip`.

## Installer and all-architecture commands

The Windows installer is built with Inno Setup 6 or 7. Install the compiler
from the [official Inno Setup download page](https://jrsoftware.org/isdl.php)
before requesting `installer` or `all` package formats.

```powershell
# x64 installer only
Set-Location D:\Firefly-Desktop-Push
$env:FIREFLY_CLIENT_API_URL = 'https://api.example.com'
.\scripts\download-cores-windows.ps1 -Architecture x64
.\package-windows.ps1 -Architecture x64 -PackageFormat installer

# Both architectures, producing portable ZIPs and installers
Set-Location D:\Firefly-Desktop-Push
$env:FIREFLY_CLIENT_API_URL = 'https://api.example.com'
.\scripts\download-cores-windows.ps1 -Architecture all
.\package-windows.ps1 -Architecture all -PackageFormat all
```

`-Architecture` accepts `x64`, `x86`, or `all` (default: `x64`).
`-PackageFormat` accepts `portable`, `installer`, or `all` (default:
`portable`). The script performs the necessary RID restore unless `-NoRestore`
is supplied and matching restore assets are already available.

The `all` command writes four packages to `artifacts\`: x64/x86 portable ZIPs
and installers. Installer outputs use these names:

- `流萤加速器-Setup-win-x64.exe`
- `流萤加速器-Setup-win-x86.exe`

The installer allows choosing the destination directory, selects the desktop
shortcut task by default, creates a Start menu shortcut and uninstall entry,
and optionally starts the application after setup. Installed editions keep
configuration and logs in the current user's local application-data directory;
uninstalling does not delete that user data.

Release installers are not code-signed by this script. Sign the final Setup EXE
with the project's trusted Authenticode certificate before public distribution.

## Prerequisites and custom cores

- Use Windows PowerShell 5.1 or PowerShell 7 and install the .NET 10 SDK.
- The first package run needs access to NuGet and GitHub Releases.
- If local scripts are blocked, run
  `Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass` in the current
  PowerShell session.
- The packaging script does not download cores itself. Run
  `scripts\download-cores-windows.ps1` first, or provide a reviewed core
  directory with `-CoreDirectory` (single architecture),
  `-CoreDirectoryX64`, or `-CoreDirectoryX86`.

A custom core directory must contain:

```text
sing_box\sing-box.exe
xray\xray.exe
mihomo\mihomo.exe
```
