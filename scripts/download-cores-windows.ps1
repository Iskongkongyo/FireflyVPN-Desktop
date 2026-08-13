[CmdletBinding()]
param(
    [ValidateSet('x64', 'x86', 'all')]
    [string]$Architecture = 'all',
    [string]$CoreVersionsPath
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
if (-not $CoreVersionsPath) {
    $CoreVersionsPath = Join-Path $repoRoot 'core-versions.json'
}
if (-not (Test-Path -LiteralPath $CoreVersionsPath)) {
    throw "Core version manifest not found: $CoreVersionsPath"
}

$versions = Get-Content -LiteralPath $CoreVersionsPath -Raw -Encoding UTF8 | ConvertFrom-Json
foreach ($coreName in @('sing-box', 'xray', 'mihomo')) {
    if ([string]::IsNullOrWhiteSpace($versions.$coreName.version)) {
        throw "A fixed version is required for $coreName in $CoreVersionsPath"
    }
}

$headers = @{
    Accept = 'application/vnd.github+json'
    'User-Agent' = 'FireflyVPN-Desktop'
    'X-GitHub-Api-Version' = '2022-11-28'
}
if ($env:GITHUB_TOKEN) {
    $headers.Authorization = "Bearer $env:GITHUB_TOKEN"
}

function Get-Release {
    param(
        [Parameter(Mandatory)] [string]$Repository,
        [Parameter(Mandatory)] [string]$Version
    )

    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repository/releases/tags/v$Version" -Headers $headers
    if ($release.draft -or $release.prerelease) {
        throw "$Repository v$Version is not a stable public release."
    }

    # GitHub limits the embedded `assets` array to its first page. Current
    # sing-box releases contain more than 100 files, so load every page before
    # selecting a platform archive.
    $assets = [System.Collections.Generic.List[object]]::new()
    $page = 1
    do {
        $pageAssets = Invoke-RestMethod -Uri "$($release.assets_url)?per_page=100&page=$page" -Headers $headers
        foreach ($asset in $pageAssets) {
            $assets.Add($asset)
        }
        $page++
    } while ($pageAssets.Count -eq 100)

    if ($assets.Count -eq 0) {
        throw "$Repository v$Version does not contain any release assets."
    }
    $release | Add-Member -NotePropertyName assets -NotePropertyValue $assets.ToArray() -Force
    return $release
}

function Get-ReleaseAsset {
    param(
        [Parameter(Mandatory)] $Release,
        [Parameter(Mandatory)] [string[]]$Patterns,
        [Parameter(Mandatory)] [string]$Description
    )

    foreach ($pattern in $Patterns) {
        $asset = @($Release.assets | Where-Object { $_.name -match $pattern } | Sort-Object name | Select-Object -First 1)[0]
        if ($null -ne $asset) {
            return $asset
        }
    }
    throw "No official release asset for $Description was found in $($Release.tag_name)."
}

function Get-VerifiedArchive {
    param(
        [Parameter(Mandatory)] $Asset,
        [Parameter(Mandatory)] [string]$TemporaryRoot
    )

    if ([string]::IsNullOrWhiteSpace($Asset.digest) -or -not $Asset.digest.StartsWith('sha256:', [StringComparison]::OrdinalIgnoreCase)) {
        throw "GitHub did not provide a SHA-256 digest for $($Asset.name)."
    }

    $archivePath = Join-Path $TemporaryRoot $Asset.name
    Invoke-WebRequest -Uri $Asset.browser_download_url -Headers $headers -OutFile $archivePath
    $expectedHash = $Asset.digest.Substring('sha256:'.Length).ToLowerInvariant()
    $actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $expectedHash) {
        throw "SHA-256 verification failed for $($Asset.name)."
    }
    return $archivePath
}

function Assert-WindowsExecutableArchitecture {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [ValidateSet('x64', 'x86')] [string]$TargetArchitecture
    )

    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 64 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) {
        throw "Not a Windows PE executable: $Path"
    }
    $peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
    if ($peOffset -lt 0 -or $peOffset + 6 -ge $bytes.Length -or
        $bytes[$peOffset] -ne 0x50 -or $bytes[$peOffset + 1] -ne 0x45 -or
        $bytes[$peOffset + 2] -ne 0 -or $bytes[$peOffset + 3] -ne 0) {
        throw "Invalid PE header: $Path"
    }
    $machine = [BitConverter]::ToUInt16($bytes, $peOffset + 4)
    $expectedMachine = if ($TargetArchitecture -eq 'x64') { 0x8664 } else { 0x014C }
    if ($machine -ne $expectedMachine) {
        throw "Architecture mismatch for $Path. Expected $TargetArchitecture."
    }
}

function Expand-ArchiveToTemporaryDirectory {
    param(
        [Parameter(Mandatory)] [string]$ArchivePath,
        [Parameter(Mandatory)] [string]$TemporaryRoot,
        [Parameter(Mandatory)] [string]$Name
    )

    $destination = Join-Path $TemporaryRoot "extract-$Name"
    Expand-Archive -LiteralPath $ArchivePath -DestinationPath $destination -Force
    return $destination
}

function Copy-OptionalSupportFiles {
    param(
        [Parameter(Mandatory)] [string]$SourceRoot,
        [Parameter(Mandatory)] [string]$Destination,
        [string[]]$Names = @()
    )

    foreach ($name in $Names) {
        $file = Get-ChildItem -LiteralPath $SourceRoot -Recurse -File -Filter $name | Select-Object -First 1
        if ($null -ne $file) {
            Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $Destination $name) -Force
        }
    }
}

function Install-WindowsCores {
    param([Parameter(Mandatory)] [ValidateSet('x64', 'x86')] [string]$TargetArchitecture)

    $assetArchitecture = if ($TargetArchitecture -eq 'x64') { 'amd64' } else { '386' }
    $xrayAssetName = if ($TargetArchitecture -eq 'x64') { 'Xray-windows-64.zip' } else { 'Xray-windows-32.zip' }
    $coreFolder = if ($TargetArchitecture -eq 'x64') { 'v2rayN-windows-64' } else { 'v2rayN-windows-32' }
    $binRoot = Join-Path $repoRoot "artifacts\core-extracted\$coreFolder\bin"
    $temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("FireflyVPN-cores-$TargetArchitecture-" + [Guid]::NewGuid().ToString('N'))

    try {
        New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
        $singDestination = Join-Path $binRoot 'sing_box'
        $xrayDestination = Join-Path $binRoot 'xray'
        $mihomoDestination = Join-Path $binRoot 'mihomo'
        New-Item -ItemType Directory -Force -Path $singDestination, $xrayDestination, $mihomoDestination | Out-Null

        $singRelease = Get-Release 'SagerNet/sing-box' $versions.'sing-box'.version
        $singAsset = Get-ReleaseAsset $singRelease @("^sing-box-$([regex]::Escape($versions.'sing-box'.version))-windows-$assetArchitecture\.zip$") "sing-box Windows $TargetArchitecture"
        $singExtract = Expand-ArchiveToTemporaryDirectory (Get-VerifiedArchive $singAsset $temporaryRoot) $temporaryRoot 'sing-box'
        $singExe = Get-ChildItem -LiteralPath $singExtract -Recurse -File -Filter 'sing-box.exe' | Select-Object -First 1
        if ($null -eq $singExe) { throw "sing-box.exe was not found in $($singAsset.name)." }
        Assert-WindowsExecutableArchitecture $singExe.FullName $TargetArchitecture
        Copy-Item -LiteralPath $singExe.FullName -Destination (Join-Path $singDestination 'sing-box.exe') -Force
        Copy-OptionalSupportFiles $singExtract $singDestination @('libcronet.dll')

        $xrayRelease = Get-Release 'XTLS/Xray-core' $versions.xray.version
        $xrayAsset = Get-ReleaseAsset $xrayRelease @("^$([regex]::Escape($xrayAssetName))$") "Xray Windows $TargetArchitecture"
        $xrayExtract = Expand-ArchiveToTemporaryDirectory (Get-VerifiedArchive $xrayAsset $temporaryRoot) $temporaryRoot 'xray'
        $xrayExe = Get-ChildItem -LiteralPath $xrayExtract -Recurse -File -Filter 'xray.exe' | Select-Object -First 1
        if ($null -eq $xrayExe) { throw "xray.exe was not found in $($xrayAsset.name)." }
        Assert-WindowsExecutableArchitecture $xrayExe.FullName $TargetArchitecture
        Copy-Item -LiteralPath $xrayExe.FullName -Destination (Join-Path $xrayDestination 'xray.exe') -Force
        Copy-OptionalSupportFiles $xrayExtract $xrayDestination @('wintun.dll')
        Copy-OptionalSupportFiles $xrayExtract $binRoot @('geoip.dat', 'geosite.dat')

        $mihomoRelease = Get-Release 'MetaCubeX/mihomo' $versions.mihomo.version
        $mihomoAsset = Get-ReleaseAsset $mihomoRelease @("^mihomo-windows-$assetArchitecture(?:-[A-Za-z0-9]+)*-v$([regex]::Escape($versions.mihomo.version))\.zip$") "Mihomo Windows $TargetArchitecture"
        $mihomoExtract = Expand-ArchiveToTemporaryDirectory (Get-VerifiedArchive $mihomoAsset $temporaryRoot) $temporaryRoot 'mihomo'
        $mihomoExe = Get-ChildItem -LiteralPath $mihomoExtract -Recurse -File -Filter '*.exe' |
            Where-Object { $_.Name -match '^mihomo(?:-windows-[^.]+)?\.exe$' } | Select-Object -First 1
        if ($null -eq $mihomoExe) { throw "Mihomo executable was not found in $($mihomoAsset.name)." }
        Assert-WindowsExecutableArchitecture $mihomoExe.FullName $TargetArchitecture
        Copy-Item -LiteralPath $mihomoExe.FullName -Destination (Join-Path $mihomoDestination 'mihomo.exe') -Force
        Copy-OptionalSupportFiles $mihomoExtract $binRoot @('Country.mmdb')

        foreach ($requiredCore in @(
                (Join-Path $singDestination 'sing-box.exe'),
                (Join-Path $xrayDestination 'xray.exe'),
                (Join-Path $mihomoDestination 'mihomo.exe')
            )) {
            if (-not (Test-Path -LiteralPath $requiredCore)) {
                throw "Required Windows $TargetArchitecture core was not staged: $requiredCore"
            }
        }
        Write-Host "Installed fixed sing-box $($versions.'sing-box'.version), Xray $($versions.xray.version), and Mihomo $($versions.mihomo.version) for Windows $TargetArchitecture."
    }
    finally {
        if (Test-Path -LiteralPath $temporaryRoot) {
            Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
        }
    }
}

$targets = if ($Architecture -eq 'all') { @('x64', 'x86') } else { @($Architecture) }
foreach ($target in $targets) {
    Install-WindowsCores $target
}
