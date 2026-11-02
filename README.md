# ラーメンMOD（開発用のメモ）

遊び方は **[説明書.md](説明書.md)** を見てください。ここは作る人のためのメモです。

## フォルダ

| 場所 | 中身 |
|---|---|
| `説明書.md` | 遊ぶ人向けの説明書（パックにも入る） |
| `仕様_ガチャとおみくじ.md` | ガチャとおみくじの仕様 |
| `配布\` | パック（zip）。`python パックを作る.py` で作り直す |
| `MODの切りかえ.ps1` | この PC のゲームの MOD をパック単位でオン・オフ |
| `ラーメンMODエディタ.pyw` | MOD の記録・設定・オン・オフを見て直す |
| `tex\` | 置き物にはる絵（作り直したら Mods の tex にもコピー） |
| `backup\` | テスト前のセーブ（`SaveGames_2026-11-01_before-test`）と、テストで作った記録 |
| `tools\` | FModel・retoc・UE4SS の元ファイル |
| `bridge\`・`test.sh` | テスト用（下を見てね） |

ゲームの MOD 本体: `C:\Program Files (x86)\Steam\steamapps\common\Japanese Ramen Simulator\RealRamenSimulator\Binaries\Win64\ue4ss\Mods\`

| MOD | 中身 |
|---|---|
| `shared\ramen_common.lua` | 共通部品（基本の形で模型を組む・どデカ表示・演出・保存・チャット・飾りの置き物） |
| `RamenEmotes` | 新しいエモート。`ramenemote sync` で使えるものを決める |
| `RamenYoi` | 酔っ払い・醤油一気飲み |
| `RamenGacha` | ガチャガチャ（清掃中看板 `Men.ItemId.Decoration.Wetfloor` を借りる） |
| `RamenOmikuji` | おみくじ箱（モップの飾り `Men.ItemId.Decoration.Mop` を借りる） |
| `RamenBenri` | 便利機能（F1 でべんりメニュー。前の Ctrl+F1〜F6 も残す）。MOD の操作はキーボードだけ（パッドの見回りはゲームが落ちたのでやめた） |
| `RamenWindow` | ウィンドウの大きさ（F11 でフルスクリーン ⇄ 小さいウィンドウ）。どのパックにも入れる |

`ramen_common.lua` を使う MOD（Gacha・Omikuji・Benri・Window）は、`Scripts` に写しを入れて配る。元は `src\ramen_common.lua`。
| `RamenDevBridge` | 開発用（パックには入れない） |

## リリース（ランチャーの自動更新）

- 版は `launcher\build.py` の `VERSION` 1 か所。exe と `payload\version.txt` に入る。タグは `v<VERSION>`
- `python リリースを作る.py` → `配布\ramen-mods.zip`・`配布\RamenLauncher.exe` ができて、`gh release create …` のコマンドが出る
- 更新先は `launcher\Core.cs` の `Updater.UpdateOwner` / `UpdateRepo`（いまは仮の `OWNER/ramen-mod`）。
  `%LOCALAPPDATA%\RamenLauncher\settings.txt` に `update_repo=owner/name` を書いても変えられる
- ためし: `RamenLauncher.exe --test update-check 1.0.0 [owner/name]`・`--test update-apply <zip> [win64]`（結果は exe のそばの test_out.txt）

## しくみのメモ

- **置き物**: パソコンの品物の表（`DT_Men_SupplyData`）に行は足せない（UE4SS の `AddRow` がこのバージョンで未対応）。
  そこで既にある飾りの行の名前・値段を書きかえて売る。届き方・置き方・セーブはゲームのまま。
  置かれた `MenBuildableActor` の見た目（`MeshComponent`）を隠して、基本の形（Cube など）で組んだ模型をくっつける。
  向き・大きさは飾りの `SaveIdentityComponent.PersistentId` ごとに `data\<ワールド>.lua` の `adjust` に保存
- **主人公**は Pawn＋Mover。体は `CharacterMeshComponent`。速さは `WalkMoveSpeed` / `SprintMoveSpeed`
- **エモート**: カタログに行を足し、`ReplicatedPlayback` と `ActiveMontage` を自分で立てて `OnRep` のあとで `Montage_Play`
- **お金**: `MenGameState` の `TrySpendMoney` / `ApplyMoneyDelta`。売り上げは `MulticastCheckoutPaymentAtLocation` をフック
- **完成ラーメン**: `Cookware.Donburi` を `SpawnItem` → `ForceDebugCompleteRamen`、`TemperatureComponent.CoolingRate = 0`
- **自分だけのメッセージ**: `MenTextChatComponent:ClientReceiveMessage`。みんなへは `ServerSendMessage`
- **画面の文字**: チャットの部品 `WBP_Men_HUD_TextChatMessage` を作ってビューポートに足す（大きい字は拡大で。字の大きさ 48 を超えると豆腐になる）

## テストのしかた

```bash
source /d/ramen-mod/test.sh
restart_game        # ゲームを起動しなおして、お店のマップを開く
check_syntax        # MOD の Lua の文法を、ゲームの Lua で確かめる
check_common
```

- `bridge\cmd.lua` を置くと、ゲームの中で実行して `bridge\out.txt` に結果を書く（`run <<'EOF' … EOF`）
- 画面の確認はコンソールの `shot showui`（`%LOCALAPPDATA%\RealRamenSimulator\Saved\Screenshots\Windows`）
- **お店を開くと、最後に遊んだワールドが読みこまれ、自動セーブされる。** テストのあとは必ず `backup\SaveGames_2026-11-01_before-test` からセーブを戻し、
  `Mods\RamenGacha\data`・`Mods\RamenOmikuji\data` のテストの記録も片づける

## やってはいけないこと

- Lua で SoftObjectProperty に代入する（ゲームが落ちる）
- `PushLocalNotification` に形の合わない引数（落ちる）
- Python の heredoc で Lua の `"\n"` を書きかえる（本物の改行になって壊れる）→ Edit で直す
- ゲームが読みこみ中のうちに、たくさんの物を作る（止まることがある）
