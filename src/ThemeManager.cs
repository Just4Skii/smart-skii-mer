using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SmartMemReduct
{
    // ── Theme definition ──

    public class Theme
    {
        public string Name { get; set; } = "";
        public Color BackColor { get; set; }
        public Color ForeColor { get; set; }
        public Color AccentColor { get; set; }
        public Color MenuBackColor { get; set; }
        public Color MenuForeColor { get; set; }
        public Color MenuHoverColor { get; set; }
        public Color BorderColor { get; set; }
        public Color ControlBackColor { get; set; }
        public Color ControlForeColor { get; set; }
        public Color ButtonBackColor { get; set; }
        public Color ButtonForeColor { get; set; }
        public Color GroupBoxForeColor { get; set; }
        public Color TabBackColor { get; set; }
        public Color TextBoxBackColor { get; set; }
        public Color TextBoxForeColor { get; set; }
        public Color SeparatorColor { get; set; }
        public Color LinkColor { get; set; }
    }

    // ── Preset themes ──

    public static class ThemeManager
    {
        // NOTE: `Dark` is declared after this member. Assigning = Dark! here runs before the
        // field initialiser, leaving CurrentTheme null. Defer the fallback to the getter.
        private static Theme? _currentTheme;
        public static Theme CurrentTheme
        {
            get => _currentTheme ?? Dark;
            private set => _currentTheme = value;
        }

        public static readonly Theme Dark = new Theme
        {
            Name = "Dark",
            BackColor = Color.FromArgb(30, 30, 34),
            ForeColor = Color.FromArgb(220, 220, 225),
            AccentColor = Color.FromArgb(70, 200, 110),
            MenuBackColor = Color.FromArgb(35, 35, 40),
            MenuForeColor = Color.FromArgb(220, 220, 225),
            MenuHoverColor = Color.FromArgb(55, 55, 62),
            BorderColor = Color.FromArgb(60, 60, 68),
            ControlBackColor = Color.FromArgb(42, 42, 48),
            ControlForeColor = Color.FromArgb(210, 210, 215),
            ButtonBackColor = Color.FromArgb(55, 55, 62),
            ButtonForeColor = Color.FromArgb(220, 220, 225),
            GroupBoxForeColor = Color.FromArgb(140, 200, 160),
            TabBackColor = Color.FromArgb(38, 38, 44),
            TextBoxBackColor = Color.FromArgb(25, 25, 30),
            TextBoxForeColor = Color.FromArgb(210, 210, 215),
            SeparatorColor = Color.FromArgb(60, 60, 65),
            LinkColor = Color.FromArgb(100, 180, 255)
        };

        public static readonly Theme Light = new Theme
        {
            Name = "Light",
            BackColor = Color.FromArgb(245, 245, 248),
            ForeColor = Color.FromArgb(30, 30, 35),
            AccentColor = Color.FromArgb(0, 120, 215),
            MenuBackColor = Color.FromArgb(250, 250, 252),
            MenuForeColor = Color.FromArgb(30, 30, 35),
            MenuHoverColor = Color.FromArgb(220, 230, 245),
            BorderColor = Color.FromArgb(200, 200, 210),
            ControlBackColor = Color.FromArgb(255, 255, 255),
            ControlForeColor = Color.FromArgb(30, 30, 35),
            ButtonBackColor = Color.FromArgb(230, 230, 235),
            ButtonForeColor = Color.FromArgb(30, 30, 35),
            GroupBoxForeColor = Color.FromArgb(0, 100, 180),
            TabBackColor = Color.FromArgb(240, 240, 244),
            TextBoxBackColor = Color.FromArgb(255, 255, 255),
            TextBoxForeColor = Color.FromArgb(20, 20, 25),
            SeparatorColor = Color.FromArgb(210, 210, 218),
            LinkColor = Color.FromArgb(0, 100, 200)
        };

        public static readonly Theme MidnightBlue = new Theme
        {
            Name = "Midnight Blue",
            BackColor = Color.FromArgb(18, 24, 38),
            ForeColor = Color.FromArgb(200, 210, 230),
            AccentColor = Color.FromArgb(80, 140, 255),
            MenuBackColor = Color.FromArgb(22, 28, 44),
            MenuForeColor = Color.FromArgb(200, 210, 230),
            MenuHoverColor = Color.FromArgb(35, 50, 80),
            BorderColor = Color.FromArgb(45, 60, 90),
            ControlBackColor = Color.FromArgb(28, 36, 55),
            ControlForeColor = Color.FromArgb(195, 205, 225),
            ButtonBackColor = Color.FromArgb(40, 55, 85),
            ButtonForeColor = Color.FromArgb(210, 220, 240),
            GroupBoxForeColor = Color.FromArgb(100, 160, 255),
            TabBackColor = Color.FromArgb(24, 32, 50),
            TextBoxBackColor = Color.FromArgb(15, 20, 32),
            TextBoxForeColor = Color.FromArgb(200, 210, 230),
            SeparatorColor = Color.FromArgb(45, 55, 80),
            LinkColor = Color.FromArgb(100, 170, 255)
        };

        public static readonly Theme Emerald = new Theme
        {
            Name = "Emerald",
            BackColor = Color.FromArgb(18, 32, 24),
            ForeColor = Color.FromArgb(200, 225, 210),
            AccentColor = Color.FromArgb(46, 204, 113),
            MenuBackColor = Color.FromArgb(22, 38, 28),
            MenuForeColor = Color.FromArgb(200, 225, 210),
            MenuHoverColor = Color.FromArgb(35, 65, 45),
            BorderColor = Color.FromArgb(45, 80, 55),
            ControlBackColor = Color.FromArgb(26, 42, 32),
            ControlForeColor = Color.FromArgb(195, 220, 205),
            ButtonBackColor = Color.FromArgb(35, 60, 42),
            ButtonForeColor = Color.FromArgb(210, 235, 220),
            GroupBoxForeColor = Color.FromArgb(80, 220, 140),
            TabBackColor = Color.FromArgb(22, 36, 28),
            TextBoxBackColor = Color.FromArgb(14, 26, 20),
            TextBoxForeColor = Color.FromArgb(200, 225, 210),
            SeparatorColor = Color.FromArgb(40, 70, 50),
            LinkColor = Color.FromArgb(80, 210, 140)
        };

        public static readonly Theme Crimson = new Theme
        {
            Name = "Crimson",
            BackColor = Color.FromArgb(32, 18, 22),
            ForeColor = Color.FromArgb(230, 200, 205),
            AccentColor = Color.FromArgb(220, 50, 80),
            MenuBackColor = Color.FromArgb(38, 22, 26),
            MenuForeColor = Color.FromArgb(230, 200, 205),
            MenuHoverColor = Color.FromArgb(70, 35, 42),
            BorderColor = Color.FromArgb(90, 45, 55),
            ControlBackColor = Color.FromArgb(44, 26, 32),
            ControlForeColor = Color.FromArgb(225, 200, 205),
            ButtonBackColor = Color.FromArgb(65, 35, 42),
            ButtonForeColor = Color.FromArgb(240, 210, 215),
            GroupBoxForeColor = Color.FromArgb(255, 100, 130),
            TabBackColor = Color.FromArgb(36, 22, 28),
            TextBoxBackColor = Color.FromArgb(26, 14, 18),
            TextBoxForeColor = Color.FromArgb(230, 200, 205),
            SeparatorColor = Color.FromArgb(80, 40, 50),
            LinkColor = Color.FromArgb(255, 120, 150)
        };

        public static readonly Theme[] AllThemes = { Dark, Light, MidnightBlue, Emerald, Crimson };

        // ── Set theme by name ──

        public static void SetTheme(string name)
        {
            foreach (var t in AllThemes)
            {
                if (string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    CurrentTheme = t;
                    return;
                }
            }
            CurrentTheme = Dark;
        }

        // ── Apply theme to a Form ──

        public static void ApplyToForm(Form form)
        {
            var t = CurrentTheme;
            form.BackColor = t.BackColor;
            form.ForeColor = t.ForeColor;

            bool isDark = !string.Equals(t.Name, "Light", StringComparison.OrdinalIgnoreCase);
            if (form.IsHandleCreated)
            {
                NativeMethods.ApplyDarkModeToWindow(form.Handle, isDark, t.BackColor, t.BorderColor, t.ForeColor);
            }

            ApplyToControls(form.Controls, t);
        }

        // ── Apply theme to a ContextMenuStrip ──

        public static void ApplyToMenu(ContextMenuStrip menu)
        {
            var t = CurrentTheme;
            menu.BackColor = t.MenuBackColor;
            menu.ForeColor = t.MenuForeColor;
            menu.Renderer = new ThemedMenuRenderer(t);

            foreach (ToolStripItem item in menu.Items)
            {
                item.ForeColor = t.MenuForeColor;
                item.BackColor = t.MenuBackColor;
            }
        }

        // ── Recursive control theming ──

        private static void ApplyToControls(Control.ControlCollection controls, Theme t)
        {
            foreach (Control c in controls)
            {
                if (c is TabControl tab)
                {
                    tab.BackColor = t.BackColor;
                    tab.ForeColor = t.ForeColor;
                    tab.DrawMode = TabDrawMode.OwnerDrawFixed;
                    tab.SizeMode = TabSizeMode.Fixed;
                    tab.ItemSize = new Size(125, 30);

                    tab.DrawItem -= Tab_DrawItem;
                    tab.DrawItem += Tab_DrawItem;

                    foreach (TabPage page in tab.TabPages)
                    {
                        page.BackColor = t.BackColor;
                        page.ForeColor = t.ForeColor;
                        ApplyToControls(page.Controls, t);
                    }
                }
                else if (c is GroupBox gb)
                {
                    gb.ForeColor = t.GroupBoxForeColor;
                    gb.BackColor = t.BackColor;
                    ApplyToControls(gb.Controls, t);
                }
                else if (c is TextBox tb)
                {
                    tb.BackColor = t.TextBoxBackColor;
                    tb.ForeColor = t.TextBoxForeColor;
                    tb.BorderStyle = BorderStyle.FixedSingle;
                }
                else if (c is RichTextBox rtb)
                {
                    rtb.BackColor = t.TextBoxBackColor;
                    rtb.ForeColor = t.TextBoxForeColor;
                }
                else if (c is Button btn)
                {
                    btn.BackColor = t.ButtonBackColor;
                    btn.ForeColor = t.ButtonForeColor;
                    btn.FlatStyle = FlatStyle.Flat;
                    btn.FlatAppearance.BorderColor = t.BorderColor;
                    btn.FlatAppearance.MouseOverBackColor = t.AccentColor;
                }
                else if (c is ComboBox cb)
                {
                    cb.BackColor = t.TextBoxBackColor;
                    cb.ForeColor = t.TextBoxForeColor;
                    cb.FlatStyle = FlatStyle.Flat;
                }
                else if (c is NumericUpDown nud)
                {
                    nud.BackColor = t.TextBoxBackColor;
                    nud.ForeColor = t.TextBoxForeColor;
                }
                else if (c is CheckBox chk)
                {
                    chk.ForeColor = t.ForeColor;
                    chk.BackColor = t.BackColor;
                }
                else if (c is RadioButton rb)
                {
                    rb.ForeColor = t.ForeColor;
                    rb.BackColor = t.BackColor;
                }
                else if (c is Label lbl)
                {
                    lbl.ForeColor = t.ForeColor;
                    lbl.BackColor = t.BackColor;
                }
                else if (c is LinkLabel ll)
                {
                    ll.LinkColor = t.LinkColor;
                    ll.BackColor = t.BackColor;
                }
                else if (c is Panel pnl)
                {
                    pnl.BackColor = t.BackColor;
                    ApplyToControls(pnl.Controls, t);
                }
                else
                {
                    c.BackColor = t.BackColor;
                    c.ForeColor = t.ForeColor;
                }

                if (c.HasChildren && !(c is TabControl) && !(c is GroupBox) && !(c is Panel))
                {
                    ApplyToControls(c.Controls, t);
                }
            }
        }

        private static void Tab_DrawItem(object? sender, DrawItemEventArgs e)
        {
            if (sender is not TabControl tab || e.Index < 0 || e.Index >= tab.TabPages.Count)
                return;

            var t = CurrentTheme;
            var page = tab.TabPages[e.Index];
            bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            var rect = e.Bounds;

            // Background
            using (var brush = new SolidBrush(isSelected ? t.ControlBackColor : t.TabBackColor))
            {
                e.Graphics.FillRectangle(brush, rect);
            }

            // Accent underline when selected
            if (isSelected)
            {
                using (var pen = new Pen(t.AccentColor, 3f))
                {
                    e.Graphics.DrawLine(pen, rect.Left + 2, rect.Bottom - 2, rect.Right - 2, rect.Bottom - 2);
                }
            }

            // Tab border
            using (var borderPen = new Pen(t.BorderColor, 1f))
            {
                e.Graphics.DrawRectangle(borderPen, rect.Left, rect.Top, rect.Width - 1, rect.Height - 1);
            }

            // Tab text
            Color textColor = isSelected ? t.AccentColor : t.ForeColor;
            using (var font = new Font("Segoe UI", 9f, isSelected ? FontStyle.Bold : FontStyle.Regular))
            using (var textBrush = new SolidBrush(textColor))
            {
                var sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                e.Graphics.DrawString(page.Text, font, textBrush, rect, sf);
            }
        }

        // ── Custom menu renderer for theming ──

        private class ThemedMenuRenderer : ToolStripProfessionalRenderer
        {
            private readonly Theme _theme;

            public ThemedMenuRenderer(Theme theme) : base(new ThemedColorTable(theme))
            {
                _theme = theme;
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = _theme.MenuForeColor;
                base.OnRenderItemText(e);
            }
        }

        private class ThemedColorTable : ProfessionalColorTable
        {
            private readonly Theme _t;
            public ThemedColorTable(Theme t) { _t = t; }

            public override Color MenuItemSelected => _t.MenuHoverColor;
            public override Color MenuBorder => _t.BorderColor;
            public override Color MenuItemBorder => _t.BorderColor;
            public override Color ToolStripDropDownBackground => _t.MenuBackColor;
            public override Color ImageMarginGradientBegin => _t.MenuBackColor;
            public override Color ImageMarginGradientMiddle => _t.MenuBackColor;
            public override Color ImageMarginGradientEnd => _t.MenuBackColor;
            public override Color SeparatorDark => _t.SeparatorColor;
            public override Color SeparatorLight => _t.SeparatorColor;
            public override Color MenuItemPressedGradientBegin => _t.AccentColor;
            public override Color MenuItemPressedGradientEnd => _t.AccentColor;
            public override Color MenuItemSelectedGradientBegin => _t.MenuHoverColor;
            public override Color MenuItemSelectedGradientEnd => _t.MenuHoverColor;
        }
    }
}
