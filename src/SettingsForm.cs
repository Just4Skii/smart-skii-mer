using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SmartMemReduct
{
    public class SettingsForm : Form
    {
        // ── Dashboard controls ──
        private Label _lblRamStatus = null!;
        private Panel _pnlRamBar = null!;
        private Label _lblRamDetails = null!;
        private Label _lblCommitDetails = null!;
        private Label _lblSelfRam = null!;
        private Label _lblAutoStatus = null!;
        private ListBox _lstActivityLog = null!;
        private System.Windows.Forms.Timer _dashboardTimer = null!;

        // ── Auto-Clean tab controls ──
        private CheckBox _chkAutoClean = null!;
        private NumericUpDown _nudThreshold = null!;
        private NumericUpDown _nudCooldown = null!;
        private NumericUpDown _nudCheckInterval = null!;
        private NumericUpDown _nudFloor = null!;
        private CheckBox _chkPurgeStandby = null!;
        private CheckBox _chkFlushModified = null!;
        private CheckBox _chkTrimBackground = null!;
        private NumericUpDown _nudTrimMinMB = null!;
        private CheckBox _chkStartWithWindows = null!;
        private CheckBox _chkNotifications = null!;
        private NumericUpDown _nudNotifyMinMB = null!;
        private CheckBox _chkPauseFullscreen = null!;

        // ── AI tab controls ──
        private CheckBox _chkAiEnabled = null!;
        private ComboBox _cboProvider = null!;
        private TextBox _txtBaseUrl = null!;
        private TextBox _txtApiKey = null!;
        private ComboBox _cboModel = null!;
        private Button _btnTestConnection = null!;
        private Button _btnAnalyzeNow = null!;
        private RichTextBox _rtbAiOutput = null!;
        private CancellationTokenSource? _aiCts;

        // ── TabControl ──
        private TabControl _tabControl = null!;

        // ── Events ──
        public event Action? OnThemeChanged;

        public SettingsForm()
        {
            InitializeComponent();
            LoadSettingsToUI();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ThemeManager.ApplyToForm(this);
        }

        private void InitializeComponent()
        {
            this.Text = "Smart Skii-mer";
            this.Size = new Size(620, 580);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = true;
            this.ShowInTaskbar = false; // Never clutter the taskbar - stays in system tray
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = new Font("Segoe UI", 9f);

            _tabControl = new TabControl
            {
                Dock = DockStyle.Fill
            };

            _tabControl.TabPages.Add(CreateDashboardTab());
            _tabControl.TabPages.Add(CreateAutoCleanTab());
            _tabControl.TabPages.Add(CreateAiTab());
            _tabControl.TabPages.Add(CreateAppearanceTab());

            this.Controls.Add(_tabControl);

            // Dashboard live update timer (1 second interval)
            _dashboardTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _dashboardTimer.Tick += (s, e) => UpdateDashboard();
            _dashboardTimer.Start();

            // Initial log entry
            LogActivity("Smart Skii-mer active and monitoring memory.");
        }

        // ══════════════════════════════════════════════════════════
        //  MINIMIZE & CLOSE → HIDE TO SYSTEM TRAY
        // ══════════════════════════════════════════════════════════

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (this.WindowState == FormWindowState.Minimized)
            {
                this.Hide();
                this.WindowState = FormWindowState.Normal;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
                SaveSettingsFromUI();
                return;
            }
            base.OnFormClosing(e);
        }

        // ══════════════════════════════════════════════════════════
        //  TAB 1: LIVE DASHBOARD & MONITOR
        // ══════════════════════════════════════════════════════════

        private TabPage CreateDashboardTab()
        {
            var page = new TabPage("📊 Dashboard");
            page.AutoScroll = true;
            int y = 12;

            // ── Live RAM Gauge Box ──
            var grpGauge = new GroupBox
            {
                Text = "Live System Memory",
                Location = new Point(12, y),
                Size = new Size(575, 155)
            };

            _lblRamStatus = new Label
            {
                Text = "RAM Usage: Calculating...",
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                Location = new Point(16, 22),
                AutoSize = true
            };
            grpGauge.Controls.Add(_lblRamStatus);

            // Visual RAM Bar
            _pnlRamBar = new Panel
            {
                Location = new Point(18, 55),
                Size = new Size(538, 26),
                BorderStyle = BorderStyle.FixedSingle
            };
            _pnlRamBar.Paint += PnlRamBar_Paint;
            grpGauge.Controls.Add(_pnlRamBar);

            _lblRamDetails = new Label
            {
                Text = "Used: -- GB | Free: -- GB | Total: -- GB",
                Location = new Point(18, 88),
                AutoSize = true,
                Font = new Font("Segoe UI", 9f)
            };
            grpGauge.Controls.Add(_lblRamDetails);

            _lblCommitDetails = new Label
            {
                Text = "Virtual Memory (Commit): --",
                Location = new Point(18, 108),
                AutoSize = true,
                ForeColor = Color.Gray
            };
            grpGauge.Controls.Add(_lblCommitDetails);

            _lblSelfRam = new Label
            {
                Text = "Smart Skii-mer RAM footprint: -- MB",
                Location = new Point(18, 128),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Italic)
            };
            grpGauge.Controls.Add(_lblSelfRam);

            page.Controls.Add(grpGauge);
            y += 165;

            // ── Quick Action Buttons ──
            var grpActions = new GroupBox
            {
                Text = "Quick Actions",
                Location = new Point(12, y),
                Size = new Size(575, 78)
            };

            var btnClean = new Button
            {
                Text = "⚡ Smart Clean (Ctrl+Alt+M)",
                Location = new Point(14, 26),
                Size = new Size(132, 38),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
            };
            btnClean.Click += (s, e) =>
            {
                var res = MemoryEngine.SmartClean(MemoryEngine.RecommendCleanLevel());
                LogActivity($"Smart Clean executed: Reclaimed {res.FreedMB:F0} MB ({res.ProcessesTrimmed} processes trimmed).");
                UpdateDashboard();
            };
            grpActions.Controls.Add(btnClean);

            var btnDeep = new Button
            {
                Text = "🔥 Deep Clean (Ctrl+Alt+D)",
                Location = new Point(152, 26),
                Size = new Size(132, 38),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
            };
            btnDeep.Click += (s, e) =>
            {
                var res = MemoryEngine.SmartClean(CleanLevel.Deep);
                LogActivity($"Deep Clean executed: Reclaimed {res.FreedMB:F0} MB ({res.ProcessesTrimmed} trimmed).");
                UpdateDashboard();
            };
            grpActions.Controls.Add(btnDeep);

            var btnUnfreeze = new Button
            {
                Text = "❄️ Unfreeze (Ctrl+Alt+F)",
                Location = new Point(290, 26),
                Size = new Size(132, 38),
                Font = new Font("Segoe UI", 8.5f)
            };
            btnUnfreeze.Click += (s, e) =>
            {
                var res = AntiFreezeEngine.EmergencyUnfreeze();
                LogActivity($"Emergency Unfreeze: DWM boosted, {res.ThrottledProcesses} workers throttled, {res.CleanStats.FreedMB:F0} MB freed.");
                UpdateDashboard();
            };
            grpActions.Controls.Add(btnUnfreeze);

            var btnThrottle = new Button
            {
                Text = "🛑 CPU Relief (Ctrl+Alt+K)",
                Location = new Point(428, 26),
                Size = new Size(132, 38),
                Font = new Font("Segoe UI", 8.5f)
            };
            btnThrottle.Click += (s, e) =>
            {
                int count = AntiFreezeEngine.ThrottleBackgroundWorkers();
                LogActivity(count > 0 ? $"Throttled {count} background workers to BelowNormal." : "No rogue CPU workers found.");
            };
            grpActions.Controls.Add(btnThrottle);

            page.Controls.Add(grpActions);
            y += 88;

            // ── Auto-Cleaner Status & Activity Feed ──
            var grpLog = new GroupBox
            {
                Text = "Activity & Auto-Cleaner Status",
                Location = new Point(12, y),
                Size = new Size(575, 230)
            };

            _lblAutoStatus = new Label
            {
                Text = "Auto-Cleaner: Active",
                Location = new Point(16, 22),
                AutoSize = true,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            grpLog.Controls.Add(_lblAutoStatus);

            _lstActivityLog = new ListBox
            {
                Location = new Point(16, 48),
                Size = new Size(542, 170),
                Font = new Font("Consolas", 8.5f),
                BorderStyle = BorderStyle.FixedSingle
            };
            grpLog.Controls.Add(_lstActivityLog);

            page.Controls.Add(grpLog);

            return page;
        }

        private void PnlRamBar_Paint(object? sender, PaintEventArgs e)
        {
            var mem = MemoryEngine.GetCurrentMemoryInfo();
            int pct = Math.Clamp(mem.MemoryLoadPercent, 0, 100);
            int fillWidth = (int)((_pnlRamBar.Width * pct) / 100.0);

            var t = ThemeManager.CurrentTheme;
            Color barColor = pct < 60 ? t.AccentColor : pct < 85 ? Color.FromArgb(255, 185, 40) : Color.FromArgb(255, 60, 60);

            using var bgBrush = new SolidBrush(t.TextBoxBackColor);
            e.Graphics.FillRectangle(bgBrush, 0, 0, _pnlRamBar.Width, _pnlRamBar.Height);

            if (fillWidth > 0)
            {
                using var fillBrush = new SolidBrush(barColor);
                e.Graphics.FillRectangle(fillBrush, 0, 0, fillWidth, _pnlRamBar.Height);
            }
        }

        public void UpdateDashboard()
        {
            try
            {
                var mem = MemoryEngine.GetCurrentMemoryInfo();
                int pct = mem.MemoryLoadPercent;
                var t = ThemeManager.CurrentTheme;

                _lblRamStatus.Text = $"RAM Usage: {pct}%";
                _lblRamStatus.ForeColor = pct < 60 ? t.AccentColor : pct < 85 ? Color.FromArgb(255, 185, 40) : Color.FromArgb(255, 60, 60);

                _lblRamDetails.Text = $"Used: {mem.UsedPhysicalGB} GB  |  Free: {mem.AvailablePhysicalGB} GB  |  Total: {mem.TotalPhysicalGB} GB";
                _lblCommitDetails.Text = $"Commit Charge: {mem.CommitLoadPercent}% ({mem.UsedCommitGB} GB / {mem.TotalCommitGB} GB)";

                double selfMB = MemoryEngine.GetSelfWorkingSetMB();
                _lblSelfRam.Text = $"Smart Skii-mer memory footprint: {selfMB:F1} MB (Featherlight)";
                _lblSelfRam.ForeColor = t.AccentColor;

                bool autoOn = SettingsManager.Current.AutoCleanEnabled;
                int threshold = SettingsManager.Current.AutoCleanThresholdPercent;
                _lblAutoStatus.Text = autoOn
                    ? $"● Auto-Cleaner: ACTIVE (Triggers when RAM > {threshold}%, cooldown {SettingsManager.Current.AutoCleanCooldownSeconds}s)"
                    : "○ Auto-Cleaner: PAUSED / MANUAL ONLY";
                _lblAutoStatus.ForeColor = autoOn ? t.AccentColor : Color.Gray;

                _pnlRamBar.Invalidate();
            }
            catch { }
        }

        public void LogActivity(string message)
        {
            try
            {
                string entry = $"[{DateTime.Now:HH:mm:ss}] {message}";
                if (_lstActivityLog.InvokeRequired)
                {
                    _lstActivityLog.Invoke(() => AddLogEntry(entry));
                }
                else
                {
                    AddLogEntry(entry);
                }
            }
            catch { }
        }

        private void AddLogEntry(string entry)
        {
            _lstActivityLog.Items.Insert(0, entry);
            if (_lstActivityLog.Items.Count > 100)
            {
                _lstActivityLog.Items.RemoveAt(_lstActivityLog.Items.Count - 1);
            }
        }

        // ══════════════════════════════════════════════════════════
        //  TAB 2: AUTO-CLEAN & RULES
        // ══════════════════════════════════════════════════════════

        private TabPage CreateAutoCleanTab()
        {
            var page = new TabPage("⚙️ Auto-Clean");
            page.AutoScroll = true;
            int y = 10;

            // ── Auto-Clean group ──
            var grpAuto = new GroupBox { Text = "Automatic Memory Optimizer", Location = new Point(10, y), Size = new Size(575, 155) };

            _chkAutoClean = new CheckBox { Text = "Enable automatic memory cleaning in background", Location = new Point(15, 25), AutoSize = true };
            grpAuto.Controls.Add(_chkAutoClean);

            grpAuto.Controls.Add(MakeLabel("Clean when RAM exceeds (%):", 15, 55));
            _nudThreshold = MakeNumeric(210, 53, 40, 95, 75);
            grpAuto.Controls.Add(_nudThreshold);

            grpAuto.Controls.Add(MakeLabel("Cooldown between cleans (sec):", 300, 55));
            _nudCooldown = MakeNumeric(490, 53, 10, 600, 30);
            grpAuto.Controls.Add(_nudCooldown);

            grpAuto.Controls.Add(MakeLabel("Check frequency (sec):", 15, 85));
            _nudCheckInterval = MakeNumeric(210, 83, 3, 60, 5);
            grpAuto.Controls.Add(_nudCheckInterval);

            grpAuto.Controls.Add(MakeLabel("Floor (skip if RAM is below %):", 300, 85));
            _nudFloor = MakeNumeric(490, 83, 20, 90, 50);
            grpAuto.Controls.Add(_nudFloor);

            _chkPauseFullscreen = new CheckBox { Text = "Pause auto-clean when running fullscreen games or video", Location = new Point(15, 118), AutoSize = true };
            grpAuto.Controls.Add(_chkPauseFullscreen);

            page.Controls.Add(grpAuto);
            y += 165;

            // ── Cleaning Operations group ──
            var grpOps = new GroupBox { Text = "Engine Clean Options", Location = new Point(10, y), Size = new Size(575, 115) };

            _chkPurgeStandby = new CheckBox { Text = "Purge Standby Cache (Zero I/O, safest, frees dead cached RAM)", Location = new Point(15, 25), AutoSize = true };
            grpOps.Controls.Add(_chkPurgeStandby);

            _chkFlushModified = new CheckBox { Text = "Flush Modified Page List", Location = new Point(15, 50), AutoSize = true };
            grpOps.Controls.Add(_chkFlushModified);

            _chkTrimBackground = new CheckBox { Text = "Trim bloated background app working sets", Location = new Point(15, 75), AutoSize = true };
            grpOps.Controls.Add(_chkTrimBackground);

            grpOps.Controls.Add(MakeLabel("Min process RAM to trim (MB):", 310, 75));
            _nudTrimMinMB = MakeNumeric(490, 73, 10, 1000, 50);
            grpOps.Controls.Add(_nudTrimMinMB);

            page.Controls.Add(grpOps);
            y += 125;

            // ── General System group ──
            var grpGeneral = new GroupBox { Text = "System & Notifications", Location = new Point(10, y), Size = new Size(575, 95) };

            _chkStartWithWindows = new CheckBox { Text = "Launch automatically on Windows startup", Location = new Point(15, 25), AutoSize = true };
            grpGeneral.Controls.Add(_chkStartWithWindows);

            _chkNotifications = new CheckBox { Text = "Show balloon notifications on clean", Location = new Point(15, 52), AutoSize = true };
            grpGeneral.Controls.Add(_chkNotifications);

            grpGeneral.Controls.Add(MakeLabel("Only notify if freed > (MB):", 310, 52));
            _nudNotifyMinMB = MakeNumeric(490, 50, 0, 500, 20);
            grpGeneral.Controls.Add(_nudNotifyMinMB);

            page.Controls.Add(grpGeneral);

            return page;
        }

        // ══════════════════════════════════════════════════════════
        //  TAB 3: AI OPTIMIZER
        // ══════════════════════════════════════════════════════════

        private TabPage CreateAiTab()
        {
            var page = new TabPage("🤖 AI Optimizer");
            page.AutoScroll = true;
            int y = 10;

            var grpApi = new GroupBox { Text = "AI Memory Doctor (BYOK — Any OpenAI-compatible API)", Location = new Point(10, y), Size = new Size(575, 195) };

            _chkAiEnabled = new CheckBox { Text = "Enable AI memory analysis", Location = new Point(15, 25), AutoSize = true };
            grpApi.Controls.Add(_chkAiEnabled);

            grpApi.Controls.Add(MakeLabel("Provider:", 15, 55));
            _cboProvider = new ComboBox { Location = new Point(110, 53), Size = new Size(200, 24), DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var p in AiOptimizer.KnownProviders)
                _cboProvider.Items.Add(p.Name);
            _cboProvider.SelectedIndexChanged += OnProviderChanged;
            grpApi.Controls.Add(_cboProvider);

            grpApi.Controls.Add(MakeLabel("Base URL:", 15, 85));
            _txtBaseUrl = new TextBox { Location = new Point(110, 83), Size = new Size(440, 24) };
            grpApi.Controls.Add(_txtBaseUrl);

            grpApi.Controls.Add(MakeLabel("API Key:", 15, 115));
            _txtApiKey = new TextBox { Location = new Point(110, 113), Size = new Size(440, 24), UseSystemPasswordChar = true };
            grpApi.Controls.Add(_txtApiKey);

            grpApi.Controls.Add(MakeLabel("Model:", 15, 145));
            _cboModel = new ComboBox { Location = new Point(110, 143), Size = new Size(300, 24), DropDownStyle = ComboBoxStyle.DropDown };
            grpApi.Controls.Add(_cboModel);

            _btnTestConnection = new Button { Text = "Test API", Location = new Point(430, 142), Size = new Size(120, 27) };
            _btnTestConnection.Click += OnTestConnection;
            grpApi.Controls.Add(_btnTestConnection);

            page.Controls.Add(grpApi);
            y += 205;

            _btnAnalyzeNow = new Button { Text = "🤖 Analyze System Memory With AI", Location = new Point(10, y), Size = new Size(575, 36) };
            _btnAnalyzeNow.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            _btnAnalyzeNow.Click += OnAnalyzeNow;
            page.Controls.Add(_btnAnalyzeNow);
            y += 44;

            _rtbAiOutput = new RichTextBox
            {
                Location = new Point(10, y),
                Size = new Size(575, 210),
                ReadOnly = true,
                Font = new Font("Consolas", 9f),
                Text = "AI analysis output will appear here.\n\nConfigure your API key above, then click \"Analyze System Memory With AI\"."
            };
            page.Controls.Add(_rtbAiOutput);

            return page;
        }

        private void OnProviderChanged(object? sender, EventArgs e)
        {
            if (_cboProvider.SelectedIndex < 0 || _cboProvider.SelectedIndex >= AiOptimizer.KnownProviders.Length)
                return;

            var provider = AiOptimizer.KnownProviders[_cboProvider.SelectedIndex];
            _txtBaseUrl.Text = provider.BaseUrl;

            _cboModel.Items.Clear();
            foreach (var m in provider.SuggestedModels)
                _cboModel.Items.Add(m);

            if (!string.IsNullOrEmpty(provider.DefaultModel))
                _cboModel.Text = provider.DefaultModel;
            else if (_cboModel.Items.Count > 0)
                _cboModel.SelectedIndex = 0;
        }

        private async void OnTestConnection(object? sender, EventArgs e)
        {
            _btnTestConnection.Enabled = false;
            _btnTestConnection.Text = "Testing...";
            _rtbAiOutput.Text = "Testing connection to API...\n";

            try
            {
                Dictionary<string, string>? headers = null;
                if (_cboProvider.SelectedIndex >= 0 && _cboProvider.SelectedIndex < AiOptimizer.KnownProviders.Length)
                    headers = AiOptimizer.KnownProviders[_cboProvider.SelectedIndex].ExtraHeaders;

                var (success, message) = await AiOptimizer.TestConnectionAsync(
                    _txtBaseUrl.Text, _txtApiKey.Text, _cboModel.Text, headers);

                _rtbAiOutput.Text = success ? $"✅ {message}" : $"❌ {message}";
            }
            catch (Exception ex)
            {
                _rtbAiOutput.Text = $"❌ Error: {ex.Message}";
            }
            finally
            {
                _btnTestConnection.Enabled = true;
                _btnTestConnection.Text = "Test API";
            }
        }

        private async void OnAnalyzeNow(object? sender, EventArgs e)
        {
            SaveAiSettingsFromUI();

            _btnAnalyzeNow.Enabled = false;
            _btnAnalyzeNow.Text = "⏳ Analyzing system...";
            _rtbAiOutput.Text = "Gathering top process memory usage and sending to AI model...\n";

            _aiCts?.Cancel();
            _aiCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            try
            {
                string result = await AiOptimizer.AnalyzeSystemAsync(_aiCts.Token);
                _rtbAiOutput.Text = result;
                LogActivity("AI Memory Doctor completed diagnosis.");
            }
            catch (Exception ex)
            {
                _rtbAiOutput.Text = $"Error: {ex.Message}";
            }
            finally
            {
                _btnAnalyzeNow.Enabled = true;
                _btnAnalyzeNow.Text = "🤖 Analyze System Memory With AI";
            }
        }

        private void SaveAiSettingsFromUI()
        {
            var ai = SettingsManager.Current.Ai;
            ai.Enabled = _chkAiEnabled.Checked;
            if (_cboProvider.SelectedIndex >= 0)
                ai.ProviderName = _cboProvider.Text;
            ai.BaseUrl = _txtBaseUrl.Text.Trim();
            ai.ApiKey = _txtApiKey.Text.Trim();
            ai.ModelName = _cboModel.Text.Trim();
        }

        // ══════════════════════════════════════════════════════════
        //  TAB 4: APPEARANCE & THEMES
        // ══════════════════════════════════════════════════════════

        private TabPage CreateAppearanceTab()
        {
            var page = new TabPage("🎨 Appearance");
            int y = 15;

            var grpTheme = new GroupBox { Text = "Theme Selection", Location = new Point(10, y), Size = new Size(575, 230) };
            int radioY = 32;

            foreach (var theme in ThemeManager.AllThemes)
            {
                var rb = new RadioButton
                {
                    Text = theme.Name,
                    Location = new Point(20, radioY),
                    AutoSize = true,
                    Tag = theme,
                    Checked = string.Equals(ThemeManager.CurrentTheme.Name, theme.Name, StringComparison.OrdinalIgnoreCase)
                };

                var swatch = new Panel
                {
                    Location = new Point(180, radioY + 1),
                    Size = new Size(350, 22),
                    BorderStyle = BorderStyle.FixedSingle
                };
                swatch.Paint += (s, e) =>
                {
                    using var bgBrush = new SolidBrush(theme.BackColor);
                    using var accentBrush = new SolidBrush(theme.AccentColor);
                    using var textBrush = new SolidBrush(theme.ForeColor);
                    e.Graphics.FillRectangle(bgBrush, 0, 0, swatch.Width, swatch.Height);
                    e.Graphics.FillRectangle(accentBrush, 0, 0, 60, swatch.Height);
                    using var font = new Font("Segoe UI", 8f);
                    e.Graphics.DrawString($"  {theme.Name} Theme", font, textBrush, 64, 3);
                };

                rb.CheckedChanged += (s, e) =>
                {
                    if (rb.Checked && rb.Tag is Theme t)
                    {
                        ThemeManager.SetTheme(t.Name);
                        SettingsManager.Current.ThemeName = t.Name;
                        ThemeManager.ApplyToForm(this);
                        OnThemeChanged?.Invoke();
                        UpdateDashboard();
                    }
                };

                grpTheme.Controls.Add(rb);
                grpTheme.Controls.Add(swatch);
                radioY += 36;
            }

            page.Controls.Add(grpTheme);
            y += 245;

            var lblInfo = new Label
            {
                Text = "• Theme colors immediately tint the window title bar, window borders, tabs, and menus.\n• Minimize button (_) hides Smart Skii-mer straight into your system tray.\n• Settings save automatically.",
                Location = new Point(15, y),
                AutoSize = true,
                ForeColor = Color.Gray
            };
            page.Controls.Add(lblInfo);

            return page;
        }

        // ══════════════════════════════════════════════════════════
        //  SETTINGS LOAD / SAVE
        // ══════════════════════════════════════════════════════════

        private void LoadSettingsToUI()
        {
            var s = SettingsManager.Current;

            _chkAutoClean.Checked = s.AutoCleanEnabled;
            _nudThreshold.Value = s.AutoCleanThresholdPercent;
            _nudCooldown.Value = s.AutoCleanCooldownSeconds;
            _nudCheckInterval.Value = s.AutoCleanCheckIntervalSeconds;
            _nudFloor.Value = s.AutoCleanFloorPercent;
            _chkPauseFullscreen.Checked = s.PauseWhenFullscreen;
            _chkPurgeStandby.Checked = s.PurgeStandbyList;
            _chkFlushModified.Checked = s.FlushModifiedList;
            _chkTrimBackground.Checked = s.TrimBackgroundWorkingSets;
            _nudTrimMinMB.Value = s.TrimMinWorkingSetMB;
            _chkStartWithWindows.Checked = s.StartWithWindows;
            _chkNotifications.Checked = s.NotificationsEnabled;
            _nudNotifyMinMB.Value = s.NotifyMinFreedMB;

            _chkAiEnabled.Checked = s.Ai.Enabled;
            for (int i = 0; i < AiOptimizer.KnownProviders.Length; i++)
            {
                if (string.Equals(AiOptimizer.KnownProviders[i].Name, s.Ai.ProviderName, StringComparison.OrdinalIgnoreCase))
                {
                    _cboProvider.SelectedIndex = i;
                    break;
                }
            }
            _txtBaseUrl.Text = s.Ai.BaseUrl;
            _txtApiKey.Text = s.Ai.ApiKey;
            _cboModel.Text = s.Ai.ModelName;

            UpdateDashboard();
        }

        private void SaveSettingsFromUI()
        {
            var s = SettingsManager.Current;

            s.AutoCleanEnabled = _chkAutoClean.Checked;
            s.AutoCleanThresholdPercent = (int)_nudThreshold.Value;
            s.AutoCleanCooldownSeconds = (int)_nudCooldown.Value;
            s.AutoCleanCheckIntervalSeconds = (int)_nudCheckInterval.Value;
            s.AutoCleanFloorPercent = (int)_nudFloor.Value;
            s.PauseWhenFullscreen = _chkPauseFullscreen.Checked;
            s.PurgeStandbyList = _chkPurgeStandby.Checked;
            s.FlushModifiedList = _chkFlushModified.Checked;
            s.TrimBackgroundWorkingSets = _chkTrimBackground.Checked;
            s.TrimMinWorkingSetMB = (int)_nudTrimMinMB.Value;
            s.StartWithWindows = _chkStartWithWindows.Checked;
            s.NotificationsEnabled = _chkNotifications.Checked;
            s.NotifyMinFreedMB = (int)_nudNotifyMinMB.Value;

            SaveAiSettingsFromUI();
            SettingsManager.Save();
        }

        private static Label MakeLabel(string text, int x, int y)
        {
            return new Label { Text = text, Location = new Point(x, y + 2), AutoSize = true };
        }

        private static NumericUpDown MakeNumeric(int x, int y, int min, int max, int value)
        {
            return new NumericUpDown
            {
                Location = new Point(x, y),
                Size = new Size(75, 24),
                Minimum = min,
                Maximum = max,
                Value = Math.Clamp(value, min, max)
            };
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _dashboardTimer.Stop();
            _dashboardTimer.Dispose();
            _aiCts?.Cancel();
            _aiCts?.Dispose();
            base.OnFormClosed(e);
        }
    }
}
