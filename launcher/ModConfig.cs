// ラーメンMODランチャー: MOD の config.lua を読んで、知っている項目の値だけ書きかえる
// （その人のファイルのコメントや、知らない項目はそのまま残す）
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RamenLauncher
{
    public enum FieldKind { Bool, Int, Number, Key, IntList, NumList, SizeList, Choice }

    public class ConfigField
    {
        public string Name;      // "price"、表の中なら "keys.use" / "fx.flash"
        public FieldKind Kind;
        public string Label;
        public string Default;   // 画面での形（"true"、"10"、"E"、"1, 2, 4"、"1280x720, 1600x900"）
        public double Min = 0, Max = 1e9;
        public string[] Choices, ChoiceLabels;   // Choice のとき: Lua の値と、画面に出す名前
        public string Group = "設定";             // 画面でまとめる見出し
        public bool Chip;                         // 小さいチェックをならべて出す（演出など）

        public ConfigField(string name, FieldKind kind, string label, string def)
        {
            Name = name; Kind = kind; Label = label; Default = def;
        }
        public ConfigField Range(double min, double max) { Min = min; Max = max; return this; }
    }

    public static class ModConfig
    {
        static ConfigField B(string n, string l, string d) { return new ConfigField(n, FieldKind.Bool, l, d); }
        static ConfigField I(string n, string l, string d) { return new ConfigField(n, FieldKind.Int, l, d); }
        static ConfigField N(string n, string l, string d) { return new ConfigField(n, FieldKind.Number, l, d); }
        static ConfigField K(string n, string l, string d) { return new ConfigField(n, FieldKind.Key, l, d); }

        static ConfigField[] Fx(string[] labels)
        {
            var names = new[] { "flash", "shake", "slowmo", "presign", "fireworks", "sound" };
            return names.Select((n, i) => B("fx." + n, "演出: " + labels[i], "true")).ToArray();
        }
        static readonly string[] FxLabels = { "光る", "ゆれる", "スロー", "前触れ", "キラキラ", "音" };

        // MOD ごとの項目。ramen-launcher.json に config があればそれ、なければ下の組みこみの表
        public static Dictionary<string, ConfigField[]> Fields
        {
            get
            {
                var d = new Dictionary<string, ConfigField[]>();
                foreach (var m in Catalog.Mods)
                {
                    if (m.Config != null && m.Config.Length > 0) d[m.Folder] = m.Config;
                    else if (BuiltinFields.ContainsKey(m.Folder)) d[m.Folder] = BuiltinFields[m.Folder];
                }
                return d;
            }
        }

        static readonly Dictionary<string, ConfigField[]> BuiltinFields = new Dictionary<string, ConfigField[]> {
            { "RamenGacha", new[] {
                I("price", "1 回の値段（$）", "10"),
                I("price10", "10 連の値段（$）", "100"),
                I("ceiling", "天井（回）", "100").Range(1, 1000),
                B("tenPull", "メニューに 10 連を出す", "true"),
                I("shopPrice", "台の値段（パソコン、$）", "50"),
                K("keys.use", "メニューを開くキー", "E"),
                I("yawStep", "向き 1 回の角度（度）", "5").Range(1, 90),
                N("scaleStep", "大きさ 1 回の量", "0.05").Range(0.01, 1),
                B("showPrompt", "台の前に案内を出す", "true"),
                B("share", "すごい結果をみんなにも見せる", "true"),
                I("rainCount", "極: 具材の雨の数", "20").Range(0, 100),
                I("ramenCount", "神: ラーメンの雨の数", "8").Range(0, 50),
            }.Concat(Fx(FxLabels)).ToArray() },
            { "RamenOmikuji", new[] {
                I("price", "1 回の値段（$）", "5"),
                I("shopPrice", "箱の値段（パソコン、$）", "30"),
                K("keys.use", "メニューを開くキー", "E"),
                I("yawStep", "向き 1 回の角度（度）", "5").Range(1, 90),
                N("scaleStep", "大きさ 1 回の量", "0.05").Range(0.01, 1),
                B("showPrompt", "箱の前に案内を出す", "true"),
                B("share", "大吉・大凶をみんなにも見せる", "true"),
            }.Concat(Fx(FxLabels)).ToArray() },
            { "RamenBenri", new[] {
                K("menuKey", "メニューを開くキー", "F1"),
                B("ctrlKeys", "Ctrl+F1〜F6 も使う", "true"),
                I("ctrlMoney", "Ctrl+F1 で足すお金（$）", "10000"),
                new ConfigField("money", FieldKind.IntList, "お金の候補（$）", "1000, 10000, 100000, 1000000"),
                I("moneyStart", "最初に選ぶ候補（何番目）", "2").Range(1, 20),
                new ConfigField("speeds", FieldKind.NumList, "ゲームの速さの候補（倍）", "1.0, 2.0, 4.0, 8.0, 0.5").Range(0.1, 20),
                new ConfigField("walkSpeeds", FieldKind.NumList, "歩くはやさの候補（倍）", "1.0, 2.0, 3.0, 0.5").Range(0.1, 20),
                I("slots", "覚えられる場所の数", "5").Range(1, 20),
                B("rememberPlaces", "覚えた場所をワールドごとに保存", "true"),
                I("maxLevel", "お店のレベルの上限", "30").Range(1, 99),
            } },
            { "RamenYoi", new[] {
                B("beer", "ビールで酔う", "true"),
                B("shoyu", "醤油一気飲みで倒れる", "true"),
                B("passOutOn", "飲みすぎで倒れる", "true"),
                N("strength", "ゆれの強さ（倍）", "1.0").Range(0, 5),
                I("beerSip", "1 口で増える酔い", "25").Range(1, 100),
                N("decayPerSec", "1 秒でさめる量", "0.4").Range(0, 10),
            } },
            { "RamenEmotes", new[] {
                B("gachaOnly", "新しいエモートはガチャで出たものだけ", "true"),
            } },
            { "RamenWindow", new[] {
                K("key", "切りかえキー", "F11"),
                B("altEnter", "Alt+Enter でも切りかえる", "true"),
                new ConfigField("fullMode", FieldKind.Choice, "フルスクリーンの種類", "nil") {
                    Choices = new[] { "nil", "0", "1" }, ChoiceLabels = new[] { "起動したときのまま", "ふつう", "ボーダーレス" } },
                new ConfigField("sizes", FieldKind.SizeList, "ウィンドウの大きさ（横x縦）", "1280x720, 1600x900"),
                B("message", "切りかえたらチャットに出す", "true"),
            } },
        };

        // ------------------------------------------------------------------
        // ファイルの場所
        // ------------------------------------------------------------------
        public static string GamePath(string win64, string mod) { return Path.Combine(Core.ModsDir(win64), mod, "config.lua"); }
        public static string DefaultPath(string mod)
        {
            var m = Catalog.Find(mod);
            return Path.Combine(m != null && m.SourceDir != null ? m.SourceDir : Path.Combine(Core.PayloadDir, "mods", mod), "config.lua");
        }

        // ゲームのものがなければ、payload のはじめの設定を読む
        public static string LoadText(string win64, string mod, out string from)
        {
            var g = win64 == null ? null : GamePath(win64, mod);
            if (g != null && File.Exists(g)) { from = g; return File.ReadAllText(g, Encoding.UTF8); }
            var d = DefaultPath(mod);
            if (File.Exists(d)) { from = d; return File.ReadAllText(d, Encoding.UTF8); }
            from = null;
            return "return {\n}\n";
        }

        public static void SaveText(string win64, string mod, string text)
        {
            var g = GamePath(win64, mod);
            Directory.CreateDirectory(Path.GetDirectoryName(g));
            if (File.Exists(g)) File.Copy(g, g + ".bak", true);
            File.WriteAllText(g, text, new UTF8Encoding(false));
        }

        public static void ResetToDefault(string win64, string mod)
        {
            var d = DefaultPath(mod);
            if (!File.Exists(d)) throw new FileNotFoundException("payload に " + mod + " のはじめの設定がありません");
            SaveText(win64, mod, File.ReadAllText(d, Encoding.UTF8));
        }

        // ------------------------------------------------------------------
        // 値の形（Lua の書き方 ⇄ 画面の書き方）
        // ------------------------------------------------------------------
        const string Scalar = "\"[^\"\\r\\n]*\"|[-+\\w.]+";
        const string Table = "\\{(?:[^{}]|\\{[^{}]*\\})*\\}";

        static string ValuePattern(ConfigField f)
        {
            return f.Kind == FieldKind.IntList || f.Kind == FieldKind.NumList || f.Kind == FieldKind.SizeList ? Table : Scalar;
        }

        static string Num(double v)
        {
            return v.ToString("0.0#####", CultureInfo.InvariantCulture);
        }

        static string[] Items(string s)
        {
            return s.Split(new[] { ',', '、', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }

        public static string ToDisplay(ConfigField f, string lua)
        {
            lua = lua.Trim();
            switch (f.Kind)
            {
                case FieldKind.Key: return lua.Trim('"');
                case FieldKind.IntList:
                case FieldKind.NumList:
                    return string.Join(", ", Items(lua.Trim('{', '}')));
                case FieldKind.SizeList:
                    return string.Join(", ", Regex.Matches(lua, @"\{\s*(\d+)\s*,\s*(\d+)\s*\}").Cast<Match>().Select(m => m.Groups[1].Value + "x" + m.Groups[2].Value));
                default: return lua;
            }
        }

        // 画面の値を確かめて、Lua の書き方にする。だめなら FormatException（理由は日本語）
        public static string ToLua(ConfigField f, string text)
        {
            var t = (text ?? "").Trim();
            var bad = new FormatException(f.Label + ": 「" + t + "」は使えません");
            switch (f.Kind)
            {
                case FieldKind.Bool:
                    if (t == "true" || t == "false") return t;
                    throw bad;
                case FieldKind.Int:
                    {
                        int v;
                        if (!int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) throw bad;
                        if (v < f.Min || v > f.Max) throw new FormatException(f.Label + ": " + f.Min + "〜" + f.Max + " にしてください");
                        return v.ToString(CultureInfo.InvariantCulture);
                    }
                case FieldKind.Number:
                    {
                        double v;
                        if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) throw bad;
                        if (v < f.Min || v > f.Max) throw new FormatException(f.Label + ": " + f.Min + "〜" + f.Max + " にしてください");
                        return Num(v);
                    }
                case FieldKind.Key:
                    if (!Regex.IsMatch(t, "^[A-Za-z0-9_]{1,30}$")) throw new FormatException(f.Label + ": キーの名前は英字・数字・_ だけ（例: E、F1、F11、INS、HOME）");
                    return "\"" + t.ToUpperInvariant() + "\"";
                case FieldKind.IntList:
                    {
                        var vals = new List<string>();
                        foreach (var s in Items(t))
                        {
                            long v;
                            if (!long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) || v < f.Min || v > f.Max) throw new FormatException(f.Label + ": 「" + s + "」は使えません（例: 1000, 10000）");
                            vals.Add(v.ToString(CultureInfo.InvariantCulture));
                        }
                        if (vals.Count == 0) throw new FormatException(f.Label + ": 1 つ以上書いてください（例: 1000, 10000）");
                        return "{ " + string.Join(", ", vals) + " }";
                    }
                case FieldKind.NumList:
                    {
                        var vals = new List<string>();
                        foreach (var s in Items(t))
                        {
                            double v;
                            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) || v < f.Min || v > f.Max) throw new FormatException(f.Label + ": 「" + s + "」は使えません（" + f.Min + "〜" + f.Max + "、例: 1.0, 2.0）");
                            vals.Add(Num(v));
                        }
                        if (vals.Count == 0) throw new FormatException(f.Label + ": 1 つ以上書いてください（例: 1.0, 2.0）");
                        return "{ " + string.Join(", ", vals) + " }";
                    }
                case FieldKind.SizeList:
                    {
                        var vals = new List<string>();
                        foreach (var s in t.Split(new[] { ',', '、' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var m = Regex.Match(s.Trim(), @"^(\d{3,5})\s*[xX×*]\s*(\d{3,5})$");
                            if (!m.Success) throw new FormatException(f.Label + ": 「" + s.Trim() + "」は使えません（例: 1280x720, 1600x900）");
                            int w = int.Parse(m.Groups[1].Value), h = int.Parse(m.Groups[2].Value);
                            if (w < 320 || w > 7680 || h < 240 || h > 4320) throw new FormatException(f.Label + ": 「" + s.Trim() + "」は大きさがおかしい");
                            vals.Add("{ " + w + ", " + h + " }");
                        }
                        if (vals.Count == 0) throw new FormatException(f.Label + ": 1 つ以上書いてください（例: 1280x720）");
                        return "{ " + string.Join(", ", vals) + " }";
                    }
                case FieldKind.Choice:
                    if (f.Choices.Contains(t)) return t;
                    throw bad;
            }
            throw bad;
        }

        // ------------------------------------------------------------------
        // 読む・書く
        // ------------------------------------------------------------------
        static Regex TopRegex(string name, ConfigField f)
        {
            return new Regex(@"^([ \t]*)" + Regex.Escape(name) + @"[ \t]*=[ \t]*(" + ValuePattern(f) + ")([ \t]*,?)([^\r\n]*)$", RegexOptions.Multiline);
        }

        // 表（keys = { ... }）の中身の場所。start は { の位置、end は } の位置
        static bool FindTable(string text, string parent, out int start, out int end)
        {
            start = end = -1;
            var m = Regex.Match(text, @"^[ \t]*" + Regex.Escape(parent) + @"[ \t]*=[ \t]*\{", RegexOptions.Multiline);
            if (!m.Success) return false;
            start = m.Index + m.Length - 1;
            int depth = 0;
            for (int i = start; i < text.Length; i++)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}' && --depth == 0) { end = i; return true; }
            }
            return false;
        }

        static Regex InnerRegex(string child, ConfigField f)
        {
            return new Regex(@"(?<![\w.])" + Regex.Escape(child) + @"(\s*=\s*)(" + ValuePattern(f) + ")");
        }

        // ファイルの値（画面の形）。ないときは null
        public static string Read(string text, ConfigField f)
        {
            var dot = f.Name.IndexOf('.');
            if (dot < 0)
            {
                var m = TopRegex(f.Name, f).Match(text);
                return m.Success ? ToDisplay(f, m.Groups[2].Value) : null;
            }
            int s, e;
            if (!FindTable(text, f.Name.Substring(0, dot), out s, out e)) return null;
            var inner = InnerRegex(f.Name.Substring(dot + 1), f).Match(text.Substring(s + 1, e - s - 1));
            return inner.Success ? ToDisplay(f, inner.Groups[2].Value) : null;
        }

        // 1 つの項目を書きかえた文を返す（display はもう ToLua で確かめたもの、でなければここで確かめる）
        public static string Write(string text, ConfigField f, string display)
        {
            var lua = ToLua(f, display);
            var dot = f.Name.IndexOf('.');
            if (dot < 0)
            {
                var re = TopRegex(f.Name, f);
                if (re.IsMatch(text))
                    return re.Replace(text, m => m.Groups[1].Value + f.Name + " = " + lua + m.Groups[3].Value + m.Groups[4].Value, 1);
                return InsertTop(text, f.Name + " = " + lua + ",  -- " + f.Label);
            }
            var parent = f.Name.Substring(0, dot);
            var child = f.Name.Substring(dot + 1);
            int s, e;
            if (!FindTable(text, parent, out s, out e))
                return InsertTop(text, parent + " = { " + child + " = " + lua + " },  -- " + f.Label);
            var body = text.Substring(s + 1, e - s - 1);
            var ire = InnerRegex(child, f);
            if (ire.IsMatch(body))
                body = ire.Replace(body, m => child + m.Groups[1].Value + lua, 1);
            else
                body = InsertInTable(body, child + " = " + lua + ",");
            return text.Substring(0, s + 1) + body + text.Substring(e);
        }

        // 表の中（{ と } のあいだ）の最後に足す
        static string InsertInTable(string body, string entry)
        {
            int nl = body.LastIndexOf('\n');
            if (nl >= 0 && body.Substring(nl + 1).Trim().Length == 0)
            {
                // 何行かに分かれた表: } の行の前に 1 行足す
                var closeIndent = body.Substring(nl + 1);
                var head = FixLastComma(body.Substring(0, nl));
                return head + "\n" + closeIndent + "    " + entry + "\n" + closeIndent;
            }
            // 1 行の表（例: { use = "E" }）
            return FixLastComma(body.TrimEnd()) + " " + entry + " ";
        }

        // ファイルの一番外の表（return { ... }）の最後に 1 行足す
        static string InsertTop(string text, string line)
        {
            int close = text.LastIndexOf('}');
            if (close < 0) throw new FormatException("config.lua の形がおかしい（} がない）");
            int ls = text.LastIndexOf('\n', Math.Max(0, close - 1)) + 1;
            var before = text.Substring(0, ls);
            var head = FixLastComma(before.TrimEnd('\r', '\n'));
            var nlStr = text.Contains("\r\n") ? "\r\n" : "\n";
            var closeLine = text.Substring(ls);
            if (text.Substring(ls, close - ls).Trim().Length > 0)
            {
                // } が値と同じ行にある（例: "  gachaOnly = true }"）
                head = FixLastComma(text.Substring(0, close).TrimEnd());
                closeLine = text.Substring(close);
            }
            return head + nlStr + "    " + line + nlStr + closeLine;
        }

        // 最後の項目に , がなければ足す（コメントだけの行・空の行はとばす）
        static string FixLastComma(string s)
        {
            var lines = s.Split('\n');
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                var raw = lines[i].TrimEnd('\r');
                int c = CommentStart(raw);
                var code = (c >= 0 ? raw.Substring(0, c) : raw).TrimEnd();
                if (code.Trim().Length == 0) continue;
                if (!code.EndsWith(",") && !code.EndsWith("{"))
                {
                    var cr = lines[i].EndsWith("\r") ? "\r" : "";
                    lines[i] = code + "," + (c >= 0 ? " " + raw.Substring(c) : "") + cr;
                }
                break;
            }
            return string.Join("\n", lines);
        }

        static int CommentStart(string line)
        {
            bool str = false;
            for (int i = 0; i < line.Length - 1; i++)
            {
                if (line[i] == '"') str = !str;
                else if (!str && line[i] == '-' && line[i + 1] == '-') return i;
            }
            return -1;
        }
    }
}
