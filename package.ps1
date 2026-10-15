# Builds the mod and packs the Nexus downloads into .\release\<version>:
#   PhoneBridge-<version>.zip          main file (BepInEx folder layout, drop into the game folder)
#   PhoneBridge-German-<version>.zip   optional German translation
#   PhoneBridge-Source-<version>.zip   source code
param([string]$Version = '1.0.0')
$root = $PSScriptRoot
Add-Type -AssemblyName System.IO.Compression.FileSystem
& (Join-Path $root 'build.ps1') -NoInstall
if ($LASTEXITCODE -ne 0) { exit 1 }

$out = Join-Path $root "release\$Version"
$stage = Join-Path $env:TEMP "PhoneBridge_stage_$Version"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null

function Pack($name, [scriptblock]$fill) {
    $dir = Join-Path $stage $name
    & $fill $dir
    $zip = Join-Path $out "$name.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    # entries written by hand with '/': Compress-Archive and ZipFile in PowerShell 5.1 store '\', which some mod managers misread
    $z = [IO.Compression.ZipFile]::Open($zip, 'Create')
    try {
        foreach ($f in Get-ChildItem $dir -Recurse -File) {
            $entry = $f.FullName.Substring($dir.Length + 1).Replace('\', '/')
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, $f.FullName, $entry)
        }
    } finally { $z.Dispose() }
    Write-Host "packed $zip"
}

# main file; the plugin folder must be called PhoneBridge (the preloader looks for lang_*.txt there)
Pack "PhoneBridge-$Version" {
    param($d)
    $p = New-Item -ItemType Directory -Force "$d\BepInEx\plugins\PhoneBridge"
    $q = New-Item -ItemType Directory -Force "$d\BepInEx\patchers\PhoneBridge"
    Copy-Item "$root\dist\PhoneBridge.dll", "$root\README.txt" $p
    Copy-Item "$root\dist\PhoneBridge.Preloader.dll" $q
}

Pack "PhoneBridge-German-$Version" {
    param($d)
    $p = New-Item -ItemType Directory -Force "$d\BepInEx\plugins\PhoneBridge"
    Copy-Item "$root\lang\lang_de.txt" $p
}

# source code (asked for by the ShopOS author; can also go on Nexus/GitHub)
Pack "PhoneBridge-Source-$Version" {
    param($d)
    New-Item -ItemType Directory -Force $d | Out-Null
    Copy-Item "$root\src", "$root\src_preloader", "$root\assets", "$root\lang" $d -Recurse
    Copy-Item "$root\build.ps1", "$root\make_icons.ps1", "$root\package.ps1", "$root\README.txt", "$root\CHANGELOG.txt" $d
}

Remove-Item $stage -Recurse -Force
