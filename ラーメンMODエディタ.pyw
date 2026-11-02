"""ラーメンMODエディタ

Japanese Ramen Simulator の自作 MOD（RamenGacha・RamenOmikuji など）が保存しているものを、
画面で見て直すための道具。ゲーム本体のセーブ（お店のデータ）にはさわらない。

- ワールドごとの記録（Mods/RamenGacha/data・Mods/RamenOmikuji/data）
    ガチャ: 回した回数・天井のカウント・手に入れたエモート・台の向きと大きさ
    おみくじ: 今日の運勢・箱の向きと大きさ
- 設定（Mods/RamenGacha/config.lua・Mods/RamenOmikuji/config.lua）
- MOD のオン・オフ（Mods/mods.txt）

ゲームを閉じてから使う。保存の前に、元のファイルを data/backup にコピーする。
"""
import json
import re
import shutil
import subprocess
import tkinter as tk
from datetime import datetime
from pathlib import Path
from tkinter import messagebox, ttk

DEFAULT_MODS = Path(r"C:\Program Files (x86)\Steam\steamapps\common\Japanese Ramen Simulator"
                    r"\RealRamenSimulator\Binaries\Win64\ue4ss\Mods")
HERE = Path(__file__).resolve().parent
SETTINGS = HERE / "ラーメンMODエディタ_設定.json"   # ワールドのよび名と、Mods フォルダの場所

RAMEN_MODS = ["RamenEmotes", "RamenBenri", "RamenYoi", "RamenGacha", "RamenOmikuji"]
MOD_LABELS = {
    "RamenEmotes": "新しいエモート",
    "RamenBenri": "便利機能（バランス崩壊）",
    "RamenYoi": "酔っ払い・醤油一気飲み",
    "RamenGacha": "ガチャガチャ",
    "RamenOmikuji": "おみくじ箱",
}


# ---------------------------------------------------------------------------
# MOD が書く Lua の表を読む・書く（ramen_common.lua の ser() と同じ形）
# ---------------------------------------------------------------------------
class LuaReader:
    TOKEN = re.compile(r'\s*(\{|\}|\[|\]|=|,|"(?:\\.|[^"\\])*"|-?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?|true|false|nil)')

    def __init__(self, text):
        self.tokens = self.TOKEN.findall(text)
        self.i = 0

    def take(self):
        t = self.tokens[self.i]
        self.i += 1
        return t

    def value(self):
        t = self.take()
        if t == "{":
            return self.table()
        if t.startswith('"'):
            return json.loads(t)
        if t in ("true", "false"):
            return t == "true"
        if t == "nil":
            return None
        return float(t) if ("." in t or "e" in t.lower()) else int(t)

    def table(self):
        out = {}
        while self.tokens[self.i] != "}":
            self.take()                      # [
            key = self.value()
            self.take()                      # ]
            self.take()                      # =
            out[key] = self.value()
            if self.tokens[self.i] == ",":
                self.take()
        self.take()                          # }
        return out


def read_lua(path):
    text = path.read_text(encoding="utf-8")
    if not text.strip():
        return {}
    return LuaReader(text).value()


def write_lua(v, ind=""):
    if isinstance(v, dict):
        lines = ["{"]
        for k, x in v.items():
            key = json.dumps(k, ensure_ascii=False) if isinstance(k, str) else str(k)
            lines.append(f"{ind}  [{key}] = {write_lua(x, ind + '  ')},")
        lines.append(ind + "}")
        return "\n".join(lines)
    if isinstance(v, bool):
        return "true" if v else "false"
    if isinstance(v, str):
        return json.dumps(v, ensure_ascii=False)
    return repr(v)


# ---------------------------------------------------------------------------
# MOD のファイルから、エモート・運勢・効果の一覧を読む
# ---------------------------------------------------------------------------
def read_emotes(mods):
    src = (mods / "RamenEmotes" / "Scripts" / "main.lua").read_text(encoding="utf-8")
    return [("Men.Emote.Mod." + t, n) for t, n in re.findall(r'\{\s*tag\s*=\s*"(\w+)",\s*name\s*=\s*"([^"]+)"', src)]


def read_fortunes(mods):
    src = (mods / "RamenOmikuji" / "Scripts" / "main.lua").read_text(encoding="utf-8")
    fortunes = []
    for key, name, effects in re.findall(
            r'\{\s*key\s*=\s*"(\w+)",\s*name\s*=\s*"([^"]+)".*?effects\s*=\s*\{([^}]*)\}', src, re.S):
        fortunes.append((key, name, re.findall(r'"(\w+)"', effects)))
    texts = dict(re.findall(r'^\s*(\w+)\s*=\s*\{\s*text\s*=\s*"([^"]+)"', src, re.M))
    return fortunes, texts


def adjust_text(adj, name):
    # 置いた台の場所はゲームのセーブにある。ここには MOD で変えた向き・大きさだけ
    if not adj:
        return f"向き・大きさを変えた{name}: なし（置き場所はゲームの模様替えで決めます）"
    rows = [f"向き {a.get('yaw', 0):+.0f}°・大きさ ×{a.get('scale', 1):.2f}" for a in adj.values()]
    return f"向き・大きさを変えた{name}: {len(rows)} 台（" + "／".join(rows[:4]) + ("…" if len(rows) > 4 else "") + "）"


def game_running():
    try:
        out = subprocess.run(["tasklist", "/FI", "IMAGENAME eq RealRamenSimulator-Win64-Shipping.exe"],
                             capture_output=True, text=True, creationflags=0x08000000).stdout
        return "RealRamenSimulator" in out
    except OSError:
        return False


def backup(path):
    if path.exists():
        dest = path.parent / "backup"
        dest.mkdir(exist_ok=True)
        shutil.copy2(path, dest / f"{path.stem}_{datetime.now():%Y%m%d_%H%M%S}{path.suffix}")


# ---------------------------------------------------------------------------
# 画面
# ---------------------------------------------------------------------------
class App(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title("ラーメンMODエディタ")
        self.geometry("760x620")
        self.settings = self.load_settings()
        self.mods = Path(self.settings.get("mods", str(DEFAULT_MODS)))
        if not self.mods.exists():
            messagebox.showerror("ラーメンMODエディタ", f"Mods フォルダが見つかりません:\n{self.mods}")
        self.world = None
        self.gacha = {}
        self.omikuji = {}

        top = ttk.Frame(self, padding=8)
        top.pack(fill="x")
        ttk.Label(top, text="ワールド:").pack(side="left")
        self.world_box = ttk.Combobox(top, state="readonly", width=60)
        self.world_box.pack(side="left", padx=4)
        self.world_box.bind("<<ComboboxSelected>>", lambda e: self.select_world())
        ttk.Button(top, text="よび名をつける", command=self.rename_world).pack(side="left", padx=4)
        ttk.Button(top, text="読みなおす", command=self.refresh).pack(side="left")

        self.tabs = ttk.Notebook(self)
        self.tabs.pack(fill="both", expand=True, padx=8, pady=4)
        self.build_gacha_tab()
        self.build_omikuji_tab()
        self.build_config_tab()
        self.build_mods_tab()

        self.status = ttk.Label(self, text="", padding=6)
        self.status.pack(fill="x")
        self.refresh()

    # ----- 設定（よび名など） -----
    def load_settings(self):
        if SETTINGS.exists():
            try:
                return json.loads(SETTINGS.read_text(encoding="utf-8"))
            except ValueError:
                pass
        return {"mods": str(DEFAULT_MODS), "names": {}}

    def save_settings(self):
        SETTINGS.write_text(json.dumps(self.settings, ensure_ascii=False, indent=2), encoding="utf-8")

    def say(self, msg):
        self.status.config(text=msg)

    # ----- ワールド -----
    def worlds(self):
        found = {}
        for mod in ("RamenGacha", "RamenOmikuji"):
            d = self.mods / mod / "data"
            if d.exists():
                for f in d.glob("*.lua"):
                    found[f.stem] = max(found.get(f.stem, 0), f.stat().st_mtime)
        return sorted(found, key=lambda w: -found[w]), found

    def world_label(self, w, mtimes):
        name = self.settings.get("names", {}).get(w, "")
        when = datetime.fromtimestamp(mtimes[w]).strftime("%m/%d %H:%M")
        return f"{name or '（よび名なし）'}　{w}　最後に遊んだ: {when}"

    def refresh(self):
        ws, mt = self.worlds()
        self.world_ids = ws
        self.world_box["values"] = [self.world_label(w, mt) for w in ws]
        if not ws:
            self.say("まだ記録がありません。ゲームでガチャやおみくじを置くと、ここに出ます。")
            return
        idx = ws.index(self.world) if self.world in ws else 0
        self.world_box.current(idx)
        self.select_world()
        self.load_config()
        self.load_mods()

    def rename_world(self):
        if not self.world:
            return
        win = tk.Toplevel(self)
        win.title("よび名")
        var = tk.StringVar(value=self.settings.get("names", {}).get(self.world, ""))
        ttk.Entry(win, textvariable=var, width=30).pack(padx=10, pady=10)

        def ok():
            self.settings.setdefault("names", {})[self.world] = var.get()
            self.save_settings()
            win.destroy()
            self.refresh()
        ttk.Button(win, text="OK", command=ok).pack(pady=(0, 10))

    def data_path(self, mod):
        return self.mods / mod / "data" / f"{self.world}.lua"

    def select_world(self):
        i = self.world_box.current()
        if i < 0:
            return
        self.world = self.world_ids[i]
        g, o = self.data_path("RamenGacha"), self.data_path("RamenOmikuji")
        self.gacha = read_lua(g) if g.exists() else {}
        self.omikuji = read_lua(o) if o.exists() else {}
        self.show_gacha()
        self.show_omikuji()
        self.say("読みこみました。直したら「保存」を押してください（ゲームは閉じておく）。")

    def check_closed(self):
        if game_running():
            messagebox.showwarning("ラーメンMODエディタ", "ゲームが起動しています。\nゲームを閉じてから保存してください"
                                   "（起動中だと、ゲームが上書きします）。")
            return False
        return True

    # ----- ガチャ -----
    def build_gacha_tab(self):
        f = ttk.Frame(self.tabs, padding=10)
        self.tabs.add(f, text="ガチャ")
        row = ttk.Frame(f)
        row.pack(fill="x")
        ttk.Label(row, text="回した回数").grid(row=0, column=0, sticky="w")
        self.g_pulls = tk.IntVar()
        ttk.Spinbox(row, from_=0, to=999999, textvariable=self.g_pulls, width=10).grid(row=0, column=1, padx=6)
        ttk.Label(row, text="天井のカウント（「神」が出てから回した回数）").grid(row=1, column=0, sticky="w", pady=4)
        self.g_pity = tk.IntVar()
        ttk.Spinbox(row, from_=0, to=999, textvariable=self.g_pity, width=10).grid(row=1, column=1, padx=6)
        self.g_place = ttk.Label(f, text="")
        self.g_place.pack(anchor="w", pady=6)
        ttk.Button(f, text="ガチャガチャの向き・大きさを全部もとに戻す", command=self.reset_machine).pack(anchor="w")

        ttk.Label(f, text="手に入れたエモート（チェックを入れると使える）").pack(anchor="w", pady=(12, 2))
        box = ttk.Frame(f)
        box.pack(fill="x")
        self.g_emotes = {}
        try:
            emotes = read_emotes(self.mods)
        except OSError:
            emotes = []
        for i, (tag, name) in enumerate(emotes):
            v = tk.BooleanVar()
            ttk.Checkbutton(box, text=name, variable=v).grid(row=i // 3, column=i % 3, sticky="w", padx=6)
            self.g_emotes[tag] = v
        btns = ttk.Frame(f)
        btns.pack(anchor="w", pady=6)
        ttk.Button(btns, text="全部チェック", command=lambda: [v.set(True) for v in self.g_emotes.values()]).pack(side="left")
        ttk.Button(btns, text="全部はずす", command=lambda: [v.set(False) for v in self.g_emotes.values()]).pack(side="left", padx=6)
        ttk.Button(f, text="ガチャを保存", command=self.save_gacha).pack(anchor="e", pady=10)

    def show_gacha(self):
        g = self.gacha
        self.g_pulls.set(int(g.get("pulls", 0)))
        self.g_pity.set(int(g.get("pity", 0)))
        owned = g.get("emotes", {})
        for tag, v in self.g_emotes.items():
            v.set(bool(owned.get(tag)))
        self.g_place.config(text=adjust_text(g.get("adjust", {}), "ガチャガチャ"))

    def reset_machine(self):
        self.gacha.pop("adjust", None)
        self.show_gacha()
        self.say("ガチャガチャの向き・大きさを消しました（保存すると反映）")

    def save_gacha(self):
        if not self.world or not self.check_closed():
            return
        g = self.gacha
        g["pulls"] = int(self.g_pulls.get())
        g["pity"] = int(self.g_pity.get())
        g["emotes"] = {tag: True for tag, v in self.g_emotes.items() if v.get()}
        p = self.data_path("RamenGacha")
        p.parent.mkdir(parents=True, exist_ok=True)
        backup(p)
        p.write_text(write_lua(g), encoding="utf-8")
        self.say(f"保存しました: {p.name}（元のファイルは data/backup に）")

    # ----- おみくじ -----
    def build_omikuji_tab(self):
        f = ttk.Frame(self.tabs, padding=10)
        self.tabs.add(f, text="おみくじ")
        try:
            self.fortunes, self.effect_texts = read_fortunes(self.mods)
        except OSError:
            self.fortunes, self.effect_texts = [], {}
        ttk.Label(f, text="今日の運勢").grid(row=0, column=0, sticky="w")
        self.o_fortune = ttk.Combobox(f, state="readonly", width=12,
                                      values=["（なし）"] + [n for _, n, _ in self.fortunes])
        self.o_fortune.grid(row=0, column=1, sticky="w", padx=6)
        self.o_fortune.bind("<<ComboboxSelected>>", lambda e: self.fill_effects())
        ttk.Label(f, text="効果").grid(row=1, column=0, sticky="w", pady=6)
        self.o_effect = ttk.Combobox(f, state="readonly", width=50)
        self.o_effect.grid(row=1, column=1, sticky="w", padx=6)
        ttk.Label(f, text="何日目の運勢か（ゲームの日付。ちがう日になると切れる）").grid(row=2, column=0, sticky="w")
        self.o_day = tk.IntVar()
        ttk.Spinbox(f, from_=0, to=9999, textvariable=self.o_day, width=8).grid(row=2, column=1, sticky="w", padx=6)
        self.o_place = ttk.Label(f, text="")
        self.o_place.grid(row=3, column=0, columnspan=2, sticky="w", pady=10)
        ttk.Button(f, text="おみくじ箱の向き・大きさを全部もとに戻す", command=self.reset_box).grid(row=4, column=0, sticky="w")
        ttk.Button(f, text="おみくじを保存", command=self.save_omikuji).grid(row=5, column=1, sticky="e", pady=20)

    def fill_effects(self, current=None):
        name = self.o_fortune.get()
        effects = next((e for _, n, e in self.fortunes if n == name), [])
        labels = [f"{k}：{self.effect_texts.get(k, k)}" for k in effects]
        self.o_effect["values"] = labels
        if labels:
            keys = [k for k in effects]
            self.o_effect.current(keys.index(current) if current in keys else 0)
        else:
            self.o_effect.set("")

    def show_omikuji(self):
        o = self.omikuji
        f = o.get("fortune")
        if f:
            name = next((n for k, n, _ in self.fortunes if k == f.get("key")), "（なし）")
            self.o_fortune.set(name)
            self.fill_effects(f.get("effect"))
            self.o_day.set(int(f.get("day", 0)))
        else:
            self.o_fortune.set("（なし）")
            self.fill_effects()
            self.o_day.set(0)
        self.o_place.config(text=adjust_text(o.get("adjust", {}), "おみくじ箱"))

    def reset_box(self):
        self.omikuji.pop("adjust", None)
        self.show_omikuji()
        self.say("おみくじ箱の向き・大きさを消しました（保存すると反映）")

    def save_omikuji(self):
        if not self.world or not self.check_closed():
            return
        o = self.omikuji
        name = self.o_fortune.get()
        key = next((k for k, n, _ in self.fortunes if n == name), None)
        if key and self.o_effect.get():
            o["fortune"] = {"key": key, "effect": self.o_effect.get().split("：")[0], "day": int(self.o_day.get())}
        else:
            o.pop("fortune", None)
        p = self.data_path("RamenOmikuji")
        p.parent.mkdir(parents=True, exist_ok=True)
        backup(p)
        p.write_text(write_lua(o), encoding="utf-8")
        self.say(f"保存しました: {p.name}（元のファイルは data/backup に）")

    # ----- 設定（config.lua） -----
    def build_config_tab(self):
        f = ttk.Frame(self.tabs, padding=10)
        self.tabs.add(f, text="設定")
        self.cfg_vars = {}
        specs = [
            ("RamenGacha", "price", "ガチャ 1 回（$）"), ("RamenGacha", "price10", "ガチャ 10 連（$）"),
            ("RamenGacha", "ceiling", "天井（回）"), ("RamenGacha", "rainCount", "極: 具材の雨の数"),
            ("RamenGacha", "ramenCount", "神: ラーメンの雨の数"), ("RamenOmikuji", "price", "おみくじ 1 回（$）"),
        ]
        r = 0
        for mod, key, label in specs:
            ttk.Label(f, text=label).grid(row=r, column=0, sticky="w", pady=2)
            v = tk.StringVar()
            ttk.Entry(f, textvariable=v, width=10).grid(row=r, column=1, sticky="w", padx=6)
            self.cfg_vars[(mod, key)] = v
            r += 1
        ttk.Label(f, text="演出（チェックで使う）").grid(row=r, column=0, sticky="w", pady=(12, 2))
        r += 1
        self.fx_vars = {}
        fx = [("flash", "画面が光る"), ("shake", "画面がゆれる"), ("slowmo", "スローモーション"),
              ("presign", "前触れ"), ("fireworks", "キラキラ・お札の雨"), ("sound", "音")]
        for c, mod in enumerate(("RamenGacha", "RamenOmikuji")):
            ttk.Label(f, text="ガチャ" if c == 0 else "おみくじ").grid(row=r, column=c, sticky="w")
        r += 1
        for i, (key, label) in enumerate(fx):
            for c, mod in enumerate(("RamenGacha", "RamenOmikuji")):
                v = tk.BooleanVar()
                ttk.Checkbutton(f, text=label, variable=v).grid(row=r + i, column=c, sticky="w")
                self.fx_vars[(mod, key)] = v
        r += len(fx)
        self.share_vars = {}
        for c, mod in enumerate(("RamenGacha", "RamenOmikuji")):
            v = tk.BooleanVar()
            ttk.Checkbutton(f, text="すごい結果をみんなにも見せる", variable=v).grid(row=r, column=c, sticky="w", pady=6)
            self.share_vars[mod] = v
        ttk.Label(f, text="確率は config.lua の rates を直接書きかえてください（ゲームの起動しなおしで反映）。",
                  foreground="#666").grid(row=r + 1, column=0, columnspan=3, sticky="w", pady=8)
        ttk.Button(f, text="設定を保存", command=self.save_config).grid(row=r + 2, column=2, sticky="e")

    def config_path(self, mod):
        return self.mods / mod / "config.lua"

    def load_config(self):
        for mod in ("RamenGacha", "RamenOmikuji"):
            p = self.config_path(mod)
            text = p.read_text(encoding="utf-8") if p.exists() else ""
            for (m, key), v in self.cfg_vars.items():
                if m == mod:
                    mm = re.search(rf"^\s*{key}\s*=\s*(\d+)", text, re.M)
                    v.set(mm.group(1) if mm else "")
            for (m, key), v in self.fx_vars.items():
                if m == mod:
                    mm = re.search(rf"\b{key}\s*=\s*(true|false)", text)
                    v.set(mm is None or mm.group(1) == "true")
            mm = re.search(r"^\s*share\s*=\s*(true|false)", text, re.M)
            self.share_vars[mod].set(mm is None or mm.group(1) == "true")

    def save_config(self):
        for mod in ("RamenGacha", "RamenOmikuji"):
            p = self.config_path(mod)
            if not p.exists():
                continue
            text = p.read_text(encoding="utf-8")
            for (m, key), v in self.cfg_vars.items():
                if m == mod and v.get().strip().isdigit():
                    text = re.sub(rf"^(\s*{key}\s*=\s*)\d+", rf"\g<1>{v.get().strip()}", text, flags=re.M)
            for (m, key), v in self.fx_vars.items():
                if m == mod:
                    text = re.sub(rf"(\b{key}\s*=\s*)(true|false)", rf"\g<1>{'true' if v.get() else 'false'}", text)
            text = re.sub(r"^(\s*share\s*=\s*)(true|false)", rf"\g<1>{'true' if self.share_vars[mod].get() else 'false'}",
                          text, flags=re.M)
            backup(p)
            p.write_text(text, encoding="utf-8")
        self.say("設定を保存しました。ゲームを起動しなおすと反映されます。")

    # ----- MOD のオン・オフ -----
    def build_mods_tab(self):
        f = ttk.Frame(self.tabs, padding=10)
        self.tabs.add(f, text="MOD のオン・オフ")
        self.mod_vars = {}
        for i, m in enumerate(RAMEN_MODS):
            v = tk.BooleanVar()
            ttk.Checkbutton(f, text=f"{MOD_LABELS[m]}（{m}）", variable=v).grid(row=i, column=0, sticky="w", pady=2)
            self.mod_vars[m] = v
        ttk.Label(f, text="RamenGacha をオフにすると、新しいエモートは最初から全部使えるようになります。",
                  foreground="#666").grid(row=len(RAMEN_MODS), column=0, sticky="w", pady=10)
        ttk.Button(f, text="保存", command=self.save_mods).grid(row=len(RAMEN_MODS) + 1, column=0, sticky="e")

    def mods_txt(self):
        return self.mods / "mods.txt"

    def load_mods(self):
        p = self.mods_txt()
        text = p.read_text(encoding="utf-8") if p.exists() else ""
        for m, v in self.mod_vars.items():
            mm = re.search(rf"^{m}\s*:\s*(\d)", text, re.M)
            v.set(bool(mm and mm.group(1) == "1"))

    def save_mods(self):
        if not self.check_closed():
            return
        p = self.mods_txt()
        lines = p.read_text(encoding="utf-8").splitlines()
        lines = [l for l in lines if l.split(":")[0].strip() not in RAMEN_MODS]
        at = next((i for i, l in enumerate(lines) if l.startswith("; Built-in keybinds")), len(lines))
        new = [f"{m} : {1 if v.get() else 0}" for m, v in self.mod_vars.items()]
        lines[at:at] = new
        p.write_text("\n".join(lines) + "\n", encoding="utf-8")
        self.say("MOD のオン・オフを保存しました。ゲームを起動しなおすと反映されます。")


if __name__ == "__main__":
    App().mainloop()
