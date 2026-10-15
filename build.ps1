# Builds PhoneBridge.dll with the .NET Framework compiler (no SDK needed)
# and copies it to BepInEx\plugins\PhoneBridge (the phone icon is embedded in the dll).
param([string]$Game = 'D:\Games\TCG Card Shop Simulator', [switch]$NoInstall, [switch]$NoLang)
$game = $Game
$managed = "$game\Card Shop Simulator_Data\Managed"
$core = "$game\BepInEx\core"
$plugins = "$game\BepInEx\plugins"
$dist = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null

& (Join-Path $PSScriptRoot 'make_icons.ps1')

$refs = @(
    "$managed\netstandard.dll", "$managed\UnityEngine.dll", "$managed\UnityEngine.CoreModule.dll",
    "$managed\UnityEngine.ImageConversionModule.dll", "$managed\Assembly-CSharp.dll",
    "$managed\UnityEngine.UI.dll", "$managed\Unity.TextMeshPro.dll", "$managed\UnityEngine.UIModule.dll",
    "$core\BepInEx.dll", "$core\0Harmony.dll",
    "$plugins\ShopOS\ShopOS.dll", "$plugins\Phone - Overhaul\Phone - Overhaul.dll"
) | ForEach-Object { "/r:$_" }

& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /noconfig /target:library /optimize /nostdlib /r:"$managed\mscorlib.dll" /r:"$managed\System.dll" /r:"$managed\System.Core.dll" $refs /resource:"$PSScriptRoot\assets\tablet_icon.png,tablet_icon.png" /out:"$dist\PhoneBridge.dll" (Join-Path $PSScriptRoot 'src\*.cs')
if ($LASTEXITCODE -ne 0) { Write-Host 'Build failed.'; exit 1 }

# preloader patcher (folder preset), runs before every plugin
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /noconfig /target:library /optimize /nostdlib /r:"$managed\mscorlib.dll" /r:"$managed\System.dll" /r:"$managed\System.Core.dll" /r:"$managed\netstandard.dll" /r:"$managed\Newtonsoft.Json.dll" /r:"$core\BepInEx.dll" /r:"$core\BepInEx.Preloader.dll" /r:"$core\Mono.Cecil.dll" /out:"$dist\PhoneBridge.Preloader.dll" (Join-Path $PSScriptRoot 'src_preloader\*.cs')
if ($LASTEXITCODE -ne 0) { Write-Host 'Preloader build failed.'; exit 1 }

if (-not $NoInstall) {
    $target = "$plugins\PhoneBridge"
    New-Item -ItemType Directory -Force $target | Out-Null
    Copy-Item "$dist\PhoneBridge.dll" $target -Force
    # icons of older builds (now inside the dll / read from ShopOS)
    Remove-Item "$target\phone_icon.png", "$target\shopos_icon.png" -ErrorAction SilentlyContinue
    $pre = "$game\BepInEx\patchers\PhoneBridge"
    New-Item -ItemType Directory -Force $pre | Out-Null
    Copy-Item "$dist\PhoneBridge.Preloader.dll" $pre -Force
    # optional translation (separate download when published); -NoLang leaves it out
    if (-not $NoLang) { Copy-Item (Join-Path $PSScriptRoot 'lang\lang_*.txt') $target -Force }
    Write-Host "Installed to $target"
}
