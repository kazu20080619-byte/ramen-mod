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

## ランチャーの拡張（MOD を足しても、ランチャーは直さない）

ランチャーは MOD の一覧・説明・キー・設定の項目を、各 MOD フォルダの 2 つのファイルから読む（`launcher\Catalog.cs`）。
新しい MOD は `src\Mods\<名前>\` に置いて、この 2 つを書けば、ランチャーの画面（MOD・MOD の設定・チェック）に出る。

- `ramen-mod.json` … 友だちの RamenModLauncher と同じ形（`id`・`name`・`app_id`・`ue4ss`・`dependencies: [{id, version: ">=1.2,<2"}]`・`multiplayer`・`assets`・`source`・`description`）。
  `version` は書かない（`ランチャー用を作る.py` が `build.py` の VERSION を入れる。zip には直下に 1 つだけ入れる）
- `ramen-launcher.json` … このランチャー用:
  ```json
  {
    "format": 1, "short": "画面の名前", "mark": "回", "who": "ホストだけ",
    "pack": "fun | benri | always",            // まとめ選び（おもしろ / バランス崩壊 / いつも）
    "summary": "カードの説明",
    "keys": [{ "key": "E", "where": "ガチャの台の前" }],   // where が同じか「どこでも」のキーは、ぶつかりとして知らせる
    "recommends": [{ "id": "ramen-emotes", "why": "…" }],  // いっしょだとよい MOD（お知らせだけ）
    "conflicts": ["other-id"],                              // いっしょに使えない MOD
    "config": { "file": "config.lua", "groups": [
      { "title": "見出し", "style": "chips(なくてもよい)", "fields": [
        { "name": "price", "kind": "int", "label": "1 回の値段（$）", "default": "10", "min": 0, "max": 1000 },
        { "name": "fx.flash", "kind": "bool", "label": "光る", "default": "true" },
        { "name": "fullMode", "kind": "choice", "label": "…", "default": "nil", "choices": [{ "value": "nil", "label": "…" }] }
      ] } ] }
  }
  ```
  `kind`: bool / int / number / key / intlist / numlist / sizelist / choice。`name` に `表.項目`（例 `keys.use`）も書ける
- ほかの人の MOD（zip）も同じ 2 つが読める。zip は友だちのランチャーと同じ決まりで確かめて、`%LOCALAPPDATA%\RamenLauncher\library\<id>\` に入れる
- 選び方の保存は `%LOCALAPPDATA%\RamenLauncher\profiles.json`
- ためし: `--test catalog`・`--test import <zip>`・`--test remove <id>`・`--test check A,B`・`--test satisfies 1.3.0 ">=1.0,<2"`・`--test profile save|delete|list`・`--test ui-smoke [絵のフォルダ]`
  （`RAMEN_LAUNCHER_DATA` にフォルダを入れると、本物の `%LOCALAPPDATA%` を使わずにためせる）

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
- **お店を開くと、最後に遊んだワールドが読みこまれ、自動セーブされる。** テストの前に、その時点のセーブと
  `Mods\RamenGacha\data`・`Mods\RamenOmikuji\data` を `backup\` にコピーして、テストのあとで戻す
- 落ちないかのストレステスト: `bash bridge/stress_test.sh <秒> <名前>`（ガチャの MOD にゲームのスレッドで命令を連打する）

## スレッドの決まり（いちばん大事）

UE4SS の `LoopAsync` / `ExecuteWithDelay` / `ExecuteAsync` は、MOD の Lua を**ゲームとは別のスレッド**で動かす。
同じ MOD の中で、ゲームのスレッドの Lua（`RegisterHook` の中身・`ExecuteInGameThread` の中身・コンソールの命令）と
同時に動くと、Lua のメモリがこわれる。症状は「`Ref was not function`」、値がまざる、関係ない関数が勝手に呼ばれる、
数十秒〜数分でゲームが落ちる、など。だから:

- くり返しは `C.loop(ms, fn)`、待ちは `C.later(秒, fn)`（中は `LoopInGameThreadWithDelay` / `ExecuteInGameThreadWithDelay`）
- キーは `C.key(キー, 修飾キー, fn)`（キーの呼び出しも別のスレッドなので、そこではゲームのスレッドに渡すだけ）
- `ramen_common` を使わない MOD（Emotes・Yoi）も、同じ形の小さな関数を持っている
- `LoopAsync` / `ExecuteWithDelay` は、古い UE4SS で `…InGameThread…` がないときの予備の道でだけ使う

## やってはいけないこと

- `LoopAsync` / `ExecuteWithDelay` を直接使う（上の「スレッドの決まり」）
- ゲームのボタンの情報（FKey）を Lua の表で作って関数に渡す・ボタンを何度も直接たしかめる（パッド対応で落ちた）
- Lua で SoftObjectProperty に代入する（ゲームが落ちる）
- `PushLocalNotification` に形の合わない引数（落ちる）
- Python の heredoc で Lua の `"\n"` を書きかえる（本物の改行になって壊れる）→ Edit で直す
- ゲームが読みこみ中のうちに、たくさんの物を作る（止まることがある）
