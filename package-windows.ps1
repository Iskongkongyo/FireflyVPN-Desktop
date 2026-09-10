[CmdletBinding()]
param(
    [ValidateSet('x64', 'x86', 'all')]
    [string]$Architecture = 'x64',
    [ValidateSet('portable', 'installer', 'all')]
    [string]$PackageFormat = 'portable',
    [string]$CoreDirectory,
    [string]$CoreDirectoryX64,
    [string]$CoreDirectoryX86,
    [switch]$NoRestore,
    [switch]$SkipBackendCheck,
    [switch]$ValidateOnly
)

$ErrorActionPreference = 'Stop'

function Get-FireflyWorkerBaseUrl {
    $endpoint = $null
    if ([string]::IsNullOrWhiteSpace($env:FIREFLY_CLIENT_API_URL) -or
        -not [Uri]::TryCreate($env:FIREFLY_CLIENT_API_URL, [UriKind]::Absolute, [ref]$endpoint) -or
        $endpoint.Scheme -ne 'https' -or
        [string]::IsNullOrWhiteSpace($endpoint.Host) -or
        -not [string]::IsNullOrWhiteSpace($endpoint.UserInfo) -or
        -not [string]::IsNullOrWhiteSpace($endpoint.Query) -or
        -not [string]::IsNullOrWhiteSpace($endpoint.Fragment)) {
        throw 'FIREFLY_CLIENT_API_URL must be an absolute HTTPS Worker base URL without credentials, query parameters, or a fragment.'
    }

    $path = $endpoint.AbsolutePath.TrimEnd('/')
    if (-not [string]::IsNullOrEmpty($path)) {
        throw 'FIREFLY_CLIENT_API_URL must be the Worker base URL, for example https://api.example.com. Do not append /api/client or /api/v2/bootstrap.'
    }

    return $endpoint.GetLeftPart([UriPartial]::Authority).TrimEnd('/')
}

function Test-FireflyWorkerContract {
    param([Parameter(Mandatory = $true)][string]$WorkerBaseUrl)

    $bootstrapUrl = "$WorkerBaseUrl/api/v2/bootstrap"
    try {
        $response = Invoke-WebRequest `
            -Uri $bootstrapUrl `
            -Method Get `
            -Headers @{
                Accept = 'application/json'
                'User-Agent' = 'FireflyVPN-Packager/2.0'
            } `
            -UseBasicParsing `
            -TimeoutSec 20
        $payload = $response.Content | ConvertFrom-Json
    }
    catch {
        $statusCode = $null
        if ($null -ne $_.Exception.Response -and
            $null -ne $_.Exception.Response.StatusCode) {
            $statusCode = [int]$_.Exception.Response.StatusCode
        }

        # Cloudflare or another edge policy may reject anonymous requests from
        # GitHub-hosted runner IPs even though normal clients can reach the
        # public bootstrap endpoint. Authentication failures still prove that
        # the configured HTTPS origin and route are reachable; the application
        # performs the authenticated contract validation at runtime.
        if ($statusCode -eq 401 -or $statusCode -eq 403) {
            Write-Warning "Firefly Worker rejected the anonymous build preflight with HTTP $statusCode. The endpoint is reachable; continuing the package build."
            return
        }

        throw "Firefly Worker preflight failed. Confirm that the backend is deployed and /api/v2/bootstrap is reachable. $($_.Exception.Message)"
    }

    if ($payload.ok -ne $true -or
        $null -eq $payload.data -or
        $null -eq $payload.data.crypto -or
        [int]$payload.data.crypto.version -ne 2 -or
        [string]$payload.data.crypto.algorithm -ne 'P256-HKDF-SHA256-A256GCM') {
        throw 'Firefly Worker preflight returned an incompatible response. Crypto V2 is required.'
    }

    Write-Host 'Firefly Worker Crypto V2 preflight passed.'
}

$fireflyWorkerBaseUrl = Get-FireflyWorkerBaseUrl
# Normalize the value inherited by MSBuild and make the effective release
# setting explicit in dotnet arguments below.
$env:FIREFLY_CLIENT_API_URL = $fireflyWorkerBaseUrl
if (-not $SkipBackendCheck) {
    Test-FireflyWorkerContract -WorkerBaseUrl $fireflyWorkerBaseUrl
}
if ($ValidateOnly) {
    Write-Host 'Windows packaging configuration is valid.'
    return
}

$repoRoot = Split-Path -Parent $PSCommandPath
$project = Join-Path $repoRoot 'Firefly\Firefly.Desktop\Firefly.Desktop.csproj'
$installerScript = Join-Path $repoRoot 'installer\FireflyVPN.iss'
$artifacts = Join-Path $repoRoot 'artifacts'
# Keep the script compatible with Windows PowerShell 5, which can interpret a
# UTF-8 script without BOM using the local ANSI code page. Construct the
# product name from Unicode code points so paths are stable in either host.
$productName = [char[]](0x6D41, 0x8424, 0x52A0, 0x901F, 0x5668) -join ''
$versionFile = Join-Path $repoRoot 'Firefly\Directory.Build.props'
$versionMatch = [regex]::Match((Get-Content -LiteralPath $versionFile -Raw), '<Version>([^<]+)</Version>')
if (-not $versionMatch.Success) {
    throw "Application version was not found in: $versionFile"
}
$appVersion = $versionMatch.Groups[1].Value.Trim()

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet SDK was not found. Install the .NET 10 SDK, then run this script again.'
}
if (-not (Test-Path -LiteralPath $project)) {
    throw "Desktop project not found: $project"
}
if ($PackageFormat -ne 'portable' -and -not (Test-Path -LiteralPath $installerScript)) {
    throw "Inno Setup script not found: $installerScript"
}
if ($Architecture -eq 'all' -and $CoreDirectory) {
    throw '-CoreDirectory is only valid when packaging one architecture. Use -CoreDirectoryX64 and/or -CoreDirectoryX86 with -Architecture all.'
}

function Find-InnoSetupCompiler {
    $command = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $candidates = @(
        (Join-Path ${env:LOCALAPPDATA} 'Programs\Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:LOCALAPPDATA} 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles} 'Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles} 'Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }

    if (@($candidates).Count -gt 0) {
        return @($candidates)[0]
    }

    throw 'Inno Setup compiler was not found. Install Inno Setup 6/7, then rerun with -PackageFormat installer or all.'
}

function Get-PackageSpecification {
    param([ValidateSet('x64', 'x86')][string]$TargetArchitecture)

    if ($TargetArchitecture -eq 'x64') {
        return [pscustomobject]@{
            Architecture = 'x64'
            RuntimeIdentifier = 'win-x64'
            CoreFolder = 'v2rayN-windows-64'
            CoreArchive = 'v2rayN-core-win-x64.zip'
            ExplicitCoreDirectory = $CoreDirectoryX64
        }
    }

    return [pscustomobject]@{
        Architecture = 'x86'
        RuntimeIdentifier = 'win-x86'
        CoreFolder = 'v2rayN-windows-32'
        CoreArchive = 'v2rayN-core-win-x86.zip'
        ExplicitCoreDirectory = $CoreDirectoryX86
    }
}

function Resolve-CoreDirectory {
    param($Specification)

    if ($Specification.ExplicitCoreDirectory) {
        return $Specification.ExplicitCoreDirectory
    }
    if ($Architecture -ne 'all' -and $CoreDirectory) {
        return $CoreDirectory
    }
    return Join-Path $artifacts ("core-extracted\{0}\bin" -f $Specification.CoreFolder)
}

function Invoke-WindowsPackage {
    param($Specification)

    $rid = $Specification.RuntimeIdentifier
    $publishDirectory = Join-Path $artifacts ("{0}-win-{1}" -f $productName, $Specification.Architecture)
    $zipPath = Join-Path $artifacts ("{0}-win-{1}.zip" -f $productName, $Specification.Architecture)
    $installerPath = Join-Path $artifacts ("{0}-Setup-win-{1}.exe" -f $productName, $Specification.Architecture)
    $coreArchive = Join-Path $artifacts $Specification.CoreArchive
    $coreDirectory = Resolve-CoreDirectory $Specification

    # A portable build stores guiConfigs beside the executable. Rebuilding the
    # same output while that app is still running can erase the test database,
    # while Windows single-instance handling continues to foreground the old
    # process. Fail early so a newly packaged binary is actually the one tested.
    $runningDesktopProcesses = @(Get-Process -Name 'Firefly' -ErrorAction SilentlyContinue)
    if ($runningDesktopProcesses.Count -gt 0) {
        throw 'A Firefly desktop process is still running. Exit it from the tray before packaging, then run this script again.'
    }

    # The core archive is deliberately not downloaded by this script. Put the
    # reviewed architecture-matched archive in artifacts\ or supply a core
    # directory explicitly. For x86, sync-singbox-windows.ps1 and
    # sync-windows-x86-cores.ps1 populate the required core layout.
    if (-not (Test-Path -LiteralPath $coreDirectory) -and (Test-Path -LiteralPath $coreArchive)) {
        $extractRoot = Split-Path -Parent (Split-Path -Parent $coreDirectory)
        if (-not (Test-Path -LiteralPath $extractRoot)) {
            New-Item -ItemType Directory -Path $extractRoot | Out-Null
        }
        Write-Host "Extracting $($Specification.Architecture) core archive to $extractRoot"
        Expand-Archive -LiteralPath $coreArchive -DestinationPath $extractRoot -Force
    }
    if (-not (Test-Path -LiteralPath $coreDirectory)) {
        throw "Core directory not found for $($Specification.Architecture): $coreDirectory`nProvide an architecture-matched core directory or place $($Specification.CoreArchive) in artifacts\."
    }
    $requiredCoreFiles = @(
        'sing_box\sing-box.exe',
        'xray\xray.exe',
        'mihomo\mihomo.exe'
    )
    foreach ($requiredCoreFile in $requiredCoreFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $coreDirectory $requiredCoreFile))) {
            throw "$requiredCoreFile was not found below the $($Specification.Architecture) core directory: $coreDirectory"
        }
    }

    # Publishing for a runtime identifier needs a matching RID target in
    # NuGet's asset file. A normal desktop build does not create that target,
    # so verify it before publish. If -NoRestore is used but the target is
    # absent, a required restore still runs rather than failing with NETSDK1047.
    $assetsFile = Join-Path $repoRoot 'Firefly\Firefly.Desktop\obj\project.assets.json'
    $targetMoniker = "net10.0/$rid"
    $hasRuntimeAssets = (Test-Path -LiteralPath $assetsFile) -and
                        (Select-String -LiteralPath $assetsFile -SimpleMatch $targetMoniker -Quiet)
    if (-not $NoRestore -or -not $hasRuntimeAssets) {
        if ($NoRestore -and -not $hasRuntimeAssets) {
            Write-Host "$rid restore assets are missing; running the required restore."
        }
        else {
            Write-Host "Restoring $rid publish assets..."
        }
        & dotnet restore $project -r $rid "-p:FireflyClientApiUrl=$fireflyWorkerBaseUrl"
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet restore for $rid failed with exit code $LASTEXITCODE"
        }
    }

    if (Test-Path -LiteralPath $publishDirectory) {
        Remove-Item -LiteralPath $publishDirectory -Recurse -Force
    }
    if ($PackageFormat -ne 'installer' -and (Test-Path -LiteralPath $zipPath)) {
        Remove-Item -LiteralPath $zipPath -Force
    }
    if ($PackageFormat -ne 'portable' -and (Test-Path -LiteralPath $installerPath)) {
        Remove-Item -LiteralPath $installerPath -Force
    }
    New-Item -ItemType Directory -Path $publishDirectory | Out-Null

    $publishArguments = @(
        'publish', $project,
        '-c', 'Release',
        '-r', $rid,
        '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:DebugType=None',
        '-p:DebugSymbols=false',
        "-p:FireflyClientApiUrl=$fireflyWorkerBaseUrl",
        '--no-restore',
        '-o', $publishDirectory
    )

    Write-Host "Publishing $productName for $($Specification.Architecture) ..."
    & dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish for $rid failed with exit code $LASTEXITCODE"
    }

    $upstreamExe = Join-Path $publishDirectory 'Firefly.exe'
    $productExe = Join-Path $publishDirectory "$productName.exe"
    if (-not (Test-Path -LiteralPath $upstreamExe)) {
        throw "Expected published executable was not produced: $upstreamExe"
    }
    Move-Item -LiteralPath $upstreamExe -Destination $productExe

    # Windows releases use the embedded ICO; remove macOS and stale upstream
    # image assets if an incremental or upstream publish output includes them.
    foreach ($imageAsset in @('v2rayN.icns', 'FireflyVPN.icns', 'v2rayN.png')) {
        $imageAssetPath = Join-Path $publishDirectory $imageAsset
        if (Test-Path -LiteralPath $imageAssetPath) {
            Remove-Item -LiteralPath $imageAssetPath -Force
        }
    }

    $coreOutput = Join-Path $publishDirectory 'bin'
    New-Item -ItemType Directory -Path $coreOutput | Out-Null
    Copy-Item -Path (Join-Path $coreDirectory '*') -Destination $coreOutput -Recurse -Force

    # Release packages do not need PDBs. The upstream executable was renamed
    # above so it is never shipped alongside the branded executable.
    Get-ChildItem -LiteralPath $publishDirectory -Recurse -Filter '*.pdb' | Remove-Item -Force

    if ($PackageFormat -ne 'installer') {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $zipPath -CompressionLevel Optimal

        $archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
        try {
            $archiveEntries = @($archive.Entries.FullName)
        }
        finally {
            $archive.Dispose()
        }
        if ($archiveEntries -notcontains "$productName.exe") {
            throw "Package verification failed for ${rid}: $productName.exe is missing."
        }
        if ($archiveEntries -contains 'Firefly.exe') {
            throw "Package verification failed for ${rid}: unbranded root Firefly.exe is present."
        }
        if ($archiveEntries -contains 'v2rayN.icns' -or $archiveEntries -contains 'FireflyVPN.icns' -or $archiveEntries -contains 'v2rayN.png') {
            throw "Package verification failed for ${rid}: macOS or upstream-only image assets are present."
        }
        foreach ($requiredCoreFile in $requiredCoreFiles) {
            $zipPathForward = "bin/$($requiredCoreFile.Replace('\', '/'))"
            $zipPathBackward = "bin\$requiredCoreFile"
            if ($archiveEntries -notcontains $zipPathForward -and $archiveEntries -notcontains $zipPathBackward) {
                throw "Package verification failed for ${rid}: $zipPathForward is missing."
            }
        }

        Write-Host "Portable package created: $zipPath"
    }

    if ($PackageFormat -ne 'portable') {
        $innoCompiler = Find-InnoSetupCompiler
        $innoArguments = @(
            '/Qp',
            "/DSourceDir=$publishDirectory",
            "/DOutputDir=$artifacts",
            "/DBuildArch=$($Specification.Architecture)",
            "/DAppVersion=$appVersion",
            $installerScript
        )
        Write-Host "Building installer for $($Specification.Architecture) ..."
        & $innoCompiler @innoArguments
        if ($LASTEXITCODE -ne 0) {
            throw "Inno Setup compilation for $rid failed with exit code $LASTEXITCODE"
        }
        if (-not (Test-Path -LiteralPath $installerPath)) {
            throw "Installer verification failed for ${rid}: expected output was not produced: $installerPath"
        }
        Write-Host "Installer created: $installerPath"
    }

    Write-Host "Verified ${rid}: $productName.exe; no unbranded root Firefly.exe; sing-box, Xray, and Mihomo cores included."
}

$targets = if ($Architecture -eq 'all') { @('x64', 'x86') } else { @($Architecture) }
foreach ($target in $targets) {
    Invoke-WindowsPackage (Get-PackageSpecification $target)
}
