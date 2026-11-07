// ラーメンMODランチャー: MOD の一覧（カタログ）
//   - 各 MOD フォルダの ramen-mod.json（友だちの RamenModLauncher と同じ形）と
//     ramen-launcher.json（このランチャー用: 見た目・キー・パック・設定の項目）から作る
//   - ほかの人の MOD（ramen-mod.json つきの zip）は %LOCALAPPDATA%\RamenLauncher\library\<id> に入れる
//   - 依存・版・ぶつかりのチェックと、選び方の保存（プロファイル）もここ
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace RamenLauncher
{
    public class ModDep
    {
        public string Id, Version;   // Version は ">=1.2,<2" のような条件（空ならなんでもよい）
    }

    public class ModKey
    {
        public string Key, Where;    // Where が同じ（か「どこでも」）キーは、ぶつかりとして知らせる
    }

    public class CheckItem
    {
        public string Level, Text;   // "error"（入れる前に止める）/ "warn" / "info"
    }

    public static class Catalog
    {
        public const string AppId = "4968640";
        public const string MetaFile = "ramen-mod.json";
        public const string LauncherFile = "ramen-launcher.json";
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 };
        static readonly Regex IdRe = new Regex(@"^[a-z0-9][a-zA-Z0-9._-]{0,79}$");
        static readonly Regex FolderRe = new Regex(@"^[A-Za-z0-9_.-]{1,64}$");
        static readonly HashSet<string> Allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".lua", ".png", ".jpg", ".jpeg", ".json", ".md", ".txt", ".ini", ".cfg" };

        public static string DataDir
        {
            get
            {
                // ためすときは RAMEN_LAUNCHER_DATA で別のフォルダにできる
                var env = Environment.GetEnvironmentVariable("RAMEN_LAUNCHER_DATA");
                if (!string.IsNullOrEmpty(env)) return env;
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RamenLauncher");
            }
        }
        public static string LibraryDir { get { return Path.Combine(DataDir, "library"); } }

        // ランチャーに書いておく、ramen-launcher.json がない古い payload 用の最低限の情報
        static readonly ModInfo[] Legacy = {
            new ModInfo("RamenGacha", "ガチャガチャ", "パソコンで買って置く。台の前で E のメニューから 1 回・10 連。具材・ラーメンの雨・エモート", true),
            new ModInfo("RamenOmikuji", "おみくじ箱", "パソコンで買って置く。大吉〜大凶で、その日いっぱい効果が続く", true),
            new ModInfo("RamenEmotes", "新しいエモート", "テーブル拭き・手裏剣・うんこ・醤油一気飲み など 11 個。ガチャガチャがあると、ガチャで出るまで使えない", true),
            new ModInfo("RamenYoi", "酔っ払い", "カウンターでビールを飲むと酔っ払う。飲みすぎ・醤油一気飲みで倒れて目が覚める", true),
            new ModInfo("RamenBenri", "便利機能（バランス崩壊）", "F1 でべんりメニュー。お金・ゲームの速さ・歩くはやさ・飛ぶ・ワープ・お店のレベル", false),
            new ModInfo("RamenWindow", "ウィンドウの大きさ", "F11 でフルスクリーン ⇄ 小さいウィンドウを切りかえ", true, true),
        };

        static ModInfo[] cache;
        static readonly object Lock = new object();

        public static ModInfo[] Mods
        {
            get { lock (Lock) { if (cache == null) cache = Load(); return cache; } }
        }

        public static void Reload() { lock (Lock) cache = Load(); }

        public static ModInfo Find(string folderOrId)
        {
            return Mods.FirstOrDefault(m => m.Folder == folderOrId) ?? Mods.FirstOrDefault(m => m.Id == folderOrId);
        }

        // ------------------------------------------------------------------
        // 読む
        // ------------------------------------------------------------------
        static ModInfo[] Load()
        {
            var list = new List<ModInfo>();
            var payMods = Path.Combine(Core.PayloadDir, "mods");
            var payVer = Updater.PayloadVersion();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // 1. ラーメンMOD（payload）。ランチャーに書いてある順、そのあと知らないものを名前順
            var dirs = Directory.Exists(payMods) ? Directory.GetDirectories(payMods).ToList() : new List<string>();
            var order = Legacy.Select(l => l.Folder).ToList();
            dirs = dirs.OrderBy(d => { int i = order.IndexOf(Path.GetFileName(d)); return i < 0 ? 999 : i; }).ThenBy(d => d).ToList();
            foreach (var d in dirs)
            {
                var folder = Path.GetFileName(d);
                if (!File.Exists(Path.Combine(d, "Scripts", "main.lua"))) continue;
                var legacy = Legacy.FirstOrDefault(l => l.Folder == folder);
                var m = legacy != null ? new ModInfo(folder, legacy.Name, legacy.Desc, legacy.Fun, legacy.Always) : new ModInfo(folder, folder, "", true);
                m.Builtin = true;
                m.SourceDir = d;
                m.Version = payVer;
                ReadMeta(m, Path.Combine(d, MetaFile));
                ReadLauncher(m, Path.Combine(d, LauncherFile));
                if (string.IsNullOrEmpty(m.Version)) m.Version = payVer;
                list.Add(m);
                seen.Add(folder);
            }
            // payload がないときも、ランチャーが知っている MOD は出しておく（適用するときに「payload がない」と言う）
            foreach (var l in Legacy.Where(l => !seen.Contains(l.Folder)))
            {
                var m = new ModInfo(l.Folder, l.Name, l.Desc, l.Fun, l.Always) { Builtin = true, SourceDir = Path.Combine(payMods, l.Folder), Version = payVer };
                list.Add(m);
                seen.Add(l.Folder);
            }
            // 2. 自分で足した MOD（library）
            if (Directory.Exists(LibraryDir))
            {
                foreach (var d in Directory.GetDirectories(LibraryDir).OrderBy(x => x))
                {
                    if (Path.GetFileName(d).StartsWith(".")) continue;
                    try
                    {
                        var sub = Directory.GetDirectories(d).FirstOrDefault(x => File.Exists(Path.Combine(x, "Scripts", "main.lua")));
                        if (sub == null) continue;
                        var folder = Path.GetFileName(sub);
                        if (seen.Contains(folder)) continue;
                        var m = new ModInfo(folder, folder, "", true) { Builtin = false, SourceDir = sub, Id = Path.GetFileName(d), Pack = "" };
                        ReadMeta(m, Path.Combine(d, MetaFile));
                        ReadLauncher(m, Path.Combine(sub, LauncherFile));
                        list.Add(m);
                        seen.Add(folder);
                    }
                    catch { }
                }
            }
            foreach (var m in list) if (string.IsNullOrEmpty(m.Id)) m.Id = m.Folder.ToLowerInvariant();
            return list.ToArray();
        }

        static Dictionary<string, object> ReadJson(string path)
        {
            if (!File.Exists(path)) return null;
            var fi = new FileInfo(path);
            if (fi.Length > 64 * 1024) throw new InvalidDataException(Path.GetFileName(path) + " が大きすぎます（64 KB まで）");
            return Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
        }

        static string Str(Dictionary<string, object> d, string k)
        {
            object v;
            return d != null && d.TryGetValue(k, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;
        }

        static IEnumerable<object> Arr(Dictionary<string, object> d, string k)
        {
            object v;
            if (d == null || !d.TryGetValue(k, out v) || !(v is IEnumerable) || v is string) return new object[0];
            return ((IEnumerable)v).Cast<object>();
        }

        // ramen-mod.json（友だちの形）
        static void ReadMeta(ModInfo m, string path)
        {
            Dictionary<string, object> d;
            try { d = ReadJson(path); } catch { return; }
            if (d == null) return;
            m.Id = Str(d, "id") ?? m.Id;
            var name = Str(d, "name");
            if (!string.IsNullOrEmpty(name) && !m.Builtin) m.Name = name;
            var v = Str(d, "version");
            if (!string.IsNullOrEmpty(v)) m.Version = v;
            var desc = Str(d, "description");
            if (!string.IsNullOrEmpty(desc) && string.IsNullOrEmpty(m.Desc)) m.Desc = desc;
            m.Multiplayer = Str(d, "multiplayer") ?? m.Multiplayer;
            m.Ue4ss = Str(d, "ue4ss") ?? "";
            m.AppId = Str(d, "app_id") ?? "";
            m.Source = Str(d, "source") ?? "";
            m.Deps = Arr(d, "dependencies").OfType<Dictionary<string, object>>()
                .Select(x => new ModDep { Id = Str(x, "id"), Version = Str(x, "version") ?? "" }).Where(x => !string.IsNullOrEmpty(x.Id)).ToList();
            m.Assets = Arr(d, "assets").Select(x => Convert.ToString(x)).Where(x => !string.IsNullOrEmpty(x)).ToList();
        }

        // ramen-launcher.json（このランチャー用）
        static void ReadLauncher(ModInfo m, string path)
        {
            Dictionary<string, object> d;
            try { d = ReadJson(path); }
            catch (Exception ex) { m.Problems.Add(LauncherFile + " が読めません: " + ex.Message); return; }
            if (d == null) return;
            m.Name = Str(d, "short") ?? m.Name;
            m.Desc = Str(d, "summary") ?? m.Desc;
            m.Mark = Str(d, "mark") ?? m.Mark;
            m.Who = Str(d, "who") ?? m.Who;
            var pack = Str(d, "pack");
            if (pack != null)
            {
                m.Pack = pack;
                m.Fun = pack != "benri";
                m.Always = pack == "always";
            }
            m.Keys = Arr(d, "keys").OfType<Dictionary<string, object>>()
                .Select(x => new ModKey { Key = (Str(x, "key") ?? "").ToUpperInvariant(), Where = Str(x, "where") ?? "どこでも" }).Where(x => x.Key != "").ToList();
            m.Recommends = Arr(d, "recommends").OfType<Dictionary<string, object>>()
                .Select(x => new ModDep { Id = Str(x, "id"), Version = Str(x, "why") ?? "" }).Where(x => !string.IsNullOrEmpty(x.Id)).ToList();
            m.Conflicts = Arr(d, "conflicts").Select(x => Convert.ToString(x)).ToList();
            object cfg;
            if (d.TryGetValue("config", out cfg) && cfg is Dictionary<string, object>)
            {
                try { m.Config = ReadConfig((Dictionary<string, object>)cfg); }
                catch (Exception ex) { m.Problems.Add("設定の項目が読めません: " + ex.Message); }
            }
        }

        static ConfigField[] ReadConfig(Dictionary<string, object> cfg)
        {
            var fields = new List<ConfigField>();
            foreach (var g in Arr(cfg, "groups").OfType<Dictionary<string, object>>())
            {
                var title = Str(g, "title") ?? "設定";
                bool fx = Str(g, "style") == "chips";
                foreach (var f in Arr(g, "fields").OfType<Dictionary<string, object>>())
                {
                    var name = Str(f, "name");
                    if (string.IsNullOrEmpty(name) || !Regex.IsMatch(name, @"^[A-Za-z_]\w*(\.[A-Za-z_]\w*)?$")) throw new InvalidDataException("項目の名前がおかしい: " + name);
                    FieldKind kind;
                    if (!Enum.TryParse(Str(f, "kind") ?? "", true, out kind)) throw new InvalidDataException(name + " の kind がおかしい");
                    var cf = new ConfigField(name, kind, Str(f, "label") ?? name, Str(f, "default") ?? "") { Group = title, Chip = fx && kind == FieldKind.Bool && Str(f, "chip") != "False" };
                    double n;
                    if (double.TryParse(Str(f, "min"), NumberStyles.Float, CultureInfo.InvariantCulture, out n)) cf.Min = n;
                    if (double.TryParse(Str(f, "max"), NumberStyles.Float, CultureInfo.InvariantCulture, out n)) cf.Max = n;
                    if (kind == FieldKind.Choice)
                    {
                        var ch = Arr(f, "choices").OfType<Dictionary<string, object>>().ToList();
                        if (ch.Count == 0) throw new InvalidDataException(name + " の choices がない");
                        cf.Choices = ch.Select(c => Str(c, "value") ?? "nil").ToArray();
                        cf.ChoiceLabels = ch.Select(c => Str(c, "label") ?? Str(c, "value")).ToArray();
                    }
                    fields.Add(cf);
                }
            }
            return fields.ToArray();
        }

        // ------------------------------------------------------------------
        // 版の条件（">=1.2,<2"）
        // ------------------------------------------------------------------
        public static bool Satisfies(string version, string cond)
        {
            if (string.IsNullOrWhiteSpace(cond)) return true;
            if (string.IsNullOrWhiteSpace(version)) return false;
            foreach (var part in cond.Split(','))
            {
                var m = Regex.Match(part.Trim(), @"^(>=|<=|==|!=|>|<|=|~=)?\s*v?(\d+(\.\d+)*)$");
                if (!m.Success) return false;
                int c = Updater.Compare(version, m.Groups[2].Value);
                switch (m.Groups[1].Value)
                {
                    case ">=": if (c < 0) return false; break;
                    case "<=": if (c > 0) return false; break;
                    case ">": if (c <= 0) return false; break;
                    case "<": if (c >= 0) return false; break;
                    case "!=": if (c == 0) return false; break;
                    default: if (c != 0) return false; break;
                }
            }
            return true;
        }

        // ------------------------------------------------------------------
        // 入れる前のチェック（依存・版・ぶつかり）
        // ------------------------------------------------------------------
        public static List<CheckItem> Check(ICollection<string> selectedFolders)
        {
            var res = new List<CheckItem>();
            var all = Mods;
            var sel = all.Where(m => selectedFolders.Contains(m.Folder)).ToList();
            Action<string, string> add = (l, t) => res.Add(new CheckItem { Level = l, Text = t });
            foreach (var f in selectedFolders.Where(f => all.All(m => m.Folder != f)))
                add("error", "「" + f + "」という MOD は見つかりません");
            foreach (var m in sel)
            {
                foreach (var p in m.Problems) add("warn", m.Name + ": " + p);
                if (!string.IsNullOrEmpty(m.AppId) && m.AppId != AppId) add("error", m.Name + " は、このゲーム用ではありません（app_id " + m.AppId + "）");
                if (!Directory.Exists(m.SourceDir)) add("error", m.Name + " の元のファイルがありません（ランチャーの zip を全部展開してね）");
                foreach (var d in m.Deps)
                {
                    var need = all.FirstOrDefault(x => x.Id == d.Id);
                    if (need == null) add("error", m.Name + " には「" + d.Id + "」が要ります（まだ入っていません）");
                    else if (!Satisfies(need.Version, d.Version)) add("error", m.Name + " には " + need.Name + " " + d.Version + " が要ります（いまは " + (string.IsNullOrEmpty(need.Version) ? "版がわからない" : need.Version) + "）");
                    else if (!selectedFolders.Contains(need.Folder)) add("error", m.Name + " を使うには、" + need.Name + " もいっしょに選んでね");
                }
                foreach (var r in m.Recommends)
                {
                    var rec = all.FirstOrDefault(x => x.Id == r.Id);
                    if (rec != null && !selectedFolders.Contains(rec.Folder)) add("info", m.Name + ": " + rec.Name + " もいっしょだと、" + (string.IsNullOrEmpty(r.Version) ? "もっと遊べます" : r.Version));
                }
                foreach (var c in m.Conflicts)
                {
                    var other = sel.FirstOrDefault(x => x.Id == c);
                    if (other != null) add("error", m.Name + " と " + other.Name + " は、いっしょに使えません");
                }
            }
            // 同じフォルダ名
            foreach (var g in sel.GroupBy(m => m.Folder, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                add("error", "フォルダ名「" + g.Key + "」が重なっています: " + string.Join("・", g.Select(m => m.Name)));
            // 同じキー（同じ場所、か「どこでも」）
            var keys = sel.SelectMany(m => m.Keys.Select(k => new { m, k })).ToList();
            var told = new HashSet<string>();
            for (int i = 0; i < keys.Count; i++)
                for (int j = i + 1; j < keys.Count; j++)
                {
                    var a = keys[i]; var b = keys[j];
                    if (a.m == b.m || a.k.Key != b.k.Key) continue;
                    if (a.k.Where != b.k.Where && a.k.Where != "どこでも" && b.k.Where != "どこでも") continue;
                    var tag = a.k.Key + a.m.Folder + b.m.Folder;
                    if (told.Add(tag)) add("warn", a.k.Key + " キーを " + a.m.Name + "（" + a.k.Where + "）と " + b.m.Name + "（" + b.k.Where + "）が使っています。「MOD の設定」で変えられます");
                }
            // 同じアセット
            foreach (var g in sel.SelectMany(m => m.Assets.Select(a => new { m, a })).GroupBy(x => x.a).Where(g => g.Count() > 1))
                add("warn", "「" + g.Key + "」を " + string.Join("・", g.Select(x => x.m.Name)) + " が変えます。どちらかしか効かないかも");
            return res;
        }

        // ------------------------------------------------------------------
        // ほかの人の MOD を足す（友だちのランチャーと同じ決まりで zip を確かめる）
        // ------------------------------------------------------------------
        public static ModInfo Import(string zipPath, Action<string> log)
        {
            if (!File.Exists(zipPath)) throw new FileNotFoundException("zip がありません: " + zipPath);
            Dictionary<string, object> meta = null;
            string root, folder;
            var files = new List<KeyValuePair<ZipArchiveEntry, string>>();   // entry → MOD フォルダの中の場所
            using (var z = ZipFile.OpenRead(zipPath))
            {
                if (z.Entries.Count > 5000) throw new InvalidDataException("ファイルが多すぎます（5,000 まで）");
                long total = 0;
                foreach (var e in z.Entries)
                {
                    var n = e.FullName;
                    if (n.Contains("\\") || n.StartsWith("/") || n.Contains(":") || n.Split('/').Any(p => p == ".." || p == "."))
                        throw new InvalidDataException("あぶないファイル名: " + n);
                    if (n.EndsWith("/")) continue;
                    if (!Allowed.Contains(Path.GetExtension(n))) throw new InvalidDataException("入れられない種類のファイル: " + n + "（.lua .png .jpg .json .md .txt .ini .cfg だけ）");
                    if (e.Length > 64L * 1024 * 1024) throw new InvalidDataException("大きすぎるファイル: " + n + "（64 MB まで）");
                    if (e.CompressedLength > 0 && e.Length / (double)e.CompressedLength > 250) throw new InvalidDataException("圧縮率がおかしいファイル: " + n);
                    total += e.Length;
                }
                if (total > 256L * 1024 * 1024) throw new InvalidDataException("展開すると大きすぎます（256 MB まで）");

                var mains = z.Entries.Where(e => e.FullName.EndsWith("/Scripts/main.lua", StringComparison.OrdinalIgnoreCase)).ToList();
                if (mains.Count != 1) throw new InvalidDataException(mains.Count == 0 ? "<MOD名>/Scripts/main.lua がありません（UE4SS の Lua MOD だけ入れられます）" : "1 つの zip に MOD は 1 つだけにしてね");
                root = mains[0].FullName.Substring(0, mains[0].FullName.Length - "/Scripts/main.lua".Length);
                folder = root.Split('/').Last();
                if (!FolderRe.IsMatch(folder)) throw new InvalidDataException("MOD のフォルダ名がおかしい: " + folder);
                var metas = z.Entries.Where(e => e.FullName == MetaFile || e.FullName == root + "/" + MetaFile).ToList();
                if (metas.Count > 1) throw new InvalidDataException("ramen-mod.json は 1 つだけにしてね");
                if (metas.Count == 1)
                {
                    if (metas[0].Length > 64 * 1024) throw new InvalidDataException("ramen-mod.json が大きすぎます（64 KB まで）");
                    using (var sr = new StreamReader(metas[0].Open(), Encoding.UTF8))
                    {
                        try { meta = Json.Deserialize<Dictionary<string, object>>(sr.ReadToEnd()); }
                        catch (Exception ex) { throw new InvalidDataException("ramen-mod.json が読めません: " + ex.Message); }
                    }
                }
                foreach (var e in z.Entries)
                {
                    if (e.FullName.EndsWith("/")) continue;
                    if (e.FullName.StartsWith(root + "/")) files.Add(new KeyValuePair<ZipArchiveEntry, string>(e, e.FullName.Substring(root.Length + 1)));
                    else if (e.FullName == MetaFile || !e.FullName.Contains("/")) continue;   // 直下の説明ファイルは入れない
                    else throw new InvalidDataException("MOD のフォルダの外にファイルがあります: " + e.FullName);
                }

                var id = Str(meta, "id") ?? Regex.Replace(folder.ToLowerInvariant(), "[^a-z0-9._-]", "-");
                if (!IdRe.IsMatch(id)) throw new InvalidDataException("id の形がおかしい: " + id);
                var app = Str(meta, "app_id");
                if (!string.IsNullOrEmpty(app) && app != AppId) throw new InvalidDataException("このゲーム用の MOD ではありません（app_id " + app + "）");
                var clash = Mods.FirstOrDefault(m => m.Builtin && (m.Folder.Equals(folder, StringComparison.OrdinalIgnoreCase) || m.Id == id));
                if (clash != null) throw new InvalidDataException(clash.Name + " はラーメンMOD です。ラーメンMOD は「更新」で新しくなります");
                var other = Mods.FirstOrDefault(m => !m.Builtin && m.Folder.Equals(folder, StringComparison.OrdinalIgnoreCase) && m.Id != id);
                if (other != null) throw new InvalidDataException("フォルダ名「" + folder + "」は " + other.Name + " が使っています");

                // 一時フォルダに出してから入れかえる
                Directory.CreateDirectory(LibraryDir);
                var tmp = Path.Combine(LibraryDir, ".tmp-" + Guid.NewGuid().ToString("N"));
                var dest = Path.Combine(LibraryDir, id);
                try
                {
                    var modDir = Path.Combine(tmp, folder);
                    foreach (var kv in files)
                    {
                        var to = Path.GetFullPath(Path.Combine(modDir, kv.Value.Replace('/', Path.DirectorySeparatorChar)));
                        if (!to.StartsWith(Path.GetFullPath(modDir) + Path.DirectorySeparatorChar)) throw new InvalidDataException("あぶないファイル名: " + kv.Key.FullName);
                        if (kv.Value == MetaFile) continue;   // メタデータは 1 つ上に置く
                        Directory.CreateDirectory(Path.GetDirectoryName(to));
                        kv.Key.ExtractToFile(to, true);
                    }
                    var m2 = meta ?? new Dictionary<string, object>();
                    m2["id"] = id;
                    if (Str(m2, "name") == null) m2["name"] = folder;
                    File.WriteAllText(Path.Combine(tmp, MetaFile), Json.Serialize(m2), new UTF8Encoding(false));
                    bool update = Directory.Exists(dest);
                    if (update) Directory.Delete(dest, true);
                    Directory.Move(tmp, dest);
                    log((update ? "MOD を新しくしました: " : "MOD を足しました: ") + (Str(m2, "name") ?? folder) + "（" + id + "）");
                }
                finally
                {
                    try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
                }
                Reload();
                return Find(folder);
            }
        }

        // 足した MOD を外す（ゲームに入っていたら、消さずにバックアップへ移す）
        public static void Remove(string id, string win64, Action<string> log)
        {
            var m = Mods.FirstOrDefault(x => x.Id == id);
            if (m == null) throw new InvalidOperationException("その MOD は見つかりません: " + id);
            if (m.Builtin) throw new InvalidOperationException("ラーメンMOD は外せません（「使う」のチェックを外してね）");
            if (Core.IsValidWin64(win64)) Core.RemoveFromGame(win64, m.Folder, log);
            Directory.Delete(Path.Combine(LibraryDir, id), true);
            log("MOD を外しました: " + m.Name);
            Reload();
        }

        // ------------------------------------------------------------------
        // プロファイル（選び方の保存）。%LOCALAPPDATA%\RamenLauncher\profiles.json
        // ------------------------------------------------------------------
        static string ProfilePath { get { return Path.Combine(DataDir, "profiles.json"); } }

        public static Dictionary<string, List<string>> Profiles()
        {
            var res = new Dictionary<string, List<string>>();
            try
            {
                if (!File.Exists(ProfilePath)) return res;
                var d = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(ProfilePath, Encoding.UTF8));
                foreach (var p in Arr(d, "profiles").OfType<Dictionary<string, object>>())
                {
                    var name = Str(p, "name");
                    if (!string.IsNullOrEmpty(name)) res[name] = Arr(p, "mods").Select(x => Convert.ToString(x)).ToList();
                }
            }
            catch { }
            return res;
        }

        static void WriteProfiles(Dictionary<string, List<string>> all)
        {
            Directory.CreateDirectory(DataDir);
            var obj = new Dictionary<string, object> {
                { "format", 1 },
                { "profiles", all.Select(kv => new Dictionary<string, object> { { "name", kv.Key }, { "mods", kv.Value } }).ToList() } };
            File.WriteAllText(ProfilePath, Json.Serialize(obj), new UTF8Encoding(false));
        }

        public static void SaveProfile(string name, IEnumerable<string> folders)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0 || name.Length > 30) throw new InvalidOperationException("名前は 1〜30 文字にしてね");
            var all = Profiles();
            if (!all.ContainsKey(name) && all.Count >= 50) throw new InvalidOperationException("保存できるのは 50 こまでです");
            all[name] = folders.Distinct().ToList();
            WriteProfiles(all);
        }

        public static void DeleteProfile(string name)
        {
            var all = Profiles();
            if (all.Remove(name ?? "")) WriteProfiles(all);
        }

        // 組みこみのまとめ選び（pack から作る）
        public static List<string> Preset(string kind)
        {
            return Mods.Where(m => m.Builtin && (kind == "all" || m.Always || (kind == "fun" ? m.Fun : !m.Fun))).Select(m => m.Folder).ToList();
        }
    }
}
