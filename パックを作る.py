"""ラーメン趣味レーター MOD のパックを作る。

ゲームのフォルダに入っている MOD（ue4ss\\Mods\\Ramen*）をもとに、
D:\\ramen-mod\\配布 に 3 つのパック（フォルダと zip）を作る。
どのパックも、中身を RealRamenSimulator\\Binaries\\Win64 にそのまま入れれば動く。
"""
import shutil
import sys
import zipfile
from pathlib import Path

WIN64 = Path(r"C:\Program Files (x86)\Steam\steamapps\common\Japanese Ramen Simulator"
             r"\RealRamenSimulator\Binaries\Win64")
OUT = Path(r"D:\ramen-mod\配布")

# パックの名前 → 入れる MOD
PACKS = {
    "おもしろ": ["RamenEmotes", "RamenYoi", "RamenGacha", "RamenOmikuji", "RamenWindow"],
    "バランス崩壊": ["RamenBenri", "RamenWindow"],
    "全部入り": ["RamenEmotes", "RamenBenri", "RamenYoi", "RamenGacha", "RamenOmikuji", "RamenWindow"],
}

# UE4SS についてくる MOD（コンソールとチート機能は Ramen の MOD が使う）
BASE_MODS = [
    ("CheatManagerEnablerMod", 1),
    ("ConsoleCommandsMod", 1),
    ("ConsoleEnablerMod", 1),
    ("SplitScreenMod", 0),
    ("LineTraceMod", 0),
    ("BPML_GenericFunctions", 1),
    ("BPModLoaderMod", 1),
]

DESCRIPTIONS = {
    "RamenEmotes": "新しいエモート 11 個（テーブル拭き・手裏剣・うんこ・醤油一気飲み など）。RamenGacha があると、ガチャで出るまで使えない",
    "RamenYoi": "カウンターでビールを飲むと酔っ払う。飲みすぎ・醤油一気飲みで倒れてリスポーン",
    "RamenGacha": "ガチャガチャ（パソコンで買って模様替えで置く。近くで E のメニューから 1 回 $10・10 連 $100、天井 100 回。向き・大きさもメニューで）",
    "RamenOmikuji": "おみくじ箱（パソコンで買って模様替えで置く。近くで E のメニューから引く $5。大吉〜大凶で、その日いっぱい効果が続く）",
    "RamenBenri": "便利機能（F1 でべんりメニュー：お金・ゲームの速さ・歩くはやさ・飛ぶ・ワープ・お店のレベル。前の Ctrl+F1〜F6 も使える）",
    "RamenWindow": "ウィンドウの大きさ（F11 でフルスクリーン → 1280x720 → 1600x900、Alt+Enter で切りかえ。次に起動しても残る）",
}

# パックに入れないもの
SKIP_NAMES = {"UE4SS.log", "mods.json"}
SKIP_SUFFIXES = {".dmp", ".log"}


def ignore(_dir, names):
    return [n for n in names
            if n in SKIP_NAMES or Path(n).suffix in SKIP_SUFFIXES]


def mods_txt(mods):
    lines = [f"{name} : {on}" for name, on in BASE_MODS]
    lines += [f"{name} : 1" for name in mods]
    lines += ["", "; Built-in keybinds, do not move up!", "Keybinds : 1", ""]
    return "\n".join(lines)


def readme(pack, mods):
    body = [f"ラーメン趣味レーター MOD「{pack}」パック", ""]
    body += [f"- {m}: {DESCRIPTIONS[m]}" for m in mods]
    body += [
        "",
        "くわしい遊び方は 説明書.md を見てね。",
        "",
        "入れ方（かんたん）:",
        "  1. この zip を右クリック →「すべて展開」",
        "  2. 展開したフォルダの「かんたん導入.bat」をダブルクリック（ゲームは閉じておく）",
        "     ゲームの場所は自動で探します。前のガチャの記録や設定は引きつぎます",
        "",
        "外し方（かんたん）:",
        "  「かんたん削除.bat」をダブルクリック（外したものは消さずに ramenmod_backup フォルダへ移します）",
        "",
        "自分で入れるとき:",
        "  dwmapi.dll と ue4ss フォルダを、次のフォルダにそのまま入れる",
        "  Japanese Ramen Simulator\\RealRamenSimulator\\Binaries\\Win64",
        "",
        "友だちと遊ぶとき:",
        "  みんな同じパックを入れてね。お金が動くもの（便利機能・ガチャ・おみくじの売り上げ）はホストのほうが確実。",
        "",
        "UE4SS（https://github.com/UE4SS-RE/RE-UE4SS、MIT ライセンス）を使っています。",
        "",
    ]
    return "\n".join(body)


def build(pack, mods):
    dest = OUT / pack
    if dest.exists():
        shutil.rmtree(dest)
    ue = dest / "ue4ss"
    (ue / "Mods").mkdir(parents=True)

    shutil.copy2(WIN64 / "dwmapi.dll", dest)
    for f in ["UE4SS.dll", "UE4SS-settings.ini", "LICENSE"]:
        shutil.copy2(WIN64 / "ue4ss" / f, ue)
    shutil.copytree(WIN64 / "ue4ss" / "UE4SS_SDK_Backends", ue / "UE4SS_SDK_Backends", ignore=ignore)

    src_mods = WIN64 / "ue4ss" / "Mods"
    for name, _ in BASE_MODS + [("Keybinds", 1)]:
        shutil.copytree(src_mods / name, ue / "Mods" / name, ignore=ignore)
    shutil.copytree(src_mods / "shared", ue / "Mods" / "shared", ignore=ignore)
    for name in mods:
        # 自作の MOD は src\Mods から（ゲームのフォルダには入れていない）
        shutil.copytree(Path(__file__).resolve().parent / "src" / "Mods" / name, ue / "Mods" / name, ignore=ignore)
        # ワールドごとの記録（data）は中身を入れない
        data = ue / "Mods" / name / "data"
        if data.exists():
            shutil.rmtree(data)
            data.mkdir()
            (data / "keep.txt").write_text("world data goes here", encoding="utf-8")

    (ue / "Mods" / "mods.txt").write_text(mods_txt(mods), encoding="utf-8")
    (dest / "はじめに読んでね.txt").write_text(readme(pack, mods), encoding="utf-8-sig")
    shutil.copy2(Path(__file__).resolve().parent / "説明書.md", dest / "説明書.md")
    # かんたん導入・削除の道具
    for f in (Path(__file__).resolve().parent / "導入ツール").iterdir():
        shutil.copy2(f, dest / f.name)

    zpath = OUT / f"ラーメンMOD_{pack}.zip"
    with zipfile.ZipFile(zpath, "w", zipfile.ZIP_DEFLATED) as z:
        for f in sorted(dest.rglob("*")):
            if f.is_file():
                z.write(f, f.relative_to(dest))
    return zpath


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    for pack, mods in PACKS.items():
        z = build(pack, mods)
        print(f"{pack}: {', '.join(mods)} -> {z.name} ({z.stat().st_size // 1024} KB)")


if __name__ == "__main__":
    sys.exit(main())
