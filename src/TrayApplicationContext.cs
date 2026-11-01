using System;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SmartMemReduct
{
    public class TrayApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon _trayIcon;
        private readonly HotkeyManager _hotkeyManager;
        private readonly System.Windows.Forms.Timer _updateTimer;
        private readonly ContextMenuStrip _contextMenu;

        // Auto-clean state
        private DateTime _lastCleanTime = DateTime.MinValue;
        private DateTime _lastAutoCleanNotifyTime = DateTime.MinValue;
        private int _consecutiveHighCount = 0;
        private int _modeTick = 0;
        private bool _modeBusy;
        private DateTime _lastAiPressure = DateTime.MinValue;
        private ModeProfile? _activeMode;
        private double _totalAutoFreedMB = 0;

        // AI auto-analyze state
        private DateTime _lastAiAnalyzeTime = DateTime.MinValue;

        // Track last icon percent to avoid redrawing when unchanged
        private int _lastRenderedPercent = -1;

        // Menu items that need runtime updates
        private ToolStripMenuItem _autoCleanMenuItem = null!;
        private ToolStripLabel _statusLabel = null!;

        // Settings form (single instance)
        private DashboardForm? _settingsForm;

        public TrayApplicationContext()
        {
            SettingsManager.Load();

            // ── Build context menu ──
            _contextMenu = new ContextMenuStrip();

            // Status label at top
            _statusLabel = new ToolStripLabel("Loading...");
            _statusLabel.Font = new Font("Segoe UI", 8.5f, FontStyle.Italic);
            _contextMenu.Items.Add(_statusLabel);
            _contextMenu.Items.Add(new ToolStripSeparator());

            var openItem = new ToolStripMenuItem("📊 Open Dashboard & Settings", null, (s, e) => OpenSettings())
            {
                Font = new Font(_contextMenu.Font, FontStyle.Bold)
            };
            _contextMenu.Items.Add(openItem);

            _contextMenu.Items.Add(new ToolStripSeparator());

            // ── Clean actions ──
            var cleanItem = new ToolStripMenuItem("⚡ Smart Clean (Ctrl+Alt+M)", null, (s, e) => ExecuteSmartClean());
            _contextMenu.Items.Add(cleanItem);

            var deepCleanItem = new ToolStripMenuItem("🔥 Deep Clean (Ctrl+Alt+D)", null, (s, e) => ExecuteDeepClean());
            _contextMenu.Items.Add(deepCleanItem);

            var unfreezeItem = new ToolStripMenuItem("❄️ Emergency Unfreeze (Ctrl+Alt+F)", null, (s, e) => ExecuteEmergencyUnfreeze());
            _contextMenu.Items.Add(unfreezeItem);

            var throttleItem = new ToolStripMenuItem("🛑 Throttle CPU Hogs (Ctrl+Alt+K)", null, (s, e) => ExecuteCpuThrottle());
            _contextMenu.Items.Add(throttleItem);

            _contextMenu.Items.Add(new ToolStripSeparator());

            // ── AI Analyzer ──
            var aiAnalyze = new ToolStripMenuItem("🤖 AI Memory Analysis", null, (s, e) => ExecuteAiAnalysis());
            _contextMenu.Items.Add(aiAnalyze);

            _contextMenu.Items.Add(new ToolStripSeparator());

            // ── Auto-clean toggle ──
            _autoCleanMenuItem = new ToolStripMenuItem($"⚙️ Auto-Clean (>{SettingsManager.Current.AutoCleanThresholdPercent}%)", null, (s, e) =>
            {
                SettingsManager.Current.AutoCleanEnabled = !SettingsManager.Current.AutoCleanEnabled;
                _autoCleanMenuItem.Checked = SettingsManager.Current.AutoCleanEnabled;
                SettingsManager.Save();
            })
            {
                Checked = SettingsManager.Current.AutoCleanEnabled
            };
            _contextMenu.Items.Add(_autoCleanMenuItem);

            _contextMenu.Items.Add(new ToolStripSeparator());

            var exitItem = new ToolStripMenuItem("❌ Exit", null, (s, e) => ExitApp());
            _contextMenu.Items.Add(exitItem);

            // Apply theme to menu
            ThemeManager.ApplyToMenu(_contextMenu);

            // ── Setup Tray Icon ──
            _trayIcon = new NotifyIcon
            {
                ContextMenuStrip = _contextMenu,
                Visible = true
            };
            // Left click or double click opens the dashboard; right click opens menu
            _trayIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    OpenSettings();
                }
            };
            _trayIcon.DoubleClick += (s, e) => OpenSettings();

            // ── Setup Hotkeys ──
            _hotkeyManager = new HotkeyManager();
            _hotkeyManager.OnSmartCleanTriggered += () => ExecuteSmartClean();
            _hotkeyManager.OnDeepCleanTriggered += () => ExecuteDeepClean();
            _hotkeyManager.OnEmergencyUnfreezeTriggered += ExecuteEmergencyUnfreeze;
            _hotkeyManager.OnCpuReliefTriggered += ExecuteCpuThrottle;

            // ── Setup Timer ──
            int intervalMs = Math.Clamp(SettingsManager.Current.AutoCleanCheckIntervalSeconds * 1000, 2000, 60000);
            _updateTimer = new System.Windows.Forms.Timer { Interval = intervalMs };
            _updateTimer.Tick += OnTimerTick;
            _updateTimer.Start();
            SettingsManager.Changed += OnSettingsChanged;

            // Initial display
            UpdateTrayDisplay();

            // Startup notification
            if (SettingsManager.Current.NotificationsEnabled)
            {
                var mem = MemoryEngine.GetCurrentMemoryInfo();
                string auto = SettingsManager.Current.AutoCleanEnabled
                    ? $"Auto-clean ON (>{SettingsManager.Current.AutoCleanThresholdPercent}%)"
                    : "Auto-clean OFF";
                _trayIcon.ShowBalloonTip(2000, "Smart Skii-mer v3.0",
                    $"RAM: {mem.MemoryLoadPercent}% ({mem.AvailablePhysicalGB} GB free) | {auto}\n" +
                    $"Theme: {ThemeManager.CurrentTheme.Name} | AI: {(SettingsManager.Current.Ai.Enabled ? "ON" : "OFF")}",
                    ToolTipIcon.Info);
            }

            // ── Automatic Boot Surge Clean ──
            // Windows boot pushes gigabytes of driver/prefetch cache into RAM.
            // 15 seconds after launch, automatically clean the boot surge on the UI thread.
            var bootTimer = new System.Windows.Forms.Timer { Interval = 15000 };
            bootTimer.Tick += (s, e) =>
            {
                bootTimer.Stop();
                bootTimer.Dispose();
                try
                {
                    ExecuteSmartClean();
                    _settingsForm?.LogActivity("Initial Windows boot surge automatically cleaned.");
                }
                catch { }
            };
            bootTimer.Start();
        }

        // ══════════════════════════════════════════════════════════
        //  TIMER TICK — the auto-clean brain
        // ══════════════════════════════════════════════════════════

        private void OnTimerTick(object? sender, EventArgs e)
        {
            UpdateTrayDisplay();

            if (++_modeTick % 4 == 0) _ = ApplyModePolicyAsync();
            if (_activeMode?.PauseCleaning == true)
            {
                _consecutiveHighCount = 0;
                return;
            }

            if (!SettingsManager.Current.AutoCleanEnabled)
            {
                _consecutiveHighCount = 0;
                return;
            }

            var mem = MemoryEngine.GetCurrentMemoryInfo();
            int pct = mem.MemoryLoadPercent;
            int threshold = SettingsManager.Current.AutoCleanThresholdPercent;
            if (_activeMode is { Id: "gaming" } or { Id: "streaming" })
                threshold = Math.Min(threshold, 85);
            int floor = SettingsManager.Current.AutoCleanFloorPercent;

            // Don't bother if below floor
            if (pct < floor)
            {
                _consecutiveHighCount = 0;
                return;
            }

            // Don't clean during fullscreen (games, video)
            if (SettingsManager.Current.PauseWhenFullscreen && MemoryEngine.IsFullscreenAppActive())
            {
                _consecutiveHighCount = 0;
                return;
            }

            // Keep Smart Skii-mer itself featherlight (3-6 MB)
            MemoryEngine.TrimSelfWorkingSet();

            if (pct >= threshold)
            {
                _consecutiveHighCount++;

                // Require 2 consecutive high readings
                if (_consecutiveHighCount < 2)
                    return;

                // Respect cooldown
                double elapsed = (DateTime.Now - _lastCleanTime).TotalSeconds;
                if (elapsed < SettingsManager.Current.AutoCleanCooldownSeconds)
                    return;

                // Pick clean level based on pressure
                CleanLevel level = pct >= 92 ? CleanLevel.Deep
                                 : pct >= threshold ? CleanLevel.Standard
                                 : CleanLevel.Light;

                _lastCleanTime = DateTime.Now;
                var result = MemoryEngine.SmartClean(level);
                _totalAutoFreedMB += result.FreedMB;
                _consecutiveHighCount = 0;

                UpdateTrayDisplay();

                _settingsForm?.LogActivity($"Auto-Clean ({level}): Reclaimed {result.FreedMB:F0} MB ({result.ProcessesTrimmed} trimmed).");

                // Throttled notifications (once per 60s, only if freed enough)
                if (SettingsManager.Current.NotificationsEnabled &&
                    result.FreedMB >= SettingsManager.Current.NotifyMinFreedMB &&
                    (DateTime.Now - _lastAutoCleanNotifyTime).TotalSeconds >= 60)
                {
                    _lastAutoCleanNotifyTime = DateTime.Now;
                    var after = MemoryEngine.GetCurrentMemoryInfo();
                    string lvl = level.ToString();
                    _trayIcon.ShowBalloonTip(2000,
                        $"Auto-Clean ({lvl})",
                        $"Freed {result.FreedMB:F0} MB → {after.MemoryLoadPercent}% ({after.AvailablePhysicalGB} GB free)",
                        ToolTipIcon.Info);
                }
            }
            else
            {
                _consecutiveHighCount = 0;
            }
        }

        // ══════════════════════════════════════════════════════════
        //  TRAY ICON
        // ══════════════════════════════════════════════════════════

        private async Task ApplyModePolicyAsync()
        {
            if (_modeBusy) return;
            _modeBusy = true;
            try
            {
                var snap = await SystemProbe.CaptureAsync(false);
                _activeMode = ModeManager.Resolve(SettingsManager.Current.Mode, snap);
                if (SettingsManager.Current.AutoEco && _activeMode.EcoBackground)
                {
                    var min = Math.Max(SettingsManager.Current.EcoMinWorkingSetMB, _activeMode.EcoMinWorkingSetMB);
                    SystemProbe.ApplyEcoToBackground(snap.Processes, min, true);
                }
                _trayIcon.Text = $"RAM {snap.Memory.MemoryLoadPercent}% · {_activeMode.Name}";

                if (snap.Memory.MemoryLoadPercent >= 85 && _activeMode is { Id: "gaming" } or { Id: "streaming" }
                    && SettingsManager.Current.AutoAiOnPressure && (DateTime.UtcNow - _lastAiPressure).TotalMinutes >= 10)
                {
                    _lastAiPressure = DateTime.UtcNow;
                    try
                    {
                        var report = await AiAdvisor.AnalyzeAsync(snap, SettingsManager.Current.Ai, _activeMode.Name);
                        if (report.Ok && report.Suggestions.Any(x => x.Action == "trim_background"))
                            SystemProbe.TrimBackground(snap.Processes, 80);
                    }
                    catch { }
                }
            }
            catch { }
            finally { _modeBusy = false; }
        }

        private void OnSettingsChanged()
        {
            _updateTimer.Interval = Math.Clamp(SettingsManager.Current.AutoCleanCheckIntervalSeconds * 1000, 2000, 60000);
            _hotkeyManager.RegisterHotkeys();
        }

        private void UpdateTrayDisplay()
        {
            try
            {
                var mem = MemoryEngine.GetCurrentMemoryInfo();
                int pct = mem.MemoryLoadPercent;

                // Update status label
                try { _statusLabel.Text = $"RAM: {pct}% ({mem.UsedPhysicalGB}/{mem.TotalPhysicalGB} GB) | Commit: {mem.CommitLoadPercent}%"; }
                catch { }

                // Only redraw icon if % changed
                if (pct != _lastRenderedPercent)
                {
                    _lastRenderedPercent = pct;

                    using var bitmap = new Bitmap(32, 32);
                    using var g = Graphics.FromImage(bitmap);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                    // Use theme accent for healthy, shift to red for high
                    Color bgColor = Color.FromArgb(30, 30, 30);
                    Color textColor;
                    if (pct < 60)
                        textColor = ThemeManager.CurrentTheme.AccentColor;
                    else if (pct < 75)
                        textColor = Color.FromArgb(180, 220, 80);
                    else if (pct < 85)
                        textColor = Color.FromArgb(255, 200, 50);
                    else if (pct < 92)
                        textColor = Color.FromArgb(255, 120, 50);
                    else
                        textColor = Color.FromArgb(255, 55, 55);

                    using var brushBg = new SolidBrush(bgColor);
                    using var brushText = new SolidBrush(textColor);
                    using var font = new Font("Segoe UI", 13, FontStyle.Bold, GraphicsUnit.Pixel);

                    g.FillRectangle(brushBg, 0, 0, 32, 32);
                    using var pen = new Pen(textColor, 1.5f);
                    g.DrawRectangle(pen, 1, 1, 30, 30);

                    string text = pct.ToString();
                    var size = g.MeasureString(text, font);
                    g.DrawString(text, font, brushText, (32 - size.Width) / 2f, (32 - size.Height) / 2f);

                    IntPtr hIcon = bitmap.GetHicon();
                    try
                    {
                        using var newIcon = Icon.FromHandle(hIcon);
                        _trayIcon.Icon = (Icon)newIcon.Clone();
                    }
                    finally
                    {
                        NativeMethods.DestroyIcon(hIcon);
                    }
                }

                string autoState = SettingsManager.Current.AutoCleanEnabled ? "Auto" : "Manual";
                string tip = $"Smart Skii-mer [{autoState}]: {pct}% ({mem.AvailablePhysicalGB} GB free)";
                if (tip.Length > 63) tip = $"SMR: {pct}% ({mem.AvailablePhysicalGB}GB free)";
                _trayIcon.Text = tip;
            }
            catch { }
        }

        // ══════════════════════════════════════════════════════════
        //  CLEAN ACTIONS
        // ══════════════════════════════════════════════════════════

        private void ExecuteSmartClean()
        {
            _lastCleanTime = DateTime.Now;
            var level = MemoryEngine.RecommendCleanLevel();
            var result = MemoryEngine.SmartClean(level);
            _totalAutoFreedMB += result.FreedMB;
            UpdateTrayDisplay();
            _settingsForm?.LogActivity($"Smart Clean ({level}): Reclaimed {result.FreedMB:F0} MB ({result.ProcessesTrimmed} trimmed).");

            if (SettingsManager.Current.NotificationsEnabled)
            {
                var mem = MemoryEngine.GetCurrentMemoryInfo();
                _trayIcon.ShowBalloonTip(2000,
                    $"Smart Clean ({level}) — Ctrl+Alt+M",
                    $"Freed {result.FreedMB:F0} MB | {result.ProcessesTrimmed} trimmed → {result.AfterPercent}% ({mem.AvailablePhysicalGB} GB free)",
                    ToolTipIcon.Info);
            }
        }

        private void ExecuteDeepClean()
        {
            _lastCleanTime = DateTime.Now;
            var result = MemoryEngine.SmartClean(CleanLevel.Deep);
            _totalAutoFreedMB += result.FreedMB;
            UpdateTrayDisplay();
            _settingsForm?.LogActivity($"Deep Clean: Reclaimed {result.FreedMB:F0} MB ({result.ProcessesTrimmed} trimmed).");

            if (SettingsManager.Current.NotificationsEnabled)
            {
                var mem = MemoryEngine.GetCurrentMemoryInfo();
                _trayIcon.ShowBalloonTip(2500,
                    "🔥 Deep Clean — Ctrl+Alt+D",
                    $"Freed {result.FreedMB:F0} MB | {result.ProcessesTrimmed} trimmed → {result.AfterPercent}% ({mem.AvailablePhysicalGB} GB free)\nDWM + foreground protected.",
                    ToolTipIcon.Info);
            }
        }

        private void ExecuteEmergencyUnfreeze()
        {
            _lastCleanTime = DateTime.Now;
            var result = AntiFreezeEngine.EmergencyUnfreeze();
            _totalAutoFreedMB += result.CleanStats.FreedMB;
            UpdateTrayDisplay();
            _settingsForm?.LogActivity($"Emergency Unfreeze: DWM boosted, {result.ThrottledProcesses} workers throttled, freed {result.CleanStats.FreedMB:F0} MB.");

            if (SettingsManager.Current.NotificationsEnabled)
            {
                var mem = MemoryEngine.GetCurrentMemoryInfo();
                _trayIcon.ShowBalloonTip(3000,
                    "❄️ Emergency Unfreeze — Ctrl+Alt+F",
                    $"DWM boosted, {result.ThrottledProcesses} throttled, freed {result.CleanStats.FreedMB:F0} MB → {mem.MemoryLoadPercent}%",
                    ToolTipIcon.Warning);
            }
        }

        private void ExecuteCpuThrottle()
        {
            int throttled = AntiFreezeEngine.ThrottleBackgroundWorkers();
            _settingsForm?.LogActivity(throttled > 0 ? $"Throttled {throttled} background CPU workers to BelowNormal." : "No rogue CPU workers found.");
            if (SettingsManager.Current.NotificationsEnabled)
            {
                _trayIcon.ShowBalloonTip(2000, "🛑 CPU Relief — Ctrl+Alt+K",
                    throttled > 0
                        ? $"Throttled {throttled} background processes to BelowNormal."
                        : "No CPU-hogging background workers found.",
                    ToolTipIcon.Info);
            }
        }

        // ══════════════════════════════════════════════════════════
        //  AI ANALYSIS (tray menu shortcut)
        // ══════════════════════════════════════════════════════════

        private async void ExecuteAiAnalysis()
        {
            if (!SettingsManager.Current.Ai.Enabled || string.IsNullOrWhiteSpace(SettingsManager.Current.Ai.ApiKey))
            {
                // Open settings to AI tab if not configured
                _trayIcon.ShowBalloonTip(3000, "🤖 AI Not Configured",
                    "Open Settings → AI Optimizer to enter your API key.",
                    ToolTipIcon.Info);
                OpenSettings();
                return;
            }

            _trayIcon.ShowBalloonTip(1500, "🤖 AI Analyzing...", "Gathering system data and querying AI model...", ToolTipIcon.Info);

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                string result = await AiOptimizer.AnalyzeSystemAsync(cts.Token);

                // Show result in a simple dialog
                MessageBox.Show(result, "🤖 AI Memory Analysis", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"AI analysis failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ══════════════════════════════════════════════════════════
        //  SETTINGS WINDOW
        // ══════════════════════════════════════════════════════════

        private void OpenSettings()
        {
            if (_settingsForm != null && !_settingsForm.IsDisposed)
            {
                _settingsForm.Show();
                if (_settingsForm.WindowState == FormWindowState.Minimized)
                {
                    _settingsForm.WindowState = FormWindowState.Normal;
                }
                _settingsForm.BringToFront();
                _settingsForm.Focus();
                return;
            }

            _settingsForm = new DashboardForm();
            _settingsForm.OnThemeChanged += () =>
            {
                ThemeManager.ApplyToMenu(_contextMenu);
                UpdateTrayDisplay();
            };
            _settingsForm.FormClosed += (s, e) =>
            {
                _autoCleanMenuItem.Text = $"⚙️ Auto-Clean (>{SettingsManager.Current.AutoCleanThresholdPercent}%)";
                _autoCleanMenuItem.Checked = SettingsManager.Current.AutoCleanEnabled;

                int newInterval = Math.Clamp(SettingsManager.Current.AutoCleanCheckIntervalSeconds * 1000, 2000, 60000);
                _updateTimer.Interval = newInterval;

                ThemeManager.ApplyToMenu(_contextMenu);
                _settingsForm = null;
            };
            _settingsForm.Show();
            _settingsForm.BringToFront();
            _settingsForm.Focus();
        }

        // ══════════════════════════════════════════════════════════
        //  STATUS
        // ══════════════════════════════════════════════════════════

        private void ShowDetailedStatus()
        {
            var mem = MemoryEngine.GetCurrentMemoryInfo();
            string autoStatus = SettingsManager.Current.AutoCleanEnabled
                ? $"ON  >{SettingsManager.Current.AutoCleanThresholdPercent}% (cooldown {SettingsManager.Current.AutoCleanCooldownSeconds}s)"
                : "OFF";

            string msg =
                $"═══════ Smart Skii-mer v3.0 ═══════\n\n" +
                $"Physical RAM: {mem.UsedPhysicalGB} / {mem.TotalPhysicalGB} GB ({mem.MemoryLoadPercent}%)\n" +
                $"Free: {mem.AvailablePhysicalGB} GB\n\n" +
                $"Commit: {mem.UsedCommitGB} / {mem.TotalCommitGB} GB ({mem.CommitLoadPercent}%)\n\n" +
                $"──── Session ────\n" +
                $"Auto-freed: {_totalAutoFreedMB:F0} MB total\n" +
                $"Last clean: {(_lastCleanTime == DateTime.MinValue ? "—" : _lastCleanTime.ToString("HH:mm:ss"))}\n\n" +
                $"──── Config ────\n" +
                $"Auto-clean: {autoStatus}\n" +
                $"Fullscreen pause: {(SettingsManager.Current.PauseWhenFullscreen ? "ON" : "OFF")}\n" +
                $"Theme: {ThemeManager.CurrentTheme.Name}\n" +
                $"AI Optimizer: {(SettingsManager.Current.Ai.Enabled ? "ON" : "OFF")}\n\n" +
                $"──── Hotkeys ────\n" +
                $"Ctrl+Alt+M  Smart Clean\n" +
                $"Ctrl+Alt+D  Deep Clean\n" +
                $"Ctrl+Alt+F  Emergency Unfreeze\n" +
                $"Ctrl+Alt+K  Throttle Background CPU";

            MessageBox.Show(msg, "Smart Skii-mer — Status", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ══════════════════════════════════════════════════════════
        //  EXIT
        // ══════════════════════════════════════════════════════════

        private void ExitApp()
        {
            _updateTimer.Stop();
            _updateTimer.Dispose();
            _hotkeyManager.Dispose();
            _settingsForm?.Close();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            Application.Exit();
        }
    }
}
