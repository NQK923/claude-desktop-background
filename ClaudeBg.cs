using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

// Shows an image behind the Claude desktop window and makes that window semi-transparent.
// Build with build.ps1 (ClaudeBg.exe), or run claude-bg.ps1. Settings live in settings.ini next to it.
public static class ClaudeBg
{
    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int idx);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int idx, int v);
    [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr h);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);

    const int GWL_EXSTYLE = -20;
    const int WS_EX_LAYERED = 0x80000;
    const uint LWA_ALPHA = 0x2;
    const uint GW_HWNDNEXT = 2;
    const uint SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40, SWP_NOOWNERZORDER = 0x200;
    const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    const int WM_HOTKEY = 0x0312;
    const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000;
    const uint EVENT_SYSTEM_FOREGROUND = 0x3, EVENT_SYSTEM_MINIMIZESTART = 0x16, EVENT_SYSTEM_MINIMIZEEND = 0x17;
    const uint EVENT_OBJECT_DESTROY = 0x8001, EVENT_OBJECT_REORDER = 0x8004, EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    const uint WINEVENT_SKIPOWNPROCESS = 0x2;

    static readonly int[] BlurFactors = { 1, 6, 12, 24 };
    static readonly string[] ImageExts = { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };

    class Settings
    {
        public string File;
        public string Image;
        public double Focused = 0.88, Unfocused = 0.75, DimDark = 0.35, DimLight = 0.35;
        public int Blur;                       // index into BlurFactors
        public Color Tint = Color.Black;
        public double AlignX = 0.5, AlignY = 0.5;
        public string SlideFolder = "";
        public int SlideMinutes;               // 0 = slideshow off

        public static Settings Load(string dir)
        {
            var s = new Settings { File = Path.Combine(dir, "settings.ini"), Image = Path.Combine(dir, "bg.jpg") };
            if (!System.IO.File.Exists(s.File)) return s;
            foreach (var line in System.IO.File.ReadAllLines(s.File))
            {
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                double d;
                bool num = double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d);
                try
                {
                    switch (k)
                    {
                        case "image": if (v.Length > 0) s.Image = v; break;
                        case "opacity": if (num) s.Focused = s.Unfocused = d; break;  // older single value
                        case "dim": if (num) s.DimDark = s.DimLight = d; break;       // older single value
                        case "focused": if (num) s.Focused = d; break;
                        case "unfocused": if (num) s.Unfocused = d; break;
                        case "dimDark": if (num) s.DimDark = d; break;
                        case "dimLight": if (num) s.DimLight = d; break;
                        case "blur": if (num) s.Blur = Math.Max(0, Math.Min(BlurFactors.Length - 1, (int)d)); break;
                        case "tint": s.Tint = ColorTranslator.FromHtml(v); break;
                        case "alignX": if (num) s.AlignX = d; break;
                        case "alignY": if (num) s.AlignY = d; break;
                        case "slideFolder": s.SlideFolder = v; break;
                        case "slideMinutes": if (num) s.SlideMinutes = Math.Max(0, (int)d); break;
                    }
                }
                catch (Exception) { }
            }
            return s;
        }

        public void Save()
        {
            Func<double, string> f = x => x.ToString(CultureInfo.InvariantCulture);
            try
            {
                System.IO.File.WriteAllLines(File, new[] {
                    "image=" + Image,
                    "focused=" + f(Focused), "unfocused=" + f(Unfocused),
                    "dimDark=" + f(DimDark), "dimLight=" + f(DimLight),
                    "blur=" + Blur,
                    "tint=" + ColorTranslator.ToHtml(Tint),
                    "alignX=" + f(AlignX), "alignY=" + f(AlignY),
                    "slideFolder=" + SlideFolder, "slideMinutes=" + SlideMinutes });
            }
            catch (IOException) { }
        }
    }

    class BgForm : Form
    {
        public event Action Hotkey;

        Image img;
        float dim, alignX = 0.5f, alignY = 0.5f;
        int blurFactor = 1;
        Color tint = Color.Black;
        Bitmap cache; // image already placed, blurred and tinted for the current size

        public BgForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Black;
            DoubleBuffered = true;
        }

        public Image Img { get { return img; } set { img = value; DropCache(); } }

        public void SetLook(float dim, int blurFactor, Color tint, float alignX, float alignY)
        {
            this.dim = dim; this.blurFactor = blurFactor; this.tint = tint; this.alignX = alignX; this.alignY = alignY;
            DropCache();
        }

        void DropCache()
        {
            if (cache != null) { cache.Dispose(); cache = null; }
            Invalidate();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80 | 0x08000000; // WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && Hotkey != null) Hotkey();
            base.WndProc(ref m);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (img == null || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            if (cache == null || cache.Size != ClientSize)
            {
                if (cache != null) cache.Dispose();
                cache = Render(ClientSize);
            }
            e.Graphics.DrawImageUnscaled(cache, 0, 0);
        }

        Bitmap Render(Size size)
        {
            var bmp = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            float s = Math.Max((float)size.Width / img.Width, (float)size.Height / img.Height);
            float w = img.Width * s, h = img.Height * s;
            float x = (size.Width - w) * alignX, y = (size.Height - h) * alignY;
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                if (blurFactor <= 1)
                    g.DrawImage(img, x, y, w, h);
                else
                {
                    // Cheap blur: draw small, then stretch back; flipped tiling keeps the edges from fading.
                    int sw = Math.Max(1, size.Width / blurFactor), sh = Math.Max(1, size.Height / blurFactor);
                    float k = (float)sw / size.Width;
                    using (var small = new Bitmap(sw, sh, PixelFormat.Format32bppPArgb))
                    using (var attrs = new ImageAttributes())
                    {
                        using (var gs = Graphics.FromImage(small))
                        {
                            gs.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            gs.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            gs.DrawImage(img, x * k, y * k, w * k, h * k);
                        }
                        attrs.SetWrapMode(WrapMode.TileFlipXY);
                        g.DrawImage(small, new Rectangle(0, 0, size.Width, size.Height), 0, 0, sw, sh, GraphicsUnit.Pixel, attrs);
                    }
                }
                using (var b = new SolidBrush(Color.FromArgb((int)(dim * 255), tint)))
                    g.FillRectangle(b, 0, 0, size.Width, size.Height);
            }
            return bmp;
        }
    }

    static Settings cfg;
    static BgForm form;
    static NotifyIcon tray;
    static ToolStripMenuItem enabledItem;
    static Timer slideTimer;
    static string currentImage;
    static readonly Random rng = new Random();

    static IntPtr target = IntPtr.Zero;
    static uint targetPid;
    static int originalExStyle;
    static int appliedAlpha = -1;
    static bool enabled = true;
    static Rectangle last = Rectangle.Empty;

    static readonly WinEventProc winEventProc = OnWinEvent; // held in a field so the GC never frees it
    static readonly List<IntPtr> globalHooks = new List<IntPtr>();
    static readonly List<IntPtr> targetHooks = new List<IntPtr>();

    static IntPtr FindClaude()
    {
        foreach (var p in Process.GetProcessesByName("claude"))
            if (p.MainWindowHandle != IntPtr.Zero && p.MainWindowTitle == "Claude")
                return p.MainWindowHandle;
        return IntPtr.Zero;
    }

    static void Attach(IntPtr h)
    {
        target = h;
        GetWindowThreadProcessId(h, out targetPid);
        originalExStyle = GetWindowLong(h, GWL_EXSTYLE);
        SetWindowLong(h, GWL_EXSTYLE, originalExStyle | WS_EX_LAYERED);
        appliedAlpha = -1;
        last = Rectangle.Empty;
        // Move, resize, show, hide, z-order and close of Claude's window arrive as events: no polling.
        targetHooks.Add(SetWinEventHook(EVENT_OBJECT_DESTROY, EVENT_OBJECT_REORDER, IntPtr.Zero, winEventProc, targetPid, 0, 0));
        targetHooks.Add(SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, winEventProc, targetPid, 0, 0));
        Sync();
    }

    static void Detach()
    {
        foreach (var hook in targetHooks) UnhookWinEvent(hook);
        targetHooks.Clear();
        if (target != IntPtr.Zero && IsWindow(target))
        {
            SetLayeredWindowAttributes(target, 0, 255, LWA_ALPHA);
            SetWindowLong(target, GWL_EXSTYLE, originalExStyle);
        }
        target = IntPtr.Zero;
    }

    static void OnWinEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (ev == EVENT_SYSTEM_FOREGROUND || (hwnd == target && idObject == 0)) Sync();
    }

    static byte ToAlpha(double opacity) { return (byte)Math.Round(Math.Max(0.3, Math.Min(1.0, opacity)) * 255); }

    static void ApplyAlpha(int a)
    {
        if (a == appliedAlpha) return;
        SetLayeredWindowAttributes(target, 0, (byte)a, LWA_ALPHA);
        appliedAlpha = a;
    }

    static bool ClaudeFocused()
    {
        uint pid;
        var fg = GetForegroundWindow();
        return fg != IntPtr.Zero && GetWindowThreadProcessId(fg, out pid) != 0 && pid == targetPid;
    }

    static void HideBg()
    {
        if (IsWindowVisible(form.Handle)) ShowWindow(form.Handle, 0);
    }

    static void Sync()
    {
        if (target == IntPtr.Zero) return;
        if (!IsWindow(target)) { Detach(); HideBg(); return; }
        if (!enabled) { ApplyAlpha(255); HideBg(); return; }
        if (!IsWindowVisible(target) || IsIconic(target)) { HideBg(); return; }

        ApplyAlpha(ToAlpha(ClaudeFocused() ? cfg.Focused : cfg.Unfocused));

        RECT r;
        DwmGetWindowAttribute(target, DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf(typeof(RECT)));
        var rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        bool moved = rect != last || !IsWindowVisible(form.Handle);
        bool zWrong = GetWindow(target, GW_HWNDNEXT) != form.Handle;
        if (moved || zWrong)
        {
            SetWindowPos(form.Handle, target, rect.X, rect.Y, rect.Width, rect.Height,
                SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_NOOWNERZORDER);
            if (rect.Size != last.Size) form.Invalidate();
            last = rect;
        }
    }

    static void Toggle()
    {
        enabled = !enabled;
        enabledItem.Checked = enabled;
        tray.Text = enabled ? "Claude background" : "Claude background (off)";
        Sync();
    }

    static bool DarkMode()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
        {
            var v = key == null ? null : key.GetValue("AppsUseLightTheme");
            return v is int && (int)v == 0;
        }
    }

    static void ApplyLook()
    {
        form.SetLook((float)(DarkMode() ? cfg.DimDark : cfg.DimLight), BlurFactors[cfg.Blur], cfg.Tint,
            (float)cfg.AlignX, (float)cfg.AlignY);
    }

    // Reads the file into memory so it is not locked and can be replaced while running.
    static Bitmap LoadImage(string path)
    {
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var img = Image.FromStream(fs))
            return new Bitmap(img);
    }

    // Tray icon cut from the centre square of the background image.
    static Icon MakeIcon(Image img)
    {
        int side = Math.Min(img.Width, img.Height);
        using (var b = new Bitmap(32, 32))
        {
            using (var g = Graphics.FromImage(b))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(img, new Rectangle(0, 0, 32, 32),
                    new Rectangle((img.Width - side) / 2, (img.Height - side) / 2, side, side), GraphicsUnit.Pixel);
            }
            IntPtr h = b.GetHicon();
            var icon = (Icon)Icon.FromHandle(h).Clone();
            DestroyIcon(h);
            return icon;
        }
    }

    static bool TrySetImage(string path, bool loud)
    {
        Bitmap img;
        try { img = LoadImage(path); }
        catch (Exception ex)
        {
            if (loud) MessageBox.Show("Could not load the image:\n" + ex.Message, "Claude background");
            return false;
        }
        var old = form.Img;
        form.Img = img;
        if (old != null) old.Dispose();
        var oldIcon = tray.Icon;
        tray.Icon = MakeIcon(img);
        if (oldIcon != null) oldIcon.Dispose();
        currentImage = path;
        return true;
    }

    static string[] SlideFiles()
    {
        if (!Directory.Exists(cfg.SlideFolder)) return new string[0];
        return Directory.GetFiles(cfg.SlideFolder)
            .Where(f => ImageExts.Contains(Path.GetExtension(f).ToLowerInvariant())).ToArray();
    }

    static void NextSlide()
    {
        var choices = SlideFiles().Where(f => !string.Equals(f, currentImage, StringComparison.OrdinalIgnoreCase)).ToArray();
        for (int tries = 0; tries < 5 && choices.Length > 0; tries++)
            if (TrySetImage(choices[rng.Next(choices.Length)], false)) return;
    }

    static void ConfigureSlideshow()
    {
        slideTimer.Stop();
        if (cfg.SlideMinutes > 0 && SlideFiles().Length > 0)
        {
            slideTimer.Interval = cfg.SlideMinutes * 60000;
            slideTimer.Start();
        }
    }

    static ToolStripMenuItem ChoiceMenu(string title, string[] labels, Func<int, bool> isChecked, Action<int> choose)
    {
        var menu = new ToolStripMenuItem(title);
        for (int i = 0; i < labels.Length; i++)
        {
            int index = i;
            if (labels[i] == "-") { menu.DropDownItems.Add(new ToolStripSeparator()); continue; }
            var item = new ToolStripMenuItem(labels[i]);
            item.Click += (s, e) => choose(index);
            menu.DropDownItems.Add(item);
        }
        menu.DropDownOpening += (s, e) =>
        {
            for (int i = 0; i < menu.DropDownItems.Count; i++)
            {
                var item = menu.DropDownItems[i] as ToolStripMenuItem;
                if (item != null) item.Checked = isChecked(i);
            }
        };
        return menu;
    }

    static ToolStripMenuItem LevelMenu(string title, double[] levels, Func<double> get, Action<double> set)
    {
        var labels = levels.Select(v => (int)Math.Round(v * 100) + "%").ToArray();
        return ChoiceMenu(title, labels, i => Math.Abs(get() - levels[i]) < 0.001, i => set(levels[i]));
    }

    static ContextMenuStrip BuildMenu(string dir)
    {
        var menu = new ContextMenuStrip();

        enabledItem = new ToolStripMenuItem("Enabled") { Checked = true, ShortcutKeyDisplayString = "Ctrl+Alt+B" };
        enabledItem.Click += (s, e) => Toggle();
        menu.Items.Add(enabledItem);
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Change image...", null, (s, e) =>
        {
            using (var dlg = new OpenFileDialog { Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All files|*.*" })
            {
                if (dlg.ShowDialog() != DialogResult.OK || !TrySetImage(dlg.FileName, true)) return;
                var bg = Path.Combine(dir, "bg.jpg");
                try
                {
                    if (!string.Equals(Path.GetFullPath(dlg.FileName), Path.GetFullPath(bg), StringComparison.OrdinalIgnoreCase))
                        File.Copy(dlg.FileName, bg, true);
                    currentImage = cfg.Image = bg;
                }
                catch (IOException) { cfg.Image = dlg.FileName; }
                cfg.SlideMinutes = 0;
                cfg.Save();
                ConfigureSlideshow();
            }
        });
        menu.Items.Add("Reload image", null, (s, e) => TrySetImage(currentImage, true));

        var slideLabels = new[] { "Choose folder...", "Next image", "-", "Off", "Every 5 minutes", "Every 15 minutes", "Every 30 minutes", "Every hour" };
        var slideMinutes = new[] { 0, 0, 0, 0, 5, 15, 30, 60 };
        menu.Items.Add(ChoiceMenu("Slideshow", slideLabels,
            i => i >= 3 && cfg.SlideMinutes == slideMinutes[i],
            i =>
            {
                if (i == 0)
                {
                    using (var dlg = new FolderBrowserDialog { SelectedPath = cfg.SlideFolder })
                    {
                        if (dlg.ShowDialog() != DialogResult.OK) return;
                        cfg.SlideFolder = dlg.SelectedPath;
                        if (cfg.SlideMinutes == 0) cfg.SlideMinutes = 15;
                    }
                    if (SlideFiles().Length == 0) MessageBox.Show("That folder has no images.", "Claude background");
                    else NextSlide();
                }
                else if (i == 1) NextSlide();
                else
                {
                    cfg.SlideMinutes = slideMinutes[i];
                    if (cfg.SlideMinutes > 0 && SlideFiles().Length == 0)
                        MessageBox.Show("Choose a folder of images first.", "Claude background");
                }
                cfg.Save();
                ConfigureSlideshow();
            }));

        menu.Items.Add(ChoiceMenu("Image position", new[] { "Left", "Center", "Right", "-", "Top", "Middle", "Bottom" },
            i => i < 3 ? Math.Abs(cfg.AlignX - i * 0.5) < 0.001 : i > 3 && Math.Abs(cfg.AlignY - (i - 4) * 0.5) < 0.001,
            i =>
            {
                if (i < 3) cfg.AlignX = i * 0.5; else cfg.AlignY = (i - 4) * 0.5;
                cfg.Save();
                ApplyLook();
            }));
        menu.Items.Add(new ToolStripSeparator());

        var opacities = new[] { 0.5, 0.6, 0.7, 0.75, 0.8, 0.85, 0.9, 0.95, 1.0 };
        menu.Items.Add(LevelMenu("Opacity while using Claude", opacities, () => cfg.Focused,
            v => { cfg.Focused = v; cfg.Save(); Sync(); }));
        menu.Items.Add(LevelMenu("Opacity while in another app", opacities, () => cfg.Unfocused,
            v => { cfg.Unfocused = v; cfg.Save(); Sync(); }));

        var dimMenu = LevelMenu("Image dim", new[] { 0.0, 0.2, 0.35, 0.5, 0.65, 0.8 },
            () => DarkMode() ? cfg.DimDark : cfg.DimLight,
            v => { if (DarkMode()) cfg.DimDark = v; else cfg.DimLight = v; cfg.Save(); ApplyLook(); });
        menu.Items.Add(dimMenu);
        menu.Opening += (s, e) => dimMenu.Text = "Image dim (" + (DarkMode() ? "dark" : "light") + " mode)";

        menu.Items.Add(ChoiceMenu("Blur", new[] { "Off", "Light", "Medium", "Strong" },
            i => cfg.Blur == i,
            i => { cfg.Blur = i; cfg.Save(); ApplyLook(); }));

        var tints = new[] { Color.Black, ColorTranslator.FromHtml("#3A0A0F"), ColorTranslator.FromHtml("#0A1430") };
        menu.Items.Add(ChoiceMenu("Tint", new[] { "Black", "Dark red", "Dark blue", "Custom..." },
            i => i < 3 ? cfg.Tint.ToArgb() == tints[i].ToArgb() : tints.All(t => t.ToArgb() != cfg.Tint.ToArgb()),
            i =>
            {
                if (i < 3) cfg.Tint = tints[i];
                else
                {
                    using (var dlg = new ColorDialog { Color = cfg.Tint, FullOpen = true })
                    {
                        if (dlg.ShowDialog() != DialogResult.OK) return;
                        cfg.Tint = dlg.Color;
                    }
                }
                cfg.Save();
                ApplyLook();
            }));
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Open settings folder", null, (s, e) => Process.Start("explorer.exe", "\"" + dir + "\""));
        menu.Items.Add("Exit (restore Claude window)", null, (s, e) => Application.Exit());
        return menu;
    }

    [STAThread]
    public static void Main()
    {
        Run(AppDomain.CurrentDomain.BaseDirectory);
    }

    public static void Run(string dir)
    {
        bool first;
        var mutex = new System.Threading.Mutex(true, "Local\\ClaudeBg", out first);
        if (!first) return; // already running

        SetProcessDpiAwarenessContext(new IntPtr(-4)); // per-monitor v2, so rects are physical pixels
        Application.EnableVisualStyles();

        cfg = Settings.Load(dir);
        form = new BgForm();
        tray = new NotifyIcon { Text = "Claude background", Visible = true };
        tray.ContextMenuStrip = BuildMenu(dir);
        tray.DoubleClick += (s, e) => Toggle();
        slideTimer = new Timer();
        slideTimer.Tick += (s, e) => NextSlide();

        if (cfg.SlideMinutes > 0 && SlideFiles().Length > 0) NextSlide();
        if (currentImage == null && !TrySetImage(cfg.Image, false)) TrySetImage(Path.Combine(dir, "bg.jpg"), true);
        ApplyLook();
        ConfigureSlideshow();

        var _ = form.Handle;
        form.Hotkey += Toggle;
        if (!RegisterHotKey(form.Handle, 1, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, (uint)Keys.B))
            tray.ShowBalloonTip(5000, "Claude background",
                "Ctrl+Alt+B is taken by another app. Use the tray icon to turn the background on and off.", ToolTipIcon.Info);

        globalHooks.Add(SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, winEventProc, 0, 0, WINEVENT_SKIPOWNPROCESS));
        globalHooks.Add(SetWinEventHook(EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND, IntPtr.Zero, winEventProc, 0, 0, WINEVENT_SKIPOWNPROCESS));
        SystemEvents.UserPreferenceChanged += (s, e) =>
        {
            if (e.Category == UserPreferenceCategory.General) form.BeginInvoke((Action)ApplyLook);
        };

        // Finds Claude when it opens; once attached, a slow safety net for any event that was missed.
        var timer = new Timer { Interval = 1000 };
        timer.Tick += (s, e) =>
        {
            if (target == IntPtr.Zero)
            {
                var h = FindClaude();
                if (h != IntPtr.Zero) Attach(h);
            }
            else Sync();
        };

        // Restore the Claude window on every way out the process can still see.
        Application.ApplicationExit += (s, e) =>
        {
            timer.Stop();
            foreach (var hook in globalHooks) UnhookWinEvent(hook);
            UnregisterHotKey(form.Handle, 1);
            Detach();
            tray.Visible = false;
        };
        Application.ThreadException += (s, e) => Application.Exit();
        AppDomain.CurrentDomain.UnhandledException += (s, e) => Detach();
        AppDomain.CurrentDomain.ProcessExit += (s, e) => Detach();
        SystemEvents.SessionEnding += (s, e) => Application.Exit();

        timer.Start();
        Application.Run();
        GC.KeepAlive(mutex);
    }
}
