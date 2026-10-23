param([ValidateSet('All', 'Unity', 'Level01', 'Player', 'Movement')][string]$Package = 'All')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Add-FolderEntries($List, [string]$Folder, [string]$Prefix) {
    $base = (Resolve-Path -LiteralPath (Join-Path $repo $Folder)).Path
    foreach ($file in Get-ChildItem -LiteralPath $base -File -Recurse) {
        $relative = $file.FullName.Substring($base.Length + 1).Replace('\', '/')
        if ($relative -match '(^|/)qa-profile-[^/]+/') { continue }
        if ($relative -match '(^|/)[^/]*_DoNotShip/') { continue }
        $List.Add(@{ Source = $file.FullName; Entry = "$Prefix/$relative" })
    }
}
function Add-FileEntry($List, [string]$File, [string]$Entry) {
    $source = Join-Path $repo $File
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Required package file missing: $source" }
    $List.Add(@{ Source = $source; Entry = $Entry })
}
function Write-AssetZip([string]$Filename, $Entries) {
    $destination = Join-Path $repo "build/$Filename"
    $temporary = "$destination.$([Guid]::NewGuid().ToString('N')).pending"
    $stream = [System.IO.File]::Open($temporary, [System.IO.FileMode]::CreateNew)
    $zip = [System.IO.Compression.ZipArchive]::new($stream, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($entry in $Entries) {
            [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $entry.Source, $entry.Entry, [System.IO.Compression.CompressionLevel]::Optimal)
        }
    } finally { $zip.Dispose(); $stream.Dispose() }
    $check = [System.IO.Compression.ZipFile]::OpenRead($temporary)
    try {
        if ($check.Entries.Count -ne $Entries.Count) { throw 'Zip entry count differs' }
        $names = @($check.Entries | ForEach-Object { $_.FullName })
        if (($names | Sort-Object -Unique).Count -ne $names.Count) { throw 'Duplicate Zip paths' }
        foreach ($entry in $check.Entries) {
            if ($entry.FullName.Contains('../') -or $entry.FullName.StartsWith('/')) { throw 'Unsafe Zip entry' }
            $reader = $entry.Open()
            try { $reader.CopyTo([System.IO.Stream]::Null) } finally { $reader.Dispose() }
        }
    } finally { $check.Dispose() }
    Move-Item -LiteralPath $temporary -Destination $destination -Force
    $file = Get-Item -LiteralPath $destination
    Write-Output "$Filename : $($Entries.Count) files, $($file.Length) bytes; archive read verified."
}

if ($Package -in @('All', 'Unity')) {
    $entries = [System.Collections.Generic.List[object]]::new()
    Add-FolderEntries $entries 'unity/Packages' 'Packages'
    Add-FolderEntries $entries 'unity/XenaUnity/Assets/Imported' 'XenaUnity/Assets/Imported'
    Add-FolderEntries $entries 'unity/XenaUnity/Assets/Generated' 'XenaUnity/Assets/Generated'
    Add-FolderEntries $entries 'unity/XenaUnity/Assets/Scenes' 'XenaUnity/Assets/Scenes'
    foreach ($name in @('Imported', 'Generated', 'Scenes')) {
        Add-FileEntry $entries "unity/XenaUnity/Assets/$name.meta" "XenaUnity/Assets/$name.meta"
    }
    Add-FileEntry $entries 'unity/XenaUnity/Assets/README.md' 'XenaUnity/Assets/README.md'
    Add-FileEntry $entries 'unity/XenaUnity/Packages/manifest.json' 'XenaUnity/Packages/manifest.json'
    Add-FolderEntries $entries 'unity/XenaUnity/ProjectSettings' 'XenaUnity/ProjectSettings'
    Add-FileEntry $entries 'docs/unity-import.md' 'Documentation/unity-import.md'
    Add-FileEntry $entries 'docs/windows-asset-preview.md' 'Documentation/windows-asset-preview.md'
    Add-FileEntry $entries 'docs/movement-prototype.md' 'Documentation/movement-prototype.md'
    Add-FileEntry $entries 'docs/xena-jump-audit.md' 'Documentation/xena-jump-audit.md'
    Add-FileEntry $entries 'docs/xena-locomotion-audit.md' 'Documentation/xena-locomotion-audit.md'
    Add-FileEntry $entries 'docs/xena-combat-audit.md' 'Documentation/xena-combat-audit.md'
    Add-FileEntry $entries 'build/combat-reference/verification.json' 'Verification/combat-native-verification.json'
    Add-FileEntry $entries 'build/unity-import/combat-player-verification.json' 'Verification/combat-player-verification.json'
    foreach ($name in @('combat-sword', 'combat-kick', 'combat-block')) {
        Add-FileEntry $entries "build/unity-import/$name.png" "Verification/$name.png"
    }
    Add-FileEntry $entries 'build/animation-audit/locomotion-verification.json' 'Verification/locomotion-verification.json'
    Add-FileEntry $entries 'build/unity-import/movement-run-cycle.png' 'Verification/movement-run-cycle.png'
    Add-FileEntry $entries 'build/animation-audit/jump-verification.json' 'Verification/jump-verification.json'
    Add-FileEntry $entries 'build/unity-import/movement-jump-air.png' 'Verification/movement-jump-air.png'
    Add-FileEntry $entries 'build/unity-import/movement-jump-land.png' 'Verification/movement-jump-land.png'
    Add-FileEntry $entries 'docs/level-resource-audit.md' 'Documentation/level-resource-audit.md'
    Add-FileEntry $entries 'build/unity-import/dotnet-verification.json' 'Verification/dotnet-verification.json'
    Add-FileEntry $entries 'build/unity-import/api-compilation.json' 'Verification/api-compilation.json'
    Add-FileEntry $entries 'build/unity-import/editor-verification.json' 'Verification/editor-verification.json'
    Add-FileEntry $entries 'build/unity-import/render-verification.json' 'Verification/render-verification.json'
    Add-FileEntry $entries 'build/unity-import/build-verification.json' 'Verification/build-verification.json'
    Add-FileEntry $entries 'build/unity-import/player-verification.json' 'Verification/player-verification.json'
    Add-FileEntry $entries 'build/unity-import/movement-build-verification.json' 'Verification/movement-build-verification.json'
    Add-FileEntry $entries 'build/unity-import/movement-player-verification.json' 'Verification/movement-player-verification.json'
    Add-FileEntry $entries 'build/unity-import/movement-player.png' 'Verification/movement-player.png'
    foreach ($name in @('unity-xena-upscale', 'unity-xena-original', 'unity-xena-pose', 'unity-level01', 'unity-level01-unlit', 'unity-level01-brightness35', 'unity-lighting-neutral-probe', 'unity-lighting-bias-probe')) {
        Add-FileEntry $entries "build/unity-import/$name.png" "Verification/$name.png"
    }
    Add-FileEntry $entries 'build/level-assets/01/terrain-verification.json' 'Verification/terrain-verification.json'
    Add-FileEntry $entries 'build/level-assets/01/lighting-verification.json' 'Verification/lighting-verification.json'
    Add-FileEntry $entries 'build/level-assets/01/spawns-verification.json' 'Verification/spawns-verification.json'
    Write-AssetZip 'Xena-Unity-import.zip' $entries
}
if ($Package -in @('All', 'Movement')) {
    $entries = [System.Collections.Generic.List[object]]::new()
    Add-FolderEntries $entries 'build/unity-movement' 'XenaMovementPrototype'
    Add-FileEntry $entries 'docs/movement-prototype.md' 'README.md'
    Add-FileEntry $entries 'docs/xena-jump-audit.md' 'Verification/xena-jump-audit.md'
    Add-FileEntry $entries 'docs/xena-locomotion-audit.md' 'Verification/xena-locomotion-audit.md'
    Add-FileEntry $entries 'docs/xena-combat-audit.md' 'Verification/xena-combat-audit.md'
    Add-FileEntry $entries 'build/combat-reference/verification.json' 'Verification/combat-native-verification.json'
    Add-FileEntry $entries 'build/unity-import/combat-player-verification.json' 'Verification/combat-player-verification.json'
    Add-FileEntry $entries 'build/animation-audit/locomotion-verification.json' 'Verification/locomotion-verification.json'
    Add-FileEntry $entries 'build/animation-audit/jump-verification.json' 'Verification/jump-verification.json'
    Add-FileEntry $entries 'build/unity-import/movement-build-verification.json' 'Verification/movement-build-verification.json'
    Add-FileEntry $entries 'build/unity-import/movement-player-verification.json' 'Verification/movement-player-verification.json'
    Write-AssetZip 'Xena-Windows-movement-prototype.zip' $entries
}
if ($Package -in @('All', 'Player')) {
    $entries = [System.Collections.Generic.List[object]]::new()
    Add-FolderEntries $entries 'build/unity-preview' 'XenaAssetPreview'
    Add-FileEntry $entries 'docs/windows-asset-preview.md' 'README.md'
    Add-FileEntry $entries 'build/unity-import/build-verification.json' 'Verification/build-verification.json'
    Add-FileEntry $entries 'build/unity-import/player-verification.json' 'Verification/player-verification.json'
    Write-AssetZip 'Xena-Windows-asset-preview.zip' $entries
}
if ($Package -in @('All', 'Level01')) {
    $entries = [System.Collections.Generic.List[object]]::new()
    Add-FolderEntries $entries 'build/level-assets/01' 'Level01'
    Add-FileEntry $entries 'docs/level-resource-audit.md' 'Documentation/level-resource-audit.md'
    Write-AssetZip 'Xena-Level01-assets.zip' $entries
}
