# ゲームに入っているラーメン MOD を、パック単位で切りかえる。
# 使い方: 右クリック →「PowerShell で実行」、または
#   powershell -ExecutionPolicy Bypass -File "D:\ramen-mod\MODの切りかえ.ps1" おもしろ
param([string]$Pack)

$modsTxt = "C:\Program Files (x86)\Steam\steamapps\common\Japanese Ramen Simulator\RealRamenSimulator\Binaries\Win64\ue4ss\Mods\mods.txt"

$packs = [ordered]@{
    "おもしろ"     = @("RamenEmotes", "RamenYoi", "RamenGacha", "RamenOmikuji")
    "バランス崩壊" = @("RamenBenri")
    "全部入り"     = @("RamenEmotes", "RamenBenri", "RamenYoi", "RamenGacha", "RamenOmikuji")
    "全部オフ"     = @()
}
$all = @("RamenEmotes", "RamenBenri", "RamenYoi", "RamenGacha", "RamenOmikuji")

if (-not (Test-Path $modsTxt)) {
    Write-Host "UE4SS が入っていません: $modsTxt"
    exit 1
}

if (-not $Pack) {
    $names = @($packs.Keys)
    for ($i = 0; $i -lt $names.Count; $i++) { Write-Host ("{0}. {1}" -f ($i + 1), $names[$i]) }
    $n = Read-Host "番号をえらんでね"
    $Pack = $names[[int]$n - 1]
}
if (-not $packs.Contains($Pack)) {
    Write-Host "知らないパックです: $Pack（$(@($packs.Keys) -join ' / ')）"
    exit 1
}

$on = $packs[$Pack]
$lines = Get-Content $modsTxt -Encoding UTF8 | Where-Object {
    $name = ($_ -split ':')[0].Trim()
    $all -notcontains $name
}
# Keybinds より上に入れる
$idx = [Array]::FindIndex([string[]]$lines, [Predicate[string]]{ param($l) $l -like '; Built-in keybinds*' })
if ($idx -lt 0) { $idx = $lines.Count }
$new = @($lines[0..($idx - 1)]) + ($all | ForEach-Object { "$_ : $(if ($on -contains $_) { 1 } else { 0 })" }) + @($lines[$idx..($lines.Count - 1)])
[IO.File]::WriteAllLines($modsTxt, [string[]]$new, (New-Object Text.UTF8Encoding $false))

Write-Host "「$Pack」にしました。ゲームを起動しなおすと反映されます。"
$all | ForEach-Object { Write-Host ("  {0}: {1}" -f $_, $(if ($on -contains $_) { "オン" } else { "オフ" })) }
