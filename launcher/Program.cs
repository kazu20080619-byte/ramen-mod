// ラーメンMODランチャー: 画面
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace RamenLauncher
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 0 && args[0] == "--test") return Tests.Run(args);
            // 新しい画面（WebView2）。使えないときは、前の画面で起動する
            WebUi.EnsureDlls();
            string why;
            if (WebUi.Available(out why))
            {
                try
                {
                    if (WebUi.RunWeb(null, null)) return 0;
                    why = "WebView2 を始められませんでした（くわしくは launcher.log）";
                }
                catch (Exception ex) { why = ex.GetType().Name + ": " + ex.Message; }
            }
            WebUi.FileLog("前の画面で起動します: " + why);
            Application.Run(new MainForm());
            return 0;
        }
    }

    class MainForm : Form
    {
        static readonly Color Bg = Color.FromArgb(28, 28, 32);
        static readonly Color Panel = Color.FromArgb(40, 40, 46);
        static readonly Color Fg = Color.FromArgb(235, 235, 235);
        static readonly Color Sub = Color.FromArgb(160, 160, 170);
        static readonly Color Accent = Color.FromArgb(255, 140, 40);

        string win64;
        readonly Settings settings = Settings.Load();

        Label lblGame, lblUe4ss, lblState, lblRunning, lblVersion;
        Button btnPlay, btnVanilla, btnUpdate;
        bool updating;
        CheckedListBox modList;
        Label modDesc;
        TextBox logBox, diagBox;
        ListBox worldList;
        List<WorldData> worlds = new List<WorldData>();
        NumericUpDown numPulls, numPity, numDay;
        CheckedListBox emoteList;
        ComboBox cmbFortune, cmbEffect;
        Label lblAdjust, lblWorld;
        bool loadingRecord;
        Timer timer;

        public MainForm()
        {
            Text = "ラーメンMODランチャー v" + Updater.LauncherVersion;
            Size = new Size(820, 620);
            MinimumSize = new Size(720, 560);
            BackColor = Bg;
            ForeColor = Fg;
            Font = new Font("Yu Gothic UI", 10f);
            StartPosition = FormStartPosition.CenterScreen;

            var title = new Label { Text = "ラーメンMODランチャー", Font = new Font("Yu Gothic UI", 16f, FontStyle.Bold), ForeColor = Accent, AutoSize = true, Location = new Point(16, 10) };
            Controls.Add(title);
            lblVersion = MakeLabel("", title.Left + title.PreferredWidth + 12, 22, Sub);
            Controls.Add(lblVersion);
            UpdateVersionLabel();

            var tabs = new TabControl { Location = new Point(12, 52), Size = new Size(780, 510), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            tabs.TabPages.Add(BuildHome());
            tabs.TabPages.Add(BuildMods());
            var cfgPage = BuildConfig();
            tabs.TabPages.Add(cfgPage);
            var recPage = BuildRecords();
            tabs.TabPages.Add(recPage);
            var diagPage = BuildDiag();
            tabs.TabPages.Add(diagPage);
            tabs.SelectedIndexChanged += (s, e) =>
            {
                if (tabs.SelectedTab == cfgPage) LoadConfig();
                if (tabs.SelectedTab == recPage) LoadRecords();
                if (tabs.SelectedTab == diagPage) LoadDiag();
            };
            Controls.Add(tabs);

            win64 = Core.IsValidWin64(settings.GameDir) ? settings.GameDir : Core.FindGameWin64();
            timer = new Timer { Interval = 2000 };
            timer.Tick += (s, e) => RefreshState();
            timer.Start();
            Shown += (s, e) =>
            {
                if (win64 == null) AskGameDir();
                RefreshState();
                LoadModChecks();
                ThreadPool.QueueUserWorkItem(_ => Updater.CleanupOldExe());
                CheckUpdate(false);
            };
        }

        // ------------------------------------------------------------------
        TabPage Page(string text)
        {
            return new TabPage(text) { BackColor = Bg, ForeColor = Fg };
        }

        Button MakeButton(string text, int x, int y, int w, bool primary, EventHandler onClick)
        {
            var b = new Button { Text = text, Location = new Point(x, y), Size = new Size(w, 40), FlatStyle = FlatStyle.Flat, BackColor = primary ? Accent : Panel, ForeColor = primary ? Color.Black : Fg };
            b.FlatAppearance.BorderColor = primary ? Accent : Color.FromArgb(70, 70, 80);
            b.Click += onClick;
            return b;
        }

        Label MakeLabel(string text, int x, int y, Color color)
        {
            return new Label { Text = text, Location = new Point(x, y), AutoSize = true, ForeColor = color, MaximumSize = new Size(720, 0) };
        }

        TabPage BuildHome()
        {
            var p = Page("ホーム");
            lblGame = MakeLabel("", 16, 16, Sub); p.Controls.Add(lblGame);
            lblUe4ss = MakeLabel("", 16, 62, Fg); p.Controls.Add(lblUe4ss);
            lblState = MakeLabel("", 16, 90, Fg); p.Controls.Add(lblState);
            lblRunning = MakeLabel("", 16, 118, Accent); p.Controls.Add(lblRunning);

            btnPlay = MakeButton("▶ MOD ありで起動", 16, 152, 240, true, (s, e) => Play(false));
            btnVanilla = MakeButton("バニラ（MOD なし）で起動", 270, 152, 240, false, (s, e) => Play(true));
            p.Controls.Add(btnPlay); p.Controls.Add(btnVanilla);
            btnUpdate = MakeButton("更新を確認", 524, 152, 160, false, (s, e) => CheckUpdate(true));
            p.Controls.Add(btnUpdate);

            p.Controls.Add(MakeLabel("・MOD のオン・オフは「MOD」タブで選んで「適用」してください。", 16, 206, Sub));
            p.Controls.Add(MakeLabel("・バニラで起動すると、UE4SS を一時的に止めて起動します（次に MOD ありで起動すると元にもどります）。", 16, 230, Sub));

            logBox = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Location = new Point(16, 266), Size = new Size(720, 190), BackColor = Panel, ForeColor = Fg, BorderStyle = BorderStyle.FixedSingle, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            p.Controls.Add(logBox);
            return p;
        }

        TabPage BuildMods()
        {
            var p = Page("MOD");
            p.Controls.Add(MakeLabel("使う MOD にチェックを入れて「適用」を押してください。ゲームは閉じておきます。", 16, 12, Sub));
            modList = new CheckedListBox { Location = new Point(16, 40), Size = new Size(360, 170), BackColor = Panel, ForeColor = Fg, BorderStyle = BorderStyle.FixedSingle, CheckOnClick = true };
            foreach (var m in Core.Mods) modList.Items.Add(m.Name);
            modList.SelectedIndexChanged += (s, e) =>
            {
                if (modList.SelectedIndex >= 0) modDesc.Text = Core.Mods[modList.SelectedIndex].Desc;
            };
            p.Controls.Add(modList);
            modDesc = new Label { Location = new Point(392, 40), Size = new Size(360, 170), ForeColor = Sub, Text = "MOD を選ぶと、ここに説明が出ます。" };
            p.Controls.Add(modDesc);

            p.Controls.Add(MakeLabel("まとめて選ぶ:", 16, 226, Sub));
            p.Controls.Add(MakeButton("おもしろ", 110, 218, 110, false, (s, e) => Preset(true, false)));
            p.Controls.Add(MakeButton("バランス崩壊", 226, 218, 120, false, (s, e) => Preset(false, true)));
            p.Controls.Add(MakeButton("全部入り", 352, 218, 110, false, (s, e) => Preset(true, true)));
            p.Controls.Add(MakeButton("全部はずす", 468, 218, 110, false, (s, e) => Preset(false, false)));

            p.Controls.Add(MakeButton("適用", 16, 280, 200, true, (s, e) => ApplyMods(false)));
            p.Controls.Add(MakeButton("UE4SS も入れなおして適用", 230, 280, 260, false, (s, e) => ApplyMods(true)));
            p.Controls.Add(MakeLabel("ガチャの回数・エモート・運勢・台の向きと大きさ・自分で変えた設定（config.lua）は残ります。\n選ばなかった MOD はオフになるだけで、記録は消えません。", 16, 336, Sub));
            return p;
        }

        // ------------------------------------------------------------------
        // MOD の設定（config.lua）
        // ------------------------------------------------------------------
        ListBox cfgModList;
        Panel cfgPanel;
        Label cfgFrom;
        string cfgMod, cfgText;
        readonly Dictionary<ConfigField, Control> cfgControls = new Dictionary<ConfigField, Control>();
        readonly Dictionary<ConfigField, string> cfgLoaded = new Dictionary<ConfigField, string>();
        List<ModInfo> cfgMods;

        TabPage BuildConfig()
        {
            var p = Page("MOD の設定");
            p.Controls.Add(MakeLabel("MOD ごとの設定（config.lua）を変えられます。ゲームを起動しなおすと反映されます。", 16, 10, Sub));
            cfgMods = Core.Mods.Where(m => ModConfig.Fields.ContainsKey(m.Folder)).ToList();
            cfgModList = new ListBox { Location = new Point(16, 36), Size = new Size(180, 380), BackColor = Panel, ForeColor = Fg, BorderStyle = BorderStyle.FixedSingle, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left };
            foreach (var m in cfgMods) cfgModList.Items.Add(m.Name);
            cfgModList.SelectedIndexChanged += (s, e) => ShowConfig();
            p.Controls.Add(cfgModList);
            cfgPanel = new Panel { Location = new Point(212, 36), Size = new Size(540, 380), AutoScroll = true, BackColor = Bg, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            p.Controls.Add(cfgPanel);
            p.Controls.Add(MakeButton("保存", 212, 424, 160, true, (s, e) => SaveConfig()));
            p.Controls.Add(MakeButton("はじめの設定にもどす", 382, 424, 200, false, (s, e) => ResetConfig()));
            cfgFrom = MakeLabel("", 16, 428, Sub);
            cfgFrom.MaximumSize = new Size(190, 0);
            p.Controls.Add(cfgFrom);
            return p;
        }

        void LoadConfig()
        {
            if (cfgModList.SelectedIndex < 0 && cfgModList.Items.Count > 0) cfgModList.SelectedIndex = 0;
            else ShowConfig();
        }

        void ShowConfig()
        {
            int i = cfgModList.SelectedIndex;
            if (i < 0) return;
            cfgMod = cfgMods[i].Folder;
            string from;
            cfgText = ModConfig.LoadText(Core.IsValidWin64(win64) ? win64 : null, cfgMod, out from);
            cfgFrom.Text = from == null ? "config.lua がありません" : from.StartsWith(Core.PayloadDir) ? "はじめの設定（まだ変えていません）" : "ゲームの設定";
            cfgPanel.SuspendLayout();
            cfgPanel.Controls.Clear();
            cfgControls.Clear();
            cfgLoaded.Clear();
            int y = 4;
            foreach (var f in ModConfig.Fields[cfgMod])
            {
                var v = ModConfig.Read(cfgText, f) ?? f.Default;
                cfgLoaded[f] = v;
                Control c;
                if (f.Kind == FieldKind.Bool)
                {
                    c = new CheckBox { Text = f.Label, Checked = v == "true", Location = new Point(4, y), AutoSize = true, ForeColor = Fg };
                }
                else
                {
                    cfgPanel.Controls.Add(new Label { Text = f.Label, Location = new Point(4, y + 3), Size = new Size(250, 22), ForeColor = Fg });
                    if (f.Kind == FieldKind.Choice)
                    {
                        var cb = new ComboBox { Location = new Point(260, y), Width = 240, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Panel, ForeColor = Fg, FlatStyle = FlatStyle.Flat };
                        foreach (var l in f.ChoiceLabels) cb.Items.Add(l);
                        cb.SelectedIndex = Math.Max(0, Array.IndexOf(f.Choices, v));
                        c = cb;
                    }
                    else c = new TextBox { Text = v, Location = new Point(260, y), Width = 240, BackColor = Panel, ForeColor = Fg, BorderStyle = BorderStyle.FixedSingle };
                }
                cfgPanel.Controls.Add(c);
                cfgControls[f] = c;
                y += 30;
            }
            cfgPanel.ResumeLayout();
        }

        string ConfigValue(ConfigField f)
        {
            var c = cfgControls[f];
            var chk = c as CheckBox;
            if (chk != null) return chk.Checked ? "true" : "false";
            var cb = c as ComboBox;
            if (cb != null) return f.Choices[Math.Max(0, cb.SelectedIndex)];
            return c.Text;
        }

        void SaveConfig()
        {
            if (cfgMod == null) return;
            if (!Core.IsValidWin64(win64)) { MessageBox.Show("ゲームの場所が決まっていません。", Text); return; }
            var text = cfgText;
            var errors = new List<string>();
            int changed = 0;
            foreach (var f in ModConfig.Fields[cfgMod])
            {
                var v = ConfigValue(f).Trim();
                if (v == cfgLoaded[f]) continue;   // 変えていない項目は、ファイルのまま
                try { text = ModConfig.Write(text, f, v); changed++; }
                catch (FormatException ex) { errors.Add(ex.Message); }
            }
            if (errors.Count > 0) { MessageBox.Show("直してください:\n" + string.Join("\n", errors), Text); return; }
            if (changed == 0) { MessageBox.Show("変えたところがありません。", Text); return; }
            try
            {
                ModConfig.SaveText(win64, cfgMod, text);
                Log(cfgMod + " の設定を保存しました（" + changed + " か所。前のものは config.lua.bak）");
                MessageBox.Show("保存しました。ゲームを起動しなおすと反映されます。", Text);
            }
            catch (Exception ex) { MessageBox.Show("保存できませんでした:\n" + ex.Message, Text); }
            ShowConfig();
        }

        void ResetConfig()
        {
            if (cfgMod == null) return;
            if (!Core.IsValidWin64(win64)) { MessageBox.Show("ゲームの場所が決まっていません。", Text); return; }
            if (MessageBox.Show(cfgMods[cfgModList.SelectedIndex].Name + " の設定を、はじめの設定にもどしますか？\n（いまのものは config.lua.bak に残します）", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            try
            {
                ModConfig.ResetToDefault(win64, cfgMod);
                Log(cfgMod + " の設定を、はじめの設定にもどしました");
                MessageBox.Show("もどしました。ゲームを起動しなおすと反映されます。", Text);
            }
            catch (Exception ex) { MessageBox.Show("もどせませんでした:\n" + ex.Message, Text); }
            ShowConfig();
        }

        TabPage BuildRecords()
        {
            var p = Page("記録・エディタ");
            p.Controls.Add(MakeLabel("ワールドごとの、ガチャとおみくじの記録です。直したら「保存」（ゲームは閉じておく）。", 16, 10, Sub));
            worldList = new ListBox { Location = new Point(16, 36), Size = new Size(200, 420), BackColor = Panel, ForeColor = Fg, BorderStyle = BorderStyle.FixedSingle, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left };
            worldList.SelectedIndexChanged += (s, e) => ShowRecord();
            p.Controls.Add(worldList);

            int x = 232;
            lblWorld = MakeLabel("", x, 36, Sub); p.Controls.Add(lblWorld);
            p.Controls.Add(MakeLabel("■ ガチャ", x, 62, Accent));
            p.Controls.Add(MakeLabel("まわした回数", x, 90, Fg));
            numPulls = new NumericUpDown { Location = new Point(x + 150, 86), Width = 90, Maximum = 999999, BackColor = Panel, ForeColor = Fg };
            p.Controls.Add(numPulls);
            p.Controls.Add(MakeLabel("天井のカウント（0〜99）", x, 122, Fg));
            numPity = new NumericUpDown { Location = new Point(x + 190, 118), Width = 60, Maximum = 99, BackColor = Panel, ForeColor = Fg };
            p.Controls.Add(numPity);
            p.Controls.Add(MakeLabel("手に入れたエモート", x, 152, Fg));
            emoteList = new CheckedListBox { Location = new Point(x, 176), Size = new Size(250, 200), BackColor = Panel, ForeColor = Fg, BorderStyle = BorderStyle.FixedSingle, CheckOnClick = true };
            foreach (var e in SaveData.EmoteList) emoteList.Items.Add(e[1]);
            p.Controls.Add(emoteList);
            p.Controls.Add(MakeButton("全部", x, 382, 80, false, (s, e) => { for (int i = 0; i < emoteList.Items.Count; i++) emoteList.SetItemChecked(i, true); }));
            p.Controls.Add(MakeButton("なし", x + 86, 382, 80, false, (s, e) => { for (int i = 0; i < emoteList.Items.Count; i++) emoteList.SetItemChecked(i, false); }));

            int x2 = 500;
            p.Controls.Add(MakeLabel("■ おみくじ", x2, 62, Accent));
            p.Controls.Add(MakeLabel("今日の運勢", x2, 90, Fg));
            cmbFortune = new ComboBox { Location = new Point(x2, 112), Width = 230, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Panel, ForeColor = Fg, FlatStyle = FlatStyle.Flat };
            cmbFortune.Items.Add("（なし）");
            foreach (var f in SaveData.Fortunes) cmbFortune.Items.Add(f.Name);
            cmbFortune.SelectedIndexChanged += (s, e) => FillEffects(null);
            p.Controls.Add(cmbFortune);
            p.Controls.Add(MakeLabel("効果", x2, 146, Fg));
            cmbEffect = new ComboBox { Location = new Point(x2, 168), Width = 230, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Panel, ForeColor = Fg, FlatStyle = FlatStyle.Flat };
            p.Controls.Add(cmbEffect);
            p.Controls.Add(MakeLabel("何日目の運勢か", x2, 204, Fg));
            numDay = new NumericUpDown { Location = new Point(x2 + 130, 200), Width = 70, Maximum = 99999, BackColor = Panel, ForeColor = Fg };
            p.Controls.Add(numDay);
            p.Controls.Add(MakeLabel("（ゲームの日付がこの日のあいだ効果が続く）", x2, 230, Sub));

            lblAdjust = MakeLabel("", x2, 268, Fg); p.Controls.Add(lblAdjust);
            p.Controls.Add(MakeButton("台・箱の向き大きさを戻す", x2, 300, 230, false, (s, e) => ResetAdjust()));
            p.Controls.Add(MakeButton("保存", x2, 382, 230, true, (s, e) => SaveRecord()));
            return p;
        }

        TabPage BuildDiag()
        {
            var p = Page("設定・診断");
            p.Controls.Add(MakeButton("ゲームの場所を変える", 16, 12, 200, false, (s, e) => { AskGameDir(); RefreshState(); LoadModChecks(); }));
            p.Controls.Add(MakeButton("ゲームのフォルダを開く", 226, 12, 200, false, (s, e) => OpenFolder(win64)));
            p.Controls.Add(MakeButton("バックアップを開く", 436, 12, 180, false, (s, e) => OpenFolder(win64 == null ? null : Path.Combine(win64, "ramenmod_backup"))));
            p.Controls.Add(MakeButton("ログを読みなおす", 16, 60, 200, false, (s, e) => LoadDiag()));
            p.Controls.Add(MakeButton("MOD を全部外す", 226, 60, 200, false, (s, e) => UninstallAll()));
            diagBox = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Location = new Point(16, 112), Size = new Size(720, 345), BackColor = Panel, ForeColor = Fg, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 9f), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            p.Controls.Add(diagBox);
            return p;
        }

        // ------------------------------------------------------------------
        void Log(string msg)
        {
            if (logBox.InvokeRequired) { UI(() => Log(msg)); return; }
            logBox.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg + "\r\n");
        }

        void RefreshState()
        {
            bool running = Core.GameRunning();
            if (!Core.IsValidWin64(win64))
            {
                lblGame.Text = "ゲームが見つかりません。「設定・診断」→「ゲームの場所を変える」で選んでください。";
                lblUe4ss.Text = lblState.Text = "";
                btnPlay.Enabled = btnVanilla.Enabled = false;
                return;
            }
            lblGame.Text = "ゲームの場所: " + win64;
            bool ue = Core.Ue4ssInstalled(win64);
            lblUe4ss.Text = ue ? "UE4SS: 入っています" + (Core.IsVanilla(win64) ? "（いまはバニラ用に止めています）" : "") : "UE4SS: まだ入っていません（「MOD」タブで適用すると入ります）";
            var on = Core.ReadEnabled(win64);
            var names = Core.Mods.Where(m => on[m.Folder]).Select(m => m.Name).ToList();
            lblState.Text = "オンの MOD: " + (names.Count > 0 ? string.Join("・", names) : "なし");
            lblRunning.Text = running ? "ゲームが起動中です。MOD の変更はゲームを閉じてから。" : "";
            btnPlay.Enabled = btnVanilla.Enabled = !running;
        }

        void LoadModChecks()
        {
            if (!Core.IsValidWin64(win64)) return;
            var on = Core.ReadEnabled(win64);
            for (int i = 0; i < Core.Mods.Length; i++) modList.SetItemChecked(i, on[Core.Mods[i].Folder]);
        }

        void Preset(bool fun, bool benri)
        {
            for (int i = 0; i < Core.Mods.Length; i++)
                modList.SetItemChecked(i, Core.Mods[i].Always ? (fun || benri) : Core.Mods[i].Fun ? fun : benri);
        }

        bool Ready()
        {
            if (!Core.IsValidWin64(win64)) { MessageBox.Show("ゲームの場所が決まっていません。", Text); return false; }
            if (Core.GameRunning()) { MessageBox.Show("ゲームが起動しています。閉じてからやってください。", Text); return false; }
            if (updating) { MessageBox.Show("更新しています。終わるまで待ってね。", Text); return false; }
            return true;
        }

        // ------------------------------------------------------------------
        // 更新（GitHub Releases）
        // ------------------------------------------------------------------
        void UpdateVersionLabel()
        {
            lblVersion.Text = "ランチャー v" + Updater.LauncherVersion + " ／ MOD v" + Updater.PayloadVersion();
        }

        void CheckUpdate(bool manual)
        {
            if (updating) return;
            var repo = settings.UpdateRepo;
            if (!Updater.RepoReady(repo))
            {
                if (manual) Log("更新先（GitHub）がまだ決まっていないので、確認できません");
                return;
            }
            updating = true;
            btnUpdate.Enabled = false;
            if (manual) Log("更新を確認しています…");
            ThreadPool.QueueUserWorkItem(_ =>
            {
                ReleaseInfo r = null;
                string err = null;
                try { r = Updater.FetchLatest(repo); }
                catch (Exception ex) { err = ex.Message; }
                UI(() => OnChecked(r, err, manual));
            });
        }

        // 別のスレッドから画面を動かす（もう閉じていたら何もしない）
        void UI(Action a)
        {
            try { if (!IsDisposed && IsHandleCreated) BeginInvoke(a); } catch (InvalidOperationException) { }
        }

        void EndUpdate()
        {
            updating = false;
            btnUpdate.Enabled = true;
        }

        void OnChecked(ReleaseInfo r, string err, bool manual)
        {
            if (err != null)
            {
                EndUpdate();
                Log(manual ? "更新を確認できませんでした: " + err : "更新の確認はできませんでした（ネットにつながっていないかも）");
                return;
            }
            bool mods = r.ModsZipUrl != null && Updater.Compare(r.Version, Updater.PayloadVersion()) > 0;
            bool exe = r.ExeUrl != null && Updater.Compare(r.Version, Updater.LauncherVersion) > 0;
            if (!mods && !exe)
            {
                EndUpdate();
                Log("いちばん新しい版です（GitHub: " + r.Tag + "）");
                if (manual) MessageBox.Show("いちばん新しい版です。", Text);
                return;
            }
            if (MessageBox.Show("新しい版 " + r.Tag + " があります。更新しますか？", Text, MessageBoxButtons.YesNo) != DialogResult.Yes)
            {
                EndUpdate();
                Log("更新しませんでした（" + r.Tag + "）");
                return;
            }
            var w = win64;
            ThreadPool.QueueUserWorkItem(_ => RunUpdate(r, mods, exe, w));
        }

        // ゲームの外の仕事なので、画面を止めないように別のスレッドで動かす
        void RunUpdate(ReleaseInfo r, bool mods, bool exe, string w)
        {
            string msg = null, restart = null;
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
                        msg = Updater.ReapplyIfInstalled(w, Log);
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
            catch (Exception ex)
            {
                Log("更新できませんでした: " + ex.Message);
                msg = "更新できませんでした:\n" + ex.Message;
            }
            UI(() =>
            {
                EndUpdate();
                RefreshState();
                LoadModChecks();
                UpdateVersionLabel();
                if (msg != null) MessageBox.Show(msg, Text);
                if (restart != null) { Process.Start(restart); Close(); }
            });
        }

        void ApplyMods(bool replaceUe4ss)
        {
            if (!Ready()) return;
            var selected = new List<string>();
            for (int i = 0; i < Core.Mods.Length; i++) if (modList.GetItemChecked(i)) selected.Add(Core.Mods[i].Folder);
            try
            {
                Core.Apply(win64, selected, replaceUe4ss, Log);
                MessageBox.Show("適用しました。「ホーム」の「MOD ありで起動」で遊べます。", Text);
            }
            catch (Exception ex) { Log("失敗: " + ex.Message); MessageBox.Show("うまくいきませんでした:\n" + ex.Message, Text); }
            RefreshState();
        }

        void Play(bool vanilla)
        {
            if (!Ready()) return;
            try
            {
                if (vanilla) { Core.SetVanilla(win64, true); Log("バニラで起動します"); }
                else
                {
                    if (!Core.Ue4ssInstalled(win64)) { MessageBox.Show("UE4SS が入っていません。先に「MOD」タブで適用してください。", Text); return; }
                    Core.SetVanilla(win64, false); Log("MOD ありで起動します");
                }
                Core.LaunchGame();
            }
            catch (Exception ex) { Log("失敗: " + ex.Message); MessageBox.Show(ex.Message, Text); }
            RefreshState();
        }

        void UninstallAll()
        {
            if (!Ready()) return;
            if (MessageBox.Show("UE4SS と MOD を全部外しますか？\n（消さずに ramenmod_backup フォルダへ移します）", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            try { var d = Core.Uninstall(win64); Log("全部外しました: " + d); }
            catch (Exception ex) { MessageBox.Show(ex.Message, Text); }
            RefreshState(); LoadModChecks();
        }

        void AskGameDir()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Japanese Ramen Simulator のフォルダ（または その中の Win64 フォルダ）を選んでください" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var pick = dlg.SelectedPath;
                var cand = new[] { pick, Path.Combine(pick, Core.Win64Rel) };
                var ok = cand.FirstOrDefault(Core.IsValidWin64);
                if (ok == null) { MessageBox.Show("そのフォルダにゲームが見つかりませんでした。", Text); return; }
                win64 = ok;
                settings.GameDir = ok;
                settings.Save();
            }
        }

        void LoadRecords()
        {
            worldList.Items.Clear();
            worlds = Core.IsValidWin64(win64) ? SaveData.Load(win64) : new List<WorldData>();
            foreach (var w in worlds) worldList.Items.Add(w.LastWrite.ToString("MM/dd HH:mm") + "  " + w.Id.Substring(0, Math.Min(8, w.Id.Length)));
            if (worlds.Count > 0) worldList.SelectedIndex = 0;
            else lblWorld.Text = "まだ記録がありません。ゲームでガチャやおみくじを使うと、ここに出ます。";
        }

        WorldData Current()
        {
            return worldList.SelectedIndex >= 0 && worldList.SelectedIndex < worlds.Count ? worlds[worldList.SelectedIndex] : null;
        }

        void ShowRecord()
        {
            var w = Current();
            if (w == null) return;
            loadingRecord = true;
            lblWorld.Text = "ワールド " + w.Id + "（最後に遊んだ " + w.LastWrite.ToString("yyyy/MM/dd HH:mm") + "）";
            numPulls.Value = Math.Min(numPulls.Maximum, Math.Max(0, w.Pulls));
            numPity.Value = Math.Min(numPity.Maximum, Math.Max(0, w.Pity));
            var owned = w.Emotes;
            for (int i = 0; i < SaveData.EmoteList.Length; i++)
                emoteList.SetItemChecked(i, owned.Contains(SaveData.EmotePrefix + SaveData.EmoteList[i][0]));
            int fi = Array.FindIndex(SaveData.Fortunes, f => f.Key == w.FortuneKey);
            cmbFortune.SelectedIndex = fi + 1;
            FillEffects(w.FortuneEffect);
            numDay.Value = Math.Min(numDay.Maximum, Math.Max(0, w.FortuneDay));
            lblAdjust.Text = "向き・大きさを変えた台: ガチャ " + w.GachaAdjusted + " ／ おみくじ " + w.OmikujiAdjusted;
            loadingRecord = false;
        }

        void FillEffects(string current)
        {
            cmbEffect.Items.Clear();
            int fi = cmbFortune.SelectedIndex - 1;
            if (fi < 0) return;
            var effects = SaveData.Fortunes[fi].Effects;
            int sel = 0;
            for (int i = 0; i < effects.Length; i++)
            {
                cmbEffect.Items.Add(effects[i][1]);
                if (effects[i][0] == current) sel = i;
            }
            cmbEffect.SelectedIndex = sel;
        }

        void ResetAdjust()
        {
            var w = Current();
            if (w == null) return;
            w.Gacha.Remove("adjust");
            w.Omikuji.Remove("adjust");
            lblAdjust.Text = "向き・大きさを変えた台: ガチャ 0 ／ おみくじ 0（保存すると反映）";
        }

        void SaveRecord()
        {
            var w = Current();
            if (w == null) return;
            if (Core.GameRunning()) { MessageBox.Show("ゲームが起動しています。閉じてから保存してください。", Text); return; }
            w.Pulls = (int)numPulls.Value;
            w.Pity = (int)numPity.Value;
            var set = new HashSet<string>();
            for (int i = 0; i < SaveData.EmoteList.Length; i++)
                if (emoteList.GetItemChecked(i)) set.Add(SaveData.EmotePrefix + SaveData.EmoteList[i][0]);
            w.Emotes = set;
            int fi = cmbFortune.SelectedIndex - 1;
            if (fi < 0) w.SetFortune(null, null, 0);
            else w.SetFortune(SaveData.Fortunes[fi].Key, SaveData.Fortunes[fi].Effects[Math.Max(0, cmbEffect.SelectedIndex)][0], (int)numDay.Value);
            try
            {
                SaveData.Save(win64, w);
                Log("記録を保存しました（ワールド " + w.Id + "）");
                MessageBox.Show("保存しました。次にゲームを起動すると反映されます。\n（元のファイルは data\\backup に残しています）", Text);
            }
            catch (Exception ex) { MessageBox.Show("保存できませんでした:\n" + ex.Message, Text); }
            LoadRecords();
        }

        void LoadDiag()
        {
            var sb = new StringBuilder();
            sb.AppendLine("ランチャー: " + AppDomain.CurrentDomain.BaseDirectory);
            sb.AppendLine("payload: " + (Directory.Exists(Core.PayloadDir) ? "あり" : "ない（zip を全部展開してください）"));
            if (Core.IsValidWin64(win64))
            {
                sb.AppendLine("ゲーム: " + win64);
                var dll = Path.Combine(win64, "dwmapi.dll");
                var off = dll + Core.OffSuffix;
                sb.AppendLine("dwmapi.dll: " + (File.Exists(dll) ? "あり（MOD あり）" : File.Exists(off) ? "止めている（バニラ）" : "ない"));
                var pay = Path.Combine(Core.PayloadDir, "dwmapi.dll");
                var cur = File.Exists(dll) ? dll : off;
                if (File.Exists(cur) && File.Exists(pay))
                    sb.AppendLine("UE4SS の版: " + (Core.Sha256(cur) == Core.Sha256(pay) ? "ランチャーと同じ" : "ちがう（別の UE4SS が入っている）"));
                sb.AppendLine();
                sb.AppendLine("---- UE4SS.log（ラーメン MOD とエラーの行だけ）----");
                sb.AppendLine(Core.TailLog(win64, 60).Replace("\n", "\r\n"));
            }
            diagBox.Text = sb.ToString();
        }

        void OpenFolder(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            Directory.CreateDirectory(path);
            Process.Start("explorer.exe", "\"" + path + "\"");
        }
    }

    // ランチャーの設定。%LOCALAPPDATA%\RamenLauncher\settings.txt
    //   game_dir=<ゲームの Win64 フォルダ>
    //   update_repo=<owner/name>（書けば、更新先の GitHub リポジトリを変えられる）
    // 前の形（1 行目にゲームの場所だけ）も読める
    class Settings
    {
        public string GameDir = "";
        public string UpdateRepoOverride = "";
        public string UpdateRepo { get { return string.IsNullOrEmpty(UpdateRepoOverride) ? Updater.DefaultRepo : UpdateRepoOverride; } }
        static string PathOf()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"RamenLauncher\settings.txt");
        }
        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(PathOf())) return s;
                foreach (var raw in File.ReadAllLines(PathOf(), Encoding.UTF8))
                {
                    var line = raw.Trim();
                    if (line.StartsWith("game_dir=")) s.GameDir = line.Substring(9).Trim();
                    else if (line.StartsWith("update_repo=")) s.UpdateRepoOverride = line.Substring(12).Trim();
                    else if (line.Length > 0 && s.GameDir == "") s.GameDir = line;
                }
            }
            catch { }
            return s;
        }
        public void Save()
        {
            var lines = new List<string> { "game_dir=" + GameDir };
            if (!string.IsNullOrEmpty(UpdateRepoOverride)) lines.Add("update_repo=" + UpdateRepoOverride);
            try { Directory.CreateDirectory(Path.GetDirectoryName(PathOf())); File.WriteAllLines(PathOf(), lines, Encoding.UTF8); } catch { }
        }
    }

    // 画面なしで中身を試す: RamenLauncher.exe --test <コマンド> ...
    static class Tests
    {
        public static int Run(string[] a)
        {
            var o = new StringBuilder();
            try
            {
                switch (a.Length > 1 ? a[1] : "")
                {
                    case "detect":
                        o.AppendLine("game=" + (Core.FindGameWin64() ?? "none"));
                        break;
                    case "state":
                        o.AppendLine("ue4ss=" + Core.Ue4ssInstalled(a[2]) + " vanilla=" + Core.IsVanilla(a[2]));
                        foreach (var kv in Core.ReadEnabled(a[2])) o.AppendLine(kv.Key + "=" + kv.Value);
                        break;
                    case "apply":
                        var sel = a.Length > 3 && a[3] != "-" ? a[3].Split(',') : new string[0];
                        Core.Apply(a[2], sel, a.Length > 4 && a[4] == "replace", s => o.AppendLine(s));
                        break;
                    case "vanilla":
                        Core.SetVanilla(a[2], a[3] == "on");
                        o.AppendLine("vanilla=" + Core.IsVanilla(a[2]));
                        break;
                    case "records":
                        foreach (var r in Core.ReadRecords(a[2]))
                            o.AppendLine(r.Id + " pulls=" + r.Pulls + " pity=" + r.Pity + " emotes=" + r.Emotes + " machines=" + r.Machines + " fortune=" + r.Fortune + "/" + r.FortuneDay + " boxes=" + r.Boxes);
                        break;
                    case "edit":
                        // edit <win64> <world> <pity> <emoteTag,..|-> <fortuneKey|-> <effect> <day>
                        var ws = SaveData.Load(a[2]);
                        var w = ws.First(x => x.Id == a[3]);
                        w.Pity = int.Parse(a[4]);
                        w.Emotes = new HashSet<string>(a[5] == "-" ? new string[0] : a[5].Split(',').Select(t => SaveData.EmotePrefix + t));
                        w.SetFortune(a[6] == "-" ? null : a[6], a[7], int.Parse(a[8]));
                        SaveData.Save(a[2], w);
                        var again = SaveData.Load(a[2]).First(x => x.Id == a[3]);
                        o.AppendLine("pity=" + again.Pity + " pulls=" + again.Pulls + " emotes=" + string.Join(",", again.Emotes) + " fortune=" + again.FortuneKey + "/" + again.FortuneEffect + "/" + again.FortuneDay + " adjG=" + again.GachaAdjusted);
                        break;
                    case "uninstall":
                        o.AppendLine("moved=" + Core.Uninstall(a[2]));
                        break;
                    case "ui-smoke":
                        // ui-smoke [絵を保存するフォルダ]
                        {
                            string why;
                            WebUi.EnsureDlls();
                            if (!WebUi.Available(out why)) { o.AppendLine("webview2=unavailable " + why); break; }
                            var tmp = Path.GetTempFileName();
                            bool ok = WebUi.RunWeb(tmp, a.Length > 2 ? a[2] : null);
                            o.AppendLine("webview2=" + (ok ? "ok" : "init-failed"));
                            o.Append(File.ReadAllText(tmp, Encoding.UTF8));
                            File.Delete(tmp);
                        }
                        break;
                    case "catalog":
                        foreach (var m in Catalog.Mods)
                            o.AppendLine(m.Folder + " id=" + m.Id + " v=" + m.Version + " builtin=" + m.Builtin + " pack=" + m.Pack + " mark=" + m.Mark
                                + " keys=" + string.Join(",", m.Keys.Select(k => k.Key + "@" + k.Where)) + " deps=" + string.Join(",", m.Deps.Select(d => d.Id + d.Version))
                                + " config=" + (m.Config == null ? 0 : m.Config.Length) + (m.Problems.Count > 0 ? " problems=" + string.Join("|", m.Problems) : ""));
                        foreach (var kv in ModConfig.Fields) o.AppendLine("fields " + kv.Key + "=" + kv.Value.Length + " groups=" + string.Join("/", kv.Value.Select(f => f.Group).Distinct()));
                        break;
                    case "import":
                        // import <zip>（RAMEN_LAUNCHER_DATA でためしのフォルダにしてから）
                        {
                            var m = Catalog.Import(a[2], s => o.AppendLine(s));
                            o.AppendLine("imported=" + m.Folder + " id=" + m.Id + " v=" + m.Version + " src=" + m.SourceDir);
                        }
                        break;
                    case "remove":
                        Catalog.Remove(a[2], a.Length > 3 ? a[3] : null, s => o.AppendLine(s));
                        o.AppendLine("mods=" + string.Join(",", Catalog.Mods.Select(m => m.Folder)));
                        break;
                    case "check":
                        // check <A,B,...>
                        foreach (var c in Catalog.Check(a.Length > 2 && a[2] != "-" ? a[2].Split(',') : new string[0])) o.AppendLine(c.Level + ": " + c.Text);
                        o.AppendLine("done");
                        break;
                    case "satisfies":
                        o.AppendLine(Catalog.Satisfies(a[2], a[3]).ToString());
                        break;
                    case "profile":
                        // profile save <名前> <A,B> | profile delete <名前> | profile list
                        if (a[2] == "save") Catalog.SaveProfile(a[3], a[4].Split(','));
                        if (a[2] == "delete") Catalog.DeleteProfile(a[3]);
                        foreach (var kv in Catalog.Profiles()) o.AppendLine(kv.Key + "=" + string.Join(",", kv.Value));
                        break;
                    case "config":
                        // config <win64> <Mod> <field> <value>（1 つ書きかえて、できたファイルを出す）
                        {
                            var cf = ModConfig.Fields[a[3]].First(x => x.Name == a[4]);
                            string from;
                            var text = ModConfig.LoadText(a[2], a[3], out from);
                            o.AppendLine("from=" + from + " before=" + (ModConfig.Read(text, cf) ?? "(none)"));
                            ModConfig.SaveText(a[2], a[3], ModConfig.Write(text, cf, string.Join(" ", a.Skip(5))));
                            var after = File.ReadAllText(ModConfig.GamePath(a[2], a[3]), Encoding.UTF8);
                            o.AppendLine("after=" + ModConfig.Read(after, cf));
                            o.AppendLine("----");
                            o.Append(after);
                        }
                        break;
                    case "update-check":
                        // update-check <いまの版のふり> [owner/name]
                        var repo = a.Length > 3 ? a[3] : Settings.Load().UpdateRepo;
                        var local = a.Length > 2 ? a[2] : Updater.PayloadVersion();
                        var rel = Updater.FetchLatest(repo);
                        o.AppendLine("repo=" + repo + " remote=" + rel.Tag + " local=" + local + " newer=" + (Updater.Compare(rel.Version, local) > 0 ? "yes" : "no"));
                        o.AppendLine("mods_zip=" + (rel.ModsZipUrl ?? "none"));
                        o.AppendLine("exe=" + (rel.ExeUrl ?? "none"));
                        break;
                    case "update-apply":
                        // update-apply <zip> [win64]（win64 を書いたときだけ、ゲームにも適用しなおす）
                        Updater.InstallPayloadZip(a[2], s => o.AppendLine(s));
                        if (a.Length > 3) o.AppendLine(Updater.ReapplyIfInstalled(a[3], s => o.AppendLine(s)));
                        Updater.FinishPayload();
                        o.AppendLine("payload_version=" + Updater.PayloadVersion());
                        break;
                    default:
                        o.AppendLine("commands: detect | state <win64> | apply <win64> <A,B|-> [replace] | vanilla <win64> on|off | records <win64> | uninstall <win64> | update-check <version> [owner/name] | update-apply <zip> [win64]");
                        break;
                }
            }
            catch (Exception ex) { o.AppendLine("ERROR: " + ex.Message); }
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_out.txt"), o.ToString(), new UTF8Encoding(false));
            return 0;
        }
    }
}
