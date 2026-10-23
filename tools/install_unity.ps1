param([switch]$CheckOnly)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$version = '6000.3.25f1'
$release = 'e1dba0a9aba4'
# Keep built-in package paths below Windows' legacy 260-character limit.
# The previous nested build/toolchains location silently lost URP source files.
$installRoot = Join-Path (Split-Path -Parent $repo) 'Unity63'
$editor = Join-Path $installRoot 'Editor/Unity.exe'
$downloads = Join-Path $repo 'build/installers'
$installer = Join-Path $downloads "UnitySetup64-$version.exe"
$url = "https://download.unity3d.com/download_unity/$release/Windows64EditorInstaller/UnitySetup64-$version.exe"

try {
    if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
        throw 'Node.js is required to run the prepared import verification.'
    }
    if ($CheckOnly) {
        Write-Host "Unity version: $version"
        Write-Host "Official installer: $url"
        Write-Host "Install folder: $installRoot"
        Write-Host "Editor installed: $(Test-Path -LiteralPath $editor)"
        Write-Host 'Configuration check only; no download or installation performed.'
        exit 0
    }
    New-Item -ItemType Directory -Force -Path $downloads | Out-Null
    if (-not (Test-Path -LiteralPath $editor)) {
        $drive = [System.IO.DriveInfo]::new([System.IO.Path]::GetPathRoot($repo))
        if ($drive.AvailableFreeSpace -lt 20GB) {
            throw 'At least 20 GB of free space is required for this installation and project cache.'
        }
        if (-not (Test-Path -LiteralPath $installer)) {
            Write-Host "Downloading Unity $version from Unity's official server..."
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
            $partial = "$installer.partial"
            Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $partial
            Move-Item -LiteralPath $partial -Destination $installer -Force
        }
        Write-Host 'Checking installer digital signature...'
        $signature = Get-AuthenticodeSignature -LiteralPath $installer
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Unity (Technologies|Software)') {
            throw "Installer signature rejected: $($signature.Status). Installer was not executed: $installer"
        }
        $hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
        [ordered]@{version=$version; url=$url; sha256=$hash; signer=$signature.SignerCertificate.Subject} |
            ConvertTo-Json | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $downloads "UnitySetup64-$version.verified.json")
        Write-Host "Installing to $installRoot ..."
        # Unity's documented NSIS syntax: /D must be last, without quotes.
        $process = Start-Process -FilePath $installer -ArgumentList "/S /D=$installRoot" -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode -notin @(0, 3010)) {
            throw "Unity installer failed with exit code $($process.ExitCode)."
        }
        if (-not (Test-Path -LiteralPath $editor)) { throw "Installer did not create $editor" }
    }
    Write-Host "Editor found: $editor"
    $urpSource = Join-Path $installRoot 'Editor/Data/Resources/PackageManager/BuiltInPackages/com.unity.render-pipelines.universal/Editor/Tools/Converters/ReadonlyMaterialConverter/ReadonlyMaterialConverter.MaterialReferenceChanger.cs'
    foreach ($required in @($urpSource, "$urpSource.meta")) {
        if (-not (Test-Path -LiteralPath $required)) {
            throw "Unity installation is incomplete (possible path length limit). Missing: $required"
        }
    }
    $windowsSupport = Join-Path $installRoot 'Editor/Data/PlaybackEngines/windowsstandalonesupport'
    if (-not (Test-Path -LiteralPath $windowsSupport)) {
        throw 'Windows standalone build support was not found in the Editor installation.'
    }
    Push-Location $repo
    try {
        & node tools/repair_unity_cache.mjs --editor $editor
        if ($LASTEXITCODE -ne 0) { throw 'Unable to verify the existing Unity package cache.' }
        Write-Host 'Running the actual Unity import and animation verification...'
        & node tools/run_unity_import.mjs --editor $editor
        if ($LASTEXITCODE -ne 0) {
            throw 'Unity verification failed. Read the diagnostic above and build/unity-import/editor.log. The installed Editor will be reused on the next attempt.'
        }
    } finally { Pop-Location }
    Write-Host 'PASS: Unity import verified. Report: decomp/build/unity-import/editor-verification.json'
    Write-Host 'Open the project using Open-Xena-Unity.cmd in the game folder.'
    exit 0
} catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
