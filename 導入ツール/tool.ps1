# ラーメンMOD かんたん導入・削除
# 使い方: かんたん導入.bat / かんたん削除.bat をダブルクリック
#   powershell -ExecutionPolicy Bypass -File tool.ps1 -Mode install|uninstall [-GameDir <Win64 フォルダ>]
param(
    [ValidateSet("install", "uninstall")][string]$Mode = "install",
    [string]$GameDir = "",
    [switch]$NoPause
)

$ErrorActionPreference = "Stop"
$AppId = "4968640"
$Here = Split-Path -Parent $MyInvocation.MyCommand.Path
$Win64Rel = "RealRamenSimulator\Binaries\Win64"
$RamenMods = @("RamenEmotes", "RamenBenri", "RamenYoi", "RamenGacha", "RamenOmikuji")

function Say($msg, $color = "White") { Write-Host $msg -ForegroundColor $color }
function Finish($code) {
    if (-not $NoPause) { Write-Host ""; Read-Host "Enter キーで閉じます" | Out-Null }
    exit $code
}

# --- ゲームの場所を Steam から探す ----------------------------------------
function Find-GameWin64 {
    $steam = $null
    foreach ($key in "HKCU:\Software\Valve\Steam", "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam", "HKLM:\SOFTWARE\Valve\Steam") {
        try {
            $p = Get-ItemProperty $key -ErrorAction Stop
            if ($p.SteamPath) { $steam = $p.SteamPath; break }
            if ($p.InstallPath) { $steam = $p.InstallPath; break }
        } catch {}
    }
    $libs = @()
    if ($steam) {
        $steam = $steam -replace "/", "\"
        $libs += $steam
        $vdf = Join-Path $steam "steamapps\libraryfolders.vdf"
        if (Test-Path $vdf) {
            foreach ($m in [regex]::Matches((Get-Content $vdf -Raw -Encoding UTF8), '"path"\s*"([^"]+)"')) {
                $libs += ($m.Groups[1].Value -replace "\\\\", "\")
            }
        }
    }
    $libs += "C:\Program Files (x86)\Steam"
    foreach ($lib in ($libs | Select-Object -Unique)) {
        $acf = Join-Path $lib "steamapps\appmanifest_$AppId.acf"
        $dir = $null
        if (Test-Path $acf) {
            $m = [regex]::Match((Get-Content $acf -Raw -Encoding UTF8), '"installdir"\s*"([^"]+)"')
            if ($m.Success) { $dir = Join-Path $lib ("steamapps\common\" + $m.Groups[1].Value) }
        }
        if (-not $dir) { $dir = Join-Path $lib "steamapps\common\Japanese Ramen Simulator" }
        $w = Join-Path $dir $Win64Rel
        if (Test-Path (Join-Path $w "RealRamenSimulator-Win64-Shipping.exe")) { return $w }
    }
    return $null
}

Say "========================================" Cyan
Say "  ラーメンMOD  かんたん$(if ($Mode -eq 'install') {'導入'} else {'削除'})" Cyan
Say "========================================" Cyan
Say ""

if (Get-Process -Name "RealRamenSimulator-Win64-Shipping" -ErrorAction SilentlyContinue) {
    Say "ゲームが起動しています。ゲームを閉じてから、もう一度やってください。" Yellow
    Finish 1
}

if (-not $GameDir) { $GameDir = Find-GameWin64 }
if (-not $GameDir -or -not (Test-Path $GameDir)) {
    Say "Japanese Ramen Simulator が見つかりませんでした。" Yellow
    Say "Win64 フォルダの場所を入力してください（例: D:\SteamLibrary\steamapps\common\Japanese Ramen Simulator\$Win64Rel）"
    $GameDir = (Read-Host "場所").Trim('"', ' ')
    if (-not (Test-Path (Join-Path $GameDir "RealRamenSimulator-Win64-Shipping.exe"))) {
        Say "その場所にゲームがありません。やめます。" Red
        Finish 1
    }
}
Say "ゲームの場所: $GameDir" Gray

$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$backupRoot = Join-Path $GameDir "ramenmod_backup"

# --- 削除 -----------------------------------------------------------------
if ($Mode -eq "uninstall") {
    $dll = Join-Path $GameDir "dwmapi.dll"
    $ue = Join-Path $GameDir "ue4ss"
    if (-not (Test-Path $dll) -and -not (Test-Path $ue)) {
        Say "MOD は入っていません。" Green
        Finish 0
    }
    $dest = Join-Path $backupRoot "removed_$stamp"
    New-Item -ItemType Directory -Force $dest | Out-Null
    if (Test-Path $dll) { Move-Item $dll $dest }
    if (Test-Path $ue) { Move-Item $ue $dest }
    Say ""
    Say "MOD を外しました。" Green
    Say "外したファイルは、消さずにここへ移しました（ガチャの記録なども入っています）:" Gray
    Say "  $dest" Gray
    Say "いらなければ、このフォルダは自分で消してください。" Gray
    Finish 0
}

# --- 導入 -----------------------------------------------------------------
$srcDll = Join-Path $Here "dwmapi.dll"
$srcUe = Join-Path $Here "ue4ss"
if (-not (Test-Path $srcDll) -or -not (Test-Path $srcUe)) {
    Say "このフォルダに dwmapi.dll と ue4ss がありません。" Red
    Say "zip を「すべて展開」してから、展開したフォルダの中の かんたん導入.bat を開いてください。" Red
    Finish 1
}

$dstUe = Join-Path $GameDir "ue4ss"
$keep = $null
if (Test-Path $dstUe) {
    # 前のを丸ごとバックアップしてから入れかえる。ガチャの記録・設定は引きつぐ
    $old = Join-Path $backupRoot "before_install_$stamp"
    New-Item -ItemType Directory -Force $old | Out-Null
    Copy-Item $dstUe $old -Recurse
    $dllOld = Join-Path $GameDir "dwmapi.dll"
    if (Test-Path $dllOld) { Copy-Item $dllOld $old }
    $keep = Join-Path $old "ue4ss"
    Say "前に入っていた MOD をバックアップしました: $old" Gray
    Remove-Item $dstUe -Recurse -Force
}

Copy-Item $srcDll $GameDir -Force
Copy-Item $srcUe $GameDir -Recurse -Force

# 前の記録（data）と、自分で変えた設定（config.lua）を戻す
if ($keep) {
    foreach ($m in $RamenMods) {
        $oldData = Join-Path $keep "Mods\$m\data"
        $newMod = Join-Path $dstUe "Mods\$m"
        if ((Test-Path $oldData) -and (Test-Path $newMod)) {
            Copy-Item (Join-Path $oldData "*") (Join-Path $newMod "data") -Recurse -Force
        }
        $oldCfg = Join-Path $keep "Mods\$m\config.lua"
        if ((Test-Path $oldCfg) -and (Test-Path $newMod)) {
            Copy-Item $oldCfg (Join-Path $newMod "config.lua") -Force
        }
    }
    Say "前のガチャの記録・運勢・台の向きと大きさ・設定を引きつぎました。" Gray
}

# ネットから落とした目印をはずす（Windows が DLL を止めないように）
Get-ChildItem $GameDir -Recurse -File -Include "dwmapi.dll", "*.dll", "*.lua", "*.ini" -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -like "$GameDir*" } | Unblock-File -ErrorAction SilentlyContinue

$on = @()
$modsTxt = Join-Path $dstUe "Mods\mods.txt"
if (Test-Path $modsTxt) {
    foreach ($line in Get-Content $modsTxt -Encoding UTF8) {
        $p = $line -split ":"
        if ($p.Count -eq 2 -and $RamenMods -contains $p[0].Trim() -and $p[1].Trim() -eq "1") { $on += $p[0].Trim() }
    }
}
$labels = @{ RamenEmotes = "新しいエモート"; RamenBenri = "便利機能"; RamenYoi = "酔っ払い"; RamenGacha = "ガチャガチャ"; RamenOmikuji = "おみくじ箱" }
Say ""
Say "導入できました！ 入った MOD:" Green
foreach ($m in $on) { Say ("  ・" + $labels[$m]) Green }
Say ""
Say "いつもどおり Steam からゲームを起動してください。遊び方は 説明書.md を見てね。"
Finish 0
