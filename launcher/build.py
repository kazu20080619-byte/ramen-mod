"""ラーメンMODランチャーを exe にして、配布用のフォルダと zip を作る。

  python build.py

- Windows 付属の C# コンパイラ（csc.exe、.NET Framework 4）で RamenLauncher.exe を作る
- dist\\ラーメンMODランチャー\\ に exe・payload（UE4SS 本体と自作 MOD・version.txt）・説明書をまとめる
- D:\\ramen-mod\\配布\\ に作るもの:
    ラーメンMODランチャー.zip（展開して使う、ふつうの配布）
    ramen-mods.zip と RamenLauncher.exe（GitHub Releases に上げて、ランチャーの自動更新で使う）
"""
import shutil
import subprocess
import sys
import zipfile
from pathlib import Path

VERSION = "1.2.0"   # ランチャーと MOD の版。リリースのタグは v<VERSION>

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
CSC = Path(r"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe")
DIST = HERE / "dist" / "ラーメンMODランチャー"
OUT = ROOT / "配布"
ZIP = OUT / "ラーメンMODランチャー.zip"
MODS_ZIP = OUT / "ramen-mods.zip"
EXE_OUT = OUT / "RamenLauncher.exe"
MODS = ["RamenEmotes", "RamenYoi", "RamenGacha", "RamenOmikuji", "RamenBenri", "RamenWindow"]
UE4SS = ROOT / "tools" / "ue4ss"          # UE4SS の元ファイル（dwmapi.dll と ue4ss フォルダ）
SKIP = {"UE4SS.log", "mods.json"}


def ignore(_dir, names):
    return [n for n in names if n in SKIP or n.endswith((".log", ".dmp")) or n == "data"]


def write_build_info():
    """版を exe に入れるための C# ファイル（手で直さない）"""
    obj = HERE / "obj"
    obj.mkdir(exist_ok=True)
    f = obj / "BuildInfo.cs"
    f.write_text(
        "// build.py が作る。手で直さない\n"
        f'[assembly: System.Reflection.AssemblyVersion("{VERSION}.0")]\n'
        f'[assembly: System.Reflection.AssemblyFileVersion("{VERSION}.0")]\n'
        "namespace RamenLauncher\n{\n"
        f'    public static class BuildInfo {{ public const string Version = "{VERSION}"; }}\n'
        "}\n",
        encoding="utf-8")
    return f


def zip_dir(src, zpath, prefix=None):
    with zipfile.ZipFile(zpath, "w", zipfile.ZIP_DEFLATED) as z:
        for f in sorted(src.rglob("*")):
            if f.is_file():
                rel = f.relative_to(src)
                z.write(f, Path(prefix) / rel if prefix else rel)


def main():
    if DIST.exists():
        shutil.rmtree(DIST)
    DIST.mkdir(parents=True)
    exe = DIST / "RamenLauncher.exe"
    cmd = [str(CSC), "/nologo", "/target:winexe", "/codepage:65001", "/optimize+", f"/out:{exe}",
           "/r:System.Windows.Forms.dll", "/r:System.Drawing.dll", "/r:System.Core.dll",
           "/r:System.IO.Compression.dll", "/r:System.IO.Compression.FileSystem.dll",
           str(HERE / "Core.cs"), str(HERE / "SaveData.cs"), str(HERE / "Program.cs"), str(write_build_info())]
    r = subprocess.run(cmd, capture_output=True, text=True, encoding="cp932", errors="replace")
    print(r.stdout, r.stderr)
    if r.returncode != 0:
        return 1

    missing = [m for m in MODS if not (ROOT / "src" / "Mods" / m).is_dir()]
    if missing:
        print(f"exe はできたが、src\\Mods に MOD がないので payload と zip は作らない: {', '.join(missing)}")
        return 1

    pay = DIST / "payload"
    pay.mkdir()
    shutil.copy2(UE4SS / "dwmapi.dll", pay / "dwmapi.dll")
    shutil.copytree(UE4SS / "ue4ss", pay / "ue4ss", ignore=ignore)
    for m in MODS:
        shutil.copytree(ROOT / "src" / "Mods" / m, pay / "mods" / m, ignore=ignore)
    (pay / "version.txt").write_text(VERSION + "\n", encoding="utf-8")
    shutil.copy2(ROOT / "説明書.md", DIST / "説明書.md")
    shutil.copy2(HERE / "つかいかた.txt", DIST / "つかいかた.txt")

    OUT.mkdir(parents=True, exist_ok=True)
    zip_dir(DIST, ZIP, "ラーメンMODランチャー")
    zip_dir(pay, MODS_ZIP)          # 中身は payload そのもの（ue4ss/・dwmapi.dll・mods/・version.txt）
    shutil.copy2(exe, EXE_OUT)
    for f in (ZIP, MODS_ZIP, EXE_OUT):
        print(f"ok: {f} ({f.stat().st_size // 1024} KB)")
    print(f"version: {VERSION}（タグ v{VERSION}）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
