"""友だちの RamenModLauncher 用の zip を作る（1 つの zip に 1 つの MOD）。

D:\\ramen-mod\\src\\Mods の MOD から、D:\\ramen-mod\\配布\\ランチャー用 に作る。
ランチャーの決まり（README・docs\\METADATA.md と、ランチャーが許しているファイルの種類）:
  - 1 zip 1 MOD。<MOD名>/Scripts/main.lua の形
  - 入れていいファイル: .lua .png .jpg .jpeg .json .md .txt .ini .cfg
  - UE4SS 本体・dll・exe・bat・ps1 は入れない
  - ZIP 直下に ramen-mod.json を 1 つ
"""
import hashlib
import json
import re
import sys
import zipfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
SRC = HERE / "src" / "Mods"
OUT = HERE / "配布" / "ランチャー用"
# 版は launcher\build.py の VERSION とそろえる（GitHub のリリースと同じ番号にする）
VERSION = re.search(r'^VERSION = "([^"]+)"', (HERE / "launcher" / "build.py").read_text(encoding="utf-8"), re.M).group(1)
ALLOWED = {".lua", ".png", ".jpg", ".jpeg", ".json", ".md", ".txt", ".ini", ".cfg"}
REPO_URL = "https://github.com/kazu20080619-byte/ramen-mod"
INDEX = OUT / "ramen-mods-index.json"  # 友だちのランチャーが読む一覧（GitHub のリリースにも上げる）

MODS = {
    "RamenEmotes": {
        "id": "ramen-emotes", "name": "ラーメンMOD: 新しいエモート", "multiplayer": "Unknown",
        "description": "テーブル拭き・手裏剣・うんこ・醤油一気飲み など 11 個のエモート。ガチャガチャ（ramen-gacha）を入れると、ガチャで出るまで使えない。",
    },
    "RamenYoi": {
        "id": "ramen-yoi", "name": "ラーメンMOD: 酔っ払い", "multiplayer": "Unknown",
        "description": "カウンターでビールを飲むと酔っ払う。飲みすぎ・醤油一気飲みで倒れて、お店の前で目が覚める。",
    },
    "RamenGacha": {
        "id": "ramen-gacha", "name": "ラーメンMOD: ガチャガチャ", "multiplayer": "Host Only",
        "description": "パソコンで「ガチャガチャ」を買って置く（清掃中看板を借りる）。台の前で E を押すとメニューが出て、1 回・10 連・向き・大きさ・動かすを選べる。天井 100 回。具材・ラーメンの雨・エモートが出る。エモートは ramen-emotes といっしょに使う。",
    },
    "RamenOmikuji": {
        "id": "ramen-omikuji", "name": "ラーメンMOD: おみくじ箱", "multiplayer": "Host Only",
        "description": "パソコンで「おみくじ箱」を買って置く（モップの飾りを借りる）。大吉〜大凶で、その日いっぱい効果が続く。酔っ払いの効果は ramen-yoi といっしょに使う。",
    },
    "RamenBenri": {
        "id": "ramen-benri", "name": "ラーメンMOD: 便利機能（バランス崩壊）", "multiplayer": "Host Only",
        "description": "F1 でべんりメニュー：お金・ゲームの速さ・歩くはやさ・飛ぶ／壁すりぬけ・ワープ（5 か所）・お店のレベルと経験値。前の Ctrl+F1〜F6 とコンソールの benri も使える。",
    },
    "RamenWindow": {
        "id": "ramen-window", "name": "ラーメンMOD: ウィンドウの大きさ", "multiplayer": "Client Only",
        "description": "F11 でフルスクリーン → ウィンドウ 1280x720 → 1600x900 と切りかえ。Alt+Enter でフルスクリーン ⇄ ウィンドウ。コンソールの ramenwindow。自分の画面だけが変わる。",
    },}

# 共通部品（ramen_common.lua）を Scripts に入れておく MOD
NEEDS_COMMON = {"RamenGacha", "RamenOmikuji", "RamenBenri", "RamenWindow"}
COMMON = HERE / "src" / "ramen_common.lua"

ID_RE = re.compile(r"^[a-z0-9][a-zA-Z0-9._-]{0,79}$")


def metadata(info):
    return {
        "id": info["id"],
        "name": info["name"],
        "version": VERSION,
        "app_id": "4968640",
        "game_builds": [],
        "ue4ss": ">=3.0.1",
        "dependencies": [],
        "multiplayer": info["multiplayer"],
        "assets": [],
        "source": REPO_URL,
        "description": info["description"],
    }


def check(zpath, mod):
    """ランチャーの決まりに合っているか、自分でも確かめる"""
    problems = []
    with zipfile.ZipFile(zpath) as z:
        names = z.namelist()
        mains = [n for n in names if n.lower().endswith("/scripts/main.lua")]
        if mains != [f"{mod}/Scripts/main.lua"]:
            problems.append(f"main.lua の場所: {mains}")
        for n in names:
            if n.endswith("/"):
                continue
            if Path(n).suffix.lower() not in ALLOWED:
                problems.append(f"入れられない種類: {n}")
            if n.startswith("/") or ".." in n.split("/") or ":" in n or "\\" in n:
                problems.append(f"あぶないパス: {n}")
            if n != "ramen-mod.json" and not n.startswith(mod + "/"):
                problems.append(f"MOD フォルダの外: {n}")
        if mod in NEEDS_COMMON and f"{mod}/Scripts/ramen_common.lua" not in names:
            problems.append("Scripts/ramen_common.lua がない")
        meta = json.loads(z.read("ramen-mod.json").decode("utf-8"))
        if not ID_RE.match(meta["id"]):
            problems.append("id の形")
        if meta["app_id"] != "4968640":
            problems.append("app_id")
        if len(z.read("ramen-mod.json")) > 64 * 1024:
            problems.append("json が大きすぎる")
        total = sum(i.file_size for i in z.infolist())
        if total > 256 * 1024 * 1024 or len(names) > 5000:
            problems.append("大きすぎる")
    return problems


def build(mod, info):
    src = SRC / mod
    zpath = OUT / f"{info['id']}-{VERSION}.zip"
    with zipfile.ZipFile(zpath, "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr("ramen-mod.json", json.dumps(metadata(info), ensure_ascii=False, indent=2))
        for f in sorted(src.rglob("*")):
            rel = f.relative_to(src)
            if f.is_dir() or rel.parts[0] == "data":
                continue  # ワールドごとの記録・書き出した絵は入れない
            z.write(f, f"{mod}/{rel.as_posix()}")
        # src\Mods の Scripts に ramen_common.lua がなければ、src の元のものを入れる
        if mod in NEEDS_COMMON and not (src / "Scripts" / "ramen_common.lua").exists():
            z.write(COMMON, f"{mod}/Scripts/ramen_common.lua")
    return zpath


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    for old in OUT.glob("*.zip"):
        old.unlink()
    ok = True
    index = {"format": 1, "version": VERSION, "tag": f"v{VERSION}", "source": REPO_URL, "mods": []}
    for mod, info in MODS.items():
        z = build(mod, info)
        problems = check(z, mod)
        print(f"{z.name}: {'OK' if not problems else problems}")
        ok = ok and not problems
        index["mods"].append({
            "id": info["id"], "folder": mod, "name": info["name"], "version": VERSION,
            "file": z.name, "size": z.stat().st_size,
            "sha256": hashlib.sha256(z.read_bytes()).hexdigest(),
            "multiplayer": info["multiplayer"], "description": info["description"],
        })
    INDEX.write_text(json.dumps(index, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"{INDEX.name}: {len(index['mods'])} MOD")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
