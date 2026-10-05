using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using DbScanner.Core;
using DbScanner.Win;

namespace DbScanner.UI
{
    public sealed class MainForm : Form
    {
        public const string CalculatorUrl = "https://killssingkurisu.github.io/db-dps-calculator/";
        const int HotkeyId = 0x0DB1;   // application hotkey ids are 0x0000-0xBFFF

        static readonly Color Bg = Color.FromArgb(38, 48, 44);
        static readonly Color Panel = Color.FromArgb(52, 65, 59);
        static readonly Color Ink = Color.FromArgb(237, 225, 187);
        static readonly Color Dim = Color.FromArgb(188, 181, 148);
        static readonly Color Accent = Color.FromArgb(79, 208, 232);

        readonly Catalog catalog;
        readonly Settings settings;
        readonly ComboBox windowBox = new ComboBox();
        readonly CheckBox bagBox = new CheckBox();
        readonly CheckBox charmsBox = new CheckBox();
        readonly ComboBox speedBox = new ComboBox();
        readonly CheckBox debugBox = new CheckBox();
        readonly TextBox folderBox = new TextBox();
        readonly Label stageLabel = new Label();
        readonly TextBox logBox = new TextBox();
        readonly Label summaryLabel = new Label();

        CancellationTokenSource cancel;
        ScanResult lastResult;
        string lastJson;
        string lastFile;

        public static string Version
        {
            get { var v = Assembly.GetExecutingAssembly().GetName().Version; return v.Major + "." + v.Minor + "." + v.Build; }
        }

        public MainForm(Catalog catalog)
        {
            this.catalog = catalog;
            settings = Settings.Load();
            Text = "DB Inventory Scanner " + Version;
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Bg;
            ForeColor = Ink;
            Font = new Font("Segoe UI", 10f);
            ClientSize = new Size(620, 640);
            MinimumSize = new Size(560, 600);
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (ArgumentException) { }
            BuildUi();
            RefreshWindows();
        }

        /* ---------- layout ---------- */

        static Label Lbl(string s, Color c, float size = 10f, bool bold = false)
        {
            return new Label { Text = s, ForeColor = c, AutoSize = true, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), Margin = new Padding(0, 2, 0, 2) };
        }

        Button MakeButton(string s, EventHandler click, bool primary = false)
        {
            var b = new Button
            {
                Text = s, AutoSize = true, FlatStyle = FlatStyle.Flat, Padding = new Padding(10, 4, 10, 4),
                BackColor = primary ? Ink : Panel, ForeColor = primary ? Bg : Ink, Margin = new Padding(0, 4, 8, 4),
                Font = new Font("Segoe UI", primary ? 11f : 10f, primary ? FontStyle.Bold : FontStyle.Regular)
            };
            b.FlatAppearance.BorderColor = primary ? Ink : Color.FromArgb(93, 116, 102);
            b.Click += click;
            return b;
        }

        void BuildUi()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(16), AutoScroll = true };
            Controls.Add(root);

            root.Controls.Add(Lbl("Dungeon Blitz Inventory Scanner", Ink, 15f, true));
            root.Controls.Add(Lbl(
                "Reads every gear piece and charm from your inventory and saves them for the DPS Calculator.\n" +
                "1. In the game, open your inventory on the Gear tab.\n" +
                "2. Pick the game window below and press Start scan.\n" +
                "3. Hands off the mouse until it's done. Moving the mouse or pressing Esc stops it.", Dim));

            var winRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 10, 0, 0) };
            windowBox.DropDownStyle = ComboBoxStyle.DropDownList;
            windowBox.Width = 430;
            windowBox.BackColor = Panel;
            windowBox.ForeColor = Ink;
            winRow.Controls.Add(windowBox);
            winRow.Controls.Add(MakeButton("Refresh", (s, e) => RefreshWindows()));
            root.Controls.Add(Lbl("Game window", Dim));
            root.Controls.Add(winRow);

            var opts = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
            bagBox.Text = "Gear in your bags (not just what you wear)";
            bagBox.AutoSize = true;
            bagBox.Checked = settings.Bag;
            charmsBox.Text = "Charms";
            charmsBox.AutoSize = true;
            charmsBox.Checked = settings.Charms;
            opts.Controls.Add(bagBox);
            opts.Controls.Add(charmsBox);
            root.Controls.Add(opts);

            var speedRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            speedBox.DropDownStyle = ComboBoxStyle.DropDownList;
            speedBox.Items.AddRange(new object[] { "Fast", "Normal", "Careful (slow computer)" });
            speedBox.SelectedIndex = Math.Max(0, Math.Min(2, settings.Speed));
            speedBox.BackColor = Panel;
            speedBox.ForeColor = Ink;
            speedRow.Controls.Add(Lbl("Speed", Dim));
            speedRow.Controls.Add(speedBox);
            debugBox.Text = "Save screenshots for troubleshooting";
            debugBox.AutoSize = true;
            debugBox.Checked = settings.Debug;
            debugBox.Margin = new Padding(16, 4, 0, 0);
            speedRow.Controls.Add(debugBox);
            root.Controls.Add(speedRow);

            var folderRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            folderBox.Width = 430;
            folderBox.Text = settings.Folder;
            folderBox.BackColor = Panel;
            folderBox.ForeColor = Ink;
            folderBox.BorderStyle = BorderStyle.FixedSingle;
            folderRow.Controls.Add(folderBox);
            folderRow.Controls.Add(MakeButton("Browse…", (s, e) => PickFolder()));
            root.Controls.Add(Lbl("Save scans in", Dim));
            root.Controls.Add(folderRow);

            var start = MakeButton("Start scan", (s, e) => StartScan(), true);
            start.Margin = new Padding(0, 14, 0, 6);
            root.Controls.Add(start);
            startButtonRef = start;

            stageLabel.AutoSize = true;
            stageLabel.ForeColor = Accent;
            stageLabel.Text = "Ready.";
            root.Controls.Add(stageLabel);

            logBox.Multiline = true;
            logBox.ReadOnly = true;
            logBox.ScrollBars = ScrollBars.Vertical;
            logBox.Height = 170;
            logBox.Width = 580;
            logBox.BackColor = Color.FromArgb(30, 38, 34);
            logBox.ForeColor = Dim;
            logBox.BorderStyle = BorderStyle.FixedSingle;
            logBox.Font = new Font("Consolas", 9f);
            root.Controls.Add(logBox);

            summaryLabel.AutoSize = true;
            summaryLabel.MaximumSize = new Size(580, 0);
            summaryLabel.ForeColor = Ink;
            summaryLabel.Margin = new Padding(0, 8, 0, 4);
            root.Controls.Add(summaryLabel);

            var resRow = new FlowLayoutPanel { AutoSize = true };
            var openCalc = MakeButton("Open in DPS Calculator", (s, e) => OpenInCalculator(), true);
            var copy = MakeButton("Copy scan", (s, e) => CopyScan());
            var folder = MakeButton("Open folder", (s, e) => OpenFolder());
            resRow.Controls.Add(openCalc);
            resRow.Controls.Add(copy);
            resRow.Controls.Add(folder);
            openCalcRef = openCalc;
            copyRef = copy;
            root.Controls.Add(resRow);
            SetResultButtons(false);

            var link = new LinkLabel { Text = "DPS Calculator: " + CalculatorUrl, AutoSize = true, LinkColor = Accent, ActiveLinkColor = Ink, Margin = new Padding(0, 10, 0, 0) };
            link.LinkClicked += (s, e) => OpenUrl(CalculatorUrl);
            root.Controls.Add(link);
        }

        Button startButtonRef, openCalcRef, copyRef;

        void SetResultButtons(bool on)
        {
            openCalcRef.Enabled = on;
            copyRef.Enabled = on;
        }

        void RefreshWindows()
        {
            var list = GameWindow.List();
            windowBox.Items.Clear();
            foreach (var w in list) windowBox.Items.Add(w);
            if (list.Count > 0) windowBox.SelectedIndex = 0;
            if (list.Count == 0 || list[0].Score == 0) Log("Couldn't spot the game window. Start the game, then press Refresh.");
        }

        void PickFolder()
        {
            using (var d = new FolderBrowserDialog { SelectedPath = folderBox.Text, Description = "Where to save scans" })
                if (d.ShowDialog(this) == DialogResult.OK) folderBox.Text = d.SelectedPath;
        }

        void Log(string s)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(Log), s); return; }
            logBox.AppendText(DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  " + s + Environment.NewLine);
        }

        /* ---------- scanning ---------- */

        void StartScan()
        {
            var w = windowBox.SelectedItem as GameWindow;
            if (w == null || !w.Exists) { MessageBox.Show(this, "Pick the game window first (press Refresh if it isn't listed).", Text); return; }
            string ocrProblem;
            var ocr = WindowsOcr.Create(out ocrProblem);
            if (ocr == null) { MessageBox.Show(this, ocrProblem, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            settings.Bag = bagBox.Checked;
            settings.Charms = charmsBox.Checked;
            settings.Speed = speedBox.SelectedIndex;
            settings.Debug = debugBox.Checked;
            settings.Folder = folderBox.Text.Trim();
            settings.Save();

            var opt = new ScanOptions { Bag = settings.Bag, Charms = settings.Charms, Debug = settings.Debug };
            int[] hover = { 150, 230, 400 }, page = { 350, 480, 750 };
            opt.HoverDelayMs = hover[settings.Speed];
            opt.PageDelayMs = page[settings.Speed];

            string folder = settings.Folder;
            try { Directory.CreateDirectory(folder); }
            catch (Exception ex) { MessageBox.Show(this, "Can't use that folder: " + ex.Message, Text); return; }
            string debugDir = null;
            if (opt.Debug)
            {
                debugDir = Path.Combine(folder, "debug-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
                Directory.CreateDirectory(debugDir);
            }

            logBox.Clear();
            summaryLabel.Text = "";
            SetResultButtons(false);
            startButtonRef.Enabled = false;
            Log("Text recognition: Windows (" + ocr.LanguageName + ").");
            Log("Scanning " + w + " …");

            cancel = new CancellationTokenSource();
            var token = cancel.Token;
            StreamWriter debugLog = null;
            if (debugDir != null) debugLog = new StreamWriter(Path.Combine(debugDir, "ocr.txt"), false, new UTF8Encoding(false));

            Native.RegisterHotKey(Handle, HotkeyId, 0, Native.VK_ESCAPE);
            WindowState = FormWindowState.Minimized;
            var thread = new Thread(() =>
            {
                ScanResult result = null;
                Exception error = null;
                try
                {
                    // A recogniser made on the scan thread, so it never has to cross apartments.
                    string unused;
                    var threadOcr = WindowsOcr.Create(out unused) ?? ocr;
                    var scanner = new Scanner(new ScreenSurface(w), threadOcr, catalog, opt) { Cancel = token };
                    scanner.Log += Log;
                    scanner.Progress += p => BeginInvoke(new Action(() =>
                        stageLabel.Text = p.Stage + (p.Items > 0 ? " · " + p.Items + " gear" : "") + (p.Charms > 0 ? " · " + p.Charms + " kinds of charms" : "") +
                                          (string.IsNullOrEmpty(p.Last) ? "" : " · " + p.Last)));
                    if (debugLog != null)
                        scanner.DebugSink = (name, img, text) =>
                        {
                            try
                            {
                                using (var bmp = ScreenSurface.ToBitmap(img)) bmp.Save(Path.Combine(debugDir, name + ".png"), ImageFormat.Png);
                                if (text != null) lock (debugLog) debugLog.WriteLine(name + "\t" + text);
                            }
                            catch (IOException) { }
                        };
                    w.Activate();
                    result = scanner.Run();
                }
                catch (Exception ex) { error = ex; }
                finally { if (debugLog != null) lock (debugLog) debugLog.Dispose(); }
                BeginInvoke(new Action(() => Finish(result, error, debugDir)));
            }) { IsBackground = true, Name = "scan" };
            thread.Start();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId && cancel != null) cancel.Cancel();
            base.WndProc(ref m);
        }

        void Finish(ScanResult result, Exception error, string debugDir)
        {
            Native.UnregisterHotKey(Handle, HotkeyId);
            WindowState = FormWindowState.Normal;
            Activate();
            startButtonRef.Enabled = true;
            if (error != null)
            {
                stageLabel.Text = "The scan failed.";
                Log("Error: " + error.Message);
                MessageBox.Show(this, "The scan failed: " + error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            lastResult = result;
            int gear = result.Gear.Count(g => g.Def != null), charms = result.Charms.Sum(c => c.Count);
            foreach (var p in result.Problems) Log("Note: " + p);
            if (gear == 0 && result.Charms.Count == 0)
            {
                stageLabel.Text = result.Cancelled ? "Stopped." : "Nothing was read.";
                summaryLabel.Text = result.Problems.LastOrDefault() ?? "Open your inventory in the game and try again.";
                return;
            }
            lastJson = Json.Write(result.ToJson("DB Inventory Scanner " + Version), true);
            string name = string.IsNullOrEmpty(result.CharacterName) ? "character" : string.Concat(result.CharacterName.Split(Path.GetInvalidFileNameChars()));
            lastFile = Path.Combine(settings.Folder, "DB inventory " + name + " " + DateTime.Now.ToString("yyyy-MM-dd HHmm", CultureInfo.InvariantCulture) + ".json");
            try { File.WriteAllText(lastFile, lastJson, new UTF8Encoding(false)); Log("Saved " + lastFile); }
            catch (IOException ex) { Log("Couldn't save the file: " + ex.Message); lastFile = null; }

            stageLabel.Text = result.Cancelled ? "Stopped early; kept what was read." : "Done.";
            summaryLabel.Text = string.Format(CultureInfo.InvariantCulture,
                "{0}{1}: {2} gear ({3} equipped) and {4} charms of {5} kinds.{6}{7}",
                string.IsNullOrEmpty(result.CharacterName) ? "Your character" : result.CharacterName,
                string.IsNullOrEmpty(result.Class) ? "" : " (" + result.Class + ")",
                gear, result.Gear.Count(g => g.Equipped && g.Def != null), charms, result.Charms.Count,
                result.Problems.Count > 0 ? " " + result.Problems.Count + " notes in the log above." : "",
                debugDir != null ? " Troubleshooting pictures: " + debugDir : "");
            SetResultButtons(true);
        }

        /* ---------- results ---------- */

        static string Base64Url(byte[] data)
        {
            return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        /// <summary>The scan in a link: raw deflate of the compact JSON, base64url (#invz=).</summary>
        public static string CalculatorLink(ScanResult result, string source)
        {
            string compact = Json.Write(result.ToJson(source), false);
            using (var ms = new MemoryStream())
            {
                using (var z = new DeflateStream(ms, CompressionLevel.Optimal, true))
                {
                    var bytes = Encoding.UTF8.GetBytes(compact);
                    z.Write(bytes, 0, bytes.Length);
                }
                return CalculatorUrl + "#invz=" + Base64Url(ms.ToArray());
            }
        }

        void OpenInCalculator()
        {
            if (lastResult == null) return;
            string url = CalculatorLink(lastResult, "DB Inventory Scanner " + Version);
            if (url.Length > 60000)
            {
                CopyScan();
                OpenUrl(CalculatorUrl);
                MessageBox.Show(this, "This scan is too big for a link, so it was copied instead. In the calculator, press “Paste scan” in the Scanned gear panel.", Text);
                return;
            }
            if (url.Length < 2000) { OpenUrl(url); return; }
            // Windows can cut long links short on their way to the browser, so a small page in
            // the temp folder forwards the browser to the whole link instead.
            try
            {
                string page = Path.Combine(Path.GetTempPath(), "DbScanner-open-calculator.html");
                File.WriteAllText(page,
                    "<!doctype html><meta charset=\"utf-8\"><title>DPS Calculator</title>" +
                    "<script>location.replace(" + Json.Write(url, false) + ");</script>" +
                    "<p style=\"font-family:sans-serif\">Opening the DPS Calculator… <a href=\"" + url + "\">Continue</a></p>",
                    new UTF8Encoding(false));
                OpenUrl(page);
            }
            catch (IOException) { OpenUrl(url); }
            catch (UnauthorizedAccessException) { OpenUrl(url); }
        }

        void CopyScan()
        {
            if (lastJson == null) return;
            try { Clipboard.SetText(lastJson); Log("Scan copied. In the calculator, press “Paste scan”."); }
            catch (System.Runtime.InteropServices.ExternalException) { Log("Couldn't use the clipboard; open the saved file instead."); }
        }

        void OpenFolder()
        {
            string folder = folderBox.Text.Trim();
            if (lastFile != null && File.Exists(lastFile)) Process.Start("explorer.exe", "/select,\"" + lastFile + "\"");
            else if (Directory.Exists(folder)) Process.Start("explorer.exe", "\"" + folder + "\"");
        }

        static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception) { }
        }
    }

    /// <summary>Remembered options, in %APPDATA%\DbScanner\settings.json.</summary>
    public sealed class Settings
    {
        public bool Bag = true, Charms = true, Debug;
        public int Speed = 1;
        public string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DB Inventory Scanner");

        static string FilePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DbScanner", "settings.json"); }
        }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                var o = Json.Parse(File.ReadAllText(FilePath)) as Dictionary<string, object>;
                if (o == null) return s;
                object v;
                if (o.TryGetValue("bag", out v) && v is bool) s.Bag = (bool)v;
                if (o.TryGetValue("charms", out v) && v is bool) s.Charms = (bool)v;
                if (o.TryGetValue("debug", out v) && v is bool) s.Debug = (bool)v;
                if (o.TryGetValue("speed", out v) && v is double) s.Speed = (int)(double)v;
                if (o.TryGetValue("folder", out v) && v is string && ((string)v).Length > 0) s.Folder = (string)v;
            }
            catch (Exception) { /* fall back to defaults */ }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, Json.Write(new JObject().Add("bag", Bag).Add("charms", Charms).Add("debug", Debug).Add("speed", Speed).Add("folder", Folder), true));
            }
            catch (Exception) { /* not fatal */ }
        }
    }
}
