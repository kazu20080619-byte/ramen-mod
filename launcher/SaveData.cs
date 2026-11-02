// ラーメンMODランチャー: MOD の記録（Mods\<MOD>\data\<ワールド>.lua）を読んで、直して、書きもどす
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RamenLauncher
{
    // MOD が書く Lua の表（ramen_common.lua の ser() と同じ形）を読み書きする
    public static class LuaTable
    {
        static readonly Regex Tok = new Regex("\\s*(\\{|\\}|\\[|\\]|=|,|\"(?:\\\\.|[^\"\\\\])*\"|-?\\d+(?:\\.\\d+)?(?:[eE][-+]?\\d+)?|true|false|nil)");

        public static Dictionary<string, object> Parse(string text)
        {
            var toks = new List<string>();
            foreach (Match m in Tok.Matches(text)) toks.Add(m.Groups[1].Value);
            int i = 0;
            if (toks.Count == 0) return new Dictionary<string, object>();
            var v = Value(toks, ref i) as Dictionary<string, object>;
            return v ?? new Dictionary<string, object>();
        }

        static object Value(List<string> t, ref int i)
        {
            var s = t[i++];
            if (s == "{")
            {
                var d = new Dictionary<string, object>();
                while (t[i] != "}")
                {
                    i++; // [
                    var key = Value(t, ref i);
                    i++; // ]
                    i++; // =
                    d[Convert.ToString(key, CultureInfo.InvariantCulture)] = Value(t, ref i);
                    if (t[i] == ",") i++;
                }
                i++; // }
                return d;
            }
            if (s.StartsWith("\"")) return Regex.Unescape(s.Substring(1, s.Length - 2));
            if (s == "true") return true;
            if (s == "false") return false;
            if (s == "nil") return null;
            return double.Parse(s, CultureInfo.InvariantCulture);
        }

        public static string Serialize(object v, string ind)
        {
            var d = v as Dictionary<string, object>;
            if (d != null)
            {
                var sb = new StringBuilder("{\n");
                foreach (var kv in d)
                {
                    int n;
                    var key = int.TryParse(kv.Key, out n) ? n.ToString() : Quote(kv.Key);
                    sb.Append(ind + "  [" + key + "] = " + Serialize(kv.Value, ind + "  ") + ",\n");
                }
                sb.Append(ind + "}");
                return sb.ToString();
            }
            if (v is bool) return (bool)v ? "true" : "false";
            if (v is string) return Quote((string)v);
            if (v is double)
            {
                var x = (double)v;
                return x == Math.Floor(x) && Math.Abs(x) < 1e15 ? ((long)x).ToString() : x.ToString("R", CultureInfo.InvariantCulture);
            }
            if (v is int) return v.ToString();
            return "nil";
        }

        static string Quote(string s)
        {
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";
        }
    }

    public class WorldData
    {
        public string Id;
        public DateTime LastWrite;
        public Dictionary<string, object> Gacha = new Dictionary<string, object>();
        public Dictionary<string, object> Omikuji = new Dictionary<string, object>();

        static int Int(Dictionary<string, object> d, string k)
        {
            object v;
            return d.TryGetValue(k, out v) && v is double ? (int)(double)v : 0;
        }
        static Dictionary<string, object> Sub(Dictionary<string, object> d, string k)
        {
            object v;
            return d.TryGetValue(k, out v) ? v as Dictionary<string, object> : null;
        }

        public int Pulls { get { return Int(Gacha, "pulls"); } set { Gacha["pulls"] = (double)value; } }
        public int Pity { get { return Int(Gacha, "pity"); } set { Gacha["pity"] = (double)value; } }

        public HashSet<string> Emotes
        {
            get
            {
                var e = Sub(Gacha, "emotes");
                return new HashSet<string>(e == null ? new string[0] : e.Keys.ToArray());
            }
            set
            {
                var d = new Dictionary<string, object>();
                foreach (var t in value) d[t] = true;
                Gacha["emotes"] = d;
            }
        }

        public int GachaAdjusted { get { var a = Sub(Gacha, "adjust"); return a == null ? 0 : a.Count; } }
        public int OmikujiAdjusted { get { var a = Sub(Omikuji, "adjust"); return a == null ? 0 : a.Count; } }

        public string FortuneKey { get { var f = Sub(Omikuji, "fortune"); return f == null ? "" : Convert.ToString(f["key"]); } }
        public string FortuneEffect { get { var f = Sub(Omikuji, "fortune"); return f == null || !f.ContainsKey("effect") ? "" : Convert.ToString(f["effect"]); } }
        public int FortuneDay { get { var f = Sub(Omikuji, "fortune"); return f == null ? 0 : Int(f, "day"); } }

        public void SetFortune(string key, string effect, int day)
        {
            if (string.IsNullOrEmpty(key)) { Omikuji.Remove("fortune"); return; }
            Omikuji["fortune"] = new Dictionary<string, object> { { "key", key }, { "effect", effect }, { "day", (double)day } };
        }
    }

    public static class SaveData
    {
        // 新しいエモート（tag の最後の部分 → 名前）
        public static readonly string[][] EmoteList = {
            new[] {"WipeTable","テーブル拭き"}, new[] {"Mopping","モップがけ"}, new[] {"WashHands","手洗い"},
            new[] {"PeelEgg","ゆで卵むき"}, new[] {"AddShoyu","醤油さし"}, new[] {"Shuriken","手裏剣"},
            new[] {"Slurp","ラーメンをすする"}, new[] {"DrinkSoup","スープを飲む"}, new[] {"Yarareta","やられた〜"},
            new[] {"Unko","うんこ"}, new[] {"ShoyuChug","醤油一気飲み"},
        };
        public const string EmotePrefix = "Men.Emote.Mod.";

        // 運勢（key, 名前, 効果の key と説明）
        public class Fortune
        {
            public string Key, Name; public string[][] Effects;
            public Fortune(string k, string n, string[][] e) { Key = k; Name = n; Effects = e; }
        }
        public static readonly Fortune[] Fortunes = {
            new Fortune("daikichi", "大吉", new[] { new[] {"sales2","売り上げ ×2"}, new[] {"exp2","お店の経験値 ×2"}, new[] {"speedSales","足が速い ＋ 売り上げ ×1.5"}, new[] {"tipMoney","チップ（会計ごとに +$3）＋ $30"} }),
            new Fortune("chukichi", "中吉", new[] { new[] {"sales15","売り上げ ×1.5"}, new[] {"exp15","お店の経験値 ×1.5"}, new[] {"speed18","足がとても速い"} }),
            new Fortune("shokichi", "小吉", new[] { new[] {"sales12","売り上げ ×1.2"}, new[] {"speed13","足が少し速い"}, new[] {"exp12","お店の経験値 ×1.2"} }),
            new Fortune("kichi", "吉", new[] { new[] {"money","お金 +$15"}, new[] {"items","いい具材が 3 個"} }),
            new Fortune("suekichi", "末吉", new[] { new[] {"nothing","特に何も起きない"}, new[] {"coins","ときどき小銭が落ちてくる"} }),
            new Fortune("kyo", "凶", new[] { new[] {"sales08","売り上げ ×0.8"}, new[] {"speed07","足が重い"}, new[] {"tipsy","1 日ずっと ほろ酔い"}, new[] {"stumble","ときどき つまずく"} }),
            new Fortune("daikyo", "大凶", new[] { new[] {"thief","泥棒が来る"}, new[] {"poop","うんこが降ってくる"}, new[] {"drunk","1 日ずっと 酔っ払い"}, new[] {"sales05","売り上げ ×0.5"} }),
        };

        static string Dir(string win64, string mod) { return Path.Combine(Core.ModsDir(win64), mod + @"\data"); }

        public static List<WorldData> Load(string win64)
        {
            var map = new Dictionary<string, WorldData>();
            foreach (var mod in new[] { "RamenGacha", "RamenOmikuji" })
            {
                var dir = Dir(win64, mod);
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.GetFiles(dir, "*.lua"))
                {
                    var id = Path.GetFileNameWithoutExtension(f);
                    WorldData w;
                    if (!map.TryGetValue(id, out w)) { w = new WorldData { Id = id }; map[id] = w; }
                    var t = File.GetLastWriteTime(f);
                    if (t > w.LastWrite) w.LastWrite = t;
                    var data = LuaTable.Parse(File.ReadAllText(f, Encoding.UTF8));
                    if (mod == "RamenGacha") w.Gacha = data; else w.Omikuji = data;
                }
            }
            return map.Values.OrderByDescending(x => x.LastWrite).ToList();
        }

        // 書きもどす。前のファイルは data\backup にコピー
        public static void Save(string win64, WorldData w)
        {
            if (Core.GameRunning()) throw new InvalidOperationException("ゲームが起動しています。閉じてから保存してください（起動中だとゲームが上書きします）。");
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            foreach (var pair in new[] { new { Mod = "RamenGacha", Data = w.Gacha }, new { Mod = "RamenOmikuji", Data = w.Omikuji } })
            {
                if (pair.Data.Count == 0) continue;
                var dir = Dir(win64, pair.Mod);
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, w.Id + ".lua");
                if (File.Exists(path))
                {
                    var b = Path.Combine(dir, "backup");
                    Directory.CreateDirectory(b);
                    File.Copy(path, Path.Combine(b, w.Id + "_" + stamp + ".lua"), true);
                }
                File.WriteAllText(path, LuaTable.Serialize(pair.Data, ""), new UTF8Encoding(false));
            }
        }
    }
}
