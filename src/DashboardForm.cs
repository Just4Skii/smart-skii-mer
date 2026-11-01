using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SmartMemReduct
{
    /// <summary>
    /// Borderless, responsive dashboard. Layout uses TableLayoutPanels so it scales
    /// instead of relying on absolute pixel positions. Destructive changes are only
    /// recommendations; the automatic action is a safe working-set trim.
    /// </summary>
    public sealed class DashboardForm : Form
    {
        private static readonly Color Bg = Color.FromArgb(7, 11, 16);
        private static readonly Color PanelBg = Color.FromArgb(12, 19, 27);
        private static readonly Color Panel2 = Color.FromArgb(16, 25, 35);
        private static readonly Color Stroke = Color.FromArgb(31, 45, 58);
        private static readonly Color Ink = Color.FromArgb(231, 241, 235);
        private static readonly Color Muted = Color.FromArgb(122, 143, 156);
        private static readonly Color Mint = Color.FromArgb(110, 231, 183);
        private static readonly Color Amber = Color.FromArgb(245, 194, 107);
        private static readonly Color Rose = Color.FromArgb(251, 113, 133);

        public event Action? OnThemeChanged;

        private readonly Panel _root = new() { Dock = DockStyle.Fill, BackColor = Bg };
        private readonly Panel _sidebar = new() { Dock = DockStyle.Left, Width = 206, BackColor = Color.FromArgb(9, 14, 20) };
        private readonly Panel _main = new() { Dock = DockStyle.Fill, BackColor = Bg };
        private readonly Panel _body = new() { Dock = DockStyle.Fill, Padding = new Padding(26, 8, 26, 22) };
        private readonly Panel _header = new() { Dock = DockStyle.Top, Height = 88, BackColor = Bg };
        private readonly Label _title = new();
        private readonly Label _subtitle = new();
        private readonly FlowLayoutPanel _chips = new();
        private readonly Label _footer = new();
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2600 };

        private readonly Dictionary<string, NavButton> _nav = new();
        private string _page = "overview";
        private SystemSnapshot? _snapshot;
        private bool _busy;
        private readonly List<string> _log = new();
        private readonly Toggle _background = new();
        private readonly Toggle _autoEco = new();
        private readonly Toggle _autoAi = new();
        private readonly Toggle _remote = new();
        private readonly ComboBox _provider = new();
        private readonly TextBox _baseUrl = new();
        private readonly TextBox _model = new();
        private readonly Label _remoteInfo = new();
        private RemoteServer? _server;
        private readonly TextBox _hotkey = new();
        private readonly TextBox _apiKey = new();
        private readonly FlowLayoutPanel _modeBar = new();

        /// <summary>
        /// Removes stale per-page content. Shared controls (AI bar, toggles, text boxes)
        /// are detached first so they are never disposed and can be re-hosted.
        /// </summary>
        private void PurgePageFields()
        {
            foreach (var root in new Control[] { _body, _report })
                foreach (var shared in new Control[] { _aiKey, _aiStatus, _analyze, _deviceLine, _apiKey, _provider, _baseUrl, _model, _hotkey, _threshold, _interval, _autoClean, _pauseGame, _trimBg, _autoEco, _autoAi, _background, _remote })
                    if (shared.Parent == root) root.Controls.Remove(shared);
        }

        private readonly Label _modeHint = new();
        private int _ecoCounter;
        private readonly HashSet<string> _ecoDone = new();
        private string _modeSummary = "Mode is detected automatically from the app you are using";
        private string _lastModeId = "";
        private bool _syncingToggles;
        private NotifyIcon? _tray;
        private ToolStripMenuItem? _trayStatus;
        private bool _exiting;

        private readonly RadialGauge _ram = new();
        private readonly Label _ramLine = new();
        private readonly Label _ramSub = new();
        private readonly Label _cpuValue = new();
        private readonly Label _cpuMeta = new();
        private readonly Sparkline _cpuSpark = new();
        private readonly Label _gpuName = new();
        private readonly Label _gpuMeta = new();
        private readonly Label _focusName = new();
        private readonly Label _focusMeta = new();
        private readonly Label _focusNote = new();
        private readonly Label _focusStats = new();

        private readonly FlowLayoutPanel _report = new();
        private readonly Label _snapshotBox = new();
        private readonly Label _aiKey = new();
        private readonly Label _aiStatus = new();
        private readonly Label _deviceLine = new();
        private readonly Button _analyze = new();

        private readonly DataGridView _grid = new();
        private readonly Label _procMeta = new();

        private readonly Label _sysMachine = new();
        private readonly Label _sysGpu = new();
        private readonly Label _sysDriver = new();
        private readonly Label _sysPower = new();
        private readonly Label _sysVendor = new();
        private readonly Label _sysSession = new();

        private readonly Toggle _autoClean = new();
        private readonly Toggle _pauseGame = new();
        private readonly Toggle _trimBg = new();
        private readonly NumericUpDown _threshold = new();
        private readonly NumericUpDown _interval = new();

        public DashboardForm(string startPage = "overview")
        {
            Text = "Smart Skii-mer";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Stroke;
            Padding = new Padding(1);
            AutoScaleMode = AutoScaleMode.None;
            MinimumSize = new Size(1080, 680);
            ClientSize = new Size(1240, 740);
            StartPosition = FormStartPosition.Manual;
            Location = new Point(148, 36);
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            Font = new Font("Segoe UI", 9f);
            KeyPreview = true;

            BuildSidebar();
            BuildHeader();

            Controls.Add(_root);
            _root.Controls.Add(_main);
            _root.Controls.Add(_sidebar);
            _main.Controls.Add(_body);
            _main.Controls.Add(_header);

            _footer.Text = "Smart Skii-mer protects the app you are using and cleans only idle background apps.";
            _footer.Dock = DockStyle.Bottom;
            _footer.Height = 26;
            _footer.ForeColor = Color.FromArgb(74, 92, 104);
            _footer.Font = new Font("Segoe UI", 7.5f);
            _footer.Padding = new Padding(2, 0, 0, 0);
            _main.Controls.Add(_footer);

            ShowPage(startPage);
            Load += async (_, _) => { Location = new Point(30, 28); if (SettingsManager.Current.RunInBackground) EnsureTray(); await RefreshAsync(true); await SelfTrimAsync(); };
            int ticks = 0;
            _timer.Tick += async (_, _) =>
            {
                if (_busy) return;
                ticks++;
                await RefreshAsync(ticks % 3 == 0);
                if (++_ecoCounter >= 6)
                {
                    _ecoCounter = 0;
                    ApplyModeAndEco();
                }
            };
            _timer.Start();
            FormClosed += (_, _) =>
            {
                _timer.Stop();
                try { if (_snapshot is not null) SystemProbe.ApplyEcoToBackground(_snapshot.Processes, 0, false); } catch { }
                GameFocus.Restore();
                GameFocus.RevertGpuPreferences();
                if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
            };
            KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
            FormClosing += (_, e) =>
            {
                if (!_exiting && e.CloseReason == CloseReason.UserClosing && SettingsManager.Current.RunInBackground)
                {
                    e.Cancel = true;
                    HideToTray();
                }
            };
            ApplyRoundedWindow();
        }

        // ── chrome ───────────────────────────────────────────────────────────
        private void BuildSidebar()
        {
            _sidebar.Controls.Add(new Label { Text = "◆", ForeColor = Mint, Font = new Font("Segoe UI", 20f), AutoSize = false, Height = 46, Width = 46, TextAlign = ContentAlignment.MiddleCenter, Location = new Point(18, 18) });
            _sidebar.Controls.Add(new Label { Text = "SMART", ForeColor = Color.FromArgb(122,143,156), Font = new Font("Segoe UI", 7f, FontStyle.Bold), AutoSize = false, Width = 120, Height = 14, Location = new Point(68, 22) });
            _sidebar.Controls.Add(new Label { Text = "Skii-mer", ForeColor = Ink, Font = new Font("Segoe UI Semibold", 13f), AutoSize = false, Width = 130, Height = 26, Location = new Point(68, 34) });
            _sidebar.Controls.Add(new Label { Text = "WORKSPACE", ForeColor = Color.FromArgb(74, 92, 104), Font = new Font("Segoe UI", 7f, FontStyle.Bold), AutoSize = false, Width = 170, Height = 18, Location = new Point(20, 96) });

            (string id, string label, string glyph)[] items =
            {
                ("overview", "Overview", "▦"),
                ("ai", "Assistant", "✦"),
                ("processes", "Processes", "☰"),
                ("system", "System", "◈"),
                ("settings", "Settings", "⚙"),
            };
            int y = 122;
            foreach (var it in items)
            {
                var b = new NavButton(it.glyph, it.label) { Location = new Point(12, y), Width = 182, Height = 42 };
                b.Click += (_, _) => ShowPage(it.id);
                _sidebar.Controls.Add(b);
                _nav[it.id] = b;
                y += 48;
            }

            var safe = new Panel { Height = 96, Dock = DockStyle.Bottom, BackColor = Color.FromArgb(11, 18, 24), Padding = new Padding(18, 12, 12, 8) };
            safe.Controls.Add(new Label { Text = "●  PROTECTED", ForeColor = Mint, Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), Dock = DockStyle.Top, Height = 20 });
            safe.Controls.Add(new Label { Text = "Focused apps and system processes are never trimmed.", ForeColor = Muted, Font = new Font("Segoe UI", 7.5f), Dock = DockStyle.Fill });
            _sidebar.Controls.Add(safe);
        }

        private void BuildHeader()
        {
            _title.Text = "Overview";
            _title.Font = new Font("Segoe UI Semibold", 21f);
            _title.ForeColor = Ink;
            _title.AutoSize = true;
            _title.Location = new Point(26, 14);

            _subtitle.Text = "Live memory, focused app and safe actions";
            _subtitle.Font = new Font("Segoe UI", 8.5f);
            _subtitle.ForeColor = Muted;
            _subtitle.AutoSize = true;
            _subtitle.Location = new Point(28, 58);

            _chips.FlowDirection = FlowDirection.LeftToRight;
            _chips.WrapContents = false;
            _chips.AutoSize = true;
            _chips.Location = new Point(430, 24);
            _chips.BackColor = Bg;

            var close = ChromeButton("✕", Rose);
            close.Click += (_, _) =>
            {
                if (SettingsManager.Current.RunInBackground) HideToTray();
                else { _exiting = true; Close(); }
            };
            var max = ChromeButton("□", Muted);
            max.Click += (_, _) => WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
            var min = ChromeButton("—", Muted);
            min.Click += (_, _) => WindowState = FormWindowState.Minimized;
            var chrome = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                Width = 134,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                BackColor = Bg,
                Padding = new Padding(0, 12, 0, 0),
            };
            chrome.Controls.Add(close);
            chrome.Controls.Add(max);
            chrome.Controls.Add(min);

            _header.Controls.Add(_title);
            _header.Controls.Add(_subtitle);
            _header.Controls.Add(_chips);
            _header.Controls.Add(chrome);

            foreach (Control c in new Control[] { _header, _title, _subtitle })
                c.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, 0x2, 0); } };
            _header.DoubleClick += (_, _) => WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        }

        private Button ChromeButton(string glyph, Color hover)
        {
            var b = new Button { Text = glyph, Width = 38, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Bg, ForeColor = Muted, Font = new Font("Segoe UI", 9f), TabStop = false };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = hover;
            b.MouseEnter += (_, _) => b.ForeColor = Ink;
            b.MouseLeave += (_, _) => b.ForeColor = Muted;
            return b;
        }

        private void ShowPage(string id)
        {
            _page = id;
            foreach (var kv in _nav) kv.Value.Active = kv.Key == id;
            (_title.Text, _subtitle.Text) = id switch
            {
                "ai" => ("Assistant", "Plain-language advice for this PC"),
                "processes" => ("Processes", "Working sets, CPU and protection state"),
                "system" => ("System", "GPU, vendor controls and power profile"),
                "settings" => ("Settings", "How cleanup and eco mode behave"),
                _ => ("Overview", "Live memory, focused app and safe actions"),
            };
            PurgePageFields();
            _body.Controls.Clear();
            _body.Controls.Add(id switch
            {
                "ai" => AiPage(),
                "processes" => ProcessPage(),
                "system" => SystemPage(),
                "settings" => SettingsPage(),
                _ => OverviewPage(),
            });
            if (id == "processes") PopulateGrid(_snapshot);
        }

        // ── cards ───────────────────────────────────────────────────────────
        private static RoundedPanel Card(string? eyebrow = null)
        {
            var p = new RoundedPanel { BackColor = PanelBg, Radius = 14, Padding = eyebrow is not null ? new Padding(18, 42, 18, 16) : new Padding(18), Margin = new Padding(0, 0, 14, 14), Dock = DockStyle.Fill };
            if (eyebrow is not null)
                p.Controls.Add(new Label { Text = eyebrow.ToUpperInvariant(), ForeColor = Color.FromArgb(90, 112, 124), Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), AutoSize = true, Location = new Point(18, 16), UseMnemonic = false });
            return p;
        }

        private static Label Line(string text, Color color, float size = 9f, bool bold = false, int height = 20)
            => new() { Text = text, ForeColor = color, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), Dock = DockStyle.Top, Height = height, AutoEllipsis = true };

        // ── overview ────────────────────────────────────────────────────────
        private Control OverviewPage()
        {
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, BackColor = Bg };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 31));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 31));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 42));

            var ramCard = Card("Memory");
            var ramInner = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = PanelBg };
            ramInner.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 158));
            ramInner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var gaugeHost = new Panel { Dock = DockStyle.Fill, BackColor = PanelBg };
            _ram.Dock = DockStyle.Fill;
            gaugeHost.Controls.Add(_ram);
            var ramText = new Panel { Dock = DockStyle.Fill, BackColor = PanelBg, Padding = new Padding(14, 26, 0, 0) };
            _ramLine.Text = "—";
            _ramLine.Font = new Font("Segoe UI Semibold", 13f);
            _ramLine.ForeColor = Ink;
            _ramLine.Dock = DockStyle.Top;
            _ramLine.Height = 34;
            _ramSub.Text = "Reading memory…";
            _ramSub.ForeColor = Muted;
            _ramSub.Font = new Font("Segoe UI", 8f);
            _ramSub.Dock = DockStyle.Top;
            _ramSub.Height = 44;
            var commit = new Label { Text = "Cleanup runs only when memory gets tight.", ForeColor = Color.FromArgb(90, 112, 124), Font = new Font("Segoe UI", 7.5f), Dock = DockStyle.Fill };
            ramText.Controls.Add(commit);
            ramText.Controls.Add(_ramSub);
            ramText.Controls.Add(_ramLine);
            ramInner.Controls.Add(gaugeHost, 0, 0);
            ramInner.Controls.Add(ramText, 1, 0);
            ramCard.Controls.Add(ramInner);
            grid.Controls.Add(ramCard, 0, 0);

            var focusCard = Card("Focused application");
            var focusStack = new Panel { Dock = DockStyle.Fill, BackColor = PanelBg };
            _focusName.Text = "—";
            _focusName.Font = new Font("Segoe UI Semibold", 15f);
            _focusName.ForeColor = Ink;
            _focusName.Dock = DockStyle.Top;
            _focusName.Height = 32;
            _focusMeta.Text = "";
            _focusMeta.ForeColor = Muted;
            _focusMeta.Font = new Font("Segoe UI", 8f);
            _focusMeta.Dock = DockStyle.Top;
            _focusMeta.Height = 20;
            _focusNote.Text = "Protected — never trimmed";
            _focusNote.ForeColor = Mint;
            _focusNote.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
            _focusNote.Dock = DockStyle.Top;
            _focusNote.Height = 24;
            focusStack.Controls.Add(new Label { Text = "Cleaning pauses while this window is active. Other apps are cleaned only when idle and memory is tight.", ForeColor = Muted, Font = new Font("Segoe UI", 7.5f), Dock = DockStyle.Top, Height = 34 });
            var focusOpen = LinkButton("Open Task Manager →", (_, _) => Open("taskmgr.exe"));
            focusOpen.Dock = DockStyle.Bottom;
            focusStack.Controls.Add(focusOpen);
            focusStack.Controls.Add(new Label { Text = "", Height = 8, Dock = DockStyle.Top });
            focusStack.Controls.Add(_focusNote);
            focusStack.Controls.Add(_focusMeta);
            focusStack.Controls.Add(_focusName);
            _focusStats.Dock = DockStyle.Bottom;
            _focusStats.Height = 64;
            _focusStats.ForeColor = Color.FromArgb(90,112,124);
            _focusStats.Font = new Font("Segoe UI", 8f);
            focusStack.Controls.Add(_focusStats);
            focusCard.Controls.Add(focusStack);
            grid.Controls.Add(focusCard, 1, 0);
            grid.SetColumnSpan(focusCard, 2);

            var cpuCard = Card("Processor");
            _cpuValue.Text = "—";
            _cpuValue.Font = new Font("Segoe UI Semibold", 26f);
            _cpuValue.ForeColor = Ink;
            _cpuValue.Dock = DockStyle.Top;
            _cpuValue.Height = 54;
            _cpuMeta.Text = "";
            _cpuMeta.ForeColor = Muted;
            _cpuMeta.Font = new Font("Segoe UI", 8f);
            _cpuMeta.Dock = DockStyle.Top;
            _cpuMeta.Height = 26;
            _cpuSpark.Height = 84;
            _cpuSpark.Dock = DockStyle.Bottom;
            cpuCard.Controls.Add(_cpuSpark);
            cpuCard.Controls.Add(_cpuMeta);
            cpuCard.Controls.Add(_cpuValue);
            grid.Controls.Add(cpuCard, 0, 1);

            var gpuCard = Card("Graphics");
            _gpuName.Text = "—";
            _gpuName.Font = new Font("Segoe UI Semibold", 11.5f);
            _gpuName.ForeColor = Ink;
            _gpuName.Dock = DockStyle.Top;
            _gpuName.Height = 28;
            _gpuMeta.Text = "";
            _gpuMeta.ForeColor = Muted;
            _gpuMeta.Font = new Font("Segoe UI", 8f);
            _gpuMeta.Dock = DockStyle.Top;
            _gpuMeta.Height = 40;
            var gfx = LinkButton("Graphics settings →", (_, _) => Open("ms-settings:display-advancedgraphics"));
            gfx.Dock = DockStyle.Bottom;
            gpuCard.Controls.Add(gfx);
            gpuCard.Controls.Add(_gpuMeta);
            gpuCard.Controls.Add(_gpuName);
            grid.Controls.Add(gpuCard, 1, 1);

            var actCard = Card("Quick actions");
            var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = PanelBg };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            actions.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            actions.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            actions.Controls.Add(Pill("Quick clean", Mint, () => DoClean(CleanLevel.Standard)), 0, 0);
            actions.Controls.Add(Pill("Free background memory", Mint, async () => await SafeTrimAsync()), 1, 0);
            actions.Controls.Add(Pill("Deep clean", Amber, () => DoClean(CleanLevel.Deep)), 0, 1);
            actions.Controls.Add(Pill("Power settings", Muted, () => Open("control.exe powercfg.cpl")), 1, 1);
            actCard.Controls.Add(actions);
            grid.Controls.Add(actCard, 2, 1);

            var outer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Bg };
            outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            outer.Controls.Add(BuildModeBar(), 0, 0);
            outer.Controls.Add(grid, 0, 1);
            return outer;
        }

        private Control BuildModeBar()
        {
            var row = new Panel { Dock = DockStyle.Fill, BackColor = Bg };
            _modeBar.Dock = DockStyle.Left;
            _modeBar.Width = 680;
            _modeBar.FlowDirection = FlowDirection.LeftToRight;
            _modeBar.WrapContents = false;
            _modeBar.BackColor = Bg;
            _modeBar.Controls.Clear();
            foreach (var m in ModeManager.All)
            {
                var b = new Button
                {
                    Text = m.Id == "eco" ? "Eco" : m.Name,
                    Tag = m.Id,
                    Width = 104,
                    Height = 30,
                    Margin = new Padding(0, 4, 8, 0),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(16, 25, 35),
                    ForeColor = Muted,
                    Font = new Font("Segoe UI Semibold", 8.5f),
                    Cursor = Cursors.Hand,
                };
                b.FlatAppearance.BorderColor = Stroke;
                b.Click += async (_, _) => await ApplyModeNowAsync(m.Id);
                _modeBar.Controls.Add(b);
            }
            _modeHint.Dock = DockStyle.Fill;
            _modeHint.AutoEllipsis = true;
            _modeHint.ForeColor = Muted;
            _modeHint.Font = new Font("Segoe UI", 8f);
            _modeHint.TextAlign = ContentAlignment.MiddleLeft;
            _modeHint.Text = "Mode is detected automatically from the app you are using";
            row.Controls.Add(_modeHint);
            row.Controls.Add(_modeBar);
            return row;
        }

        private async Task ApplyModeNowAsync(string id)
        {
            SettingsManager.Current.Mode = id;
            SettingsManager.Save();
            if (_snapshot is null) _snapshot = await SystemProbe.CaptureAsync(true);
            var snap = _snapshot!;
            var mode = ModeManager.Resolve(id, snap);
            var focusText = mode.Id == "gaming" ? GameFocus.Apply(snap) : "";
            if (mode.Id != "gaming") GameFocus.Restore();

            SystemProbe.ApplyEcoToBackground(snap.Processes, 0, false);
            _ecoDone.Clear();

            var eco = 0;
            if (SettingsManager.Current.AutoEco && mode.EcoBackground)
            {
                var eligible = snap.Processes.Where(p => !_ecoDone.Contains($"{p.Name}|{p.Pid}")).ToList();
                eco = SystemProbe.ApplyEcoToBackground(eligible, Math.Max(40, Math.Min(SettingsManager.Current.EcoMinWorkingSetMB, mode.EcoMinWorkingSetMB)), true);
                foreach (var p in eligible) _ecoDone.Add($"{p.Name}|{p.Pid}");
            }
            var (trimmed, freed, _) = SystemProbe.TrimBackground(snap.Processes, 70);
            _modeSummary = $"{mode.Name}" + (string.IsNullOrEmpty(focusText) ? "" : $" · {focusText}") + $" · {eco} apps throttled · {freed:0} MB freed · cleaning {(mode.PauseCleaning ? "paused" : "armed")}";
            _modeHint.Text = _modeSummary;
            await RefreshAsync(true);
            if (!_busy) await AnalyzeAsync();
        }

        private void ApplyModeAndEco()
        {
            var snap = _snapshot;
            if (snap is null) return;
            var mode = ModeManager.Resolve(SettingsManager.Current.Mode, snap);
            if (mode.Id != _lastModeId)
            {
                _lastModeId = mode.Id;
                if (mode.Id == "gaming")
                {
                    var focus = GameFocus.Apply(snap);
                    _modeSummary = $"Gaming · {focus} · cleaning paused";
                }
                else
                {
                    GameFocus.Restore();
                }
            }
            foreach (Control c in _modeBar.Controls)
            {
                if (c is Button b && b.Tag is string id)
                {
                    var on = id.Equals(SettingsManager.Current.Mode, StringComparison.OrdinalIgnoreCase) ||
                             (SettingsManager.Current.Mode == "auto" && mode.Id == id);
                    b.BackColor = on ? Color.FromArgb(20, 44, 34) : Color.FromArgb(16, 25, 35);
                    b.ForeColor = on ? Mint : Muted;
                    b.FlatAppearance.BorderColor = on ? Color.FromArgb(38, 74, 58) : Stroke;
                }
            }
            if (SettingsManager.Current.AutoEco && mode.EcoBackground)
            {
                var eligible = snap.Processes.Where(p => !_ecoDone.Contains($"{p.Name}|{p.Pid}")).ToList();
                var n = SystemProbe.ApplyEcoToBackground(eligible, Math.Max(SettingsManager.Current.EcoMinWorkingSetMB, mode.EcoMinWorkingSetMB), true);
                foreach (var p in eligible) _ecoDone.Add($"{p.Name}|{p.Pid}");
                _modeSummary = $"{mode.Name} · {n} apps in eco · cleaning {(mode.PauseCleaning ? "paused" : "armed")} · {snap.ForegroundName} protected";
                _modeHint.Text = _modeSummary;
            }
            else
            {
                _modeSummary = $"{mode.Name} mode · eco throttling is off";
                _modeHint.Text = _modeSummary;
            }
        }

        private Button Pill(string text, Color color, Action action)
        {
            var b = new Button { Text = text, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 8), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(19, 31, 40), ForeColor = color, Font = new Font("Segoe UI Semibold", 9.5f), Cursor = Cursors.Hand };
            b.FlatAppearance.BorderColor = Stroke;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(26, 42, 52);
            b.Click += (_, _) => action();
            return b;
        }

        private Button Pill(string text, Color color, Func<Task> action)
        {
            var b = Pill(text, color, () => { });
            b.Click += async (_, _) => await action();
            return b;
        }

        private static Button LinkButton(string text, EventHandler onClick)
        {
            var b = new Button { Text = text, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = PanelBg, ForeColor = Mint, Font = new Font("Segoe UI Semibold", 9.5f), TextAlign = ContentAlignment.MiddleLeft, Cursor = Cursors.Hand };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Panel2;
            b.Click += onClick;
            return b;
        }

        // ── AI ──────────────────────────────────────────────────────────────
        private Control AiPage()
        {
            var wrap = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Bg };
            wrap.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
            wrap.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var bar = new RoundedPanel { Dock = DockStyle.Fill, BackColor = PanelBg, Radius = 14, Padding = new Padding(18, 10, 14, 10), Margin = new Padding(0, 0, 0, 8) };
            _aiKey.Text = "AI is optional. A key is stored on this PC; add your own in Settings to use your account.";
            _aiKey.ForeColor = Muted;
            _aiKey.Font = new Font("Segoe UI", 8f);
            _aiKey.Dock = DockStyle.None;
            _aiKey.Location = new Point(18, 8);
            _aiKey.Width = 780;
            _aiKey.Height = 18;
            _aiStatus.Text = "We look at memory, processor, graphics and your running apps. Nothing leaves this PC until you press Analyse.";
            _aiStatus.ForeColor = Muted;
            _aiStatus.Font = new Font("Segoe UI", 8f);
            _aiStatus.Dock = DockStyle.None;
            _aiStatus.Location = new Point(18, 30);
            _aiStatus.Width = 780;
            _aiStatus.Height = 24;
            _analyze.Text = "Analyse my PC";
            _analyze.Width = 168;
            _analyze.Height = 34;
            _analyze.Dock = DockStyle.None;
            _analyze.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _analyze.Margin = new Padding(0);
            _analyze.FlatAppearance.BorderSize = 1;
            _analyze.FlatStyle = FlatStyle.Flat;
            _analyze.BackColor = Color.FromArgb(18, 40, 32);
            _analyze.ForeColor = Mint;
            _analyze.Font = new Font("Segoe UI Semibold", 9f);
            _analyze.Cursor = Cursors.Hand;
            _analyze.FlatAppearance.BorderColor = Color.FromArgb(38, 74, 58);
            _analyze.Click += async (_, _) => await AnalyzeAsync();
            _deviceLine.Location = new Point(18, 56);
            _deviceLine.Width = 780;
            _deviceLine.Height = 46;
            _deviceLine.ForeColor = Color.FromArgb(90, 112, 124);
            _deviceLine.Font = new Font("Segoe UI", 7.5f);
            _deviceLine.Text = "Detecting this device…";
            bar.Resize += (_, _) =>
            {
                var textWidth = Math.Max(320, bar.ClientSize.Width - 230);
                _aiKey.Width = textWidth;
                _aiStatus.Width = textWidth;
                _deviceLine.Width = textWidth;
                _analyze.Left = Math.Max(18 + textWidth + 12, bar.ClientSize.Width - _analyze.Width - 16);
                _analyze.Top = (bar.ClientSize.Height - _analyze.Height) / 2;
            };
            bar.Controls.Add(_deviceLine);
            bar.Controls.Add(_analyze);
            bar.Controls.Add(_aiStatus);
            bar.Controls.Add(_aiKey);
            wrap.Controls.Add(bar, 0, 0);

            var reportCard = Card("Your recommendations");
            _report.Dock = DockStyle.Fill;
            _report.FlowDirection = FlowDirection.TopDown;
            _report.WrapContents = false;
            _report.AutoScroll = false;
            _report.BackColor = PanelBg;
            _report.Padding = new Padding(0, 22, 0, 10);
            _report.Controls.Add(new Label { Text = "Press \u201cAnalyse my PC\u201d. You will get plain-language suggestions, sorted by impact.", ForeColor = Muted, Font = new Font("Segoe UI", 9f), AutoSize = false, Width = 760, Height = 32, Margin = new Padding(0, 6, 0, 8) });
            reportCard.Controls.Add(_report);
            wrap.Controls.Add(reportCard, 0, 1);
            return wrap;
        }

        // ── processes ───────────────────────────────────────────────────────
        private Control ProcessPage()
        {
            var wrap = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, BackColor = Bg };
            wrap.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            wrap.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var bar = new Panel { Dock = DockStyle.Fill, BackColor = Bg };
            _procMeta.Text = "Waiting for first sample…";
            _procMeta.ForeColor = Muted;
            _procMeta.Font = new Font("Segoe UI", 8.5f);
            _procMeta.Dock = DockStyle.Left;
            _procMeta.Width = 560;
            _procMeta.TextAlign = ContentAlignment.MiddleLeft;
            var trim = new Button { Text = "Trim selected", Width = 130, Height = 30, Dock = DockStyle.Right, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(18, 40, 32), ForeColor = Mint, Font = new Font("Segoe UI Semibold", 8.5f) };
            trim.FlatAppearance.BorderColor = Stroke;
            trim.Click += async (_, _) => await TrimSelectedAsync();
            var refresh = new Button { Text = "Refresh", Width = 96, Height = 30, Dock = DockStyle.Right, FlatStyle = FlatStyle.Flat, BackColor = Panel2, ForeColor = Muted, Font = new Font("Segoe UI", 8.5f) };
            refresh.FlatAppearance.BorderColor = Stroke;
            refresh.Click += async (_, _) => await RefreshAsync(true);
            bar.Controls.Add(_procMeta);
            bar.Controls.Add(trim);
            bar.Controls.Add(refresh);
            wrap.Controls.Add(bar, 0, 0);

            var card = Card(null);
            _grid.Dock = DockStyle.Fill;
            _grid.BackgroundColor = Color.FromArgb(8, 13, 18);
            _grid.GridColor = Color.FromArgb(24, 36, 45);
            _grid.BorderStyle = BorderStyle.None;
            _grid.EnableHeadersVisualStyles = false;
            _grid.ColumnHeadersDefaultCellStyle.BackColor = Panel2;
            _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Panel2;
            _grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Muted;
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = Muted;
            _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 8.5f);
            _grid.ColumnHeadersHeight = 34;
            _grid.DefaultCellStyle.BackColor = Color.FromArgb(9, 15, 21);
            _grid.DefaultCellStyle.ForeColor = Ink;
            _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(22, 44, 36);
            _grid.DefaultCellStyle.SelectionForeColor = Ink;
            _grid.DefaultCellStyle.Font = new Font("Segoe UI", 8.5f);
            _grid.RowHeadersVisible = false;
            _grid.AllowUserToAddRows = false;
            _grid.ReadOnly = true;
            _grid.RowTemplate.Height = 30;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.Columns.Add("name", "App");
            _grid.Columns.Add("ram", "Memory (MB)");
            _grid.Columns.Add("cpu", "CPU %");
            _grid.Columns.Add("state", "Status");
            card.Controls.Add(_grid);
            wrap.Controls.Add(card, 0, 1);
            return wrap;
        }

        // ── system ──────────────────────────────────────────────────────────
        private Control SystemPage()
        {
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, BackColor = Bg };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 32));

            var machine = Card("Machine");
            _sysMachine.Dock = DockStyle.Top;
            _sysMachine.Height = 46;
            _sysMachine.ForeColor = Ink;
            _sysMachine.Font = new Font("Segoe UI Semibold", 12f);
            _sysSession.ForeColor = Muted;
            _sysSession.Dock = DockStyle.Top;
            _sysSession.Height = 60;
            _sysSession.Font = new Font("Segoe UI", 8.5f);
            machine.Controls.Add(_sysSession);
            machine.Controls.Add(_sysMachine);
            grid.Controls.Add(machine, 0, 0);

            var graphics = Card("Graphics");
            _sysGpu.Dock = DockStyle.Top;
            _sysGpu.Height = 44;
            _sysGpu.ForeColor = Ink;
            _sysGpu.Font = new Font("Segoe UI Semibold", 11f);
            _sysDriver.Dock = DockStyle.Top;
            _sysDriver.Height = 40;
            _sysDriver.ForeColor = Muted;
            _sysDriver.Font = new Font("Segoe UI", 8.5f);
            var gfxBtn = LinkButton("Windows graphics settings →", (_, _) => Open("ms-settings:display-advancedgraphics"));
            gfxBtn.Dock = DockStyle.Bottom;
            graphics.Controls.Add(gfxBtn);
            graphics.Controls.Add(_sysDriver);
            graphics.Controls.Add(_sysGpu);
            grid.Controls.Add(graphics, 1, 0);

            var power = Card("Power & thermals");
            _sysPower.Dock = DockStyle.Top;
            _sysPower.Height = 46;
            _sysPower.ForeColor = Ink;
            _sysPower.Font = new Font("Segoe UI Semibold", 11f);
            var powerBtn = LinkButton("Open power options →", (_, _) => Open("control.exe powercfg.cpl"));
            powerBtn.Dock = DockStyle.Bottom;
            power.Controls.Add(powerBtn);
            power.Controls.Add(_sysPower);
            grid.Controls.Add(power, 0, 1);

            var vendor = Card("Vendor controls");
            _sysVendor.Dock = DockStyle.Fill;
            _sysVendor.ForeColor = Muted;
            _sysVendor.Font = new Font("Segoe UI", 8.5f);
            var vendorBtn = LinkButton("Open vendor app →", (_, _) => OpenVendor());
            vendorBtn.Dock = DockStyle.Bottom;
            vendor.Controls.Add(_sysVendor);
            vendor.Controls.Add(vendorBtn);
            grid.Controls.Add(vendor, 1, 1);

            var note = Card("Recommendation policy");
            note.Controls.Add(Line("Smart Skii-mer never changes GPU drivers, fan curves or vendor modes automatically. It detects the vendor stack (Armoury Crate, MSI Center, Omen, etc.) and recommends the profile; you apply it in the vendor app.", Muted, 8.5f, false, 56));
            grid.Controls.Add(note, 0, 2);
            grid.SetColumnSpan(note, 2);
            return grid;
        }

        // ── settings ────────────────────────────────────────────────────────
        private Control SettingsPage()
        {
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Bg };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));

            var governor = Card("Cleanup behaviour");
            var stack = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = PanelBg };
            stack.Controls.Add(ToggleRow(_autoClean, "Auto-clean on memory pressure", "Runs a safe trim when RAM crosses the threshold."));
            stack.Controls.Add(ToggleRow(_pauseGame, "Protect fullscreen games", "Pauses cleaning in a fullscreen game."));
            stack.Controls.Add(ToggleRow(_trimBg, "Trim background working sets", "Only idle background processes."));
            stack.Controls.Add(ToggleRow(_background, "Keep running in the background", "Closing the window keeps it in the tray."));
            stack.Controls.Add(ToggleRow(_autoEco, "Eco mode for background apps", "Idle apps only; never the app you are using."));
            stack.Controls.Add(ToggleRow(_autoAi, "Assistant may act at critical pressure", "At 85% RAM while gaming, frees background memory."));
            stack.Controls.Add(ToggleRow(_remote, "Phone remote (same Wi-Fi)", "Pair with a 4-digit code from the phone browser."));
            _remote.CheckedChanged += (_, _) =>
            {
                if (_syncingToggles) return;
                ToggleRemote(_remote.Checked);
            };
            var enableRemote = new Button { Text = "Allow phone access (admin, once)", Width = 230, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(18, 40, 32), ForeColor = Mint, Font = new Font("Segoe UI Semibold", 8.5f), Margin = new Padding(0, 6, 0, 0) };
            enableRemote.FlatAppearance.BorderColor = Stroke;
            enableRemote.Click += (_, _) =>
            {
                try
                {
                    var cmd = System.IO.Path.Combine(AppContext.BaseDirectory, "enable-remote.cmd");
                    if (!System.IO.File.Exists(cmd)) { Toast("enable-remote.cmd not found next to the app", true); return; }
                    Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{cmd}\"") { UseShellExecute = true, Verb = "runas" });
                }
                catch { Toast("Admin permission was declined", true); }
            };
            stack.Controls.Add(enableRemote);
            governor.Controls.Add(stack);
            grid.Controls.Add(governor, 0, 0);

            var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Bg };
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 42));

            var aiCard = Card("Any AI provider");
            aiCard.Controls.Add(new Label
            {
                Text = "Optional. Pick a provider, paste its key. Works with any OpenAI-compatible API plus Gemini.",
                ForeColor = Muted,
                Font = new Font("Segoe UI", 8f),
                AutoSize = false,
                Location = new Point(18, 40),
                Size = new Size(400, 30),
            });

            _provider.DropDownStyle = ComboBoxStyle.DropDownList;
            _provider.FlatStyle = FlatStyle.Flat;
            _provider.BackColor = Color.FromArgb(8, 13, 18);
            _provider.ForeColor = Ink;
            _provider.Font = new Font("Segoe UI", 9f);
            _provider.Location = new Point(18, 72);
            _provider.Width = 400;
            foreach (var p in AiProviders.Presets) _provider.Items.Add(p.Name);
            _provider.SelectedIndex = 0;
            aiCard.Controls.Add(_provider);

            aiCard.Controls.Add(new Label { Text = "Key", ForeColor = Muted, Font = new Font("Segoe UI", 8f), AutoSize = true, Location = new Point(18, 108) });
            _apiKey.Dock = DockStyle.None;
            _apiKey.Location = new Point(18, 128);
            _apiKey.Width = 400;
            _apiKey.Height = 28;
            _apiKey.UseSystemPasswordChar = true;
            _apiKey.BackColor = Color.FromArgb(8, 13, 18);
            _apiKey.ForeColor = Ink;
            _apiKey.BorderStyle = BorderStyle.FixedSingle;
            _apiKey.Font = new Font("Segoe UI", 9f);
            _apiKey.PlaceholderText = "sk-or-...";
            aiCard.Controls.Add(_apiKey);

            aiCard.Controls.Add(new Label { Text = "Model", ForeColor = Muted, Font = new Font("Segoe UI", 8f), AutoSize = true, Location = new Point(18, 160) });
            _model.Location = new Point(18, 180);
            _model.Width = 400;
            _model.Height = 28;
            _model.BackColor = Color.FromArgb(8, 13, 18);
            _model.ForeColor = Ink;
            _model.BorderStyle = BorderStyle.FixedSingle;
            _model.Font = new Font("Segoe UI", 9f);
            aiCard.Controls.Add(_model);

            aiCard.Controls.Add(new Label { Text = "Endpoint (optional override)", ForeColor = Muted, Font = new Font("Segoe UI", 8f), AutoSize = true, Location = new Point(18, 212) });
            _baseUrl.Location = new Point(18, 232);
            _baseUrl.Width = 400;
            _baseUrl.Height = 28;
            _baseUrl.BackColor = Color.FromArgb(8, 13, 18);
            _baseUrl.ForeColor = Ink;
            _baseUrl.BorderStyle = BorderStyle.FixedSingle;
            _baseUrl.Font = new Font("Segoe UI", 9f);
            _baseUrl.PlaceholderText = "https://api.deepseek.com/v1";
            aiCard.Controls.Add(_baseUrl);

            _provider.SelectedIndexChanged += (_, _) =>
            {
                var preset = AiProviders.Presets[Math.Max(0, _provider.SelectedIndex)];
                _model.Text = preset.DefaultModel;
                _baseUrl.Text = preset.Kind == "compat" ? preset.BaseUrl : "";
                _apiKey.PlaceholderText = preset.KeyHint;
            };

            right.Controls.Add(aiCard, 0, 0);

            var sc = Card("Thresholds & shortcut");
            _remoteInfo.Dock = DockStyle.None;
            _remoteInfo.AutoSize = false;
            _remoteInfo.Location = new Point(18, 172);
            _remoteInfo.Size = new Size(400, 52);
            _remoteInfo.ForeColor = Mint;
            _remoteInfo.Font = new Font("Segoe UI", 8f);
            sc.Controls.Add(_remoteInfo);
            sc.Controls.Add(new Label { Text = "Trigger at RAM %", ForeColor = Muted, AutoSize = true, Location = new Point(18, 48) });
            _threshold.Minimum = 50;
            _threshold.Maximum = 95;
            _threshold.Width = 70;
            _threshold.Location = new Point(190, 44);
            sc.Controls.Add(_threshold);
            sc.Controls.Add(new Label { Text = "Check interval (s)", ForeColor = Muted, AutoSize = true, Location = new Point(18, 80) });
            _interval.Minimum = 2;
            _interval.Maximum = 60;
            _interval.Width = 70;
            _interval.Location = new Point(190, 76);
            sc.Controls.Add(_interval);

            sc.Controls.Add(new Label { Text = "Deep clean hotkey", ForeColor = Muted, AutoSize = true, Location = new Point(18, 116) });
            _hotkey.Width = 190;
            _hotkey.Height = 28;
            _hotkey.ReadOnly = true;
            _hotkey.BackColor = Color.FromArgb(8, 13, 18);
            _hotkey.ForeColor = Ink;
            _hotkey.BorderStyle = BorderStyle.FixedSingle;
            _hotkey.Font = new Font("Segoe UI", 9f);
            _hotkey.Location = new Point(18, 138);
            _hotkey.KeyDown += (_, e) =>
            {
                e.SuppressKeyPress = true;
                if (e.KeyCode is Keys.ControlKey or Keys.Menu or Keys.ShiftKey or Keys.LWin or Keys.RWin) return;
                var parts = new System.Collections.Generic.List<string>();
                if ((e.Modifiers & Keys.Control) != 0) parts.Add("Ctrl");
                if ((e.Modifiers & Keys.Alt) != 0) parts.Add("Alt");
                if ((e.Modifiers & Keys.Shift) != 0) parts.Add("Shift");
                parts.Add(e.KeyCode.ToString());
                _hotkey.Text = string.Join("+", parts);
            };
            sc.Controls.Add(_hotkey);

            var save = new Button { Text = "Save settings", Width = 140, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(18, 40, 32), ForeColor = Mint, Font = new Font("Segoe UI Semibold", 8.5f), Location = new Point(18, 182) };
            save.FlatAppearance.BorderColor = Stroke;
            save.Click += (_, _) => SaveSettings();
            sc.Controls.Add(save);

            right.Controls.Add(sc, 0, 1);
            grid.Controls.Add(right, 1, 0);
            return grid;
        }

        private Control ToggleRow(Toggle toggle, string title, string desc)
        {
            var p = new Panel { Width = 480, Height = 56, BackColor = PanelBg, Margin = new Padding(0, 0, 0, 6) };
            p.Controls.Add(new Label { Text = title, ForeColor = Ink, Font = new Font("Segoe UI Semibold", 9.5f), AutoSize = true, Location = new Point(0, 2) });
            p.Controls.Add(new Label { Text = desc, ForeColor = Muted, Font = new Font("Segoe UI", 8f), AutoSize = false, Width = 370, Height = 28, Location = new Point(0, 24) });
            toggle.Location = new Point(420, 8);
            toggle.Width = 50;
            toggle.Height = 24;
            p.Controls.Add(toggle);
            return p;
        }

        // ── data ────────────────────────────────────────────────────────────
        private async Task RefreshAsync(bool full)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                var snap = await Task.Run(async () => await SystemProbe.CaptureAsync(full));
                _snapshot = snap;
                Apply(snap, full);
                if (full) ApplyModeAndEco();
            }
            catch (Exception ex) { _log.Add(ex.Message); }
            finally { _busy = false; }
        }

        private void Apply(SystemSnapshot s, bool full)
        {
            var pct = (int)Math.Round(s.Memory.UsedPhysicalGB / Math.Max(1, s.Memory.TotalPhysicalGB) * 100);
            var selfMb = MemoryEngine.GetSelfWorkingSetMB();
            if (selfMb > 80)
            {
                MemoryEngine.TrimSelfWorkingSet();
                selfMb = MemoryEngine.GetSelfWorkingSetMB();
            }
            _ram.Value = pct;
            _ramLine.Text = $"{s.Memory.UsedPhysicalGB:0.0} / {s.Memory.TotalPhysicalGB:0.0} GB";
            _ramSub.Text = $"{s.Memory.AvailablePhysicalGB:0.0} GB free\n{s.Memory.CommitLoadPercent}% reserved";
            if (full)
            {
                _cpuValue.Text = $"{s.CpuPercent:0}%";
                _cpuMeta.Text = $"{Environment.ProcessorCount} logical cores · sampled live";
                _cpuSpark.Add(s.CpuPercent);
            }
            _gpuName.Text = string.IsNullOrWhiteSpace(s.Gpu) ? "No graphics adapter detected" : s.Gpu;
            _gpuMeta.Text = string.IsNullOrWhiteSpace(s.GpuDriver) ? "Driver version unavailable" : $"Driver {s.GpuDriver}";
            _focusName.Text = s.ForegroundName;
            _focusMeta.Text = $"Using {(s.Processes.FirstOrDefault(x => x.Foreground)?.WorkingSetMB ?? 0):0} MB";
            _focusNote.Text = s.ForegroundFullscreenGame ? "Fullscreen game — cleaning paused" : "Protected — never trimmed";
            _focusStats.Text = $"Memory in use: {(s.Processes.FirstOrDefault(x => x.Foreground)?.WorkingSetMB ?? 0):0} MB\nMode: {(s.ForegroundFullscreenGame ? "fullscreen game" : "desktop")}\nProtection: never cleaned";
            _focusNote.ForeColor = s.ForegroundFullscreenGame ? Amber : Mint;

            _sysMachine.Text = $"{s.Manufacturer} {s.Model}".Trim();
            _sysSession.Text = $"Foreground: {s.ForegroundName} (PID {s.ForegroundPid})\nSession state: {(s.ForegroundFullscreenGame ? "fullscreen game" : "desktop")}";
            _sysGpu.Text = s.Gpu;
            _sysDriver.Text = $"Driver {s.GpuDriver}";
            _sysPower.Text = string.IsNullOrWhiteSpace(s.PowerPlan) ? "Unknown power plan" : s.PowerPlan;
            _deviceLine.Text = $"{s.Manufacturer} {s.Model} · {(s.IsLaptop ? "laptop" : "desktop")}\n{s.CpuName} · {s.Gpu}";
            _sysVendor.Text = s.VendorServices.Count == 0
                ? "No vendor control services detected. Use Windows graphics/power settings."
                : "Detected: " + string.Join(", ", s.VendorServices.Take(6));

            UpdateTrayStatus(s);
            _chips.Controls.Clear();
            _chips.Controls.Add(Chip($"RAM {pct}%", pct > 85 ? Rose : pct > 70 ? Amber : Mint));
            _chips.Controls.Add(Chip($"App {selfMb:0.0} MB", selfMb < 30 ? Mint : Amber));
            _chips.Controls.Add(Chip($"CPU {s.CpuPercent:0}%", s.CpuPercent > 80 ? Rose : Mint));
            _chips.Controls.Add(Chip(s.ForegroundFullscreenGame ? "GAME MODE" : "LIVE", s.ForegroundFullscreenGame ? Amber : Mint));

            if (_page == "processes" && full) PopulateGrid(s);
            if (_page == "settings") LoadSettingsUi();
        }

        private Control Chip(string text, Color color) => new Label
        {
            Text = "  " + text + "  ",
            ForeColor = color,
            BackColor = Color.FromArgb(14, 24, 32),
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 6, 8, 0),
            Padding = new Padding(6, 5, 6, 5),
        };

        private void PopulateGrid(SystemSnapshot? s)
        {
            if (s is null || _grid.Columns.Count == 0) return;
            _grid.Rows.Clear();
            foreach (var p in s.Processes.Take(26))
                _grid.Rows.Add(p.Name, p.WorkingSetMB.ToString("0.0"), p.CpuPercent.ToString("0.0"),
                    p.Foreground ? "Focused" : p.Protected ? "Protected" : p.GameLikely ? "Game" : "Background");
            _procMeta.Text = $"{s.Processes.Count} processes · RAM {s.Memory.UsedPhysicalGB:0.0}/{s.Memory.TotalPhysicalGB:0.0} GB · commit {s.Memory.CommitLoadPercent}%";
        }

        private async Task SelfTrimAsync()
        {
            await Task.Yield();
            var mb = MemoryEngine.GetSelfWorkingSetMB();
            if (mb > 80) { MemoryEngine.TrimSelfWorkingSet(); mb = MemoryEngine.GetSelfWorkingSetMB(); }
            _footer.Text = $"Smart Skii-mer is using {mb:0.0} MB itself · your apps are never closed automatically.";
        }

        private void DoClean(CleanLevel level)
        {
            try
            {
                var r = MemoryEngine.SmartClean(level);
                Toast($"Cleaned {r.FreedMB:0.0} MB · RAM now {r.AfterPercent}%");
                _ = RefreshAsync(false);
            }
            catch (Exception ex) { Toast("Clean failed: " + ex.Message, true); }
        }

        private async Task SafeTrimAsync()
        {
            if (_snapshot is null) _snapshot = await SystemProbe.CaptureAsync(true);
            var (n, freed, _) = SystemProbe.TrimBackground(_snapshot.Processes, 80);
            Toast($"Trimmed {n} background process(es) · ~{freed:0.0} MB");
            await RefreshAsync(true);
        }

        private async Task TrimSelectedAsync()
        {
            if (_grid.SelectedRows.Count == 0) { Toast("Select a process first"); return; }
            var pname = _grid.SelectedRows[0].Cells[0].Value?.ToString();
            var p = _snapshot?.Processes.FirstOrDefault(x => x.Name == pname);
            if (p is null) return;
            var (n, freed, _) = SystemProbe.TrimBackground(new[] { p }, 0);
            Toast(n > 0 ? $"Trimmed {p.Name} · ~{freed:0.0} MB" : $"{p.Name} is protected or focused", n == 0);
            await RefreshAsync(true);
        }

        private void Toast(string message, bool error = false)
        {
            _aiStatus.Text = message;
            _aiStatus.ForeColor = error ? Rose : Mint;
            _log.Add(message);
        }

        private async Task AnalyzeAsync()
        {
            if (_busy) return;
            _busy = true;
            _analyze.Enabled = false;
            _analyze.Text = "Looking…";
            _aiStatus.Text = "Checking memory, processor, graphics and running apps…";
            try
            {
                var snap = await Task.Run(async () => await SystemProbe.CaptureAsync(true));
                _snapshot = snap;
                Apply(snap, true);
                var modeName = ModeManager.Resolve(SettingsManager.Current.Mode, snap).Name;
                var report = await AiAdvisor.AnalyzeAsync(snap, SettingsManager.Current.Ai, modeName);
                RenderReport(report);
            }
            catch (Exception ex)
            {
                _aiStatus.Text = "Could not finish the check. Showing local results instead.";
                _aiStatus.ForeColor = Rose;
            }
            finally
            {
                _busy = false;
                _analyze.Enabled = true;
                _analyze.Text = "Analyse my PC";
            }
        }

        private void RenderReport(AiReport r)
        {
            PurgePageFields();
            _report.Controls.Clear();
            if (!r.Ok)
            {
                _aiStatus.Text = "Could not analyse: " + r.Error;
                _aiStatus.ForeColor = Rose;
                return;
            }
            _aiStatus.Text = $"{r.Provider} · {FriendlyHealth(r.Health)}";
            _aiStatus.ForeColor = Mint;

            var grid = new TableLayoutPanel { Dock = DockStyle.None, Width = 900, Height = 300, ColumnCount = 2, RowCount = 2, BackColor = PanelBg, Margin = new Padding(0) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var assessment = AdviceCard("Assessment", string.IsNullOrWhiteSpace(r.Summary) ? "No issues found." : r.Summary, "info", null, 880, 110);
            grid.Controls.Add(assessment, 0, 0);
            grid.SetColumnSpan(assessment, 2);

            var suggestions = r.Suggestions.Take(2).ToList();
            if (suggestions.Count == 0 && string.IsNullOrWhiteSpace(r.Summary))
                suggestions.Add(new AiSuggestion { Title = "Nothing to change", Detail = "The model returned no suggestions for this snapshot.", Impact = "No action needed", Risk = "low", Action = "none" });
            for (int i = 0; i < suggestions.Count; i++)
            {
                var card = AdviceCard(suggestions[i].Title, suggestions[i].Detail + (string.IsNullOrWhiteSpace(suggestions[i].Impact) ? "" : "\n\nImpact: " + suggestions[i].Impact), suggestions[i].Risk, suggestions[i], 430, 190);
                grid.Controls.Add(card, i % 2, 1);
            }
            _report.Controls.Add(grid);
        }

        private Control AdviceCard(string title, string body, string tag, AiSuggestion? s, int width = 452, int height = 136)
        {
            var card = new RoundedPanel { Width = width, Height = height, BackColor = Panel2, Radius = 12, Padding = new Padding(14), Margin = new Padding(0, 0, 0, 8) };
            card.Controls.Add(new Label { Text = tag.ToUpperInvariant(), ForeColor = tag is "high" or "critical" ? Rose : tag is "medium" or "strained" ? Amber : Mint, Font = new Font("Segoe UI", 6.5f, FontStyle.Bold), AutoSize = true, Location = new Point(16, 12) });
            card.Controls.Add(new Label { Text = title, ForeColor = Ink, Font = new Font("Segoe UI Semibold", 10f), AutoSize = false, Width = width - 40, Height = 22, Location = new Point(16, 30) });
            card.Controls.Add(new Label { Text = body, ForeColor = Muted, Font = new Font("Segoe UI", 8f), AutoSize = false, Width = width - 40, Height = height - 66, Location = new Point(16, 54) });
            if (s is not null && s.Action != "none")
            {
                var go = new Button { Text = ActionLabel(s.Action), Width = 150, Height = 26, Left = width - 176, Top = height - 38, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(18, 40, 32), ForeColor = Mint, Font = new Font("Segoe UI Semibold", 8f) };
                go.FlatAppearance.BorderColor = Stroke;
                go.Click += async (_, _) => await ApplyAsync(s);
                card.Controls.Add(go);
            }
            return card;
        }

        private static string FriendlyHealth(string h) => h switch { "healthy" => "everything looks healthy", "strained" => "some pressure", "critical" => "needs attention", _ => "checked" };

        private static string ActionLabel(string a) => a switch
        {
            "trim_background" => "Trim background",
            "open_graphics_settings" => "Open graphics",
            "open_power_settings" => "Open power",
            "open_vendor_app" => "Open vendor app",
            "update_gpu_driver" => "Driver info",
            "disable_startup" => "Startup apps",
            "open_nvidia" => "NVIDIA settings",
            "open_amd" => "AMD settings",
            "open_game_bar" => "Game Bar",
            "set_power_high" => "High performance",
            "set_game_mode" => "Enable Game Mode",
            _ => "Info",
        };

        private async Task ApplyAsync(AiSuggestion s)
        {
            switch (s.Action)
            {
                case "trim_background": await SafeTrimAsync(); break;
                case "open_graphics_settings": Open("ms-settings:display-advancedgraphics"); break;
                case "open_power_settings": Open("control.exe powercfg.cpl"); break;
                case "open_vendor_app": OpenVendor(); break;
                case "update_gpu_driver": Open("ms-settings:windowsupdate"); break;
                case "disable_startup": Open("ms-settings:startupapps"); break;
                case "open_nvidia": OpenNvidia(); break;
                case "open_amd": OpenAmd(); break;
                case "open_game_bar": Open("ms-gamingoverlay:"); break;
                case "set_power_high": ApplyHighPerformance(); break;
                case "set_game_mode": ApplyGameMode(); break;
            }
        }

        private void LoadSettingsUi()
        {
            var s = SettingsManager.Current;
            _autoClean.Checked = s.AutoCleanEnabled;
            _pauseGame.Checked = s.PauseWhenFullscreen;
            _trimBg.Checked = s.TrimBackgroundWorkingSets;
            _background.Checked = s.RunInBackground;
            _autoEco.Checked = s.AutoEco;
            _autoAi.Checked = s.AutoAiOnPressure;
            _syncingToggles = true;
            var presetIndex = Array.FindIndex(AiProviders.Presets, p => p.Kind == s.Ai.Kind || p.Name == s.Ai.ProviderName);
            _provider.SelectedIndex = presetIndex < 0 ? 0 : presetIndex;
            _model.Text = s.Ai.ModelName;
            _baseUrl.Text = s.Ai.CustomBaseUrl;
            var storedKey = LocalKeyStore.LoadProvider();
            _apiKey.Text = string.IsNullOrEmpty(storedKey) ? "" : "********";
            _remote.Checked = _server?.Running == true;
            _syncingToggles = false;
            _remoteInfo.Text = _server?.Running == true
                ? $"Open on your phone: http://{(RemoteServer.LocalAddresses().FirstOrDefault() ?? "<pc-ip>")}:{RemoteServer.Port}/  ·  code {_server.PairCode}"
                : "Remote off.";
            _hotkey.Text = s.DeepCleanHotkey;
            _threshold.Value = Math.Clamp(s.AutoCleanThresholdPercent, 50, 95);
            _interval.Value = Math.Clamp(s.AutoCleanCheckIntervalSeconds, 2, 60);
        }

        private void ToggleRemote(bool enable)
        {
            if (enable)
            {
                _server ??= new RemoteServer(cmd => HandleRemoteCommand(cmd), BuildRemoteStatus);
                _server.Start();
                if (!_server.Running)
                {
                    _remote.Checked = false;
                    Toast("Remote blocked: " + _server.LastError + " — run Smart Skii-mer --allow-lan once.", true);
                    return;
                }
                Toast($"Remote ready · code {_server.PairCode}");
            }
            else
            {
                _server?.Dispose();
                _server = null;
                Toast("Remote stopped");
            }
            if (_page == "settings") LoadSettingsUi();
        }

        private string BuildRemoteStatus()
        {
            try
            {
                var s = _snapshot;
                if (s is null) return "{\"ram\":0,\"cpu\":0,\"mode\":\"-\",\"modeCmd\":\"mode_balance\",\"app\":\"-\",\"time\":\"-\"}";
                var pct = (int)Math.Round(s.Memory.UsedPhysicalGB / Math.Max(1, s.Memory.TotalPhysicalGB) * 100);
                var mode = ModeManager.Resolve(SettingsManager.Current.Mode, s);
                var modeCmd = mode.Id switch
                {
                    "gaming" => "mode_game",
                    "streaming" => "mode_stream",
                    "work" => "mode_work",
                    "eco" => "mode_eco",
                    "balanced" => "mode_balance",
                    _ => "mode_auto",
                };
                return JsonSerializer.Serialize(new
                {
                    ram = pct,
                    cpu = (int)Math.Round(s.CpuPercent),
                    mode = mode.Name,
                    modeCmd,
                    app = s.ForegroundName,
                    time = s.CapturedAt.ToString("HH:mm:ss"),
                });
            }
            catch { return "{\"ram\":0}"; }
        }

        private void HandleRemoteCommand(string cmd)
        {
            BeginInvoke(async () =>
            {
                try
                {
                    switch (cmd)
                    {
                        case "quick_clean": DoClean(CleanLevel.Standard); break;
                        case "deep_clean": DoClean(CleanLevel.Deep); break;
                        case "free_background": await SafeTrimAsync(); break;
                        case "mode_game": await ApplyModeNowAsync("gaming"); break;
                        case "mode_stream": await ApplyModeNowAsync("streaming"); break;
                        case "mode_work": await ApplyModeNowAsync("work"); break;
                        case "mode_eco": await ApplyModeNowAsync("eco"); break;
                        case "mode_balance": await ApplyModeNowAsync("balanced"); break;
                        case "mode_auto": await ApplyModeNowAsync("auto"); break;
                        case "status": await RefreshAsync(true); break;
                    }
                }
                catch { }
            });
        }

        private void SaveSettings()
        {
            var s = SettingsManager.Current;
            s.AutoCleanEnabled = _autoClean.Checked;
            s.PauseWhenFullscreen = _pauseGame.Checked;
            s.TrimBackgroundWorkingSets = _trimBg.Checked;
            s.RunInBackground = _background.Checked;
            s.AutoEco = _autoEco.Checked;
            s.AutoAiOnPressure = _autoAi.Checked;
            if (!string.IsNullOrWhiteSpace(_hotkey.Text)) s.DeepCleanHotkey = _hotkey.Text;
            if (_provider.SelectedIndex >= 0 && _provider.SelectedIndex < AiProviders.Presets.Length)
            {
                var preset = AiProviders.Presets[_provider.SelectedIndex];
                s.Ai.Kind = preset.Kind;
                s.Ai.ProviderName = preset.Name;
                s.Ai.BaseUrl = string.IsNullOrWhiteSpace(_baseUrl.Text) ? preset.BaseUrl : _baseUrl.Text.Trim();
                s.Ai.CustomBaseUrl = preset.Kind == "compat" ? _baseUrl.Text.Trim() : "";
                s.Ai.ModelName = string.IsNullOrWhiteSpace(_model.Text) ? preset.DefaultModel : _model.Text.Trim();
            }
            var keyText = _apiKey.Text.Trim();
            if (keyText.Length > 8 && !keyText.StartsWith("*") && !keyText.Contains(' ')) s.Ai.ApiKey = keyText;
            s.AutoCleanThresholdPercent = (int)_threshold.Value;
            s.AutoCleanCheckIntervalSeconds = (int)_interval.Value;
            SettingsManager.Save();
            Toast("Settings saved");
        }

        private static string KeyLabel()
        {
            var info = ProfileKeys.FindOpenRouter(SettingsManager.Current.Ai.ApiKey);
            return info is null
                ? "No key detected — add OPENROUTER_API_KEY to your PowerShell profile"
                : $"Key: {info.Provider} · {ProfileKeys.Mask(info.Key)} · {info.Source}";
        }

        private void OpenNvidia()
        {
            foreach (var path in new[]
            {
                @"C:\Program Files\NVIDIA Corporation\Control Panel Client\nvcplui.exe",
                @"C:\Program Files\NVIDIA Corporation\NVIDIA app\CEF\NVIDIA app.exe",
            })
                if (System.IO.File.Exists(path)) { Open(path); return; }
            Open("ms-settings:display-advancedgraphics");
        }

        private void OpenAmd()
        {
            try
            {
                var ad = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AMD", "CNext", "CNext", "RadeonSoftware.exe");
                if (System.IO.File.Exists(ad)) { Open(ad); return; }
            }
            catch { }
            Open("ms-settings:display-advancedgraphics");
        }

        private void ApplyHighPerformance()
        {
            if (MessageBox.Show(this, "Switch Windows to the High performance power plan? You can change it back in Power options.", "Apply setting", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                Process.Start(new ProcessStartInfo("powercfg", "/setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c") { UseShellExecute = false, CreateNoWindow = true });
                Toast("Power plan set to High performance");
            }
            catch (Exception ex) { Toast("Could not change the power plan: " + ex.Message, true); }
        }

        private void ApplyGameMode()
        {
            if (MessageBox.Show(this, "Turn on Windows Game Mode for this user?", "Apply setting", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\GameBar");
                k?.SetValue("AutoGameModeEnabled", 1, Microsoft.Win32.RegistryValueKind.DWord);
                Toast("Game Mode enabled");
            }
            catch (Exception ex) { Toast("Could not enable Game Mode: " + ex.Message, true); }
        }

        private void OpenVendor()
        {
            foreach (var path in new[]
            {
                @"C:\Program Files\ASUS\ArmouryCrate\ArmouryCrate.exe",
                @"C:\Program Files (x86)\ASUS\ArmouryCrate\ArmouryCrate.exe",
                @"C:\Program Files\MSI\MSI Center\MSI Center.exe",
                @"C:\Program Files\OEM\OmenCommandCenter\OmenCommandCenter.exe",
            })
                if (System.IO.File.Exists(path)) { Open(path); return; }
            Open("ms-settings:appsfeatures");
        }

        private static void Open(string target)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
        }

        public void LogActivity(string message)
        {
            _log.Add(message);
            if (IsHandleCreated && !IsDisposed)
                BeginInvoke(() => { if (!IsDisposed) Toast(message); });
        }

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        private void UpdateTrayStatus(SystemSnapshot? s)
        {
            if (_tray is null) return;
            if (s is null)
            {
                _tray.Text = "Smart Skii-mer";
                if (_trayStatus is not null) _trayStatus.Text = "Reading usage…";
                return;
            }
            var pct = (int)Math.Round(s.Memory.UsedPhysicalGB / Math.Max(1, s.Memory.TotalPhysicalGB) * 100);
            var mode = ModeManager.Resolve(SettingsManager.Current.Mode, s);
            _tray.Text = $"RAM {pct}% · CPU {s.CpuPercent:0}% · {mode.Name}";
            if (_trayStatus is not null)
                _trayStatus.Text = $"RAM {pct}% · CPU {s.CpuPercent:0}% · {mode.Name} mode";
        }

        private void HideToTray()
        {
            EnsureTray();
            ShowInTaskbar = false;
            Hide();
            try { _tray?.ShowBalloonTip(2500, "Smart Skii-mer is still running", "Your apps stay protected. Double-click the tray icon to reopen.", ToolTipIcon.Info); } catch { }
        }

        private void ShowFromTray()
        {
            ShowInTaskbar = true;
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        private void EnsureTray()
        {
            if (_tray is not null) return;
            var menu = new ContextMenuStrip();
            _trayStatus = new ToolStripMenuItem("Reading usage…") { Enabled = false, Font = new Font("Segoe UI Semibold", 9f) };
            menu.Items.Add(_trayStatus);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Open Smart Skii-mer", null, (_, _) => ShowFromTray());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => { _exiting = true; _tray!.Visible = false; Close(); Application.Exit(); });
            _tray = new NotifyIcon
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application,
                Text = "Smart Skii-mer",
                Visible = true,
                ContextMenuStrip = menu,
            };
            _tray.DoubleClick += (_, _) => ShowFromTray();
            UpdateTrayStatus(null);
        }

        private void ApplyRoundedWindow()
        {
            try
            {
                int round = 2;
                DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
            }
            catch { }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x84;
            if (m.Msg == WM_NCHITTEST && WindowState == FormWindowState.Normal)
            {
                var pos = PointToClient(new Point(m.LParam.ToInt32() & 0xFFFF, m.LParam.ToInt32() >> 16));
                const int edge = 8;
                bool left = pos.X <= edge, right = pos.X >= ClientSize.Width - edge;
                bool top = pos.Y <= edge, bottom = pos.Y >= ClientSize.Height - edge;
                int hit = 0;
                if (left && top) hit = 13; else if (right && top) hit = 14;
                else if (left && bottom) hit = 16; else if (right && bottom) hit = 17;
                else if (left) hit = 10; else if (right) hit = 11;
                else if (top) hit = 12; else if (bottom) hit = 15;
                if (hit != 0) { m.Result = (IntPtr)hit; return; }
            }
            base.WndProc(ref m);
        }

        // ── controls ────────────────────────────────────────────────────────
        private sealed class RoundedPanel : Panel
        {
            [System.ComponentModel.Browsable(false)]
            [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
            public int Radius { get; set; } = 12;
            public RoundedPanel() { DoubleBuffered = true; }
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var path = RoundPath(new Rectangle(0, 0, Width - 1, Height - 1), Radius);
                using var pen = new Pen(Stroke);
                e.Graphics.DrawPath(pen, path);
            }
            private static GraphicsPath RoundPath(Rectangle r, int radius)
            {
                var p = new GraphicsPath();
                int d = radius * 2;
                p.AddArc(r.X, r.Y, d, d, 180, 90);
                p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                p.CloseFigure();
                return p;
            }
        }

        private sealed class NavButton : Control
        {
            private readonly string _glyph;
            private bool _hover;
            [System.ComponentModel.Browsable(false)]
            [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
            public bool Active { get; set; }
            public NavButton(string glyph, string label)
            {
                _glyph = glyph;
                Text = label;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
                Cursor = Cursors.Hand;
                ForeColor = Muted;
                Font = new Font("Segoe UI", 9f);
            }
            protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (Active)
                {
                    using var b = new SolidBrush(Color.FromArgb(16, 34, 28));
                    using var path = Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 10);
                    g.FillPath(b, path);
                    using var accent = new SolidBrush(Mint);
                    g.FillRectangle(accent, 0, 10, 3, Height - 20);
                }
                else if (_hover)
                {
                    using var b = new SolidBrush(Panel2);
                    using var path = Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 10);
                    g.FillPath(b, path);
                }
                using var fg = new SolidBrush(Active || _hover ? Ink : Muted);
                g.DrawString(_glyph, new Font("Segoe UI", 11f), fg, 14, (Height - 20) / 2f);
                g.DrawString(Text, Font, fg, 44, (Height - 18) / 2f);
            }
            private static GraphicsPath Rounded(Rectangle r, int radius)
            {
                var p = new GraphicsPath();
                int d = radius * 2;
                p.AddArc(r.X, r.Y, d, d, 180, 90);
                p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                p.CloseFigure();
                return p;
            }
        }

        private sealed class RadialGauge : Control
        {
            private int _value;
            [System.ComponentModel.Browsable(false)]
            [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
            public int Value { get => _value; set { _value = Math.Clamp(value, 0, 100); Invalidate(); } }
            public RadialGauge() { DoubleBuffered = true; }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int size = Math.Min(Width, Height) - 18;
                var rect = new Rectangle((Width - size) / 2, (Height - size) / 2, size, size);
                using var back = new Pen(Color.FromArgb(29, 42, 50), 11) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawArc(back, rect, -90, 360);
                using var fore = new Pen(_value < 70 ? Mint : _value < 85 ? Amber : Rose, 11) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawArc(fore, rect, -90, (int)(360 * _value / 100.0));
                var text = $"{_value}%";
                using var big = new Font("Segoe UI Semibold", 23f);
                var ts = g.MeasureString(text, big);
                g.DrawString(text, big, new SolidBrush(Ink), (Width - ts.Width) / 2, (Height - ts.Height) / 2 - 4);
                using var small = new Font("Segoe UI", 7f);
                const string label = "RAM IN USE";
                var ls = g.MeasureString(label, small);
                g.DrawString(label, small, new SolidBrush(Muted), (Width - ls.Width) / 2, Height / 2 + 20);
            }
        }

        private sealed class Sparkline : Control
        {
            private readonly List<double> _points = new();
            public Sparkline() { DoubleBuffered = true; }
            public void Add(double v)
            {
                _points.Add(Math.Clamp(v, 0, 100));
                if (_points.Count > 60) _points.RemoveAt(0);
                Invalidate();
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (_points.Count < 2) return;
                var pts = new List<PointF>();
                for (int i = 0; i < _points.Count; i++)
                {
                    float x = Width * i / (float)(_points.Count - 1);
                    float y = Height - (float)(_points[i] / 100.0 * (Height - 6)) - 3;
                    pts.Add(new PointF(x, y));
                }
                using var fill = new LinearGradientBrush(new Rectangle(0, 0, Math.Max(1, Width), Math.Max(1, Height)), Color.FromArgb(60, 110, 231, 183), Color.FromArgb(0, 110, 231, 183), 90f);
                var poly = new List<PointF>(pts) { new(Width, Height), new(0, Height) };
                g.FillPolygon(fill, poly.ToArray());
                using var line = new Pen(Mint, 1.6f);
                g.DrawLines(line, pts.ToArray());
            }
        }

        private sealed class Toggle : CheckBox
        {
            public Toggle()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
                Cursor = Cursors.Hand;
                Text = "";
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var track = new Rectangle(0, 3, Width - 1, Height - 7);
                using var path = new GraphicsPath();
                path.AddArc(track.X, track.Y, track.Height, track.Height, 90, 180);
                path.AddArc(track.Right - track.Height, track.Y, track.Height, track.Height, 270, 180);
                path.CloseFigure();
                using var bg = new SolidBrush(Checked ? Color.FromArgb(36, 88, 66) : Color.FromArgb(30, 43, 53));
                g.FillPath(bg, path);
                int knob = Height - 10;
                int x = Checked ? Width - knob - 4 : 4;
                using var k = new SolidBrush(Checked ? Mint : Color.FromArgb(110, 130, 142));
                g.FillEllipse(k, x, 5, knob, knob);
            }
        }
    }
}
