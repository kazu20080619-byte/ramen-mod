"""GitHub Releases に上げるものを作って、gh のコマンドを出す（gh はここでは動かさない）。

  python リリースを作る.py

1. launcher\\build.py を動かす（配布\\ramen-mods.zip と 配布\\RamenLauncher.exe ができる）
2. 上げるための gh release create のコマンドを表示する
"""
import importlib.util
import shutil
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
BUILD = HERE / "launcher" / "build.py"


def load_build():
    spec = importlib.util.spec_from_file_location("ramen_build", BUILD)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def main():
    r = subprocess.run([sys.executable, str(BUILD)])
    if r.returncode != 0:
        print("build.py が失敗しました。リリースは作りません。")
        return 1
    b = load_build()
    tag = f"v{b.VERSION}"
    for f in (b.MODS_ZIP, b.EXE_OUT):
        if not f.exists():
            print(f"ない: {f}")
            return 1
    notes = (f"ラーメンMOD {tag}。ランチャーを使っている人は、起動すると「更新しますか？」と聞かれます。"
             "はじめての人は RamenModLauncher.zip を展開して RamenLauncher.exe を起動してね。")
    print()
    print("GitHub に上げるときは、D:\\ramen-mod で次を動かす:")
    print()
    # GitHub はファイル名の日本語を消すので、英語の名前のコピーを上げる
    shutil.copy2(HERE / "配布" / "ラーメンMODランチャー.zip", HERE / "配布" / "RamenModLauncher.zip")
    # 友だちのランチャー用（1 MOD = 1 zip）と一覧も作って、いっしょに上げる
    if subprocess.run([sys.executable, str(HERE / "ランチャー用を作る.py")]).returncode != 0:
        print("ランチャー用を作る.py が失敗しました。リリースは作りません。")
        return 1
    print(f'gh release create {tag} 配布/ramen-mods.zip 配布/RamenLauncher.exe 配布/RamenModLauncher.zip '
          f'配布/ランチャー用/*.zip 配布/ランチャー用/ramen-mods-index.json '
          f'--title "ラーメンMOD {tag}" --notes "{notes}"')
    return 0


if __name__ == "__main__":
    sys.exit(main())
