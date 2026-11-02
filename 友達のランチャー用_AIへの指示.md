# RamenModLauncher に「ラーメンMOD の自動更新」を足すための、AI への指示

（ここから下を、そのまま AI に貼ってください）

---

あなたは私の Mod ランチャー「RamenModLauncher」（Japanese Ramen Simulator 用、Steam AppID 4968640、UE4SS の Lua MOD を入れるツール）のコードを直す担当です。
友だちが作っている「ラーメンMOD」を、GitHub から自動で新しい版に更新できるようにしてください。

## 1. 更新を見に行く場所

- GitHub リポジトリ: `kazu20080619-byte/ramen-mod`（公開。ログインやトークンはいらない）
- 最新版の情報: `GET https://api.github.com/repos/kazu20080619-byte/ramen-mod/releases/latest`
  - ヘッダー `User-Agent: RamenModLauncher` と `Accept: application/vnd.github+json` を付ける
  - 返ってきた JSON の `tag_name`（例 `v1.2.0`）が版。`assets[]` の `name` と `browser_download_url` でファイルを取る
- リリースの中で使うファイル:
  - `ramen-mods-index.json` … MOD の一覧（下の形）。**まずこれを読む**
  - `ramen-<id>-<版>.zip` … MOD 1 つにつき zip 1 つ（このランチャーの決まりの形）
  - ほかのファイル（`ramen-mods.zip`・`RamenLauncher.exe`・`RamenModLauncher.zip`）は友だち自身のランチャー用なので、**使わない**

## 2. `ramen-mods-index.json` の形

```json
{
  "format": 1,
  "version": "1.2.0",
  "tag": "v1.2.0",
  "source": "https://github.com/kazu20080619-byte/ramen-mod",
  "mods": [
    {
      "id": "ramen-gacha",
      "folder": "RamenGacha",
      "name": "ラーメンMOD: ガチャガチャ",
      "version": "1.2.0",
      "file": "ramen-gacha-1.2.0.zip",
      "size": 25731,
      "sha256": "（64 文字の16進数）",
      "multiplayer": "Host Only",
      "description": "…"
    }
  ]
}
```

- `format` が 1 でなければ、更新しない（「ランチャーの更新が必要です」と出す）
- `id` は各 zip の中の `ramen-mod.json` の `id` と同じ
- `folder` は `ue4ss/Mods/` の下のフォルダ名

## 3. 各 MOD の zip の形（いまのランチャーの決まりどおり）

- zip の直下に `ramen-mod.json`（`id`・`name`・`version`・`app_id: "4968640"`・`ue4ss: ">=3.0.1"`・`multiplayer`・`source` など）
- `<folder>/Scripts/main.lua` が 1 つ。ほかに `Scripts/*.lua` や `config.lua` がある
- 入っているのは `.lua .png .jpg .jpeg .json .md .txt .ini .cfg` だけ。dll・exe・bat・ps1・UE4SS 本体は入っていない
- **いまある「zip の中身をたしかめる仕組み」は、そのまま通してください。ゆるめないこと**

## 4. 更新の流れ

1. ランチャー起動時（と「更新を確認」ボタン）に、裏のスレッドで 1 の URL を見る。画面は止めない。ネットがない・失敗したときは何も言わずにやめる
2. GitHub の API は、ログインなしだと 1 時間に 60 回まで。**最後に見た時刻と結果を保存して、1 時間以内は見に行かない**（ボタンを押したときだけは見に行ってよい）
3. `ramen-mods-index.json` をダウンロードして読む
4. このランチャーに入っているラーメンMOD（`id` が `ramen-` で始まるもの）ごとに、入っている版と `version` をくらべる（`1.10.0 > 1.9.0` のように、数字ごとにくらべる）
5. 新しいものがあれば「ラーメンMOD に新しい版があります（ガチャガチャ 1.1.0 → 1.2.0 など）。更新しますか？」と聞く
6. 「はい」なら、各 zip をダウンロードして:
   - `size` と `sha256` が一覧と同じか確かめる。ちがえば、その MOD は入れない
   - いつもの「zip から MOD を入れる」処理で入れる
7. ゲームが起動しているときは、ダウンロードだけして「ゲームを閉じてから入れてください」と出す

## 5. 守ってほしいこと（大事）

- 各 MOD の **`data` フォルダと `config.lua` は消さない・上書きしない**（ガチャの回数・エモート・運勢・設定が入っている）。新しい zip に `config.lua` があっても、もう入っているならそのまま残す
- ダウンロード先は `api.github.com` と `github.com` / `objects.githubusercontent.com` だけ。ほかの URL から取らない
- 一覧にない MOD・`ramen-` で始まらない MOD には、さわらない
- 送るのは上の GET だけ。ほかの情報（PC の情報など）はどこにも送らない
- .NET Framework なら、通信の前に `ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;` を入れる

## 6. 試し方

1. ラーメンMOD の zip（例 `ramen-gacha-1.2.0.zip`）の中の `ramen-mod.json` の `version` を `1.0.0` に書きかえたものを、ランチャーで入れる
2. ランチャーを起動しなおす → 「新しい版があります」と出る
3. 「はい」→ 1.2.0 になる。`ue4ss/Mods/RamenGacha/data` の中身が消えていないことを確かめる
4. ネットを切って起動 → エラーを出さずにふつうに使える

できたら、直したファイルと、どこを変えたかを短く教えてください。
