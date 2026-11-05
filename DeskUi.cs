using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

// Native controls and original vector art: the desk has no web view, asset downloads or UI timers.
// The runtime owns the single frame clock and calls InvalidateDeskUi only while it is needed.
public static partial class ClaudeBg
{
    static StudioForm studio;
    static CompanionForm companion;

    static void ShowStudio()
    {
        if (cfg == null) return;
        if (studio == null || studio.IsDisposed)
        {
            var next = new StudioForm();
            next.FormClosed += delegate { if (studio == next) studio = null; };
            studio = next;
        }
        if (studio.WindowState == FormWindowState.Minimized) studio.WindowState = FormWindowState.Normal;
        studio.Show();
        studio.Activate();
        ChangeDeskPreferences(); // also wakes the shared frame driver after a static/hidden desk
    }

    static void UpdateCompanionWindow(Rectangle? targetBounds)
    {
        if (cfg == null || !cfg.CompanionVisible || cfg.Mode == DeskMode.Present ||
            !targetBounds.HasValue || targetBounds.Value.Width <= 0 || targetBounds.Value.Height <= 0)
        {
            if (companion != null && !companion.IsDisposed) companion.Hide();
            return;
        }
        if (companion == null || companion.IsDisposed) companion = new CompanionForm();
        companion.PlaceBeside(targetBounds.Value);
    }

    static bool DeskUiNeedsFrames()
    {
        return (studio != null && !studio.IsDisposed && studio.NeedsFrames) ||
               (companion != null && !companion.IsDisposed && companion.NeedsFrames);
    }

    static void InvalidateDeskUi()
    {
        if (studio != null && !studio.IsDisposed && studio.Visible && studio.WindowState != FormWindowState.Minimized)
            studio.RefreshFrame();
        if (companion != null && !companion.IsDisposed && companion.Visible) companion.RefreshFrame();
    }

    static void CloseDeskUi()
    {
        var a = studio; studio = null;
        var b = companion; companion = null;
        if (a != null) { a.Close(); a.Dispose(); }
        if (b != null) { b.Close(); b.Dispose(); }
    }

    // Pure placement logic is shared with the UI smoke tests. A narrow screen always gets a bounded
    // compact companion, never an off-screen dock or a dock covering the centre of the conversation.
    internal static Rectangle CalculateCompanionBounds(Rectangle target, Rectangle workingArea,
        Size expandedSize, Size compactSize, out bool compact)
    {
        int gap = Math.Max(8, expandedSize.Width / 18);
        int fullWidth = Math.Max(1, expandedSize.Width), fullHeight = Math.Max(1, expandedSize.Height);
        compact = true;
        int x, y, width, height;
        bool fullFits = fullWidth <= workingArea.Width && fullHeight <= workingArea.Height;
        if (fullFits && target.Right + gap + fullWidth <= workingArea.Right && target.Right >= workingArea.Left)
        {
            compact = false; x = target.Right + gap; width = fullWidth; height = fullHeight;
            y = target.Top + Math.Max(0, (target.Height - height) / 2);
        }
        else if (fullFits && target.Left - gap - fullWidth >= workingArea.Left && target.Left <= workingArea.Right)
        {
            compact = false; x = target.Left - gap - fullWidth; width = fullWidth; height = fullHeight;
            y = target.Top + Math.Max(0, (target.Height - height) / 2);
        }
        else
        {
            width = Math.Max(1, Math.Min(compactSize.Width, Math.Max(1, workingArea.Width)));
            height = Math.Max(1, Math.Min(compactSize.Height, Math.Max(1, workingArea.Height)));
            x = Math.Min(target.Right, workingArea.Right) - width - gap;
            y = Math.Min(target.Bottom, workingArea.Bottom) - height - gap;
        }
        x = Math.Max(workingArea.Left, Math.Min(x, workingArea.Right - width));
        y = Math.Max(workingArea.Top, Math.Min(y, workingArea.Bottom - height));
        return new Rectangle(x, y, width, height);
    }

    static bool DeskMotionIsLive()
    {
        return cfg != null && cfg.SceneAnimations && systemMotion && cfg.Motion > 0 && cfg.Mode != DeskMode.Present;
    }

    static string DeskTimeText(int seconds)
    {
        seconds = Math.Max(0, seconds);
        return (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
    }

    static string DeskFocusAction()
    {
        return FocusRunning() ? "Pause" : (FocusHasSession() ? "Resume" : "Start focus");
    }

    static string DeskSceneName(SceneKind scene)
    {
        switch (scene)
        {
            case SceneKind.RainyStudy: return "Rainy Study";
            case SceneKind.Aquarium: return "Aquarium";
            case SceneKind.NightTrain: return "Night Train";
            default: return "Image";
        }
    }

    static string DeskModeDescription(DeskMode mode)
    {
        switch (mode)
        {
            case DeskMode.Relax: return "A little more movement. A little room to wander.";
            case DeskMode.Present: return "A still scene. N\u1ebfp steps away while you present.";
            default: return "Gentle motion. N\u1ebfp settles down while you focus.";
        }
    }

    static void ChooseDeskImage(IWin32Window owner)
    {
        using (var dialog = new OpenFileDialog { Filter = ImageFilter, Title = "Choose your desk image" })
        {
            if (dialog.ShowDialog(owner) != DialogResult.OK || !TrySetImage(dialog.FileName, true)) return;
            string copy = Path.Combine(appDir, "bg" + Path.GetExtension(dialog.FileName).ToLowerInvariant());
            try
            {
                if (!string.Equals(Path.GetFullPath(dialog.FileName), Path.GetFullPath(copy), StringComparison.OrdinalIgnoreCase))
                    File.Copy(dialog.FileName, copy, true);
                currentImage = cfg.Image = copy;
            }
            catch (Exception) { cfg.Image = dialog.FileName; }
            cfg.Scene = SceneKind.Image;
            cfg.SlideMinutes = 0;
            ConfigureSlideshow();
            ChangeDeskPreferences();
        }
    }

    sealed class DeskPalette
    {
        internal readonly Color Background, Card, Ink, Muted, Line, Accent, AccentSoft;
        internal DeskPalette(bool dark)
        {
            Background = dark ? Color.FromArgb(27, 32, 31) : Color.FromArgb(244, 243, 237);
            Card = dark ? Color.FromArgb(36, 43, 41) : Color.FromArgb(253, 252, 248);
            Ink = dark ? Color.FromArgb(237, 237, 222) : Color.FromArgb(39, 55, 48);
            Muted = dark ? Color.FromArgb(169, 184, 172) : Color.FromArgb(110, 124, 114);
            Line = dark ? Color.FromArgb(65, 78, 70) : Color.FromArgb(217, 224, 213);
            Accent = dark ? Color.FromArgb(159, 197, 147) : Color.FromArgb(67, 108, 73);
            AccentSoft = dark ? Color.FromArgb(52, 70, 53) : Color.FromArgb(231, 238, 222);
        }
    }

    static GraphicsPath DeskRoundedRectangle(RectangleF rectangle, float radius)
    {
        var path = new GraphicsPath();
        float diameter = Math.Min(Math.Min(rectangle.Width, rectangle.Height), radius * 2);
        if (diameter <= 0) return path;
        path.AddArc(rectangle.X, rectangle.Y, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Y, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.X, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    static void PaintDeskControls(Control root, DeskPalette palette)
    {
        root.ForeColor = palette.Ink;
        var button = root as Button;
        var panel = root as Panel;
        var combo = root as ComboBox;
        var number = root as NumericUpDown;
        if (button != null)
        {
            bool primary = string.Equals(button.Tag as string, "primary", StringComparison.Ordinal);
            button.BackColor = primary ? palette.Accent : palette.Card;
            button.ForeColor = primary ? palette.Background : palette.Ink;
            button.FlatAppearance.BorderColor = primary ? palette.Accent : palette.Line;
            button.FlatAppearance.MouseOverBackColor = primary ? palette.Accent : palette.AccentSoft;
            button.FlatAppearance.MouseDownBackColor = primary ? palette.Accent : palette.Line;
        }
        else if (combo != null || number != null) root.BackColor = palette.Card;
        else if (root is ScenePreview || root is CompanionAvatar || root is DeskProgress) { }
        else if (root is Form) root.BackColor = palette.Background;
        else if (panel != null) root.BackColor = string.Equals(root.Tag as string, "card", StringComparison.Ordinal) ? palette.Card : root.Parent == null ? palette.Background : root.Parent.BackColor;
        else root.BackColor = root.Parent == null ? palette.Background : root.Parent.BackColor;
        if (string.Equals(root.Tag as string, "muted", StringComparison.Ordinal)) root.ForeColor = palette.Muted;
        if (string.Equals(root.Tag as string, "accent", StringComparison.Ordinal)) root.ForeColor = palette.Accent;
        foreach (Control child in root.Controls) PaintDeskControls(child, palette);
    }

    sealed class DeskActionButton : Button
    {
        readonly bool noActivate;
        internal DeskActionButton(bool noActivate)
        {
            this.noActivate = noActivate;
            if (noActivate) { SetStyle(ControlStyles.Selectable, false); TabStop = false; }
        }
        protected override void WndProc(ref Message message)
        {
            if (noActivate && message.Msg == 0x21) { message.Result = new IntPtr(3); return; }
            base.WndProc(ref message);
        }
    }

    static Button DeskButton(string text, bool primary, bool noActivate = false)
    {
        return new DeskActionButton(noActivate) {
            Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(64, 32), Padding = new Padding(10, 4, 10, 4),
            Margin = new Padding(0, 0, 8, 0), FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false, Cursor = Cursors.Hand, Tag = primary ? "primary" : "button"
        };
    }

    static Label DeskLabel(string text, Font font, string role)
    {
        return new Label { Text = text, Font = font, AutoSize = true, Margin = new Padding(0), Tag = role };
    }

    // All controls remain real WinForms controls with tab order and accessible labels. The settings
    // column scrolls independently at the minimum size; the preview and timer stay in view.
    sealed class StudioForm : Form
    {
        readonly List<Font> ownedFonts = new List<Font>();
        readonly ComboBox sceneChoice, modeChoice;
        readonly TrackBar motion, opacity, darkDim, lightDim;
        readonly CheckBox animations, companionToggle;
        readonly NumericUpDown focusMinutes;
        readonly Label motionValue, opacityValue, darkDimValue, lightDimValue;
        readonly Label modeHint, previewTitle, sceneHint, clock, focusState, growth, focusNote;
        readonly Button focusButton, resetButton, finishButton, imageButton;
        readonly ScenePreview preview;
        readonly DeskProgress progress;
        bool syncing;
        bool darkTheme;

        public StudioForm()
        {
            Text = "Scene Studio \u00b7 Claude Living Desk";
            Name = "SceneStudio";
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(900, 650);
            MinimumSize = new Size(720, 540);
            Font = OwnFont(9F, FontStyle.Regular);
            DoubleBuffered = true;
            var titleFont = OwnFont(21F, FontStyle.Bold);
            var sectionFont = OwnFont(10F, FontStyle.Bold);
            var smallFont = OwnFont(8.25F, FontStyle.Regular);
            var clockFont = OwnFont(30F, FontStyle.Regular);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 3 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            var heading = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 1, Margin = new Padding(0, 0, 0, 18) };
            heading.Controls.Add(DeskLabel("Make room for a good day.", titleFont, "ink"));
            var subtitle = DeskLabel("Living scenes, a small companion, and a little time for one thing.", Font, "muted");
            subtitle.Margin = new Padding(0, 5, 0, 0);
            heading.Controls.Add(subtitle);
            root.Controls.Add(heading, 0, 0);

            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.Controls.Add(body, 0, 1);

            var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 0, 14, 0) };
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.Controls.Add(left, 0, 0);

            var previewCard = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12), Margin = new Padding(0, 0, 0, 12), Tag = "card" };
            previewCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            previewCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            previewCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            previewCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            previewTitle = DeskLabel("Rainy Study", sectionFont, "ink");
            previewTitle.Margin = new Padding(0, 0, 0, 9);
            previewCard.Controls.Add(previewTitle, 0, 0);
            preview = new ScenePreview { Dock = DockStyle.Fill, Margin = new Padding(0), Name = "ScenePreview", AccessibleName = "Live scene preview" };
            previewCard.Controls.Add(preview, 0, 1);
            sceneHint = DeskLabel("", smallFont, "muted");
            sceneHint.Dock = DockStyle.Fill;
            sceneHint.Margin = new Padding(0, 9, 0, 0);
            previewCard.Controls.Add(sceneHint, 0, 2);
            left.Controls.Add(previewCard, 0, 0);

            var focusCard = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 5, Padding = new Padding(14), Margin = new Padding(0), Tag = "card" };
            focusCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var focusHeading = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(0) };
            focusHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            focusHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            focusHeading.Controls.Add(DeskLabel("A moment of focus", sectionFont, "ink"), 0, 0);
            focusState = DeskLabel("Ready", smallFont, "accent");
            focusState.Anchor = AnchorStyles.Right;
            focusHeading.Controls.Add(focusState, 1, 0);
            focusCard.Controls.Add(focusHeading, 0, 0);
            var clockRow = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 5, 0, 6) };
            clockRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
            clockRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
            clock = DeskLabel("25:00", clockFont, "ink");
            clock.Name = "FocusClock";
            clockRow.Controls.Add(clock, 0, 0);
            growth = DeskLabel("0 sessions grown", smallFont, "muted");
            growth.Dock = DockStyle.Fill;
            growth.TextAlign = ContentAlignment.MiddleRight;
            clockRow.Controls.Add(growth, 1, 0);
            focusCard.Controls.Add(clockRow, 0, 1);
            progress = new DeskProgress { Dock = DockStyle.Top, Height = 6, Margin = new Padding(0, 0, 0, 12), Name = "FocusProgress" };
            focusCard.Controls.Add(progress, 0, 2);
            var focusActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true, Margin = new Padding(0) };
            focusButton = DeskButton("Start focus", true); focusButton.Name = "FocusToggle";
            resetButton = DeskButton("Reset", false); resetButton.Name = "FocusReset";
            finishButton = DeskButton("Finish", false); finishButton.Name = "FocusFinish";
            focusActions.Controls.Add(focusButton); focusActions.Controls.Add(resetButton); focusActions.Controls.Add(finishButton);
            focusCard.Controls.Add(focusActions, 0, 3);
            focusNote = DeskLabel("Finish a session to grow a little more green.", smallFont, "muted");
            focusNote.Dock = DockStyle.Fill;
            focusNote.Margin = new Padding(0, 9, 0, 0);
            focusCard.Controls.Add(focusNote, 0, 4);
            left.Controls.Add(focusCard, 0, 1);

            var settingsViewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12), Margin = new Padding(0), Tag = "card", Name = "SettingsViewport" };
            var settings = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Padding = new Padding(0, 0, 7, 0), Margin = new Padding(0), Tag = "card" };
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            settingsViewport.Controls.Add(settings);
            body.Controls.Add(settingsViewport, 1, 0);
            AddSetting(settings, DeskLabel("YOUR SPACE", sectionFont, "accent"), 0, 12);

            AddSetting(settings, DeskLabel("Scene", Font, "ink"), 0, 5);
            sceneChoice = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, FlatStyle = FlatStyle.Flat, Name = "SceneChoice", AccessibleName = "Scene" };
            sceneChoice.Items.AddRange(new object[] { "Image", "Rainy Study", "Aquarium", "Night Train" });
            AddSetting(settings, sceneChoice, 0, 12);
            AddSetting(settings, DeskLabel("Mode", Font, "ink"), 0, 5);
            modeChoice = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, FlatStyle = FlatStyle.Flat, Name = "ModeChoice", AccessibleName = "Desk mode" };
            modeChoice.Items.AddRange(new object[] { "Focus", "Relax", "Present" });
            AddSetting(settings, modeChoice, 0, 6);
            modeHint = DeskLabel("", smallFont, "muted");
            modeHint.Dock = DockStyle.Top;
            AddSetting(settings, modeHint, 0, 12);

            motion = MakeSlider("Motion", 0, 100);
            motionValue = AddSlider(settings, "Motion", motion, Font);
            animations = new CheckBox { Text = "Animate scenes", AutoSize = true, Name = "AnimateScenes", AccessibleName = "Animate scenes" };
            companionToggle = new CheckBox { Text = "Show N\u1ebfp companion", AutoSize = true, Name = "ShowCompanion", AccessibleName = "Show Nep companion" };
            AddSetting(settings, animations, 0, 4);
            AddSetting(settings, companionToggle, 0, 13);
            AddSetting(settings, DeskLabel("Focus length", Font, "ink"), 0, 5);
            var durationRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0), WrapContents = false };
            focusMinutes = new NumericUpDown { Minimum = 5, Maximum = 120, Increment = 5, Width = 76, Name = "FocusMinutes", AccessibleName = "Focus session minutes" };
            durationRow.Controls.Add(focusMinutes);
            var minutesText = DeskLabel("minutes", Font, "muted");
            minutesText.Margin = new Padding(6, 4, 0, 0);
            durationRow.Controls.Add(minutesText);
            AddSetting(settings, durationRow, 0, 18);

            AddSetting(settings, DeskLabel("APPEARANCE", sectionFont, "accent"), 0, 12);
            opacity = MakeSlider("FocusedOpacity", 30, 100);
            opacityValue = AddSlider(settings, "Claude opacity while focused", opacity, Font);
            darkDim = MakeSlider("ImageDimDark", 0, 100);
            darkDimValue = AddSlider(settings, "Image dim \u00b7 dark Claude", darkDim, Font);
            lightDim = MakeSlider("ImageDimLight", 0, 100);
            lightDimValue = AddSlider(settings, "Image dim \u00b7 light Claude", lightDim, Font);
            imageButton = DeskButton("Choose image...", false); imageButton.Name = "ChooseImage"; imageButton.Dock = DockStyle.Top;
            AddSetting(settings, imageButton, 0, 7);
            var imageNote = DeskLabel("Your image stays on this device. Dim controls apply in Image mode.", smallFont, "muted");
            imageNote.Dock = DockStyle.Top;
            AddSetting(settings, imageNote, 0, 4);

            var footer = DeskLabel("Changes are saved automatically. Close Studio to keep your desk running.", smallFont, "muted");
            footer.Margin = new Padding(0, 12, 0, 0);
            footer.Dock = DockStyle.Fill;
            root.Controls.Add(footer, 0, 2);

            sceneChoice.SelectedIndexChanged += delegate { if (!syncing && sceneChoice.SelectedIndex >= 0) { cfg.Scene = (SceneKind)sceneChoice.SelectedIndex; ChangeDeskPreferences(); } };
            modeChoice.SelectedIndexChanged += delegate { if (!syncing && modeChoice.SelectedIndex >= 0) { cfg.Mode = (DeskMode)modeChoice.SelectedIndex; ChangeDeskPreferences(); } };
            motion.ValueChanged += delegate { if (!syncing) { cfg.Motion = motion.Value / 100.0; ChangeDeskPreferences(); } };
            opacity.ValueChanged += delegate { if (!syncing) { cfg.Focused = opacity.Value / 100.0; ChangeDeskPreferences(); } };
            darkDim.ValueChanged += delegate { if (!syncing) { cfg.DimDark = darkDim.Value / 100.0; ChangeDeskPreferences(); } };
            lightDim.ValueChanged += delegate { if (!syncing) { cfg.DimLight = lightDim.Value / 100.0; ChangeDeskPreferences(); } };
            animations.CheckedChanged += delegate { if (!syncing) { cfg.SceneAnimations = animations.Checked; ChangeDeskPreferences(); } };
            companionToggle.CheckedChanged += delegate { if (!syncing) { cfg.CompanionVisible = companionToggle.Checked; ChangeDeskPreferences(); } };
            focusMinutes.ValueChanged += delegate { if (!syncing) { cfg.FocusMinutes = (int)focusMinutes.Value; ChangeDeskPreferences(); } };
            imageButton.Click += delegate { ChooseDeskImage(this); };
            focusButton.Click += delegate { ToggleFocusSession(); };
            resetButton.Click += delegate { ResetFocusSession(); };
            finishButton.Click += delegate { CompleteFocusSession(); };
            DeskChanged += PreferencesChanged;
            darkTheme = !claudeDark;
            PreferencesChanged();
        }

        Font OwnFont(float size, FontStyle style)
        {
            var font = new Font("Segoe UI", size, style, GraphicsUnit.Point);
            ownedFonts.Add(font); return font;
        }

        static void AddSetting(TableLayoutPanel table, Control control, int top, int bottom)
        {
            int row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            control.Margin = new Padding(0, top, 0, bottom);
            table.Controls.Add(control, 0, row);
        }

        static TrackBar MakeSlider(string name, int minimum, int maximum)
        {
            return new TrackBar { Minimum = minimum, Maximum = maximum, TickStyle = TickStyle.None,
                SmallChange = 1, LargeChange = 5, AutoSize = false, Height = 28,
                Dock = DockStyle.Top, Margin = new Padding(0), Name = name, AccessibleName = name };
        }

        static Label AddSlider(TableLayoutPanel settings, string title, TrackBar slider, Font font)
        {
            var row = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, Margin = new Padding(0) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var name = DeskLabel(title, font, "ink");
            name.Dock = DockStyle.Fill;
            row.Controls.Add(name, 0, 0);
            var value = DeskLabel("0%", font, "muted");
            value.Anchor = AnchorStyles.Right;
            value.Margin = new Padding(7, 0, 0, 0);
            row.Controls.Add(value, 1, 0);
            AddSetting(settings, row, 0, 1);
            AddSetting(settings, slider, 0, 9);
            slider.AccessibleName = title;
            return value;
        }

        internal bool NeedsFrames
        {
            get { return Visible && WindowState != FormWindowState.Minimized &&
                cfg.Scene != SceneKind.Image && DeskMotionIsLive(); }
        }

        void PreferencesChanged()
        {
            if (IsDisposed || cfg == null) return;
            syncing = true;
            try
            {
                sceneChoice.SelectedIndex = Math.Max(0, Math.Min(3, (int)cfg.Scene));
                modeChoice.SelectedIndex = Math.Max(0, Math.Min(2, (int)cfg.Mode));
                motion.Value = ToPercent(cfg.Motion, 0);
                opacity.Value = ToPercent(cfg.Focused, 30);
                darkDim.Value = ToPercent(cfg.DimDark, 0);
                lightDim.Value = ToPercent(cfg.DimLight, 0);
                animations.Checked = cfg.SceneAnimations;
                companionToggle.Checked = cfg.CompanionVisible;
                focusMinutes.Value = Math.Max(5, Math.Min(120, cfg.FocusMinutes));
                motionValue.Text = motion.Value + "%";
                opacityValue.Text = opacity.Value + "%";
                darkDimValue.Text = darkDim.Value + "%";
                lightDimValue.Text = lightDim.Value + "%";
                modeHint.Text = DeskModeDescription(cfg.Mode);
                previewTitle.Text = DeskSceneName(cfg.Scene) + "  /  " + cfg.Mode;
                sceneHint.Text = cfg.Scene == SceneKind.Image ? "A familiar view, made yours." :
                    cfg.Mode == DeskMode.Present ? "Still for your audience." : "Move your pointer across the preview.";
                motion.Enabled = cfg.SceneAnimations && cfg.Mode != DeskMode.Present;
                darkDim.Enabled = lightDim.Enabled = cfg.Scene == SceneKind.Image;
                if (darkTheme != claudeDark)
                {
                    darkTheme = claudeDark;
                    PaintDeskControls(this, new DeskPalette(darkTheme));
                }
            }
            finally { syncing = false; }
            preview.Invalidate();
            RefreshFrame();
        }

        static int ToPercent(double value, int minimum)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return minimum;
            return Math.Max(minimum, Math.Min(100, (int)Math.Round(value * 100)));
        }

        internal void RefreshFrame()
        {
            int remaining = FocusSecondsRemaining();
            string time = DeskTimeText(remaining);
            if (clock.Text != time) clock.Text = time;
            string action = DeskFocusAction();
            if (focusButton.Text != action) focusButton.Text = action;
            bool has = FocusHasSession();
            focusState.Text = FocusRunning() ? "In progress" : has ? "Paused" : "Ready";
            resetButton.Enabled = finishButton.Enabled = has;
            focusMinutes.Enabled = !has;
            growth.Text = cfg.CompletedSessions + (cfg.CompletedSessions == 1 ? " session grown" : " sessions grown");
            focusNote.Text = has ? "Your current session keeps its length until you finish or reset." : "Finish a session to grow a little more green.";
            progress.Value = has ? 1.0 - remaining / (double)Math.Max(1, cfg.FocusMinutes * 60) : 0;
            if (cfg.Scene != SceneKind.Image && DeskMotionIsLive()) preview.Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) DeskChanged -= PreferencesChanged;
            base.Dispose(disposing);
            if (disposing) { foreach (var font in ownedFonts) font.Dispose(); ownedFonts.Clear(); }
        }
    }

    sealed class ScenePreview : Control
    {
        PointF pointer = new PointF(0.5F, 0.5F);
        public ScenePreview()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = false;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            pointer = new PointF(Math.Max(0, Math.Min(1, e.X / (float)Math.Max(1, Width))),
                Math.Max(0, Math.Min(1, e.Y / (float)Math.Max(1, Height))));
            if (cfg != null && cfg.Scene != SceneKind.Image && DeskMotionIsLive()) Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            pointer = new PointF(0.5F, 0.5F);
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width <= 1 || Height <= 1 || cfg == null) return;
            Graphics g = e.Graphics;
            var state = g.Save();
            try
            {
                g.Clear(new DeskPalette(claudeDark).Card);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var shape = DeskRoundedRectangle(new RectangleF(0, 0, Width - 1, Height - 1), 11)) g.SetClip(shape);
                var frame = CurrentSceneFrame(pointer);
                if (frame.Scene != SceneKind.Image) ScenePainter.Paint(g, ClientRectangle, frame);
                else if (bgImage != null)
                {
                    using (var background = new SolidBrush(Color.Black)) g.FillRectangle(background, ClientRectangle);
                    float scale = Math.Max(Width / (float)bgImage.Width, Height / (float)bgImage.Height);
                    float width = bgImage.Width * scale, height = bgImage.Height * scale;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(bgImage, (Width - width) * lookAlignX, (Height - height) * lookAlignY, width, height);
                    using (var dim = new SolidBrush(Color.FromArgb((int)(Math.Max(0, Math.Min(1, lookDim)) * 255), lookTint)))
                        g.FillRectangle(dim, ClientRectangle);
                }
                else
                {
                    var palette = new DeskPalette(claudeDark);
                    using (var fill = new LinearGradientBrush(ClientRectangle, palette.AccentSoft, palette.Card, 70F))
                        g.FillRectangle(fill, ClientRectangle);
                    float cx = Width / 2F, cy = Height / 2F - 15;
                    using (var line = new Pen(palette.Accent, 2))
                    using (var leaf = new SolidBrush(palette.Accent))
                    {
                        g.DrawLine(line, cx, cy + 11, cx, cy - 13);
                        g.FillEllipse(leaf, cx - 18, cy - 13, 18, 10);
                        g.FillEllipse(leaf, cx, cy - 20, 18, 10);
                        g.DrawArc(line, cx - 21, cy + 2, 42, 18, 0, 180);
                    }
                    using (var text = new SolidBrush(palette.Muted))
                    using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near })
                        g.DrawString("Choose a photo to make this space yours.", Font, text,
                            new RectangleF(18, cy + 33, Math.Max(1, Width - 36), Math.Max(1, Height - cy - 38)), format);
                }
            }
            finally { g.Restore(state); }
        }
    }

    sealed class DeskProgress : Control
    {
        double value;
        internal double Value
        {
            get { return value; }
            set { double next = Math.Max(0, Math.Min(1, value)); if (Math.Abs(this.value - next) > 0.00001) { this.value = next; Invalidate(); } }
        }
        public DeskProgress()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = false;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width <= 0 || Height <= 0) return;
            var palette = new DeskPalette(claudeDark);
            e.Graphics.Clear(Parent == null ? palette.Card : Parent.BackColor);
            using (var track = new SolidBrush(palette.Line))
            using (var fill = new SolidBrush(palette.Accent))
            using (var shape = DeskRoundedRectangle(ClientRectangle, Height / 2F))
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.FillPath(track, shape);
                if (value > 0)
                    using (var filled = DeskRoundedRectangle(new RectangleF(0, 0, (float)(Width * value), Height), Height / 2F))
                        e.Graphics.FillPath(fill, filled);
            }
        }
    }

    // No-activate means pressing a timer button or greeting Nếp does not move keyboard focus away
    // from the Claude composer. Opening Studio is an explicit action and may activate that window.
    sealed class CompanionForm : Form
    {
        readonly List<Font> ownedFonts = new List<Font>();
        readonly TableLayoutPanel fullLayout;
        readonly CompanionAvatar avatar, compactAvatar;
        readonly Label greeting, time, status, count;
        readonly Button toggle, reset, finish;
        readonly DeskProgress progress;
        bool compact, darkTheme;
        int lastCompleted, greetingIndex, greetingStarted, celebrationStarted;
        bool greeted, celebrating, artWasMoving;
        Region roundedRegion;

        public CompanionForm()
        {
            Text = "N\u1ebfp \u00b7 Living Desk";
            Name = "NepCompanion";
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = OwnFont(9F, FontStyle.Regular);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            ClientSize = new Size(240, 440);
            DoubleBuffered = true;
            Padding = new Padding(1);

            fullLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, Padding = new Padding(14, 11, 14, 12), Margin = new Padding(0), Tag = "card" };
            fullLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            fullLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            fullLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            for (int i = 2; i < 8; i++) fullLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(fullLayout);
            var title = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new Padding(0) };
            title.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            title.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            title.Controls.Add(DeskLabel("N\u1ebfp", OwnFont(13F, FontStyle.Bold), "ink"), 0, 0);
            var hide = DeskButton("\u00d7", false, true);
            hide.MinimumSize = new Size(24, 24); hide.Padding = new Padding(0); hide.Margin = new Padding(0);
            hide.TabStop = false; hide.AccessibleName = "Hide companion";
            hide.Click += delegate { cfg.CompanionVisible = false; ChangeDeskPreferences(); };
            title.Controls.Add(hide, 1, 0);
            fullLayout.Controls.Add(title, 0, 0);
            avatar = new CompanionAvatar(this, false) { Dock = DockStyle.Fill, Margin = new Padding(0), MinimumSize = new Size(80, 84), AccessibleName = "Greet Nep" };
            avatar.Click += delegate { Greet(); };
            fullLayout.Controls.Add(avatar, 0, 1);
            greeting = DeskLabel("One thing at a time.", Font, "muted");
            greeting.Dock = DockStyle.Fill; greeting.TextAlign = ContentAlignment.MiddleCenter;
            greeting.Margin = new Padding(0, 0, 0, 7);
            fullLayout.Controls.Add(greeting, 0, 2);
            var timerRow = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 6) };
            timerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
            timerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
            time = DeskLabel("25:00", OwnFont(23F, FontStyle.Regular), "ink");
            time.Name = "CompanionClock";
            timerRow.Controls.Add(time, 0, 0);
            status = DeskLabel("Ready", OwnFont(8F, FontStyle.Regular), "accent");
            status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleRight;
            timerRow.Controls.Add(status, 1, 0);
            fullLayout.Controls.Add(timerRow, 0, 3);
            progress = new DeskProgress { Dock = DockStyle.Top, Height = 5, Margin = new Padding(0, 0, 0, 9) };
            fullLayout.Controls.Add(progress, 0, 4);
            var actions = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 2, Margin = new Padding(0) };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            toggle = DeskButton("Start focus", true, true); toggle.Dock = DockStyle.Fill; toggle.TabStop = false; toggle.Margin = new Padding(0, 0, 0, 6);
            toggle.Name = "CompanionFocusToggle";
            actions.Controls.Add(toggle, 0, 0); actions.SetColumnSpan(toggle, 2);
            reset = DeskButton("Reset", false, true); reset.Dock = DockStyle.Fill; reset.TabStop = false; reset.Margin = new Padding(0, 0, 3, 0);
            finish = DeskButton("Finish", false, true); finish.Dock = DockStyle.Fill; finish.TabStop = false; finish.Margin = new Padding(3, 0, 0, 0);
            actions.Controls.Add(reset, 0, 1); actions.Controls.Add(finish, 1, 1);
            fullLayout.Controls.Add(actions, 0, 5);
            count = DeskLabel("0 sessions grown", OwnFont(8F, FontStyle.Regular), "muted");
            count.Dock = DockStyle.Fill; count.TextAlign = ContentAlignment.MiddleCenter; count.Margin = new Padding(0, 8, 0, 6);
            fullLayout.Controls.Add(count, 0, 6);
            var open = DeskButton("Open Scene Studio", false, true); open.Dock = DockStyle.Fill; open.Margin = new Padding(0); open.TabStop = false;
            open.Click += delegate { ShowStudio(); };
            fullLayout.Controls.Add(open, 0, 7);

            compactAvatar = new CompanionAvatar(this, true) { Dock = DockStyle.Fill, Margin = new Padding(0), Visible = false, AccessibleName = "Open Scene Studio", Name = "CompactCompanion" };
            compactAvatar.Click += delegate { ShowStudio(); };
            Controls.Add(compactAvatar);
            toggle.Click += delegate { ToggleFocusSession(); };
            reset.Click += delegate { ResetFocusSession(); };
            finish.Click += delegate { CompleteFocusSession(); };
            lastCompleted = cfg == null ? 0 : cfg.CompletedSessions;
            DeskChanged += PreferencesChanged;
            darkTheme = !claudeDark;
            PreferencesChanged();
        }

        Font OwnFont(float size, FontStyle style)
        {
            var font = new Font("Segoe UI", size, style, GraphicsUnit.Point);
            ownedFonts.Add(font); return font;
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80 | 0x08000000; return cp; }
        }
        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (cfg != null) SetWindowDisplayAffinity(Handle, cfg.HideInCapture ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);
            int on = 1;
            DwmSetWindowAttribute(Handle, DWMWA_TRANSITIONS_FORCEDISABLED, ref on, sizeof(int));
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x21) { message.Result = new IntPtr(3); return; } // WM_MOUSEACTIVATE -> MA_NOACTIVATE
            base.WndProc(ref message);
        }

        internal bool NeedsFrames
        {
            get { return Visible && DeskMotionIsLive(); }
        }
        bool GreetingActive { get { return greeted && unchecked(Environment.TickCount - greetingStarted) < 5000; } }
        internal bool CelebrationActive { get { return celebrating && unchecked(Environment.TickCount - celebrationStarted) < 4200; } }

        internal void PlaceBeside(Rectangle target)
        {
            Rectangle work = Screen.FromRectangle(target).WorkingArea;
            float scale;
            using (var graphics = CreateGraphics()) scale = Math.Max(1F, graphics.DpiX / 96F);
            bool nextCompact;
            Rectangle place = CalculateCompanionBounds(target, work,
                new Size((int)(240 * scale), (int)(440 * scale)),
                new Size((int)(94 * scale), (int)(110 * scale)), out nextCompact);
            SetCompactLayout(nextCompact);
            if (Bounds != place) Bounds = place;
            if (!Visible) Show();
            SetWindowPos(Handle, IntPtr.Zero, place.X, place.Y, place.Width, place.Height,
                SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_NOOWNERZORDER);
        }

        // Tests can render either layout without showing or activating a native window.
        internal void SetCompactLayout(bool value)
        {
            if (compact == value) return;
            compact = value;
            fullLayout.Visible = !compact;
            compactAvatar.Visible = compact;
            if (compact) compactAvatar.BringToFront(); else fullLayout.BringToFront();
        }

        void PreferencesChanged()
        {
            if (IsDisposed || cfg == null) return;
            if (cfg.CompletedSessions > lastCompleted)
            {
                celebrating = true;
                celebrationStarted = Environment.TickCount;
            }
            lastCompleted = cfg.CompletedSessions;
            if (darkTheme != claudeDark)
            {
                darkTheme = claudeDark;
                PaintDeskControls(this, new DeskPalette(darkTheme));
            }
            if (IsHandleCreated) SetWindowDisplayAffinity(Handle, cfg.HideInCapture ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);
            avatar.Invalidate(); compactAvatar.Invalidate();
            RefreshFrame();
        }

        void Greet()
        {
            greeted = true; greetingStarted = Environment.TickCount; greetingIndex++;
            RefreshFrame();
            ChangeDeskPreferences();
        }

        internal void RefreshFrame()
        {
            if (cfg == null) return;
            int remaining = FocusSecondsRemaining();
            string display = DeskTimeText(remaining);
            if (time.Text != display) time.Text = display;
            string action = DeskFocusAction();
            if (toggle.Text != action) toggle.Text = action;
            bool has = FocusHasSession();
            status.Text = FocusRunning() ? "Focusing" : has ? "Paused" : "Ready";
            reset.Enabled = finish.Enabled = has;
            count.Text = cfg.CompletedSessions + (cfg.CompletedSessions == 1 ? " session grown" : " sessions grown");
            progress.Value = has ? 1 - remaining / (double)Math.Max(1, cfg.FocusMinutes * 60) : 0;
            if (CelebrationActive) greeting.Text = "A little progress. A new leaf.";
            else if (GreetingActive)
            {
                string[] greetings = { "One small step is still a step.", "Good to have you here.", "A breath, then the next thing.", "Your little corner is ready." };
                greeting.Text = greetings[greetingIndex % greetings.Length];
            }
            else greeting.Text = cfg.Mode == DeskMode.Relax ? "There is room to wander." : "One thing at a time.";
            bool moving = DeskMotionIsLive() || CelebrationActive;
            if (moving || artWasMoving) { avatar.Invalidate(); compactAvatar.Invalidate(); }
            artWasMoving = moving;
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            using (var path = DeskRoundedRectangle(new RectangleF(0, 0, ClientSize.Width, ClientSize.Height), 16))
            {
                var next = new Region(path);
                Region = next;
                var old = roundedRegion; roundedRegion = next;
                if (old != null) old.Dispose();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Width < 2 || Height < 2) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(new DeskPalette(claudeDark).Line))
            using (var path = DeskRoundedRectangle(new RectangleF(0.5F, 0.5F, Width - 1, Height - 1), 16))
                e.Graphics.DrawPath(pen, path);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) DeskChanged -= PreferencesChanged;
            base.Dispose(disposing);
            if (disposing)
            {
                if (roundedRegion != null) { roundedRegion.Dispose(); roundedRegion = null; }
                foreach (var font in ownedFonts) font.Dispose();
                ownedFonts.Clear();
            }
        }
    }

    sealed class CompanionAvatar : Control
    {
        readonly CompanionForm owner;
        readonly bool compact;
        public CompanionAvatar(CompanionForm owner, bool compact)
        {
            this.owner = owner; this.compact = compact;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            Cursor = Cursors.Hand; TabStop = false;
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x21) { message.Result = new IntPtr(3); return; }
            base.WndProc(ref message);
        }

        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Invalidate(); }
        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width <= 1 || Height <= 1 || cfg == null) return;
            var palette = new DeskPalette(claudeDark);
            e.Graphics.Clear(palette.Card);
            Point mouse = PointToClient(Control.MousePosition);
            var pointer = new PointF(Math.Max(0, Math.Min(1, mouse.X / (float)Math.Max(1, Width))),
                Math.Max(0, Math.Min(1, mouse.Y / (float)Math.Max(1, Height))));
            SceneFrame frame = CurrentSceneFrame(pointer);
            Rectangle art = new Rectangle(0, 0, Width, Math.Max(1, Height - (compact ? 22 : 0)));
            PaintNep(e.Graphics, art, frame, owner.CelebrationActive);
            if (compact)
            {
                using (var brush = new SolidBrush(palette.Ink))
                using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    e.Graphics.DrawString(FocusHasSession() ? DeskTimeText(FocusSecondsRemaining()) : "N\u1ebfp", Font, brush,
                        new RectangleF(2, Height - 24, Width - 4, 22), format);
            }
        }
    }

    // Nếp is a rice-shaped sprout friend drawn from paths, not a sprite or a borrowed character.
    // Reactions come only from the pointer, chosen mode and local focus session state.
    internal static void PaintNep(Graphics graphics, Rectangle bounds, SceneFrame frame, bool celebrate)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0 || frame == null) return;
        var state = graphics.Save();
        try
        {
            graphics.SetClip(bounds, CombineMode.Intersect);
            float scale = Math.Min(bounds.Width / 210F, bounds.Height / 154F);
            graphics.TranslateTransform(bounds.X + (bounds.Width - 210 * scale) / 2F, bounds.Y + (bounds.Height - 154 * scale) / 2F);
            graphics.ScaleTransform(scale, scale);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            double live = frame.Mode == DeskMode.Present || float.IsNaN(frame.Motion) || float.IsInfinity(frame.Motion)
                ? 0 : Math.Max(0, Math.Min(1, frame.Motion));
            double seconds = double.IsNaN(frame.Seconds) || double.IsInfinity(frame.Seconds) ? 0 : frame.Seconds % 86400;
            float breath = (float)Math.Sin(seconds * (frame.Mode == DeskMode.Relax ? 2.7 : 1.55)) * (float)live;
            float bounce = frame.Mode == DeskMode.Relax ? breath * 2.3F : breath * 0.7F;
            if (celebrate) bounce -= (float)Math.Abs(Math.Sin(seconds * 5)) * 6F * (float)live;
            var ink = Color.FromArgb(68, 78, 53);
            var green = Color.FromArgb(108, 144, 83);
            var mint = Color.FromArgb(173, 196, 122);
            using (var shadow = new SolidBrush(Color.FromArgb(frame.Dark ? 40 : 20, 45, 64, 43)))
                graphics.FillEllipse(shadow, 47, 133, 116, 13);
            using (var stalk = new Pen(Color.FromArgb(110, 145, 89), 2.6F))
            using (var leaf = new SolidBrush(mint))
            using (var pot = new SolidBrush(Color.FromArgb(172, 133, 96)))
            {
                graphics.DrawLine(stalk, 33, 130, 33, 112 - Math.Min(5, frame.CompletedSessions) * 3);
                graphics.FillEllipse(leaf, 23, 111 - Math.Min(5, frame.CompletedSessions) * 3, 11, 6);
                graphics.FillEllipse(leaf, 34, 105 - Math.Min(5, frame.CompletedSessions) * 3, 11, 6);
                graphics.FillPolygon(pot, new[] { new PointF(24, 128), new PointF(44, 128), new PointF(41, 142), new PointF(27, 142) });
                if (frame.CompletedSessions > 3) graphics.FillEllipse(leaf, 25, 116, 10, 5);
            }
            graphics.TranslateTransform(0, bounce);
            using (var feet = new SolidBrush(Color.FromArgb(204, 193, 153)))
            {
                graphics.FillEllipse(feet, 73, 125, 23, 13);
                graphics.FillEllipse(feet, 116, 125, 23, 13);
            }
            using (var body = new GraphicsPath())
            {
                body.AddBezier(56, 102, 48, 67, 73, 31, 102, 30);
                body.AddBezier(102, 30, 134, 24, 158, 58, 157, 96);
                body.AddBezier(157, 96, 164, 130, 139, 136, 107, 134);
                body.AddBezier(107, 134, 76, 137, 55, 130, 56, 102);
                body.CloseFigure();
                using (var fill = new LinearGradientBrush(new Rectangle(52, 29, 111, 110), Color.FromArgb(250, 242, 206), Color.FromArgb(228, 218, 177), 85F))
                using (var edge = new Pen(Color.FromArgb(185, 177, 138), 1.6F))
                { graphics.FillPath(fill, body); graphics.DrawPath(edge, body); }
            }
            using (var hand = new Pen(Color.FromArgb(229, 218, 178), 11F))
            {
                hand.StartCap = hand.EndCap = LineCap.Round;
                if (celebrate)
                {
                    graphics.DrawLine(hand, 61, 96, 44, 77);
                    graphics.DrawLine(hand, 153, 95, 170, 77);
                }
                else
                {
                    graphics.DrawLine(hand, 60, 99, 48, 108);
                    graphics.DrawLine(hand, 153, 99, 163, 109);
                }
            }
            using (var sprout = new Pen(green, 3F))
            using (var leaf = new SolidBrush(green))
            using (var lightLeaf = new SolidBrush(mint))
            {
                graphics.DrawBezier(sprout, 108, 34, 108, 26, 113, 23, 117, 19);
                using (var shape = new GraphicsPath())
                {
                    shape.AddBezier(112, 28, 94, 28, 89, 17, 89, 14);
                    shape.AddBezier(89, 14, 106, 12, 113, 20, 112, 28);
                    graphics.FillPath(leaf, shape);
                }
                using (var shape = new GraphicsPath())
                {
                    shape.AddBezier(113, 24, 114, 9, 127, 7, 134, 8);
                    shape.AddBezier(134, 8, 132, 22, 121, 28, 113, 24);
                    graphics.FillPath(lightLeaf, shape);
                }
                using (var scarf = new GraphicsPath())
                {
                    scarf.AddBezier(67, 114, 92, 122, 129, 123, 149, 113);
                    scarf.AddLine(149, 113, 146, 121);
                    scarf.AddBezier(146, 121, 120, 133, 88, 130, 68, 120);
                    scarf.CloseFigure(); graphics.FillPath(leaf, scarf);
                }
                graphics.FillPolygon(lightLeaf, new[] { new PointF(133, 120), new PointF(148, 135), new PointF(139, 136), new PointF(127, 122) });
            }
            bool resting = frame.Mode == DeskMode.Focus;
            float pointerX = float.IsNaN(frame.Pointer.X) || float.IsInfinity(frame.Pointer.X) ? 0.5F : Math.Max(0, Math.Min(1, frame.Pointer.X));
            float pointerY = float.IsNaN(frame.Pointer.Y) || float.IsInfinity(frame.Pointer.Y) ? 0.5F : Math.Max(0, Math.Min(1, frame.Pointer.Y));
            float dx = (pointerX - 0.5F) * 3.5F * (float)live;
            float dy = (pointerY - 0.5F) * 2.8F * (float)live;
            using (var eyes = new Pen(ink, 2.7F))
            using (var pupils = new SolidBrush(ink))
            using (var cheek = new SolidBrush(Color.FromArgb(110, 219, 150, 112)))
            {
                eyes.StartCap = eyes.EndCap = LineCap.Round;
                if (resting && !celebrate)
                {
                    graphics.DrawArc(eyes, 79, 82, 12, 8, 8, 164);
                    graphics.DrawArc(eyes, 122, 82, 12, 8, 8, 164);
                }
                else if (celebrate)
                {
                    graphics.DrawArc(eyes, 79, 84, 12, 9, 187, 166);
                    graphics.DrawArc(eyes, 122, 84, 12, 9, 187, 166);
                }
                else
                {
                    graphics.FillEllipse(pupils, 82 + dx, 83 + dy, 5, 8);
                    graphics.FillEllipse(pupils, 124 + dx, 83 + dy, 5, 8);
                }
                graphics.FillEllipse(cheek, 67, 95, 14, 6);
                graphics.FillEllipse(cheek, 134, 95, 14, 6);
                graphics.DrawArc(eyes, 101, 92, 12, 10, 17, 145);
            }
            if (celebrate || frame.Mode == DeskMode.Relax)
            {
                using (var sparkle = new Pen(Color.FromArgb(190, 160, 84), 1.8F))
                {
                    int amount = celebrate ? 5 : 1;
                    for (int i = 0; i < amount; i++)
                    {
                        float sx = i % 2 == 0 ? 39 - i * 2 : 174 + i * 2;
                        float sy = 40 + (i / 2) * 25 + (float)Math.Sin(seconds * 1.7 + i) * 2 * (float)live;
                        graphics.DrawLine(sparkle, sx - 4, sy, sx + 4, sy);
                        graphics.DrawLine(sparkle, sx, sy - 4, sx, sy + 4);
                    }
                }
            }
        }
        finally { graphics.Restore(state); }
    }
}
