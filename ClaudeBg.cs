using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Wic = System.Windows.Media.Imaging;
using Timer = System.Windows.Forms.Timer;

[assembly: AssemblyTitle("Claude background")]
[assembly: AssemblyProduct("Claude background")]
[assembly: AssemblyDescription("Shows an image behind the Claude desktop window. Unofficial.")]
[assembly: AssemblyCopyright("Copyright (c) 2026 NQK923, MIT License")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]
[assembly: AssemblyInformationalVersion("1.1.0")]

// Shows an image behind each Claude desktop window and makes those windows semi-transparent.
// Build with build.ps1 (ClaudeBg.exe), or run claude-bg.ps1. Settings live in settings.ini next to it.
public static class ClaudeBg
{
    const string RepoUrl = "https://github.com/NQK923/claude-desktop-background";

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder name, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int idx);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int idx, int v);
    [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] static extern bool GetLayeredWindowAttributes(IntPtr h, out uint key, out byte alpha, out uint flags);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr h, uint affinity);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr h);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);

    // Windows' own interface for virtual desktops (Win+Tab); a process may move only its own windows.
    [ComImport, Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IVirtualDesktopManager
    {
        [PreserveSig] int IsWindowOnCurrentVirtualDesktop(IntPtr hwnd, out int onCurrent);
        [PreserveSig] int GetWindowDesktopId(IntPtr hwnd, out Guid desktopId);
        [PreserveSig] int MoveWindowToDesktop(IntPtr hwnd, ref Guid desktopId);
    }
    [ComImport, Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a")] class VirtualDesktopManager { }

    const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
    const int WS_EX_LAYERED = 0x80000;
    const int WS_CAPTION_THICKFRAME = 0x00C40000; // Claude's main windows have both; its popups have neither
    const uint LWA_ALPHA = 0x2;
    const uint GW_HWNDNEXT = 2, GW_OWNER = 4, GA_ROOTOWNER = 3;
    const uint SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40, SWP_NOOWNERZORDER = 0x200;
    const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    const uint PW_RENDERFULLCONTENT = 2;
    const uint WDA_NONE = 0, WDA_EXCLUDEFROMCAPTURE = 0x11;
    const int WM_HOTKEY = 0x0312;
    const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000;
    const uint EVENT_SYSTEM_FOREGROUND = 0x3, EVENT_SYSTEM_MINIMIZESTART = 0x16, EVENT_SYSTEM_MINIMIZEEND = 0x17;
    const uint EVENT_OBJECT_DESTROY = 0x8001, EVENT_OBJECT_REORDER = 0x8004, EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    const uint EVENT_OBJECT_CLOAKED = 0x8017, EVENT_OBJECT_UNCLOAKED = 0x8018;
    const uint WINEVENT_SKIPOWNPROCESS = 0x2;

    // A second ClaudeBg.exe started with --toggle, --next or --restore posts this to the running one.
    const string HostTitle = "ClaudeBg host";
    const int WM_APP_COMMAND = 0x8000 + 1;
    const int CmdToggle = 1, CmdNext = 2, CmdExit = 3;

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValue = "ClaudeBg";

    static readonly int[] BlurFactors = { 1, 6, 12, 24 };
    // WebP, HEIC, AVIF and JPEG XL load when Windows has their codec (free in the Microsoft Store).
    static readonly string[] ImageExts = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".heic", ".avif", ".jxl" };
    static readonly string[] CodecExts = { ".webp", ".heic", ".avif", ".jxl" };
    const string ImageFilter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.heic;*.avif;*.jxl|All files|*.*";

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
        public bool HideInCapture;             // keep the image out of screenshots and screen sharing

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
                        case "hideInCapture": s.HideInCapture = v == "1" || v.ToLowerInvariant() == "true"; break;
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
                    "slideFolder=" + SlideFolder, "slideMinutes=" + SlideMinutes,
                    "hideInCapture=" + (HideInCapture ? "1" : "0") });
            }
            catch (IOException) { }
        }
    }

    // What every background window draws; ApplyLook copies the settings here and redraws them all.
    static Image bgImage;
    static float lookDim, lookAlignX = 0.5f, lookAlignY = 0.5f;
    static int lookBlur = 1;
    static Color lookTint = Color.Black;

    class BgForm : Form
    {
        Bitmap cache;    // image already placed, blurred and tinted for the current size
        bool cacheFast;  // drawn quickly during a resize; drawn properly once the size settles
        readonly Timer settle = new Timer { Interval = 150 };

        public BgForm()
        {
            settle.Tick += (s, e) => { settle.Stop(); if (cacheFast) DropCache(); };
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Black;
            DoubleBuffered = true;
        }

        public void DropCache()
        {
            if (cache != null) { cache.Dispose(); cache = null; }
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { settle.Dispose(); if (cache != null) cache.Dispose(); }
            base.Dispose(disposing);
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

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (bgImage == null || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            if (cache == null || cache.Size != ClientSize)
            {
                bool fast = cache != null; // the size changed: draw quickly now, properly when it settles
                if (cache != null) cache.Dispose();
                cache = Render(ClientSize, fast);
                cacheFast = fast;
                if (fast) { settle.Stop(); settle.Start(); }
            }
            e.Graphics.DrawImageUnscaled(cache, 0, 0);
        }

        static Bitmap Render(Size size, bool fast)
        {
            var img = bgImage;
            var quality = fast ? InterpolationMode.Bilinear : InterpolationMode.HighQualityBicubic;
            var offset = fast ? PixelOffsetMode.HighSpeed : PixelOffsetMode.HighQuality;
            var bmp = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            float s = Math.Max((float)size.Width / img.Width, (float)size.Height / img.Height);
            float w = img.Width * s, h = img.Height * s;
            float x = (size.Width - w) * lookAlignX, y = (size.Height - h) * lookAlignY;
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = quality;
                g.PixelOffsetMode = offset;
                if (lookBlur <= 1)
                    g.DrawImage(img, x, y, w, h);
                else
                {
                    // Cheap blur: draw small, then stretch back; flipped tiling keeps the edges from fading.
                    int sw = Math.Max(1, size.Width / lookBlur), sh = Math.Max(1, size.Height / lookBlur);
                    float k = (float)sw / size.Width;
                    using (var small = new Bitmap(sw, sh, PixelFormat.Format32bppPArgb))
                    using (var attrs = new ImageAttributes())
                    {
                        using (var gs = Graphics.FromImage(small))
                        {
                            gs.InterpolationMode = quality;
                            gs.PixelOffsetMode = offset;
                            gs.DrawImage(img, x * k, y * k, w * k, h * k);
                        }
                        attrs.SetWrapMode(WrapMode.TileFlipXY);
                        g.DrawImage(small, new Rectangle(0, 0, size.Width, size.Height), 0, 0, sw, sh, GraphicsUnit.Pixel, attrs);
                    }
                }
                using (var b = new SolidBrush(Color.FromArgb((int)(lookDim * 255), lookTint)))
                    g.FillRectangle(b, 0, 0, size.Width, size.Height);
            }
            return bmp;
        }
    }

    // Receives Ctrl+Alt+B and the commands of a second ClaudeBg.exe; found by its title.
    class HostWindow : NativeWindow
    {
        public HostWindow() { CreateHandle(new CreateParams { Caption = HostTitle }); }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY) Toggle();
            else if (m.Msg == WM_APP_COMMAND)
            {
                int c = (int)m.WParam;
                if (c == CmdToggle) Toggle();
                else if (c == CmdNext) NextSlide();
                else if (c == CmdExit) Application.Exit();
            }
            base.WndProc(ref m);
        }
    }

    // One Claude window and the background window kept under it.
    class Attachment
    {
        public IntPtr Target;
        public uint Pid;
        public int OriginalExStyle;
        public int AppliedAlpha = -1;
        public Rectangle Last = Rectangle.Empty;
        public Guid Desktop = Guid.Empty;
        public BgForm Form;
    }

    static Settings cfg;
    static string appDir;
    static HostWindow host;
    static NotifyIcon tray;
    static ToolStripMenuItem enabledItem;
    static Timer slideTimer;
    static string currentImage;
    static readonly Random rng = new Random();
    static SynchronizationContext ui;
    static IVirtualDesktopManager desktops; // null where Windows has none

    static readonly Dictionary<IntPtr, Attachment> attached = new Dictionary<IntPtr, Attachment>();
    static readonly Dictionary<uint, IntPtr[]> pidHooks = new Dictionary<uint, IntPtr[]>();
    static readonly Dictionary<uint, bool> claudePids = new Dictionary<uint, bool>(); // pid -> is claude.exe
    static bool enabled = true;
    static bool claudeDark;
    static int lastThemeCheck = Environment.TickCount - 60000;
    static string logPath;

    static readonly WinEventProc winEventProc = OnWinEvent; // held in a field so the GC never frees it
    static readonly List<IntPtr> globalHooks = new List<IntPtr>();

    static bool IsClaudePid(uint pid)
    {
        bool yes;
        if (claudePids.TryGetValue(pid, out yes)) return yes;
        try { using (var p = Process.GetProcessById((int)pid)) yes = p.ProcessName.Equals("claude", StringComparison.OrdinalIgnoreCase); }
        catch (ArgumentException) { yes = false; } // already gone
        claudePids[pid] = yes;
        return yes;
    }

    // Claude's main windows: visible, unowned Chromium windows of claude.exe with a caption and a resize frame.
    // Matching on the class and style instead of the title keeps working when the title changes.
    static List<IntPtr> FindClaudeWindows()
    {
        var found = new List<IntPtr>();
        var cls = new StringBuilder(64);
        EnumWindows((h, l) =>
        {
            if (!IsWindowVisible(h) || GetWindow(h, GW_OWNER) != IntPtr.Zero) return true;
            if ((GetWindowLong(h, GWL_STYLE) & WS_CAPTION_THICKFRAME) != WS_CAPTION_THICKFRAME) return true;
            cls.Length = 0;
            GetClassName(h, cls, cls.Capacity);
            if (cls.ToString() != "Chrome_WidgetWin_1") return true;
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            if (IsClaudePid(pid)) found.Add(h);
            return true;
        }, IntPtr.Zero);
        return found;
    }

    // Makes Claude's windows opaque again when no instance is running to do it (it was killed).
    static void RestoreClaude()
    {
        foreach (var h in FindClaudeWindows())
        {
            SetLayeredWindowAttributes(h, 0, 255, LWA_ALPHA);
            SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE) & ~WS_EX_LAYERED);
        }
    }

    // Claude's own theme, from the most common colour of its window content; null when it cannot be read.
    static bool? ClaudeDark(IntPtr target)
    {
        RECT r;
        if (DwmGetWindowAttribute(target, DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf(typeof(RECT))) != 0) return null;
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        if (w < 40 || h < 40) return null;
        using (var b = new Bitmap(w, h))
        {
            using (var g = Graphics.FromImage(b))
            {
                var dc = g.GetHdc();
                bool ok = PrintWindow(target, dc, PW_RENDERFULLCONTENT);
                g.ReleaseHdc(dc);
                if (!ok) return null;
            }
            var counts = new Dictionary<int, int>();
            for (int y = h / 20; y < h; y += h / 20)
                for (int x = w / 40; x < w; x += w / 40)
                {
                    int c = b.GetPixel(x, y).ToArgb() & 0xFFFFFF, n;
                    counts.TryGetValue(c, out n);
                    counts[c] = n + 1;
                }
            int top = counts.OrderByDescending(kv => kv.Value).First().Key;
            if (top == 0) return null; // pure black: the capture failed
            return Color.FromArgb(top).GetBrightness() < 0.5f;
        }
    }

    // Reading Claude's window costs a full capture, so it happens on attach, when Claude comes to the
    // front, when Windows' theme changes, and once a minute; never more than once every 15 seconds.
    static void CheckTheme(bool force)
    {
        if (!force && unchecked(Environment.TickCount - lastThemeCheck) < 15000) return;
        var a = attached.Values.FirstOrDefault(x => IsWindowVisible(x.Target) && !IsIconic(x.Target));
        if (a == null || !enabled) return;
        lastThemeCheck = Environment.TickCount;
        var dark = ClaudeDark(a.Target);
        if (dark.HasValue && dark.Value != claudeDark)
        {
            claudeDark = dark.Value;
            ApplyLook();
        }
    }

    static void Log(string message)
    {
        try { File.AppendAllText(logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + message + Environment.NewLine); }
        catch (Exception) { }
    }

    static void Attach(IntPtr h)
    {
        var a = new Attachment { Target = h, Form = new BgForm() };
        GetWindowThreadProcessId(h, out a.Pid);
        a.OriginalExStyle = GetWindowLong(h, GWL_EXSTYLE);
        SetWindowLong(h, GWL_EXSTYLE, a.OriginalExStyle | WS_EX_LAYERED);
        var _ = a.Form.Handle;
        ApplyCaptureAffinity(a);
        attached[h] = a;
        // Move, resize, show, hide, z-order, desktop and close of Claude's windows arrive as events: no polling.
        if (!pidHooks.ContainsKey(a.Pid))
            pidHooks[a.Pid] = new[] {
                SetWinEventHook(EVENT_OBJECT_DESTROY, EVENT_OBJECT_REORDER, IntPtr.Zero, winEventProc, a.Pid, 0, 0),
                SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, winEventProc, a.Pid, 0, 0),
                SetWinEventHook(EVENT_OBJECT_CLOAKED, EVENT_OBJECT_UNCLOAKED, IntPtr.Zero, winEventProc, a.Pid, 0, 0) };
        Sync(a);
        CheckTheme(true);
    }

    static void Detach(Attachment a)
    {
        attached.Remove(a.Target);
        if (IsWindow(a.Target))
        {
            SetLayeredWindowAttributes(a.Target, 0, 255, LWA_ALPHA);
            SetWindowLong(a.Target, GWL_EXSTYLE, a.OriginalExStyle);
        }
        if (!a.Form.IsDisposed) a.Form.Dispose();
        if (!attached.Values.Any(x => x.Pid == a.Pid))
        {
            IntPtr[] hooks;
            if (pidHooks.TryGetValue(a.Pid, out hooks)) foreach (var hook in hooks) UnhookWinEvent(hook);
            pidHooks.Remove(a.Pid);
        }
    }

    static void DetachAll()
    {
        foreach (var a in attached.Values.ToList()) Detach(a);
    }

    static void OnWinEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (ev == EVENT_SYSTEM_FOREGROUND)
        {
            SyncAll();
            if (attached.ContainsKey(GetAncestor(hwnd, GA_ROOTOWNER))) CheckTheme(false);
            return;
        }
        Attachment a;
        if (idObject == 0 && attached.TryGetValue(hwnd, out a)) Sync(a);
    }

    static byte ToAlpha(double opacity) { return (byte)Math.Round(Math.Max(0.3, Math.Min(1.0, opacity)) * 255); }

    static void ApplyAlpha(Attachment a, int alpha)
    {
        if (alpha == a.AppliedAlpha) return;
        SetLayeredWindowAttributes(a.Target, 0, (byte)alpha, LWA_ALPHA);
        a.AppliedAlpha = alpha;
    }

    // Claude may rebuild its window (a DPI or monitor change, an update) and drop what was set on it.
    static void Verify(Attachment a)
    {
        int ex = GetWindowLong(a.Target, GWL_EXSTYLE);
        uint key, flags;
        byte alpha;
        if ((ex & WS_EX_LAYERED) == 0) SetWindowLong(a.Target, GWL_EXSTYLE, ex | WS_EX_LAYERED);
        if ((ex & WS_EX_LAYERED) == 0 || !GetLayeredWindowAttributes(a.Target, out key, out alpha, out flags) || alpha != a.AppliedAlpha)
            a.AppliedAlpha = -1;
    }

    static bool Focused(Attachment a)
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        var root = GetAncestor(fg, GA_ROOTOWNER);
        if (fg == a.Target || root == a.Target) return true;
        uint pid;
        GetWindowThreadProcessId(fg, out pid);
        return pid == a.Pid && !attached.ContainsKey(root); // a Claude popup that belongs to no other window
    }

    static void HideBg(Attachment a)
    {
        if (IsWindowVisible(a.Form.Handle)) ShowWindow(a.Form.Handle, 0);
    }

    static void ApplyCaptureAffinity(Attachment a)
    {
        SetWindowDisplayAffinity(a.Form.Handle, cfg.HideInCapture ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);
    }

    static void SyncAll()
    {
        foreach (var a in attached.Values.ToList()) Sync(a);
    }

    static void Sync(Attachment a)
    {
        if (!IsWindow(a.Target)) { Detach(a); return; }
        if (!enabled) { ApplyAlpha(a, 255); HideBg(a); return; }
        if (!IsWindowVisible(a.Target) || IsIconic(a.Target)) { HideBg(a); return; }

        ApplyAlpha(a, ToAlpha(Focused(a) ? cfg.Focused : cfg.Unfocused));

        RECT r;
        DwmGetWindowAttribute(a.Target, DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf(typeof(RECT)));
        var rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        var fh = a.Form.Handle;
        bool moved = rect != a.Last || !IsWindowVisible(fh);
        bool zWrong = GetWindow(a.Target, GW_HWNDNEXT) != fh;
        if (moved || zWrong)
        {
            SetWindowPos(fh, a.Target, rect.X, rect.Y, rect.Width, rect.Height,
                SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_NOOWNERZORDER);
            if (rect.Size != a.Last.Size) a.Form.Invalidate();
            a.Last = rect;
        }

        // Keep the background on the same virtual desktop as its Claude window.
        Guid desk;
        if (desktops != null && desktops.GetWindowDesktopId(a.Target, out desk) == 0 && desk != Guid.Empty && desk != a.Desktop
            && desktops.MoveWindowToDesktop(fh, ref desk) == 0)
            a.Desktop = desk;
    }

    static void Toggle()
    {
        enabled = !enabled;
        enabledItem.Checked = enabled;
        tray.Text = enabled ? "Claude background" : "Claude background (off)";
        SyncAll();
    }

    static bool WindowsDark()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
        {
            var v = key == null ? null : key.GetValue("AppsUseLightTheme");
            return v is int && (int)v == 0;
        }
    }

    static void ApplyLook()
    {
        lookDim = (float)(claudeDark ? cfg.DimDark : cfg.DimLight);
        lookBlur = BlurFactors[cfg.Blur];
        lookTint = cfg.Tint;
        lookAlignX = (float)cfg.AlignX;
        lookAlignY = (float)cfg.AlignY;
        foreach (var a in attached.Values) a.Form.DropCache();
    }

    // The largest screen: no window needs more image than that, so bigger photos are shrunk on load.
    static Size LargestScreen()
    {
        var s = Screen.AllScreens.OrderByDescending(x => (long)x.Bounds.Width * x.Bounds.Height).First().Bounds.Size;
        return new Size(Math.Max(s.Width, 640), Math.Max(s.Height, 480));
    }

    // Decodes with Windows Imaging Component, which also reads WebP (and HEIC with its extension),
    // shrinks it to cover the largest screen, and holds no lock on the file.
    static Bitmap LoadImage(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        Wic.BitmapSource src;
        try
        {
            var dec = Wic.BitmapDecoder.Create(new MemoryStream(bytes), Wic.BitmapCreateOptions.PreservePixelFormat, Wic.BitmapCacheOption.OnLoad);
            src = dec.Frames[0];
        }
        catch (Exception)
        {
            using (var img = Image.FromStream(new MemoryStream(bytes))) return Fit(img); // older GDI+ route
        }
        var screen = LargestScreen();
        double s = Math.Max((double)screen.Width / src.PixelWidth, (double)screen.Height / src.PixelHeight);
        if (s < 1) src = new Wic.TransformedBitmap(src, new System.Windows.Media.ScaleTransform(s, s));
        src = new Wic.FormatConvertedBitmap(src, System.Windows.Media.PixelFormats.Pbgra32, null, 0);
        var bmp = new Bitmap(src.PixelWidth, src.PixelHeight, PixelFormat.Format32bppPArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, bmp.PixelFormat);
        try { src.CopyPixels(System.Windows.Int32Rect.Empty, data.Scan0, data.Stride * data.Height, data.Stride); }
        finally { bmp.UnlockBits(data); }
        return bmp;
    }

    static Bitmap Fit(Image img)
    {
        var screen = LargestScreen();
        double s = Math.Min(1, Math.Max((double)screen.Width / img.Width, (double)screen.Height / img.Height));
        var bmp = new Bitmap(Math.Max(1, (int)(img.Width * s)), Math.Max(1, (int)(img.Height * s)), PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(img, 0, 0, bmp.Width, bmp.Height);
        }
        return bmp;
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
            Log("Could not load " + path + ": " + ex.Message);
            string ext = Path.GetExtension(path).ToLowerInvariant();
            string hint = CodecExts.Contains(ext)
                ? "\n\nWindows needs a codec for " + ext + " files: search the Microsoft Store for \"" + ext.Substring(1).ToUpperInvariant() + " Image Extensions\"."
                : "";
            if (loud) MessageBox.Show("Could not load the image:\n" + ex.Message + hint, "Claude background");
            return false;
        }
        var old = bgImage;
        bgImage = img;
        foreach (var a in attached.Values) a.Form.DropCache();
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

    // Start with Windows: a Run entry for ClaudeBg.exe; an older Startup-folder shortcut counts too.
    static string StartupShortcut()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Claude background.lnk");
    }

    static bool StartsWithWindows()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
            if (key != null && key.GetValue(RunValue) != null) return true;
        return File.Exists(StartupShortcut());
    }

    static void SetStartsWithWindows(bool on)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (on) key.SetValue(RunValue, "\"" + Application.ExecutablePath + "\"");
            else key.DeleteValue(RunValue, false);
        }
        if (!on && File.Exists(StartupShortcut())) File.Delete(StartupShortcut());
    }

    static bool RunningAsExe()
    {
        return Path.GetFileName(Application.ExecutablePath).Equals("ClaudeBg.exe", StringComparison.OrdinalIgnoreCase);
    }

    static string Version()
    {
        return ((AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
            typeof(ClaudeBg).Assembly, typeof(AssemblyInformationalVersionAttribute))).InformationalVersion;
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

    static ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        enabledItem = new ToolStripMenuItem("Enabled") { Checked = true, ShortcutKeyDisplayString = "Ctrl+Alt+B" };
        enabledItem.Click += (s, e) => Toggle();
        menu.Items.Add(enabledItem);
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Change image...", null, (s, e) =>
        {
            using (var dlg = new OpenFileDialog { Filter = ImageFilter })
            {
                if (dlg.ShowDialog() != DialogResult.OK || !TrySetImage(dlg.FileName, true)) return;
                // Keep a copy as bg.jpg (or bg.webp...) so the image survives the original moving.
                var copy = Path.Combine(appDir, "bg" + Path.GetExtension(dlg.FileName).ToLowerInvariant());
                try
                {
                    if (!string.Equals(Path.GetFullPath(dlg.FileName), Path.GetFullPath(copy), StringComparison.OrdinalIgnoreCase))
                        File.Copy(dlg.FileName, copy, true);
                    currentImage = cfg.Image = copy;
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
            v => { cfg.Focused = v; cfg.Save(); SyncAll(); }));
        menu.Items.Add(LevelMenu("Opacity while in another app", opacities, () => cfg.Unfocused,
            v => { cfg.Unfocused = v; cfg.Save(); SyncAll(); }));

        var dimMenu = LevelMenu("Image dim", new[] { 0.0, 0.2, 0.35, 0.5, 0.65, 0.8 },
            () => claudeDark ? cfg.DimDark : cfg.DimLight,
            v => { if (claudeDark) cfg.DimDark = v; else cfg.DimLight = v; cfg.Save(); ApplyLook(); });
        menu.Items.Add(dimMenu);

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

        var captureItem = new ToolStripMenuItem("Hide image in screenshots and screen sharing") { Checked = cfg.HideInCapture };
        captureItem.Click += (s, e) =>
        {
            cfg.HideInCapture = captureItem.Checked = !cfg.HideInCapture;
            cfg.Save();
            foreach (var a in attached.Values) ApplyCaptureAffinity(a);
        };
        menu.Items.Add(captureItem);

        var startupItem = new ToolStripMenuItem("Start with Windows");
        startupItem.Click += (s, e) =>
        {
            try { SetStartsWithWindows(!StartsWithWindows()); }
            catch (Exception ex) { MessageBox.Show("Could not change it:\n" + ex.Message, "Claude background"); }
        };
        menu.Items.Add(startupItem);

        menu.Opening += (s, e) =>
        {
            dimMenu.Text = "Image dim (Claude " + (claudeDark ? "dark" : "light") + " theme)";
            startupItem.Enabled = RunningAsExe();
            startupItem.Checked = StartsWithWindows();
        };
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Open settings folder", null, (s, e) => Process.Start("explorer.exe", "\"" + appDir + "\""));
        menu.Items.Add("About Claude background " + Version(), null, (s, e) =>
        {
            var answer = MessageBox.Show(
                "Claude background " + Version() + "\n\nUnofficial; not affiliated with Anthropic.\nMIT License.\n\nOpen the project page?\n" + RepoUrl,
                "About Claude background", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (answer == DialogResult.Yes) Process.Start(RepoUrl);
        });
        menu.Items.Add("Exit (restore Claude window)", null, (s, e) => Application.Exit());
        return menu;
    }

    // ClaudeBg.exe                 runs the background
    // ClaudeBg.exe --toggle|--next  sends that to the running one (for other hotkey tools or a Stream Deck)
    // ClaudeBg.exe --restore        exits the running one, or makes Claude opaque if none runs (it was killed)
    [STAThread]
    public static int Main(string[] args)
    {
        string arg = args.Length > 0 ? args[0].TrimStart('-', '/').ToLowerInvariant() : "";
        if (arg == "") { Run(AppDomain.CurrentDomain.BaseDirectory); return 0; }

        int cmd = arg == "toggle" ? CmdToggle : arg == "next" ? CmdNext : arg == "restore" || arg == "exit" ? CmdExit : 0;
        if (cmd == 0)
        {
            MessageBox.Show("Usage: ClaudeBg.exe [--toggle | --next | --restore]", "Claude background");
            return 2;
        }
        var running = FindWindow(null, HostTitle);
        if (running != IntPtr.Zero) PostMessage(running, WM_APP_COMMAND, (IntPtr)cmd, IntPtr.Zero);
        else if (cmd == CmdExit) RestoreClaude();
        return 0;
    }

    public static void Run(string dir)
    {
        bool first;
        var mutex = new Mutex(true, "Local\\ClaudeBg", out first);
        if (!first) return; // already running

        SetProcessDpiAwarenessContext(new IntPtr(-4)); // per-monitor v2, so rects are physical pixels
        Application.EnableVisualStyles();

        appDir = dir;
        logPath = Path.Combine(dir, "claude-bg.log");
        cfg = Settings.Load(dir);
        claudeDark = WindowsDark(); // until Claude's own window can be read
        try { desktops = (IVirtualDesktopManager)new VirtualDesktopManager(); }
        catch (Exception) { desktops = null; }

        host = new HostWindow();
        ui = new WindowsFormsSynchronizationContext();
        tray = new NotifyIcon { Text = "Claude background", Visible = true };
        tray.ContextMenuStrip = BuildMenu();
        tray.DoubleClick += (s, e) => Toggle();
        slideTimer = new Timer();
        slideTimer.Tick += (s, e) => NextSlide();

        if (cfg.SlideMinutes > 0 && SlideFiles().Length > 0) NextSlide();
        if (currentImage == null && !TrySetImage(cfg.Image, false)) TrySetImage(Path.Combine(dir, "bg.jpg"), true);
        ApplyLook();
        ConfigureSlideshow();

        if (!RegisterHotKey(host.Handle, 1, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, (uint)Keys.B))
            tray.ShowBalloonTip(5000, "Claude background",
                "Ctrl+Alt+B is taken by another app. Use the tray icon to turn the background on and off.", ToolTipIcon.Info);

        globalHooks.Add(SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, winEventProc, 0, 0, WINEVENT_SKIPOWNPROCESS));
        globalHooks.Add(SetWinEventHook(EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND, IntPtr.Zero, winEventProc, 0, 0, WINEVENT_SKIPOWNPROCESS));
        SystemEvents.UserPreferenceChanged += (s, e) =>
        {
            if (e.Category == UserPreferenceCategory.General) ui.Post(_ => CheckTheme(true), null);
        };

        // Every 2 seconds: attach Claude windows that opened, and check the attached ones for anything an
        // event missed. Claude's theme is read at most once a minute from here.
        var timer = new Timer { Interval = 2000 };
        timer.Tick += (s, e) =>
        {
            foreach (var h in FindClaudeWindows())
                if (!attached.ContainsKey(h)) Attach(h);
            foreach (var a in attached.Values.ToList())
            {
                if (IsWindow(a.Target) && enabled) Verify(a);
                Sync(a);
            }
            if (unchecked(Environment.TickCount - lastThemeCheck) > 60000) CheckTheme(false);
            if (claudePids.Count > 64) claudePids.Clear(); // forget pids of processes long gone
        };

        // Restore the Claude windows on every way out the process can still see.
        Application.ApplicationExit += (s, e) =>
        {
            DetachAll();
            timer.Stop();
            foreach (var hook in globalHooks) UnhookWinEvent(hook);
            tray.Visible = false;
        };
        Application.ThreadException += (s, e) => Log(e.Exception.ToString()); // keep running
        AppDomain.CurrentDomain.UnhandledException += (s, e) => { Log(e.ExceptionObject.ToString()); DetachAll(); };
        AppDomain.CurrentDomain.ProcessExit += (s, e) => DetachAll();
        SystemEvents.SessionEnding += (s, e) => Application.Exit();

        timer.Start();
        Application.Run();
        GC.KeepAlive(mutex);
    }
}
