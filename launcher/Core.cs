// ラーメンMODランチャー: 画面に関係ない中身（ゲームの検出・MOD の適用・記録の読み取り）
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace RamenLauncher
{
    public class ModInfo
    {
        public string Folder;   // ue4ss\Mods の中のフォルダ名
        public string Name;     // 画面に出す名前
        public string Desc;     // 説明
        public bool Fun;        // おもしろ系か（false はバランス崩壊系）
        public bool Always;     // どのパックにも入れる便利系か
        // ここから下は ramen-mod.json / ramen-launcher.json から（Catalog.cs）
        public string Id, Version = "", Multiplayer = "Unknown", Ue4ss = "", AppId = "", Source = "";
        public string Mark = "・", Who = "", Pack = "";
        public string SourceDir;            // 適用するときのコピー元（payload\mods\X か library\<id>\X）
        public bool Builtin;                // ラーメンMOD（payload）か、自分で足した MOD（library）か
        public List<ModDep> Deps = new List<ModDep>();
        public List<ModDep> Recommends = new List<ModDep>();
        public List<ModKey> Keys = new List<ModKey>();
        public List<string> Assets = new List<string>();
        public List<string> Conflicts = new List<string>();
        public List<string> Problems = new List<string>();
        public ConfigField[] Config;        // 「MOD の設定」に出す項目（なければ出さない）
        public ModInfo(string folder, string name, string desc, bool fun, bool always = false)
        {
            Folder = folder; Name = name; Desc = desc; Fun = fun; Always = always;
        }
    }

    public class WorldRecord
    {
        public string Id;
        public DateTime LastWrite;
        public int Pulls, Pity, Emotes, Machines, Boxes;
        public List<string> EmoteNames = new List<string>();
        public string Fortune = "";
        public int FortuneDay = -1;
    }

    public static class Core
    {
        public const string AppId = "4968640";
        public const string GameExe = "RealRamenSimulator-Win64-Shipping.exe";
        public const string Win64Rel = @"RealRamenSimulator\Binaries\Win64";
        public const string OffSuffix = ".ramen-off";

        // MOD の一覧は、各 MOD の ramen-mod.json / ramen-launcher.json から作る（Catalog.cs）
        public static ModInfo[] Mods { get { return Catalog.Mods; } }

        static readonly Dictionary<string, string> EmoteNames = new Dictionary<string, string> {
            {"WipeTable","テーブル拭き"},{"Mopping","モップがけ"},{"WashHands","手洗い"},{"PeelEgg","ゆで卵むき"},
            {"AddShoyu","醤油さし"},{"Shuriken","手裏剣"},{"Slurp","ラーメンをすする"},{"DrinkSoup","スープを飲む"},
            {"Yarareta","やられた〜"},{"Unko","うんこ"},{"ShoyuChug","醤油一気飲み"},
        };
        static readonly Dictionary<string, string> FortuneNames = new Dictionary<string, string> {
            {"daikichi","大吉"},{"chukichi","中吉"},{"shokichi","小吉"},{"kichi","吉"},{"suekichi","末吉"},{"kyo","凶"},{"daikyo","大凶"},
        };

        // ランチャーのそばの payload フォルダ（UE4SS 本体と MOD の元）
        public static string PayloadDir
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "payload"); }
        }

        // ------------------------------------------------------------------
        // ゲームの場所
        // ------------------------------------------------------------------
        public static string FindGameWin64()
        {
            var libs = new List<string>();
            string steam = null;
            foreach (var key in new[] { @"HKEY_CURRENT_USER\Software\Valve\Steam", @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam" })
            {
                try
                {
                    steam = (Registry.GetValue(key, "SteamPath", null) ?? Registry.GetValue(key, "InstallPath", null)) as string;
                    if (!string.IsNullOrEmpty(steam)) break;
                }
                catch { }
            }
            if (!string.IsNullOrEmpty(steam))
            {
                steam = steam.Replace('/', '\\');
                libs.Add(steam);
                var vdf = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
                if (File.Exists(vdf))
                {
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf, Encoding.UTF8), "\"path\"\\s*\"([^\"]+)\""))
                        libs.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
                }
            }
            libs.Add(@"C:\Program Files (x86)\Steam");
            foreach (var lib in libs.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string dir = null;
                var acf = Path.Combine(lib, @"steamapps\appmanifest_" + AppId + ".acf");
                if (File.Exists(acf))
                {
                    var m = Regex.Match(File.ReadAllText(acf, Encoding.UTF8), "\"installdir\"\\s*\"([^\"]+)\"");
                    if (m.Success) dir = Path.Combine(lib, @"steamapps\common\" + m.Groups[1].Value);
                }
                if (dir == null) dir = Path.Combine(lib, @"steamapps\common\Japanese Ramen Simulator");
                var w = Path.Combine(dir, Win64Rel);
                if (File.Exists(Path.Combine(w, GameExe))) return w;
            }
            return null;
        }

        public static bool IsValidWin64(string w)
        {
            return !string.IsNullOrEmpty(w) && File.Exists(Path.Combine(w, GameExe));
        }

        public static bool GameRunning()
        {
            return Process.GetProcessesByName(Path.GetFileNameWithoutExtension(GameExe)).Length > 0;
        }

        // その場所（Win64）のゲームが起動しているか。場所が調べられないときは「起動している」とみなす
        public static bool GameRunning(string w)
        {
            var exe = Path.GetFullPath(Path.Combine(w, GameExe));
            foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(GameExe)))
            {
                try { if (string.Equals(Path.GetFullPath(p.MainModule.FileName), exe, StringComparison.OrdinalIgnoreCase)) return true; }
                catch { return true; }
            }
            return false;
        }

        // ------------------------------------------------------------------
        // いまの状態
        // ------------------------------------------------------------------
        public static string ModsDir(string w) { return Path.Combine(w, @"ue4ss\Mods"); }

        public static bool Ue4ssInstalled(string w)
        {
            return File.Exists(Path.Combine(w, @"ue4ss\UE4SS.dll")) &&
                   (File.Exists(Path.Combine(w, "dwmapi.dll")) || File.Exists(Path.Combine(w, "dwmapi.dll" + OffSuffix)));
        }

        // バニラ（MOD なし）にしているか = dwmapi.dll を止めている
        public static bool IsVanilla(string w)
        {
            return !File.Exists(Path.Combine(w, "dwmapi.dll"));
        }

        public static Dictionary<string, bool> ReadEnabled(string w)
        {
            var on = new Dictionary<string, bool>();
            foreach (var m in Mods) on[m.Folder] = false;
            var txt = Path.Combine(ModsDir(w), "mods.txt");
            if (!File.Exists(txt)) return on;
            foreach (var line in File.ReadAllLines(txt, Encoding.UTF8))
            {
                var p = line.Split(':');
                if (p.Length != 2) continue;
                var name = p[0].Trim();
                if (on.ContainsKey(name) && Directory.Exists(Path.Combine(ModsDir(w), name)))
                    on[name] = p[1].Trim() == "1";
            }
            return on;
        }

        public static string Sha256(string file)
        {
            using (var s = SHA256.Create())
            using (var f = File.OpenRead(file))
                return BitConverter.ToString(s.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
        }

        // ------------------------------------------------------------------
        // 適用: 選んだ MOD を入れて、mods.txt を書く。記録（data）と自分の設定（config.lua）は残す
        // ------------------------------------------------------------------
        public static string Apply(string w, ICollection<string> selected, bool replaceUe4ss, Action<string> log)
        {
            if (GameRunning(w)) throw new InvalidOperationException("ゲームが起動しています。閉じてからやってください。");
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backup = Path.Combine(w, @"ramenmod_backup\launcher_" + stamp);
            Directory.CreateDirectory(backup);

            // 1. UE4SS 本体
            var payUe = Path.Combine(PayloadDir, "ue4ss");
            var payDll = Path.Combine(PayloadDir, "dwmapi.dll");
            if (!Ue4ssInstalled(w) || replaceUe4ss)
            {
                if (!Directory.Exists(payUe) || !File.Exists(payDll))
                    throw new FileNotFoundException("ランチャーの payload フォルダに UE4SS がありません。zip を全部展開してください。");
                foreach (var f in new[] { "dwmapi.dll", "dwmapi.dll" + OffSuffix })
                {
                    var p = Path.Combine(w, f);
                    if (File.Exists(p)) { File.Copy(p, Path.Combine(backup, f), true); File.Delete(p); }
                }
                // ue4ss フォルダは、本体と付属 MOD だけ上書き（ほかの人の MOD や記録は残す）
                CopyDir(payUe, Path.Combine(w, "ue4ss"), true, null);
                File.Copy(payDll, Path.Combine(w, "dwmapi.dll"), true);
                log("UE4SS を入れました");
            }
            else if (IsVanilla(w))
            {
                SetVanilla(w, false);
                log("MOD ありにもどしました");
            }

            // 2. ラーメンの MOD（選んだもの）。config.lua と data は前のを残す
            var mods = ModsDir(w);
            var txt = Path.Combine(mods, "mods.txt");
            if (File.Exists(txt)) File.Copy(txt, Path.Combine(backup, "mods.txt"), true);
            foreach (var m in Mods)
            {
                if (!selected.Contains(m.Folder)) continue;
                var src = m.SourceDir;
                if (!Directory.Exists(src)) throw new DirectoryNotFoundException(m.Builtin ? "payload に " + m.Folder + " がありません" : m.Name + " の元のファイルがありません: " + src);
                var dst = Path.Combine(mods, m.Folder);
                CopyDir(src, dst, true, new[] { "config.lua" });
                if (!File.Exists(Path.Combine(dst, "config.lua")) && File.Exists(Path.Combine(src, "config.lua")))
                    File.Copy(Path.Combine(src, "config.lua"), Path.Combine(dst, "config.lua"));
                log(m.Name + " を入れました");
            }

            // 2b. エモートのタグ（RealRamenSimulator\Config\Tags\RamenMod.ini）。エモートを使うときだけ置く
            var tagsSrc = Path.Combine(PayloadDir, @"gameconfig\Tags\" + TagsIni);
            var tagsDst = TagsPath(w);
            if (selected.Contains("RamenEmotes") && File.Exists(tagsSrc))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(tagsDst));
                if (File.Exists(tagsDst)) File.Copy(tagsDst, Path.Combine(backup, TagsIni), true);
                File.Copy(tagsSrc, tagsDst, true);
                log("エモートのタグを入れました（" + tagsDst + "）");
            }
            else if (!selected.Contains("RamenEmotes") && File.Exists(tagsDst))
            {
                File.Copy(tagsDst, Path.Combine(backup, TagsIni), true);
                File.Delete(tagsDst);
                log("エモートのタグを外しました");
            }

            // 3. mods.txt: ラーメンの行だけ書きかえる（選ばなかったものは 0 にして、フォルダと記録は残す）
            var lines = File.Exists(txt) ? File.ReadAllLines(txt, Encoding.UTF8).ToList() : new List<string>();
            var names = new HashSet<string>(Mods.Select(x => x.Folder));
            lines = lines.Where(l => !names.Contains(l.Split(':')[0].Trim())).ToList();
            int at = lines.FindIndex(l => l.StartsWith("; Built-in keybinds"));
            if (at < 0) at = lines.Count;
            var add = Mods.Where(x => Directory.Exists(Path.Combine(mods, x.Folder)))
                          .Select(x => x.Folder + " : " + (selected.Contains(x.Folder) ? "1" : "0")).ToList();
            lines.InsertRange(at, add);
            File.WriteAllLines(txt, lines, new UTF8Encoding(false));
            log("MOD のオン・オフを書きました（バックアップ: " + backup + "）");
            return backup;
        }

        // 足した MOD をゲームから外す: フォルダはバックアップへ移し、mods.txt の行を消す
        public static void RemoveFromGame(string w, string folder, Action<string> log)
        {
            var dir = Path.Combine(ModsDir(w), folder);
            var txt = Path.Combine(ModsDir(w), "mods.txt");
            if (!Directory.Exists(dir) && !File.Exists(txt)) return;
            if (GameRunning(w)) throw new InvalidOperationException("ゲームが起動しています。閉じてからやってください。");
            if (Directory.Exists(dir))
            {
                var dest = Path.Combine(w, @"ramenmod_backup\removed_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                Directory.CreateDirectory(dest);
                Directory.Move(dir, Path.Combine(dest, folder));
                log(folder + " をゲームから外しました（" + dest + " に移しました）");
            }
            if (File.Exists(txt))
            {
                var lines = File.ReadAllLines(txt, Encoding.UTF8).Where(l => l.Split(':')[0].Trim() != folder).ToList();
                File.WriteAllLines(txt, lines, new UTF8Encoding(false));
            }
        }

        // バニラ起動用: dwmapi.dll の名前を変えて、UE4SS を読みこまないようにする
        public static void SetVanilla(string w, bool vanilla)
        {
            if (GameRunning(w)) throw new InvalidOperationException("ゲームが起動しています。閉じてからやってください。");
            var on = Path.Combine(w, "dwmapi.dll");
            var off = on + OffSuffix;
            if (vanilla && File.Exists(on)) { if (File.Exists(off)) File.Delete(off); File.Move(on, off); }
            if (!vanilla && File.Exists(off)) { if (File.Exists(on)) File.Delete(on); File.Move(off, on); }
        }

        // 全部外す（消さずにバックアップへ移す）
        public static string Uninstall(string w)
        {
            if (GameRunning(w)) throw new InvalidOperationException("ゲームが起動しています。閉じてからやってください。");
            var dest = Path.Combine(w, @"ramenmod_backup\removed_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(dest);
            foreach (var f in new[] { "dwmapi.dll", "dwmapi.dll" + OffSuffix })
            {
                var p = Path.Combine(w, f);
                if (File.Exists(p)) File.Move(p, Path.Combine(dest, f));
            }
            var ue = Path.Combine(w, "ue4ss");
            if (Directory.Exists(ue)) Directory.Move(ue, Path.Combine(dest, "ue4ss"));
            var tags = TagsPath(w);
            if (File.Exists(tags)) File.Move(tags, Path.Combine(dest, TagsIni));
            return dest;
        }

        // エモートのタグを置く場所: Win64 の 2 つ上（RealRamenSimulator）\Config\Tags
        public const string TagsIni = "RamenMod.ini";
        public static string TagsPath(string w)
        {
            return Path.GetFullPath(Path.Combine(w, @"..\..\Config\Tags\" + TagsIni));
        }

        public static void LaunchGame()
        {
            Process.Start(new ProcessStartInfo("steam://rungameid/" + AppId) { UseShellExecute = true });
        }

        static void CopyDir(string src, string dst, bool overwrite, string[] keepIfExists)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src))
            {
                var name = Path.GetFileName(f);
                var to = Path.Combine(dst, name);
                if (keepIfExists != null && keepIfExists.Contains(name) && File.Exists(to)) continue;
                File.Copy(f, to, overwrite);
            }
            foreach (var d in Directory.GetDirectories(src))
            {
                var name = Path.GetFileName(d);
                if (name.Equals("data", StringComparison.OrdinalIgnoreCase) && Directory.Exists(Path.Combine(dst, name))) continue;
                CopyDir(d, Path.Combine(dst, name), overwrite, keepIfExists);
            }
        }

        // ------------------------------------------------------------------
        // 記録（ガチャ・おみくじ）を読む
        // ------------------------------------------------------------------
        static int ReadInt(string s, string key)
        {
            var m = Regex.Match(s, "\\[\"" + key + "\"\\]\\s*=\\s*(-?\\d+)");
            return m.Success ? int.Parse(m.Groups[1].Value) : 0;
        }

        static int CountBlockEntries(string s, string key)
        {
            var m = Regex.Match(s, "\\[\"" + key + "\"\\]\\s*=\\s*\\{(.*?)\\n\\s{0,2}\\},", RegexOptions.Singleline);
            if (!m.Success) return 0;
            return Regex.Matches(m.Groups[1].Value, "\\[\"[0-9a-f]{20,}\"\\]\\s*=").Count;
        }

        public static List<WorldRecord> ReadRecords(string w)
        {
            var map = new Dictionary<string, WorldRecord>();
            Func<string, WorldRecord> get = id =>
            {
                WorldRecord r;
                if (!map.TryGetValue(id, out r)) { r = new WorldRecord { Id = id }; map[id] = r; }
                return r;
            };
            var gdir = Path.Combine(ModsDir(w), @"RamenGacha\data");
            if (Directory.Exists(gdir))
                foreach (var f in Directory.GetFiles(gdir, "*.lua"))
                {
                    var s = File.ReadAllText(f, Encoding.UTF8);
                    var r = get(Path.GetFileNameWithoutExtension(f));
                    r.LastWrite = File.GetLastWriteTime(f);
                    r.Pulls = ReadInt(s, "pulls");
                    r.Pity = ReadInt(s, "pity");
                    foreach (Match m in Regex.Matches(s, "\"Men\\.Emote\\.Mod\\.(\\w+)\"\\]\\s*=\\s*true"))
                    {
                        string n;
                        r.EmoteNames.Add(EmoteNames.TryGetValue(m.Groups[1].Value, out n) ? n : m.Groups[1].Value);
                    }
                    r.Emotes = r.EmoteNames.Count;
                    r.Machines = CountBlockEntries(s, "adjust");
                }
            var odir = Path.Combine(ModsDir(w), @"RamenOmikuji\data");
            if (Directory.Exists(odir))
                foreach (var f in Directory.GetFiles(odir, "*.lua"))
                {
                    var s = File.ReadAllText(f, Encoding.UTF8);
                    var r = get(Path.GetFileNameWithoutExtension(f));
                    var t = File.GetLastWriteTime(f);
                    if (t > r.LastWrite) r.LastWrite = t;
                    var fk = Regex.Match(s, "\\[\"key\"\\]\\s*=\\s*\"(\\w+)\"");
                    if (fk.Success)
                    {
                        string n;
                        r.Fortune = FortuneNames.TryGetValue(fk.Groups[1].Value, out n) ? n : fk.Groups[1].Value;
                        r.FortuneDay = ReadInt(s, "day");
                    }
                    r.Boxes = CountBlockEntries(s, "adjust");
                }
            return map.Values.OrderByDescending(x => x.LastWrite).ToList();
        }

        public static string TailLog(string w, int lines)
        {
            var p = Path.Combine(w, @"ue4ss\UE4SS.log");
            if (!File.Exists(p)) return "（UE4SS.log がまだありません。一度ゲームを MOD ありで起動してください）";
            using (var fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs, Encoding.UTF8))
            {
                var all = sr.ReadToEnd().Split('\n');
                var keep = all.Where(l => l.Contains("Ramen") || l.Contains("ramen_common") || l.Contains("error") || l.Contains("Error")).ToList();
                return string.Join("\n", keep.Skip(Math.Max(0, keep.Count - lines)));
            }
        }
    }

    public class ReleaseInfo
    {
        public string Tag, Version, ModsZipUrl, ExeUrl;
    }

    // ------------------------------------------------------------------
    // GitHub Releases からの自動更新。
    // ネットに出すのは「いちばん新しい版を教えて」と、そのファイルのダウンロードだけ（ほかは何も送らない）
    // ------------------------------------------------------------------
    public static class Updater
    {
        // 更新先のリポジトリ。settings.txt の update_repo=owner/name で上書きできる
        public const string UpdateOwner = "kazu20080619-byte";
        public const string UpdateRepo = "ramen-mod";
        public const string ModsAsset = "ramen-mods.zip";
        public const string ExeAsset = "RamenLauncher.exe";
        const string NewExe = "RamenLauncher.new.exe";
        const string OldExe = "RamenLauncher.old.exe";

        public static string DefaultRepo { get { return UpdateOwner + "/" + UpdateRepo; } }

        public static bool RepoReady(string repo)
        {
            return !string.IsNullOrEmpty(repo) && Regex.IsMatch(repo, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$") && !repo.StartsWith("OWNER/");
        }

        public static string LauncherVersion { get { return BuildInfo.Version; } }

        public static string PayloadVersion()
        {
            var p = Path.Combine(Core.PayloadDir, "version.txt");
            try { return File.Exists(p) ? File.ReadAllText(p, Encoding.UTF8).Trim() : "0.0.0"; } catch { return "0.0.0"; }
        }

        // "1.2.0" と "v1.10" などをくらべる（a が新しければ正）
        public static int Compare(string a, string b)
        {
            var x = Parts(a);
            var y = Parts(b);
            for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
            {
                int p = i < x.Length ? x[i] : 0, q = i < y.Length ? y[i] : 0;
                if (p != q) return p.CompareTo(q);
            }
            return 0;
        }

        static int[] Parts(string v)
        {
            var m = Regex.Match((v ?? "").Trim().TrimStart('v', 'V'), @"^\d+(\.\d+)*");
            if (!m.Success) return new int[0];
            return m.Value.Split('.').Select(s => { int n; return int.TryParse(s, out n) ? n : 0; }).ToArray();
        }

        static HttpWebRequest Request(string url, int timeout)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "RamenLauncher/" + LauncherVersion;
            req.Timeout = timeout;
            req.ReadWriteTimeout = timeout;
            return req;
        }

        public static ReleaseInfo FetchLatest(string repo)
        {
            if (!RepoReady(repo)) throw new InvalidOperationException("更新先（GitHub のリポジトリ）がまだ決まっていません");
            var req = Request("https://api.github.com/repos/" + repo + "/releases/latest", 15000);
            req.Accept = "application/vnd.github+json";
            string json;
            using (var res = req.GetResponse())
            using (var sr = new StreamReader(res.GetResponseStream(), Encoding.UTF8))
                json = sr.ReadToEnd();
            var tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
            if (!tag.Success) throw new InvalidDataException("GitHub の答えに tag_name がありません");
            var r = new ReleaseInfo { Tag = tag.Groups[1].Value };
            r.Version = r.Tag.TrimStart('v', 'V');
            foreach (Match m in Regex.Matches(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\""))
            {
                var url = m.Groups[1].Value;
                if (!url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)) continue;
                var name = Uri.UnescapeDataString(url.Substring(url.LastIndexOf('/') + 1));
                if (name.Equals(ModsAsset, StringComparison.OrdinalIgnoreCase)) r.ModsZipUrl = url;
                else if (name.Equals(ExeAsset, StringComparison.OrdinalIgnoreCase)) r.ExeUrl = url;
            }
            return r;
        }

        static void Download(string url, string to)
        {
            if (!url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("GitHub 以外からはダウンロードしません: " + url);
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            using (var res = Request(url, 60000).GetResponse())
            using (var s = res.GetResponseStream())
            using (var f = File.Create(to))
                s.CopyTo(f);
        }

        // ramen-mods.zip をダウンロードする一時フォルダの場所（まだ作らない）
        public static string TempZipPath()
        {
            return Path.Combine(Path.GetTempPath(), "RamenLauncher_update_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"), ModsAsset);
        }

        public static void DownloadMods(ReleaseInfo r, string zip)
        {
            Download(r.ModsZipUrl, zip);
        }

        public static void DeleteTemp(string zip)
        {
            try { Directory.Delete(Path.GetDirectoryName(zip), true); } catch { }
        }

        // zip を確かめて payload と入れかえる。前のものは FinishPayload まで payload.old に残す
        public static string InstallPayloadZip(string zip, Action<string> log)
        {
            string ver;
            using (var z = ZipFile.OpenRead(zip))
            {
                var v = z.Entries.FirstOrDefault(e => e.FullName == "version.txt");
                if (v == null) throw new InvalidDataException("zip に version.txt がありません");
                if (!z.Entries.Any(e => e.FullName.Replace('\\', '/').StartsWith("mods/")))
                    throw new InvalidDataException("zip に mods フォルダがありません");
                using (var sr = new StreamReader(v.Open(), Encoding.UTF8)) ver = sr.ReadToEnd().Trim();
                if (Parts(ver).Length == 0) throw new InvalidDataException("version.txt の中身がおかしい: " + ver);
            }
            var pay = Core.PayloadDir.TrimEnd('\\');
            var neu = pay + ".new";
            var old = pay + ".old";
            if (Directory.Exists(neu)) Directory.Delete(neu, true);
            try { ZipFile.ExtractToDirectory(zip, neu); }   // zip の外に出るパスは、ここで止まる
            catch
            {
                try { Directory.Delete(neu, true); } catch { }
                throw;
            }
            if (Directory.Exists(old)) Directory.Delete(old, true);
            if (Directory.Exists(pay)) Directory.Move(pay, old);
            try { Directory.Move(neu, pay); }
            catch
            {
                if (Directory.Exists(old) && !Directory.Exists(pay)) Directory.Move(old, pay);
                throw;
            }
            log("MOD の元（payload）を v" + ver + " にしました");
            Catalog.Reload();
            return ver;
        }

        public static void FinishPayload()
        {
            var old = Core.PayloadDir.TrimEnd('\\') + ".old";
            try { if (Directory.Exists(old)) Directory.Delete(old, true); } catch { }
        }

        // ゲームに MOD が入っていたら、同じ選び方でもう一度「適用」する。画面に出す言葉を返す
        public static string ReapplyIfInstalled(string w, Action<string> log)
        {
            if (!Core.IsValidWin64(w)) return "更新しました。";
            var on = Core.ReadEnabled(w);
            var sel = Core.Mods.Where(m => on[m.Folder]).Select(m => m.Folder).ToList();
            if (sel.Count == 0) return "更新しました。「MOD」タブで選んで「適用」すると、ゲームに入ります。";
            if (Core.GameRunning())
            {
                log("ゲームが起動中なので、ゲームの MOD はまだ前のままです");
                return "更新しました。ゲームを閉じてから『適用』を押してね。";
            }
            Core.Apply(w, sel, false, log);
            return "更新しました。ゲームの MOD も新しくしました。";
        }

        // 動いている exe を RamenLauncher.old.exe にして、新しい exe を置く。置いた exe の場所を返す
        public static string ReplaceExe(string url, Action<string> log)
        {
            var exe = Assembly.GetEntryAssembly().Location;
            var dir = Path.GetDirectoryName(exe);
            var neu = Path.Combine(dir, NewExe);
            var old = Path.Combine(dir, OldExe);
            Download(url, neu);
            var head = new byte[2];
            using (var f = File.OpenRead(neu))
                if (f.Length < 4096 || f.Read(head, 0, 2) != 2 || head[0] != 'M' || head[1] != 'Z')
                    throw new InvalidDataException("ダウンロードした RamenLauncher.exe がおかしい");
            if (File.Exists(old)) File.Delete(old);
            File.Move(exe, old);
            try { File.Move(neu, exe); }
            catch { File.Move(old, exe); throw; }
            log("ランチャーを新しくしました。起動しなおします");
            return exe;
        }

        // 前の更新で残った RamenLauncher.old.exe を消す（前のランチャーが閉じきるまで少し待つ）
        public static void CleanupOldExe()
        {
            var old = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), OldExe);
            for (int i = 0; i < 10 && File.Exists(old); i++)
            {
                try { File.Delete(old); } catch { System.Threading.Thread.Sleep(500); }
            }
        }
    }
}
