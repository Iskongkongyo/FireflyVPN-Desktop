# Windows installer packaging

The installer wizard uses Simplified Chinese and carries its language definition
in `ChineseSimplified.isl`; no separate Inno Setup language pack is needed on
the build machine.

Installed copies store the install location in the current-user registry so the
client can keep settings, logs, databases, and copied cores in Local AppData.
No `NotStoreConfigHere.txt` marker is included in the installer or application
directory.

Before packaging the x86 edition, stage all three bundled cores:

```powershell
.\sync-singbox-windows.ps1 -Architecture x86
.\sync-windows-x86-cores.ps1
```

The second command obtains the current stable Windows x86 archives for Xray and
Mihomo from their official GitHub releases, checks the publisher-provided
SHA-256 digest, and places them in the same `bin\xray` and `bin\mihomo`
locations used by the application.

The Windows installer is built with Inno Setup 6 or 7. Install the compiler
from the [official Inno Setup download page](https://jrsoftware.org/isdl.php),
then run the repository packaging script from PowerShell.

```powershell
# x64 installer only
.\package-windows.ps1 -Architecture x64 -PackageFormat installer

# x86 installer only
.\package-windows.ps1 -Architecture x86 -PackageFormat installer

# Both architectures, producing portable ZIPs and installers
.\package-windows.ps1 -Architecture all -PackageFormat all
```

`-PackageFormat portable` preserves the previous ZIP-only behavior and remains
the default. Installer outputs are written to `artifacts/` as:

- `流萤加速器-Setup-win-x64.exe`
- `流萤加速器-Setup-win-x86.exe`

The installer allows choosing the destination directory, selects the desktop
shortcut task by default, creates a Start menu shortcut and uninstall entry,
and optionally starts the application after setup. Installed editions keep
configuration and logs in the current user's local application-data directory;
uninstalling does not delete that user data.

Release installers are not code-signed by this script. Sign the final Setup EXE
with the project's trusted Authenticode certificate before public distribution.
