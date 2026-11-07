// ラーメンMODランチャー: WebView2 の画面（launcher\ui の HTML）と、C# の中身をつなぐ
//   JS → C#: chrome.webview.postMessage({id, cmd, args})
//   C# → JS: {id, ok, result | error}、知らせは {evt, ...}
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace RamenLauncher
{
    static class WebUi
    {
        public static string DataDir
        {
            get { return Catalog.DataDir; }
        }

        public static void FileLog(string msg)
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                File.AppendAllText(Path.Combine(DataDir, "launcher.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + msg + "\r\n", Encoding.UTF8);
            }
            catch { }
        }

        static readonly string[] Dlls = { "Microsoft.Web.WebView2.Core.dll", "Microsoft.Web.WebView2.WinForms.dll", "WebView2Loader.dll" };

        // 前の版から exe だけ更新されたとき: payload\webview2 にある dll を exe のそばにコピーする
        public static void EnsureDlls()
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            var src = Path.Combine(Core.PayloadDir, "webview2");
            foreach (var d in Dlls)
            {
                var to = Path.Combine(dir, d);
                var from = Path.Combine(src, d);
                try { if (!File.Exists(to) && File.Exists(from)) File.Copy(from, to); } catch (Exception ex) { FileLog("dll のコピー: " + ex.Message); }
            }
        }

        // WebView2 が使えるか（dll がない・ランタイムがないときは false）
        public static bool Available(out string why)
        {
            why = null;
            try { return Probe(out why); }
            catch (Exception ex) { why = ex.GetType().Name + ": " + ex.Message; return false; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool Probe(out string why)
        {
            why = null;
            var v = CoreWebView2Environment.GetAvailableBrowserVersionString();
            if (string.IsNullOrEmpty(v)) { why = "WebView2 ランタイムがありません"; return false; }
            return true;
        }

        // exe に入れた ui ファイルを %LOCALAPPDATA%\RamenLauncher\ui\<版> に出す
        public static string ExtractUi()
        {
            var dir = Path.Combine(DataDir, "ui", BuildInfo.Version);
            Directory.CreateDirectory(dir);
            var asm = Assembly.GetExecutingAssembly();
            foreach (var name in asm.GetManifestResourceNames().Where(n => n.StartsWith("ui/")))
            {
                var to = Path.Combine(dir, name.Substring(3));
                using (var s = asm.GetManifestResourceStream(name))
                using (var f = File.Create(to))
                    s.CopyTo(f);
            }
            return dir;
        }

        // ふつうに起動: WebView2 が使えればそれ、だめなら前の画面
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool RunWeb(string smokeOut, string shotDir)
        {
            var host = new WebHost(smokeOut, shotDir);
            Application.Run(host);
            return !host.InitFailed;
        }
    }

    class WebHost : Form
    {
        readonly WebView2 web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(22, 17, 14) };
        readonly Settings settings = Settings.Load();
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 };
        readonly string smokeOut, shotDir;
        string win64;
        bool lastRunning, busy;
        System.Windows.Forms.Timer timer;
        public bool InitFailed;

        public WebHost(string smokeOut, string shotDir)
        {
            this.smokeOut = smokeOut;
            this.shotDir = shotDir;
            Text = "ラーメンMODランチャー v" + Updater.LauncherVersion;
            Size = new Size(1280, 820);
            MinimumSize = new Size(980, 640);
            BackColor = Color.FromArgb(22, 17, 14);
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Assembly.GetEntryAssembly().Location); } catch { }
            Controls.Add(web);
            win64 = Core.IsValidWin64(settings.GameDir) ? settings.GameDir : Core.FindGameWin64();
            Load += async (s, e) => await Init();
        }

        async Task Init()
        {
            try
            {
                var uiDir = WebUi.ExtractUi();
                var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(WebUi.DataDir, "WebView2"));
                await web.EnsureCoreWebView2Async(env);
                var cw = web.CoreWebView2;
                cw.Settings.AreDevToolsEnabled = false;
                cw.Settings.IsStatusBarEnabled = false;
                cw.Settings.AreDefaultContextMenusEnabled = false;
                cw.Settings.IsZoomControlEnabled = false;
                cw.SetVirtualHostNameToFolderMapping("app.local", uiDir, CoreWebView2HostResourceAccessKind.DenyCors);
                cw.WebMessageReceived += (s, e) =>
                {
                    List<string> files = null;
                    var extra = e.AdditionalObjects;
                    if (extra != null && extra.Count > 0)
                        files = extra.OfType<CoreWebView2File>().Select(f => f.Path).ToList();
                    OnMessage(e.WebMessageAsJson, files);
                };
                cw.NewWindowRequested += (s, e) => e.Handled = true;   // 新しい窓は開かない
                cw.NavigationStarting += (s, e) =>
                {
                    if (!e.Uri.StartsWith("https://app.local/")) e.Cancel = true;   // ほかのサイトへは行かない
                };
                cw.NavigationCompleted += (s, e) => OnLoaded(e.IsSuccess);
                cw.Navigate("https://app.local/index.html");
            }
            catch (Exception ex)
            {
                WebUi.FileLog("WebView2 を始められませんでした: " + ex);
                InitFailed = true;
                Close();
            }
        }

        void OnLoaded(bool ok)
        {
            if (!ok) { WebUi.FileLog("ページを読めませんでした"); return; }
            if (smokeOut != null) { RunSmoke(); return; }
            timer = new System.Windows.Forms.Timer { Interval = 2000 };
            timer.Tick += (s, e) =>
            {
                bool r = Core.GameRunning();
                if (r != lastRunning) { lastRunning = r; Push(new Dictionary<string, object> { { "evt", "state" } }); }
            };
            lastRunning = Core.GameRunning();
            timer.Start();
            ThreadPool.QueueUserWorkItem(_ => Updater.CleanupOldExe());
            ThreadPool.QueueUserWorkItem(_ => StartupUpdateCheck());
        }

        // ------------------------------------------------------------------
        // JS とのやりとり
        // ------------------------------------------------------------------
        void Push(object msg)
        {
            var s = json.Serialize(msg);
            UI(() => { if (web.CoreWebView2 != null) web.CoreWebView2.PostWebMessageAsJson(s); });
        }

        void UI(Action a)
        {
            try { if (!IsDisposed && IsHandleCreated) BeginInvoke(a); } catch (InvalidOperationException) { }
        }

        T OnUi<T>(Func<T> f)
        {
            return InvokeRequired ? (T)Invoke(f) : f();
        }

        void Log(string text)
        {
            WebUi.FileLog(text);
            Push(new Dictionary<string, object> { { "evt", "log" }, { "text", DateTime.Now.ToString("HH:mm:ss") + " " + text } });
        }

        void OnMessage(string raw, List<string> files)
        {
            Dictionary<string, object> m;
            try { m = json.Deserialize<Dictionary<string, object>>(raw); } catch { return; }
            object id = m.ContainsKey("id") ? m["id"] : null;
            var cmd = m.ContainsKey("cmd") ? m["cmd"] as string : null;
            var args = (m.ContainsKey("args") ? m["args"] as Dictionary<string, object> : null) ?? new Dictionary<string, object>();
            if (cmd == null) return;
            // 画面にドロップされたファイル（postMessageWithAdditionalObjects）の場所。ページからはパスを書けない
            args.Remove("paths");
            if (files != null && files.Count > 0) args["paths"] = files;
            // 時間がかかる仕事は別のスレッドで（画面を止めない）
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var reply = new Dictionary<string, object> { { "id", id } };
                try { reply["result"] = Dispatch(cmd, args); reply["ok"] = true; }
                catch (Exception ex) { reply["ok"] = false; reply["error"] = ex.Message; Log(cmd + " 失敗: " + ex.Message); }
                Push(reply);
            });
        }

        static string S(Dictionary<string, object> a, string k) { object v; return a.TryGetValue(k, out v) && v != null ? Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) : null; }
        static bool B(Dictionary<string, object> a, string k) { object v; return a.TryGetValue(k, out v) && v is bool && (bool)v; }
        static int I(Dictionary<string, object> a, string k) { object v; return a.TryGetValue(k, out v) && v != null ? Convert.ToInt32(v) : 0; }
        static List<string> L(Dictionary<string, object> a, string k)
        {
            object v;
            if (!a.TryGetValue(k, out v) || !(v is IEnumerable) || v is string) return new List<string>();
            return ((IEnumerable)v).Cast<object>().Select(x => Convert.ToString(x)).ToList();
        }

        void NeedGame() { if (!Core.IsValidWin64(win64)) throw new InvalidOperationException("ゲームの場所が決まっていません。「診断」で選んでください。"); }
        void NeedClosed() { NeedGame(); if (Core.GameRunning()) throw new InvalidOperationException("ゲームが起動しています。閉じてからやってください。"); if (busy) throw new InvalidOperationException("ほかの作業をしています。少し待ってね。"); }

        object Dispatch(string cmd, Dictionary<string, object> a)
        {
            switch (cmd)
            {
                case "state": return State();
                case "setGamePath": return SetGamePath();
                case "apply":
                    {
                        NeedClosed();
                        var sel = L(a, "selected");
                        var errs = Catalog.Check(sel).Where(c => c.Level == "error").ToList();
                        if (errs.Count > 0 && !B(a, "force")) throw new InvalidOperationException(string.Join("\n", errs.Select(c => c.Text)));
                        Core.Apply(win64, sel, B(a, "replaceUe4ss"), Log);
                        return State();
                    }
                case "check":
                    return Catalog.Check(L(a, "selected")).Select(c => new Dictionary<string, object> { { "level", c.Level }, { "text", c.Text } }).ToList();
                case "importMod":
                    {
                        var paths = L(a, "paths");
                        if (paths.Count == 0)
                        {
                            var picked = OnUi(() =>
                            {
                                using (var dlg = new OpenFileDialog { Filter = "MOD の zip|*.zip", Multiselect = true, Title = "足す MOD の zip を選んでね" })
                                    return dlg.ShowDialog(this) == DialogResult.OK ? dlg.FileNames : null;
                            });
                            if (picked == null) return State();
                            paths = picked.ToList();
                        }
                        var errors = new List<string>();
                        foreach (var p in paths)
                        {
                            try { Catalog.Import(p, Log); }
                            catch (Exception ex) { errors.Add(Path.GetFileName(p) + ": " + ex.Message); Log("足せませんでした: " + Path.GetFileName(p) + ": " + ex.Message); }
                        }
                        if (errors.Count > 0 && errors.Count == paths.Count) throw new InvalidOperationException(string.Join("\n", errors));
                        var st = (Dictionary<string, object>)State();
                        st["importErrors"] = errors;
                        return st;
                    }
                case "removeMod":
                    if (Core.GameRunning()) throw new InvalidOperationException("ゲームが起動しています。閉じてからやってください。");
                    Catalog.Remove(S(a, "id"), win64, Log);
                    return State();
                case "saveProfile":
                    Catalog.SaveProfile(S(a, "name"), L(a, "selected"));
                    Log("選び方を保存しました: " + S(a, "name"));
                    return State();
                case "deleteProfile":
                    Catalog.DeleteProfile(S(a, "name"));
                    return State();
                case "vanilla":
                    NeedClosed();
                    Core.SetVanilla(win64, B(a, "on"));
                    Log(B(a, "on") ? "バニラ（MOD なし）にしました" : "MOD ありにもどしました");
                    return State();
                case "launch":
                    {
                        NeedClosed();
                        bool mods = B(a, "withMods");
                        if (mods && !Core.Ue4ssInstalled(win64)) throw new InvalidOperationException("UE4SS が入っていません。先に「MOD」で「ゲームに入れる」を押してね。");
                        Core.SetVanilla(win64, !mods);
                        Log(mods ? "MOD ありで起動します" : "MOD なしで起動します");
                        Core.LaunchGame();
                        return true;
                    }
                case "uninstall":
                    NeedClosed();
                    Log("全部外しました: " + Core.Uninstall(win64));
                    return State();
                case "records": return Records();
                case "saveRecord": return SaveRecord(a);
                case "getConfig": return GetConfig(S(a, "mod"));
                case "setConfig": return SetConfig(a);
                case "resetConfig":
                    NeedGame();
                    ModConfig.ResetToDefault(win64, S(a, "mod"));
                    Log(S(a, "mod") + " の設定を、はじめの設定にもどしました");
                    return GetConfig(S(a, "mod"));
                case "checkUpdate": return CheckUpdate();
                case "runUpdate": return RunUpdate();
                case "openFolder": OpenFolder(S(a, "kind")); return true;
                case "diag": return Diag();
            }
            throw new InvalidOperationException("知らないコマンド: " + cmd);
        }

        // ------------------------------------------------------------------
        object State()
        {
            bool valid = Core.IsValidWin64(win64);
            Catalog.Reload();
            var on = valid ? Core.ReadEnabled(win64) : Core.Mods.ToDictionary(m => m.Folder, m => false);
            var cfg = ModConfig.Fields;
            var mods = Core.Mods.Select(m => new Dictionary<string, object> {
                { "folder", m.Folder }, { "id", m.Id }, { "name", m.Name }, { "desc", m.Desc }, { "on", on[m.Folder] },
                { "fun", m.Fun }, { "always", m.Always }, { "builtin", m.Builtin }, { "version", m.Version },
                { "mark", m.Mark }, { "who", m.Who }, { "multiplayer", m.Multiplayer },
                { "keys", m.Keys.Select(k => k.Key).Distinct().ToList() },
                { "deps", m.Deps.Select(d => d.Id + (string.IsNullOrEmpty(d.Version) ? "" : " " + d.Version)).ToList() },
                { "hasConfig", cfg.ContainsKey(m.Folder) },
                { "problems", m.Problems } }).ToList();
            var st = new Dictionary<string, object> {
                { "gameDir", win64 ?? "" }, { "gameFound", valid },
                { "ue4ss", valid && Core.Ue4ssInstalled(win64) }, { "vanilla", valid && Core.IsVanilla(win64) },
                { "running", Core.GameRunning() }, { "busy", busy },
                { "launcherVersion", Updater.LauncherVersion }, { "modsVersion", Updater.PayloadVersion() },
                { "mods", mods },
                { "presets", new Dictionary<string, object> { { "fun", Catalog.Preset("fun") }, { "benri", Catalog.Preset("benri") }, { "all", Catalog.Preset("all") } } },
                { "profiles", Catalog.Profiles().Select(kv => new Dictionary<string, object> { { "name", kv.Key }, { "mods", kv.Value } }).ToList() },
            };
            if (valid)
            {
                var w = SaveData.Load(win64).FirstOrDefault();
                if (w != null)
                {
                    var f = SaveData.Fortunes.FirstOrDefault(x => x.Key == w.FortuneKey);
                    st["record"] = new Dictionary<string, object> {
                        { "pulls", w.Pulls }, { "pity", w.Pity }, { "ceiling", Ceiling() },
                        { "fortune", f == null ? "" : f.Name },
                        { "effect", f == null ? "" : (f.Effects.FirstOrDefault(e => e[0] == w.FortuneEffect) ?? new[] { "", "" })[1] } };
                }
            }
            return st;
        }

        int Ceiling()
        {
            string from;
            var text = ModConfig.LoadText(win64, "RamenGacha", out from);
            ConfigField[] fs;
            if (!ModConfig.Fields.TryGetValue("RamenGacha", out fs) || !fs.Any(x => x.Name == "ceiling")) return 100;
            var v = ModConfig.Read(text, fs.First(x => x.Name == "ceiling"));
            int n;
            return int.TryParse(v, out n) ? n : 100;
        }

        object SetGamePath()
        {
            var picked = OnUi(() =>
            {
                using (var dlg = new FolderBrowserDialog { Description = "Japanese Ramen Simulator のフォルダ（または その中の Win64 フォルダ）を選んでください" })
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return null;
                    var cand = new[] { dlg.SelectedPath, Path.Combine(dlg.SelectedPath, Core.Win64Rel) };
                    return cand.FirstOrDefault(Core.IsValidWin64) ?? "";
                }
            });
            if (picked == "") throw new InvalidOperationException("そのフォルダにゲームが見つかりませんでした。");
            if (picked != null)
            {
                win64 = picked;
                settings.GameDir = picked;
                settings.Save();
                Log("ゲームの場所: " + picked);
            }
            return State();
        }

        object Records()
        {
            var list = Core.IsValidWin64(win64) ? SaveData.Load(win64) : new List<WorldData>();
            return new Dictionary<string, object> {
                { "ceiling", Core.IsValidWin64(win64) ? Ceiling() : 100 },
                { "emotes", SaveData.EmoteList.Select(e => new Dictionary<string, object> { { "tag", e[0] }, { "name", e[1] } }).ToList() },
                { "fortunes", SaveData.Fortunes.Select(f => new Dictionary<string, object> { { "key", f.Key }, { "name", f.Name },
                    { "effects", f.Effects.Select(e => new Dictionary<string, object> { { "key", e[0] }, { "name", e[1] } }).ToList() } }).ToList() },
                { "worlds", list.Select(w => new Dictionary<string, object> {
                    { "id", w.Id }, { "lastWrite", w.LastWrite.ToString("M/d HH:mm") },
                    { "pulls", w.Pulls }, { "pity", w.Pity },
                    { "emotes", w.Emotes.Where(t => t.StartsWith(SaveData.EmotePrefix)).Select(t => t.Substring(SaveData.EmotePrefix.Length)).ToList() },
                    { "fortuneKey", w.FortuneKey ?? "" }, { "fortuneEffect", w.FortuneEffect ?? "" }, { "fortuneDay", w.FortuneDay } }).ToList() },
            };
        }

        object SaveRecord(Dictionary<string, object> a)
        {
            NeedGame();
            if (Core.GameRunning()) throw new InvalidOperationException("ゲームが起動しています。閉じてから保存してください。");
            var w = SaveData.Load(win64).First(x => x.Id == S(a, "id"));
            w.Pulls = Math.Max(0, I(a, "pulls"));
            w.Pity = Math.Max(0, Math.Min(99999, I(a, "pity")));
            w.Emotes = new HashSet<string>(L(a, "emotes").Select(t => SaveData.EmotePrefix + t));
            var fk = S(a, "fortuneKey");
            if (string.IsNullOrEmpty(fk)) w.SetFortune(null, null, 0);
            else w.SetFortune(fk, S(a, "fortuneEffect"), I(a, "fortuneDay"));
            SaveData.Save(win64, w);
            Log("記録を保存しました（ワールド " + w.Id + "）");
            return Records();
        }

        object GetConfig(string mod)
        {
            if (mod == null || !ModConfig.Fields.ContainsKey(mod)) throw new InvalidOperationException("設定がない MOD です");
            string from;
            var text = ModConfig.LoadText(Core.IsValidWin64(win64) ? win64 : null, mod, out from);
            return new Dictionary<string, object> {
                { "mod", mod },
                { "from", from == null ? "none" : Core.IsValidWin64(win64) && from == ModConfig.GamePath(win64, mod) ? "game" : "default" },
                { "fields", ModConfig.Fields[mod].Select(f => new Dictionary<string, object> {
                    { "name", f.Name }, { "kind", f.Kind.ToString() }, { "label", f.Label },
                    { "value", ModConfig.Read(text, f) ?? f.Default }, { "min", f.Min }, { "max", f.Max },
                    { "choices", f.Choices }, { "choiceLabels", f.ChoiceLabels }, { "group", f.Group }, { "chip", f.Chip } }).ToList() },
            };
        }

        // values: { 項目: 値 }（変えたものだけ）。field / value 1 つでもよい
        object SetConfig(Dictionary<string, object> a)
        {
            NeedGame();
            var mod = S(a, "mod");
            var values = a.ContainsKey("values") ? a["values"] as Dictionary<string, object> : null;
            if (values == null) values = new Dictionary<string, object> { { S(a, "field") ?? "", S(a, "value") } };
            string from;
            var text = ModConfig.LoadText(win64, mod, out from);
            var errors = new List<string>();
            foreach (var kv in values)
            {
                var f = ModConfig.Fields[mod].FirstOrDefault(x => x.Name == kv.Key);
                if (f == null) { errors.Add("知らない項目: " + kv.Key); continue; }
                try { text = ModConfig.Write(text, f, Convert.ToString(kv.Value, System.Globalization.CultureInfo.InvariantCulture)); }
                catch (FormatException ex) { errors.Add(ex.Message); }
            }
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
            ModConfig.SaveText(win64, mod, text);
            Log(mod + " の設定を保存しました（" + values.Count + " か所。前のものは config.lua.bak）");
            return GetConfig(mod);
        }

        // ------------------------------------------------------------------
        // 更新
        // ------------------------------------------------------------------
        ReleaseInfo pending;

        void StartupUpdateCheck()
        {
            try
            {
                var r = CheckUpdate() as Dictionary<string, object>;
                if (r != null && (bool)r["available"]) Push(new Dictionary<string, object> { { "evt", "update" }, { "info", r } });
            }
            catch (Exception ex) { WebUi.FileLog("更新の確認はできませんでした: " + ex.Message); }
        }

        object CheckUpdate()
        {
            var repo = settings.UpdateRepo;
            if (!Updater.RepoReady(repo)) throw new InvalidOperationException("更新先（GitHub）がまだ決まっていません");
            var r = Updater.FetchLatest(repo);
            bool mods = r.ModsZipUrl != null && Updater.Compare(r.Version, Updater.PayloadVersion()) > 0;
            bool exe = r.ExeUrl != null && Updater.Compare(r.Version, Updater.LauncherVersion) > 0;
            pending = mods || exe ? r : null;
            return new Dictionary<string, object> { { "available", mods || exe }, { "tag", r.Tag }, { "mods", mods }, { "exe", exe } };
        }

        object RunUpdate()
        {
            if (busy) throw new InvalidOperationException("ほかの作業をしています");
            if (pending == null) CheckUpdate();
            var r = pending;
            if (r == null) return new Dictionary<string, object> { { "message", "いちばん新しい版です。" } };
            bool mods = r.ModsZipUrl != null && Updater.Compare(r.Version, Updater.PayloadVersion()) > 0;
            bool exe = r.ExeUrl != null && Updater.Compare(r.Version, Updater.LauncherVersion) > 0;
            busy = true;
            string msg = "更新しました。", restart = null;
            try
            {
                if (mods)
                {
                    Log("MOD をダウンロードしています（" + r.Tag + "）…");
                    var zip = Updater.TempZipPath();
                    try
                    {
                        Updater.DownloadMods(r, zip);
                        Updater.InstallPayloadZip(zip, Log);
                        msg = Updater.ReapplyIfInstalled(win64, Log);
                        Updater.FinishPayload();
                    }
                    finally { Updater.DeleteTemp(zip); }  // 失敗しても、ダウンロードした一時ファイルは消す
                }
                if (exe)
                {
                    Log("ランチャーをダウンロードしています（" + r.Tag + "）…");
                    restart = Updater.ReplaceExe(r.ExeUrl, Log);
                }
            }
            finally { busy = false; }
            pending = null;
            if (restart != null)
                UI(() => { Process.Start(restart); Close(); });
            return new Dictionary<string, object> { { "message", msg }, { "restart", restart != null } };
        }

        // ------------------------------------------------------------------
        void OpenFolder(string kind)
        {
            string path = null;
            if (kind == "game") path = win64;
            else if (kind == "backup" && win64 != null) path = Path.Combine(win64, "ramenmod_backup");
            else if (kind == "launcher") path = AppDomain.CurrentDomain.BaseDirectory;
            else if (kind == "data") path = WebUi.DataDir;
            if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("フォルダが決まっていません");
            Directory.CreateDirectory(path);
            Process.Start("explorer.exe", "\"" + path + "\"");
        }

        object Diag()
        {
            var sb = new StringBuilder();
            sb.AppendLine("ランチャー: " + AppDomain.CurrentDomain.BaseDirectory);
            sb.AppendLine("payload: " + (Directory.Exists(Core.PayloadDir) ? "あり（v" + Updater.PayloadVersion() + "）" : "ない（zip を全部展開してください）"));
            sb.AppendLine("更新先: " + settings.UpdateRepo);
            if (Core.IsValidWin64(win64))
            {
                var dll = Path.Combine(win64, "dwmapi.dll");
                var off = dll + Core.OffSuffix;
                sb.AppendLine("dwmapi.dll: " + (File.Exists(dll) ? "あり（MOD あり）" : File.Exists(off) ? "止めている（バニラ）" : "ない"));
                var pay = Path.Combine(Core.PayloadDir, "dwmapi.dll");
                var cur = File.Exists(dll) ? dll : off;
                if (File.Exists(cur) && File.Exists(pay))
                    sb.AppendLine("UE4SS の版: " + (Core.Sha256(cur) == Core.Sha256(pay) ? "ランチャーと同じ" : "ちがう（別の UE4SS が入っている）"));
                sb.AppendLine();
                sb.AppendLine("---- UE4SS.log（ラーメン MOD とエラーの行だけ）----");
                sb.AppendLine(Core.TailLog(win64, 60));
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // --test ui-smoke: ページが読めて、state が 1 回往復できるか。shotDir があれば 4 画面の絵も撮る
        // ------------------------------------------------------------------
        async void RunSmoke()
        {
            var o = new StringBuilder();
            try
            {
                var cw = web.CoreWebView2;
                o.AppendLine("title=" + json.Deserialize<string>(await cw.ExecuteScriptAsync("document.title")));
                var tcs = new TaskCompletionSource<string>();
                EventHandler<CoreWebView2WebMessageReceivedEventArgs> h = (s, e) =>
                {
                    var m = json.Deserialize<Dictionary<string, object>>(e.WebMessageAsJson);
                    if (m.ContainsKey("smoke")) tcs.TrySetResult(e.WebMessageAsJson);
                };
                cw.WebMessageReceived += h;
                await cw.ExecuteScriptAsync("app.call('state').then(function(r){chrome.webview.postMessage({smoke:true, ok:true, gameFound:r.gameFound, mods:r.mods.length, version:r.launcherVersion});}, function(e){chrome.webview.postMessage({smoke:true, ok:false, error:String(e)});})");
                var done = await Task.WhenAny(tcs.Task, Task.Delay(15000));
                o.AppendLine("state=" + (done == tcs.Task ? tcs.Task.Result : "timeout"));
                cw.WebMessageReceived -= h;
                if (shotDir != null)
                {
                    Directory.CreateDirectory(shotDir);
                    foreach (var screen in new[] { "home", "mods", "settings", "records" })
                    {
                        await cw.ExecuteScriptAsync("app.show('" + screen + "')");
                        await Task.Delay(1500);
                        var file = Path.Combine(shotDir, "launcher-v13-" + screen + ".png");
                        using (var f = File.Create(file))
                            await cw.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, f);
                        o.AppendLine("shot=" + file);
                    }
                }
            }
            catch (Exception ex) { o.AppendLine("ERROR: " + ex.Message); }
            File.WriteAllText(smokeOut, o.ToString(), new UTF8Encoding(false));
            Close();
        }
    }
}
