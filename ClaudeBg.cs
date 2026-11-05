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
[assembly: AssemblyDescription("Living scenes, a focus companion and photos for the Claude desktop window. Unofficial.")]
[assembly: AssemblyCopyright("Copyright (c) 2026 NQK923, MIT License")]
[assembly: AssemblyVersion("1.5.0.0")]
[assembly: AssemblyFileVersion("1.5.0.0")]
[assembly: AssemblyInformationalVersion("1.5.0")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Tests")]

// Shows a living scene or image behind each Claude desktop window and makes it semi-transparent.
// Build with build.ps1 (ClaudeBg.exe), or run claude-bg.ps1. Settings live in settings.ini next to it.
public static partial class ClaudeBg
{
    const string RepoUrl = "https://github.com/NQK923/claude-desktop-background";

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct ANIMATIONINFO { public int Size, MinAnimate; }

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
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
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
    [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint action, uint param, ref int value, uint winIni);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfo")] static extern bool SystemParametersInfoAnimation(uint action, uint param, ref ANIMATIONINFO value, uint winIni);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] static extern bool GetProcessTimes(IntPtr p, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr p, uint flags, StringBuilder name, ref int size);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] static extern int DwmGetWindowAttributeInt(IntPtr h, int attr, out int value, int size);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);

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
    const int DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_TRANSITIONS_FORCEDISABLED = 3, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_BORDER_COLOR = 34;
    const int DWMWA_CLOAKED = 14;
    const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);
    const int DWMWCP_DONOTROUND = 1, DWMWCP_ROUND = 2;
    const uint SPI_GETANIMATION = 0x48, SPI_GETCLIENTAREAANIMATION = 0x1042;
    const int AnimationHoldMs = 300; // how long Windows' open, restore, maximize and snap animations run
    const uint PW_RENDERFULLCONTENT = 2;
    const uint WDA_NONE = 0, WDA_EXCLUDEFROMCAPTURE = 0x11;
    const int WM_HOTKEY = 0x0312;
    const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000;
    const uint EVENT_SYSTEM_FOREGROUND = 0x3, EVENT_SYSTEM_MINIMIZESTART = 0x16, EVENT_SYSTEM_MINIMIZEEND = 0x17;
    const uint EVENT_SYSTEM_MOVESIZESTART = 0xA, EVENT_SYSTEM_MOVESIZEEND = 0xB;
    const uint EVENT_OBJECT_DESTROY = 0x8001, EVENT_OBJECT_SHOW = 0x8002, EVENT_OBJECT_REORDER = 0x8004, EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    const uint EVENT_OBJECT_CLOAKED = 0x8017, EVENT_OBJECT_UNCLOAKED = 0x8018;
    const uint WINEVENT_SKIPOWNPROCESS = 0x2;
    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    // A second ClaudeBg.exe started with --toggle, --next or --restore posts this to the running one;
    // started with no command, it posts CmdAlready so the running one says where it is.
    const string HostTitle = "ClaudeBg host";
    const int WM_APP_COMMAND = 0x8000 + 1;
    const int CmdToggle = 1, CmdNext = 2, CmdExit = 3, CmdAlready = 4, CmdStudio = 5;

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
        public SceneKind Scene = SceneKind.RainyStudy;
        public DeskMode Mode = DeskMode.Focus;
        public double Motion = 0.45;
        public bool SceneAnimations = true;
        public bool CompanionVisible = true;
        public int FocusMinutes = 25;
        public int CompletedSessions;

        static double Limit(double value, double fallback, double min, double max)
        {
            return double.IsNaN(value) || double.IsInfinity(value) ? fallback : Math.Max(min, Math.Min(max, value));
        }

        static bool Boolean(string value, bool fallback)
        {
            if (value == "1") return true;
            if (value == "0") return false;
            bool parsed;
            return bool.TryParse(value, out parsed) ? parsed : fallback;
        }

        public void Normalize()
        {
            Focused = Limit(Focused, 0.88, 0.3, 1);
            Unfocused = Limit(Unfocused, 0.75, 0.3, 1);
            DimDark = Limit(DimDark, 0.35, 0, 1);
            DimLight = Limit(DimLight, 0.35, 0, 1);
            AlignX = Limit(AlignX, 0.5, 0, 1);
            AlignY = Limit(AlignY, 0.5, 0, 1);
            Blur = Math.Max(0, Math.Min(BlurFactors.Length - 1, Blur));
            SlideMinutes = Math.Max(0, Math.Min(int.MaxValue / 60000, SlideMinutes));
            Motion = Limit(Motion, 0.45, 0, 1);
            FocusMinutes = Math.Max(5, Math.Min(120, FocusMinutes));
            CompletedSessions = Math.Max(0, CompletedSessions);
            if (!Enum.IsDefined(typeof(SceneKind), Scene)) Scene = SceneKind.Image;
            if (!Enum.IsDefined(typeof(DeskMode), Mode)) Mode = DeskMode.Focus;
        }

        public static Settings Load(string dir)
        {
            var s = new Settings { File = Path.Combine(dir, "settings.ini"), Image = Path.Combine(dir, "bg.jpg") };
            if (!System.IO.File.Exists(s.File)) return s;
            // An upgrade keeps the user's photo and does not add a companion until they choose one.
            s.Scene = SceneKind.Image;
            s.CompanionVisible = false;
            string[] lines;
            try { lines = System.IO.File.ReadAllLines(s.File); }
            catch (Exception ex) { Log("Could not read " + s.File + ": " + ex.Message); return s; }
            foreach (var line in lines)
            {
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                double d;
                bool num = double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)
                    && !double.IsNaN(d) && !double.IsInfinity(d);
                try
                {
                    switch (k)
                    {
                        // Relative paths are next to settings.ini, not wherever Windows started the program.
                        case "image": if (v.Length > 0) s.Image = Path.Combine(dir, v); break;
                        case "opacity": if (num) s.Focused = s.Unfocused = d; break;  // older single value
                        case "dim": if (num) s.DimDark = s.DimLight = d; break;       // older single value
                        case "focused": if (num) s.Focused = d; break;
                        case "unfocused": if (num) s.Unfocused = d; break;
                        case "dimDark": if (num) s.DimDark = d; break;
                        case "dimLight": if (num) s.DimLight = d; break;
                        case "blur": if (num) s.Blur = (int)Math.Max(0, Math.Min(BlurFactors.Length - 1, d)); break;
                        case "tint": s.Tint = ColorTranslator.FromHtml(v); break;
                        case "alignX": if (num) s.AlignX = Math.Max(0, Math.Min(1, d)); break;
                        case "alignY": if (num) s.AlignY = Math.Max(0, Math.Min(1, d)); break;
                        case "slideFolder": s.SlideFolder = v.Length > 0 ? Path.Combine(dir, v) : ""; break;
                        case "slideMinutes": if (num) s.SlideMinutes = (int)Math.Max(0, Math.Min(int.MaxValue / 60000, d)); break;
                        case "hideInCapture": s.HideInCapture = Boolean(v, s.HideInCapture); break;
                        case "scene":
                            SceneKind scene;
                            if (Enum.TryParse<SceneKind>(v, true, out scene) && Enum.IsDefined(typeof(SceneKind), scene)) s.Scene = scene;
                            break;
                        case "mode":
                            DeskMode mode;
                            if (Enum.TryParse<DeskMode>(v, true, out mode) && Enum.IsDefined(typeof(DeskMode), mode)) s.Mode = mode;
                            break;
                        case "motion": if (num) s.Motion = d; break;
                        case "sceneAnimations": s.SceneAnimations = Boolean(v, s.SceneAnimations); break;
                        case "companionVisible": s.CompanionVisible = Boolean(v, s.CompanionVisible); break;
                        case "focusMinutes": if (num) s.FocusMinutes = (int)Math.Max(5, Math.Min(120, d)); break;
                        case "completedSessions": if (num) s.CompletedSessions = (int)Math.Max(0, Math.Min(int.MaxValue, d)); break;
                    }
                }
                catch (Exception) { }
            }
            s.Normalize();
            return s;
        }

        public void Save()
        {
            Normalize();
            Func<double, string> f = x => x.ToString(CultureInfo.InvariantCulture);
            // Written beside it first and then swapped in, so a crash or full disk never leaves half a file.
            string tmp = File + ".tmp";
            try
            {
                System.IO.File.WriteAllLines(tmp, new[] {
                    "image=" + Image,
                    "focused=" + f(Focused), "unfocused=" + f(Unfocused),
                    "dimDark=" + f(DimDark), "dimLight=" + f(DimLight),
                    "blur=" + Blur,
                    "tint=" + ColorTranslator.ToHtml(Tint),
                    "alignX=" + f(AlignX), "alignY=" + f(AlignY),
                    "slideFolder=" + SlideFolder, "slideMinutes=" + SlideMinutes,
                    "hideInCapture=" + (HideInCapture ? "1" : "0"),
                    "scene=" + Scene, "mode=" + Mode, "motion=" + f(Motion),
                    "sceneAnimations=" + (SceneAnimations ? "1" : "0"),
                    "companionVisible=" + (CompanionVisible ? "1" : "0"),
                    "focusMinutes=" + FocusMinutes, "completedSessions=" + CompletedSessions });
                if (System.IO.File.Exists(File)) System.IO.File.Replace(tmp, File, null);
                else System.IO.File.Move(tmp, File);
            }
            catch (Exception ex) { Log("Could not save " + File + ": " + ex.Message); }
            finally
            {
                try { if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp); }
                catch (Exception) { }
            }
        }
    }

    // What every background window draws; ApplyLook copies the settings here and redraws them all.
    static Image bgImage;
    static float lookDim, lookAlignX = 0.5f, lookAlignY = 0.5f;
    static int lookBlur = 1;
    static Color lookTint = Color.Black;

    class BgForm : Form
    {
        Bitmap cache;    // image already placed, blurred and tinted for the current canvas
        bool cacheFast;  // drawn quickly during a resize; drawn properly once the size settles
        readonly Timer settle = new Timer { Interval = 150 };

        // The image is laid out for the whole Claude window (the canvas); while Windows animates a size
        // change this window may cover only part of it, at Offset, so the image never shifts when it grows back.
        public Size Canvas;
        public Point Offset;

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

        // Appear and vanish at once, in step with the Claude window, instead of with DWM's own fade.
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int on = 1, none = DWMWA_COLOR_NONE;
            DwmSetWindowAttribute(Handle, DWMWA_TRANSITIONS_FORCEDISABLED, ref on, sizeof(int));
            // Rounded windows get a border from Windows 11; this one would cover the image's edge under Claude's.
            DwmSetWindowAttribute(Handle, DWMWA_BORDER_COLOR, ref none, sizeof(int));
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            var canvas = Canvas.IsEmpty ? ClientSize : Canvas;
            if (canvas.Width <= 0 || canvas.Height <= 0) return;
            if (cfg != null && cfg.Scene != SceneKind.Image)
            {
                var pointer = PointToClient(Cursor.Position);
                var frame = CurrentSceneFrame(new PointF((pointer.X + Offset.X) / (float)canvas.Width,
                    (pointer.Y + Offset.Y) / (float)canvas.Height));
                var state = e.Graphics.Save();
                try
                {
                    e.Graphics.TranslateTransform(-Offset.X, -Offset.Y);
                    ScenePainter.Paint(e.Graphics, new Rectangle(Point.Empty, canvas), frame);
                }
                finally { e.Graphics.Restore(state); }
                return;
            }
            if (bgImage == null) { e.Graphics.Clear(Color.Black); return; }
            if (cache == null || cache.Size != canvas)
            {
                bool fast = cache != null; // the size changed: draw quickly now, properly when it settles
                if (cache != null) cache.Dispose();
                cache = Render(canvas, fast);
                cacheFast = fast;
                if (fast) { settle.Stop(); settle.Start(); }
            }
            e.Graphics.DrawImageUnscaled(cache, -Offset.X, -Offset.Y);
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
                else if (c == CmdStudio) ShowStudio();
                else if (c == CmdAlready)
                    tray.ShowBalloonTip(5000, "Claude background is already running",
                        "Right-click this icon and choose Scene Studio.", ToolTipIcon.Info);
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
        public int Alpha = 255;        // what the Claude window has now
        public int Goal = 255;         // what it is fading to
        public int FadeFrom = 255;     // where the running fade started
        public int FadeStart;          // tick count it started at
        public bool Shown;             // false while hidden or minimized, so its next appearance animates
        public bool WasMinimized;      // how it went away, which decides how it comes back
        public bool Landing;           // appearing: opaque, image out of sight, until Windows' animation ends
        public bool Moving;            // inside a drag or resize
        public int HoldUntil;          // tick count: when Windows' animation ends
        public Rectangle HoldRect;     // where the image may be while a size change animates
        public Rectangle Last = Rectangle.Empty;   // where the Claude window was at the last sync
        public Rectangle Placed = Rectangle.Empty; // where its background window is
        public int Corners = -1;       // corner preference given to the background window
        public Guid Desktop = Guid.Empty;
        public bool Eligible;         // visible, not minimized/cloaked, and on the current virtual desktop
        public BgForm Form;
    }

    static Settings cfg;
    static string appDir;
    static HostWindow host;
    static NotifyIcon tray;
    static ToolStripMenuItem enabledItem;
    static Timer slideTimer;
    static Timer fadeTimer;  // runs only while a window is fading
    static Timer holdTimer;  // runs only while an animation is being waited out
    static string currentImage;
    static readonly Random rng = new Random();
    static SynchronizationContext ui;
    static IVirtualDesktopManager desktops; // null where Windows has none

    static readonly Dictionary<IntPtr, Attachment> attached = new Dictionary<IntPtr, Attachment>();
    static readonly Dictionary<uint, IntPtr[]> pidHooks = new Dictionary<uint, IntPtr[]>();
    // pid -> whether it is claude.exe, with the process's start time: Windows reuses pids, so an entry
    // counts only for the process that had the pid when it was made.
    struct PidInfo { public long Created; public bool IsClaude; }
    static readonly Dictionary<uint, PidInfo> claudePids = new Dictionary<uint, PidInfo>();
    static readonly StringBuilder nameBuf = new StringBuilder(1024);
    static bool enabled = true;
    static bool claudeDark;
    static int lastThemeCheck = Environment.TickCount - 60000;
    static string logPath;

    static readonly WinEventProc winEventProc = OnWinEvent; // held in a field so the GC never frees it
    static readonly List<IntPtr> globalHooks = new List<IntPtr>();

    static bool IsClaudePid(uint pid)
    {
        IntPtr p = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (p == IntPtr.Zero) return false; // already gone, or not ours to look at
        try
        {
            long created, exited, kernel, user;
            if (!GetProcessTimes(p, out created, out exited, out kernel, out user)) return false;
            PidInfo info;
            if (claudePids.TryGetValue(pid, out info) && info.Created == created) return info.IsClaude;
            nameBuf.Length = 0;
            int size = nameBuf.Capacity;
            info.Created = created;
            info.IsClaude = QueryFullProcessImageName(p, 0, nameBuf, ref size)
                && Path.GetFileName(nameBuf.ToString(0, size)).Equals("claude.exe", StringComparison.OrdinalIgnoreCase);
            claudePids[pid] = info;
            return info.IsClaude;
        }
        finally { CloseHandle(p); }
    }

    // Claude's main windows: visible, unowned Chromium windows of claude.exe with a caption and a resize frame.
    // Matching on the class and style instead of the title keeps working when the title changes.
    static readonly StringBuilder classBuf = new StringBuilder(64);

    static bool IsClaudeMain(IntPtr h)
    {
        if (!IsWindowVisible(h) || GetWindow(h, GW_OWNER) != IntPtr.Zero) return false;
        if ((GetWindowLong(h, GWL_STYLE) & WS_CAPTION_THICKFRAME) != WS_CAPTION_THICKFRAME) return false;
        classBuf.Length = 0;
        GetClassName(h, classBuf, classBuf.Capacity);
        if (classBuf.ToString() != "Chrome_WidgetWin_1") return false;
        uint pid;
        GetWindowThreadProcessId(h, out pid);
        return IsClaudePid(pid);
    }

    static List<IntPtr> FindClaudeWindows()
    {
        var found = new List<IntPtr>();
        EnumWindows((h, l) => { if (IsClaudeMain(h)) found.Add(h); return true; }, IntPtr.Zero);
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
        using (var b = new Bitmap(w, h, PixelFormat.Format32bppRgb))
        {
            using (var g = Graphics.FromImage(b))
            {
                var dc = g.GetHdc();
                bool ok = PrintWindow(target, dc, PW_RENDERFULLCONTENT);
                g.ReleaseHdc(dc);
                if (!ok) return null;
            }
            var data = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, b.PixelFormat);
            try { return DarkFromSamples(data.Scan0, data.Stride, w, h); }
            finally { b.UnlockBits(data); }
        }
    }

    // Votes a 40 x 20 grid of pixels for the most common colour (the page background) and says whether it
    // is dark; null for pure black, which is what a failed capture gives.
    static bool? DarkFromSamples(IntPtr scan0, int stride, int w, int h)
    {
        var counts = new Dictionary<int, int>();
        for (int y = h / 20; y < h; y += h / 20)
            for (int x = w / 40; x < w; x += w / 40)
            {
                int c = Marshal.ReadInt32(scan0, y * stride + x * 4) & 0xFFFFFF, n;
                counts.TryGetValue(c, out n);
                counts[c] = n + 1;
            }
        int top = counts.OrderByDescending(kv => kv.Value).First().Key;
        if (top == 0) return null;
        return Color.FromArgb(top).GetBrightness() < 0.5f;
    }

    // Reading Claude's window costs a full capture, so it happens on attach, when Claude comes to the
    // front, when Windows' theme changes, and once a minute; never more than once every 15 seconds.
    static void CheckTheme(bool force)
    {
        if (!force && unchecked(Environment.TickCount - lastThemeCheck) < 15000) return;
        var a = attached.Values.FirstOrDefault(x => VisibleClaudeTarget(x.Target));
        if (a == null || !enabled) return;
        lastThemeCheck = Environment.TickCount;
        bool? dark;
        try { dark = ClaudeDark(a.Target); }
        catch (Exception ex) { Log("Could not read Claude's theme: " + ex.Message); return; } // e.g. out of memory for a huge window
        if (dark.HasValue && dark.Value != claudeDark)
        {
            claudeDark = dark.Value;
            ApplyLook();
            NotifyDeskChanged();
        }
    }

    static void Log(string message)
    {
        if (logPath == null) return;
        try
        {
            // Kept small: past 1 MB the older half goes.
            var info = new FileInfo(logPath);
            if (info.Exists && info.Length > 1 << 20)
            {
                var lines = File.ReadAllLines(logPath);
                File.WriteAllLines(logPath, lines.Skip(lines.Length / 2));
            }
            File.AppendAllText(logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + message + Environment.NewLine);
        }
        catch (Exception) { }
    }

    // Makes the attached Claude windows opaque again with plain Win32 calls only, so it is safe from any
    // thread and while the process is going down; the background windows go with the process.
    static void RestoreTargets()
    {
        Attachment[] all;
        try { all = attached.Values.ToArray(); }
        catch (InvalidOperationException) { RestoreClaude(); return; } // changed under us on the UI thread
        foreach (var a in all)
            if (IsWindow(a.Target))
            {
                SetLayeredWindowAttributes(a.Target, 0, 255, LWA_ALPHA);
                SetWindowLong(a.Target, GWL_EXSTYLE, a.OriginalExStyle);
            }
    }

    static void Attach(IntPtr h)
    {
        var a = new Attachment { Target = h, Form = new BgForm() };
        GetWindowThreadProcessId(h, out a.Pid);
        a.OriginalExStyle = GetWindowLong(h, GWL_EXSTYLE);
        SetWindowLong(h, GWL_EXSTYLE, a.OriginalExStyle | WS_EX_LAYERED);
        WriteAlpha(a, 255); // starts opaque and fades in once its image is behind it
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
        UpdateTrayText();
        SyncDeskWindows();
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
        UpdateTrayText();
        SyncDeskWindows();
    }

    static void UpdateTrayText()
    {
        if (tray == null) return;
        tray.Text = !enabled ? "Claude background: off (Ctrl+Alt+B)"
            : attached.Count == 0 ? "Claude background: waiting for Claude"
            : "Claude background: on";
    }

    static void DetachAll()
    {
        foreach (var a in attached.Values.ToList()) Detach(a);
    }

    // Windows calls this from inside the message loop, where an exception would end the process instead
    // of reaching Application.ThreadException: log it and carry on.
    static void OnWinEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (deskClosing) return;
        try { HandleWinEvent(ev, hwnd, idObject); }
        catch (Exception ex) { Log(ex.ToString()); }
    }

    static void HandleWinEvent(uint ev, IntPtr hwnd, int idObject)
    {
        if (ev == EVENT_SYSTEM_FOREGROUND)
        {
            SyncAll();
            if (attached.ContainsKey(GetAncestor(hwnd, GA_ROOTOWNER))) CheckTheme(false);
            return;
        }
        if (idObject != 0) return;
        Attachment a;
        if (ev == EVENT_OBJECT_SHOW && !attached.ContainsKey(hwnd))
        {
            // A Claude window opening: attach now rather than at the next scan.
            if (IsClaudeMain(hwnd)) Attach(hwnd);
            return;
        }
        if (!attached.TryGetValue(hwnd, out a)) return;
        if (ev == EVENT_SYSTEM_MOVESIZESTART) a.Moving = true;
        else if (ev == EVENT_SYSTEM_MOVESIZEEND) a.Moving = false;
        Sync(a);
        SyncDeskWindows();
    }

    static byte ToAlpha(double opacity) { return (byte)Math.Round(Math.Max(0.3, Math.Min(1.0, opacity)) * 255); }

    // Honours Windows' "Animation effects" setting: windows fade in, maximize and snap with it.
    static bool Animations()
    {
        int on = 1;
        return !SystemParametersInfo(SPI_GETCLIENTAREAANIMATION, 0, ref on, 0) || on != 0;
    }

    // Minimizing and restoring animate under a setting of their own ("Animate windows when minimizing
    // and maximizing"), which can be off while the rest is on.
    static bool MinimizeAnimations()
    {
        var info = new ANIMATIONINFO { Size = Marshal.SizeOf(typeof(ANIMATIONINFO)), MinAnimate = 1 };
        return !SystemParametersInfoAnimation(SPI_GETANIMATION, (uint)info.Size, ref info, 0) || info.MinAnimate != 0;
    }

    static void WriteAlpha(Attachment a, int alpha)
    {
        SetLayeredWindowAttributes(a.Target, 0, (byte)alpha, LWA_ALPHA);
        a.Alpha = alpha;
    }

    const int FadeMs = 180;

    static void SetAlpha(Attachment a, int goal, bool fade)
    {
        if (a.Goal != goal)
        {
            // A new goal restarts the fade from wherever the window is now, so it never jumps.
            a.FadeFrom = a.Alpha;
            a.FadeStart = Environment.TickCount;
            a.Goal = goal;
        }
        if (a.Alpha == goal) return;
        if (!fade || !Animations()) WriteAlpha(a, goal);
        else if (fadeTimer == null) WriteAlpha(a, goal);
        else if (!fadeTimer.Enabled) fadeTimer.Start();
    }

    // Where a fade is after elapsed ms: eased out (cubic), so it starts quickly and settles gently, and it
    // takes the same time however the timer ticks.
    static int FadeAlpha(int from, int goal, int elapsed)
    {
        if (elapsed >= FadeMs) return goal;
        double t = 1 - Math.Max(0, elapsed) / (double)FadeMs;
        return from + (int)Math.Round((goal - from) * (1 - t * t * t));
    }

    // One frame of every running fade.
    static void FadeStep()
    {
        bool running = false;
        int now = Environment.TickCount;
        foreach (var a in attached.Values.ToList())
        {
            if (a.Alpha == a.Goal) continue;
            WriteAlpha(a, FadeAlpha(a.FadeFrom, a.Goal, unchecked(now - a.FadeStart)));
            if (a.Alpha == a.Goal) Sync(a); // a fade to opaque ends by hiding the image
            else running = true;
        }
        if (!running) fadeTimer.Stop();
    }

    // Claude may rebuild its window (a DPI or monitor change, an update) and drop what was set on it.
    static void Verify(Attachment a)
    {
        int ex = GetWindowLong(a.Target, GWL_EXSTYLE);
        uint key, flags;
        byte alpha;
        bool lost = (ex & WS_EX_LAYERED) == 0;
        if (lost) SetWindowLong(a.Target, GWL_EXSTYLE, ex | WS_EX_LAYERED);
        if (lost || !GetLayeredWindowAttributes(a.Target, out key, out alpha, out flags) || alpha != a.Alpha)
            WriteAlpha(a, a.Alpha);
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

    // Shows the background at place, just under its Claude window, with the image laid out for canvas.
    static void PlaceBg(Attachment a, Rectangle place, Rectangle canvas)
    {
        var fh = a.Form.Handle;
        var offset = new Point(place.X - canvas.X, place.Y - canvas.Y);
        bool redraw = a.Form.Canvas != canvas.Size || a.Form.Offset != offset || place.Size != a.Placed.Size;
        a.Form.Canvas = canvas.Size;
        a.Form.Offset = offset;
        if (place != a.Placed || !IsWindowVisible(fh) || GetWindow(a.Target, GW_HWNDNEXT) != fh)
            SetWindowPos(fh, a.Target, place.X, place.Y, place.Width, place.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_NOOWNERZORDER);
        if (redraw) a.Form.Invalidate();
        a.Placed = place;
    }

    // Windows 11 rounds the corners of Claude's window except when it is maximized or snapped; the
    // background follows, so no corner of the image shows outside Claude's. Windows 10 ignores it.
    static void MatchCorners(Attachment a, Rectangle rect)
    {
        var work = Screen.FromRectangle(rect).WorkingArea;
        int edges = (rect.Left == work.Left ? 1 : 0) + (rect.Right == work.Right ? 1 : 0)
                  + (rect.Top == work.Top ? 1 : 0) + (rect.Bottom == work.Bottom ? 1 : 0);
        int corners = IsZoomed(a.Target) || edges >= 2 ? DWMWCP_DONOTROUND : DWMWCP_ROUND;
        if (corners == a.Corners) return;
        DwmSetWindowAttribute(a.Form.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref corners, sizeof(int));
        a.Corners = corners;
    }

    static void ApplyCaptureAffinity(Attachment a)
    {
        SetWindowDisplayAffinity(a.Form.Handle, cfg.HideInCapture ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);
    }

    static void SyncAll()
    {
        foreach (var a in attached.Values.ToList()) Sync(a);
        SyncDeskWindows();
    }

    // The rule throughout: Claude shows the desktop through itself as little as Windows allows. The image
    // comes in behind an opaque window, which then fades to its opacity, and never shows outside it.
    static void Sync(Attachment a)
    {
        if (!IsWindow(a.Target)) { Detach(a); return; }
        bool minimized = IsIconic(a.Target);
        a.Eligible = VisibleClaudeTarget(a.Target);
        if (!a.Eligible)
        {
            // Hidden or minimized: opaque now, so its restore animation runs opaque too.
            a.Shown = false;
            a.WasMinimized = minimized;
            SetAlpha(a, 255, false);
            HideBg(a);
            return;
        }

        RECT r;
        if (DwmGetWindowAttribute(a.Target, DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf(typeof(RECT))) != 0)
        {
            a.Eligible = false;
            SetAlpha(a, 255, false);
            HideBg(a);
            return;
        }
        var rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            a.Eligible = false;
            SetAlpha(a, 255, false);
            HideBg(a);
            return;
        }
        var fh = a.Form.Handle;
        int now = Environment.TickCount;
        bool holding = unchecked(a.HoldUntil - now) > 0;

        if (!a.Shown)
        {
            // Opening and restoring animate in DWM while the window already reports where it ends, so an
            // image there would show around it: it stays opaque without one until the animation is over.
            // Without the animation there is nothing to wait for.
            a.Shown = true;
            bool animates = a.WasMinimized ? MinimizeAnimations() : Animations();
            a.Landing = animates;
            a.HoldUntil = animates ? now + AnimationHoldMs : now;
            holding = animates;
        }
        else if (rect.Size != a.Last.Size && !a.Moving && IsWindowVisible(fh) && Animations())
        {
            // Maximizing, restoring down and snapping animate a picture of the window taken before, with
            // the opacity it had then, between its old and new frame: making it opaque now would not show.
            // That picture always covers what both frames share, so the image stays just there meanwhile.
            a.HoldRect = Rectangle.Intersect(holding ? a.HoldRect : a.Placed, rect);
            a.HoldUntil = now + AnimationHoldMs;
            holding = true;
        }
        // A plain move does not animate: the image simply follows below.
        if (holding && holdTimer != null && !holdTimer.Enabled) holdTimer.Start();
        if (!holding) a.Landing = false;
        a.Last = rect;

        if (!enabled || !HasBackground() || cfg.Mode == DeskMode.Present || a.Moving || a.Landing)
        {
            // Present restores an opaque window immediately; drags and landings also remain opaque.
            SetAlpha(a, 255, cfg.Mode != DeskMode.Present && !a.Moving && !a.Landing);
            if (a.Alpha == 255) { HideBg(a); a.Placed = Rectangle.Empty; }
            return;
        }

        MatchCorners(a, rect);
        if (holding)
        {
            if (a.HoldRect.IsEmpty) { HideBg(a); a.Placed = Rectangle.Empty; }
            else PlaceBg(a, a.HoldRect, rect);
            return; // the opacity stays as it was, so the animation lands without a flash
        }
        PlaceBg(a, rect, rect);
        SetAlpha(a, ToAlpha(Focused(a) ? cfg.Focused : cfg.Unfocused), true);

        // Keep the background on the same virtual desktop as its Claude window.
        Guid desk;
        if (desktops != null && desktops.GetWindowDesktopId(a.Target, out desk) == 0 && desk != Guid.Empty && desk != a.Desktop
            && desktops.MoveWindowToDesktop(fh, ref desk) == 0)
            a.Desktop = desk;
    }

    static void Toggle()
    {
        enabled = !enabled;
        if (enabledItem != null) enabledItem.Checked = enabled;
        UpdateTrayText();
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
        if (cfg == null) return;
        lookDim = (float)(claudeDark ? cfg.DimDark : cfg.DimLight);
        lookBlur = BlurFactors[cfg.Blur];
        lookTint = cfg.Tint;
        lookAlignX = (float)cfg.AlignX;
        lookAlignY = (float)cfg.AlignY;
        foreach (var a in attached.Values) a.Form.DropCache();
        InvalidateDeskUi();
    }

    // The largest screen: no window needs more image than that, so bigger photos are shrunk on load.
    static Size LargestScreen()
    {
        var s = Screen.AllScreens.OrderByDescending(x => (long)x.Bounds.Width * x.Bounds.Height).First().Bounds.Size;
        return new Size(Math.Max(s.Width, 640), Math.Max(s.Height, 480));
    }

    // Decodes with Windows Imaging Component, which also reads WebP (and HEIC with its extension),
    // shrinks it to cover the largest screen, turns it the way the camera held it, and holds no lock on the file.
    static Bitmap LoadImage(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        Wic.BitmapSource src;
        int orientation;
        try
        {
            var dec = Wic.BitmapDecoder.Create(new MemoryStream(bytes), Wic.BitmapCreateOptions.PreservePixelFormat, Wic.BitmapCacheOption.OnLoad);
            src = dec.Frames[0];
            orientation = WicOrientation(src.Metadata as Wic.BitmapMetadata);
        }
        catch (Exception)
        {
            using (var img = Image.FromStream(new MemoryStream(bytes))) return Fit(img); // older GDI+ route
        }
        // Rotated a quarter turn, the image's width ends up as its height.
        bool turned = orientation >= 5;
        int w = turned ? src.PixelHeight : src.PixelWidth, h = turned ? src.PixelWidth : src.PixelHeight;
        var screen = LargestScreen();
        double s = Math.Max((double)screen.Width / w, (double)screen.Height / h);
        if (s < 1) src = new Wic.TransformedBitmap(src, new System.Windows.Media.ScaleTransform(s, s));
        src = new Wic.FormatConvertedBitmap(src, System.Windows.Media.PixelFormats.Pbgra32, null, 0);
        var bmp = new Bitmap(src.PixelWidth, src.PixelHeight, PixelFormat.Format32bppPArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, bmp.PixelFormat);
        try { src.CopyPixels(System.Windows.Int32Rect.Empty, data.Scan0, data.Stride * data.Height, data.Stride); }
        finally { bmp.UnlockBits(data); }
        Orient(bmp, orientation);
        return bmp;
    }

    // The EXIF orientation (1 to 8) phones and cameras write instead of turning the pixels; 1 when there is none.
    static int WicOrientation(Wic.BitmapMetadata meta)
    {
        if (meta == null) return 1;
        try
        {
            var v = meta.GetQuery("System.Photo.Orientation");
            int o = v is ushort ? (ushort)v : 1;
            return o >= 1 && o <= 8 ? o : 1;
        }
        catch (Exception) { return 1; } // formats without that metadata throw
    }

    static readonly RotateFlipType[] Orientations = {
        RotateFlipType.RotateNoneFlipNone, RotateFlipType.RotateNoneFlipNone, RotateFlipType.RotateNoneFlipX,
        RotateFlipType.Rotate180FlipNone, RotateFlipType.RotateNoneFlipY, RotateFlipType.Rotate90FlipX,
        RotateFlipType.Rotate90FlipNone, RotateFlipType.Rotate270FlipX, RotateFlipType.Rotate270FlipNone };

    static void Orient(Image img, int orientation)
    {
        if (orientation > 1 && orientation <= 8) img.RotateFlip(Orientations[orientation]);
    }

    static Bitmap Fit(Image img)
    {
        const int OrientationTag = 0x0112;
        if (img.PropertyIdList.Contains(OrientationTag))
        {
            var bytes = img.GetPropertyItem(OrientationTag).Value;
            if (bytes != null && bytes.Length >= 2) Orient(img, BitConverter.ToUInt16(bytes, 0));
        }
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

    // Bumped by every image change, so a slow load that finishes after a newer choice is thrown away.
    static int loadSeq;

    static bool TrySetImage(string path, bool loud)
    {
        loadSeq++;
        Bitmap img;
        try { img = LoadImage(path); }
        catch (Exception ex) { LoadFailed(path, ex, loud); return false; }
        ShowImage(img, path);
        return true;
    }

    // Decodes on a thread of its own and swaps the image in on the UI thread: a large photo or a HEIC
    // can take a second to decode, which would otherwise stall fades and drags. Used where nobody waits
    // on the result (slideshow, a new display); done, if given, gets whether it worked.
    static void SetImageAsync(string path, Action<bool> done)
    {
        int seq = ++loadSeq;
        var worker = new Thread(() =>
        {
            Bitmap img = null;
            Exception error = null;
            try { img = LoadImage(path); }
            catch (Exception ex) { error = ex; }
            try
            {
                ui.Post(_ =>
                {
                    if (deskClosing || seq != loadSeq) { if (img != null) img.Dispose(); return; } // a newer choice won
                    if (img != null) ShowImage(img, path); else LoadFailed(path, error, false);
                    if (done != null) done(img != null);
                }, null);
            }
            catch (Exception) { if (img != null) img.Dispose(); } // the program is closing: nowhere to show it
        });
        worker.IsBackground = true;
        worker.SetApartmentState(ApartmentState.STA); // WIC's codecs expect it
        worker.Start();
    }

    static void LoadFailed(string path, Exception ex, bool loud)
    {
        Log("Could not load " + path + ": " + ex.Message);
        if (!loud) return;
        string ext = Path.GetExtension(path).ToLowerInvariant();
        string hint = CodecExts.Contains(ext)
            ? "\n\nWindows needs a codec for " + ext + " files: search the Microsoft Store for \"" + ext.Substring(1).ToUpperInvariant() + " Image Extensions\"."
            : "";
        MessageBox.Show("Could not load the image:\n" + ex.Message + hint, "Claude background");
    }

    static void ShowImage(Bitmap img, string path)
    {
        var old = bgImage;
        bgImage = img;
        foreach (var a in attached.Values) a.Form.DropCache();
        if (old != null) old.Dispose();
        if (tray != null)
        {
            var oldIcon = tray.Icon;
            tray.Icon = MakeIcon(img);
            if (oldIcon != null) oldIcon.Dispose();
        }
        currentImage = path;
        SyncAll(); // the first image brings the backgrounds in
        InvalidateDeskUi();
    }

    static string[] SlideFiles()
    {
        if (cfg == null || string.IsNullOrEmpty(cfg.SlideFolder) || !Directory.Exists(cfg.SlideFolder)) return new string[0];
        try
        {
            return Directory.GetFiles(cfg.SlideFolder)
                .Where(f => ImageExts.Contains(Path.GetExtension(f).ToLowerInvariant())).ToArray();
        }
        catch (Exception ex) { Log("Could not list " + cfg.SlideFolder + ": " + ex.Message); return new string[0]; }
    }

    // The slideshow deals the folder like a shuffled deck: every image once before any comes again,
    // and a new round never starts with the image on screen. Files added or removed are picked up.
    static readonly List<string> slideDeck = new List<string>();

    static string TakeSlide()
    {
        var files = SlideFiles();
        if (files.Length == 0) return null;
        var present = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
        slideDeck.RemoveAll(f => !present.Contains(f));
        if (slideDeck.Count == 0)
        {
            slideDeck.AddRange(files.OrderBy(f => rng.Next()));
            if (slideDeck.Count > 1 && string.Equals(slideDeck[0], currentImage, StringComparison.OrdinalIgnoreCase))
            {
                slideDeck.Add(slideDeck[0]);
                slideDeck.RemoveAt(0);
            }
        }
        var next = slideDeck[0];
        slideDeck.RemoveAt(0);
        if (files.Length == 1 && string.Equals(next, currentImage, StringComparison.OrdinalIgnoreCase)) return null; // nothing new
        return next;
    }

    static void NextSlide()
    {
        if (cfg == null || cfg.Scene != SceneKind.Image || deskClosing) return;
        NextSlide(5);
    }

    static void NextSlide(int tries)
    {
        if (cfg == null || cfg.Scene != SceneKind.Image || deskClosing) return;
        var next = tries > 0 ? TakeSlide() : null;
        if (next != null) SetImageAsync(next, ok => { if (!ok) NextSlide(tries - 1); });
    }

    static void ConfigureSlideshow()
    {
        if (slideTimer == null) return;
        slideTimer.Stop();
        if (cfg != null && cfg.Scene == SceneKind.Image && cfg.SlideMinutes > 0 && SlideFiles().Length > 0)
        {
            slideTimer.Interval = (int)Math.Min(int.MaxValue, (long)cfg.SlideMinutes * 60000);
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
        menu.Items.Add("Scene Studio...", null, (s, e) => ShowStudio());
        var scenes = new[] { SceneKind.RainyStudy, SceneKind.Aquarium, SceneKind.NightTrain, SceneKind.Image };
        menu.Items.Add(ChoiceMenu("Scene", new[] { "Rainy Study", "Aquarium", "Night Train", "My image" },
            i => cfg.Scene == scenes[i], i => { cfg.Scene = scenes[i]; ChangeDeskPreferences(); }));
        menu.Items.Add(ChoiceMenu("Mode", new[] { "Focus", "Relax", "Present (opaque Claude)" },
            i => cfg.Mode == (DeskMode)i, i => { cfg.Mode = (DeskMode)i; ChangeDeskPreferences(); }));
        var companionItem = new ToolStripMenuItem("Show Sunny companion");
        companionItem.Click += (s, e) => { cfg.CompanionVisible = !cfg.CompanionVisible; ChangeDeskPreferences(); };
        menu.Items.Add(companionItem);
        var focusItem = new ToolStripMenuItem("Start focus session");
        focusItem.Click += (s, e) => ToggleFocusSession();
        menu.Items.Add(focusItem);
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
                catch (Exception) { cfg.Image = dlg.FileName; } // e.g. the folder is read-only
                cfg.SlideMinutes = 0;
                cfg.Scene = SceneKind.Image;
                ChangeDeskPreferences();
            }
        });
        menu.Items.Add("Reload image", null, (s, e) => { if (currentImage != null) TrySetImage(currentImage, true); });

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
                    else
                    {
                        cfg.Scene = SceneKind.Image;
                        ChangeDeskPreferences();
                        NextSlide();
                        return;
                    }
                }
                else if (i == 1) NextSlide();
                else
                {
                    cfg.SlideMinutes = slideMinutes[i];
                    if (cfg.SlideMinutes > 0 && SlideFiles().Length == 0)
                        MessageBox.Show("Choose a folder of images first.", "Claude background");
                    else if (cfg.SlideMinutes > 0) cfg.Scene = SceneKind.Image;
                }
                ChangeDeskPreferences();
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
            v => { cfg.Focused = v; ChangeDeskPreferences(); }));
        menu.Items.Add(LevelMenu("Opacity while in another app", opacities, () => cfg.Unfocused,
            v => { cfg.Unfocused = v; ChangeDeskPreferences(); }));

        var dimMenu = LevelMenu("Image dim", new[] { 0.0, 0.2, 0.35, 0.5, 0.65, 0.8 },
            () => claudeDark ? cfg.DimDark : cfg.DimLight,
            v => { if (claudeDark) cfg.DimDark = v; else cfg.DimLight = v; ChangeDeskPreferences(); });
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

        var captureItem = new ToolStripMenuItem("Hide background and companion in screen capture") { Checked = cfg.HideInCapture };
        captureItem.Click += (s, e) =>
        {
            cfg.HideInCapture = captureItem.Checked = !cfg.HideInCapture;
            cfg.Save();
            foreach (var a in attached.Values) ApplyCaptureAffinity(a);
            NotifyDeskChanged();
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
            companionItem.Checked = cfg.CompanionVisible;
            focusItem.Text = FocusRunning() ? "Pause focus session" : FocusHasSession() ? "Resume focus session" : "Start focus session";
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
    // ClaudeBg.exe --studio         opens Scene Studio, starting the app if necessary
    // ClaudeBg.exe --restore        exits the running one, or makes Claude opaque if none runs (it was killed)
    [STAThread]
    public static int Main(string[] args)
    {
        string arg = args.Length > 0 ? args[0].TrimStart('-', '/').ToLowerInvariant() : "";
        if (arg == "") { Run(AppDomain.CurrentDomain.BaseDirectory); return 0; }

        int cmd = arg == "toggle" ? CmdToggle : arg == "next" ? CmdNext : arg == "studio" ? CmdStudio
            : arg == "restore" || arg == "exit" ? CmdExit : 0;
        if (cmd == 0)
        {
            MessageBox.Show("Usage: ClaudeBg.exe [--studio | --toggle | --next | --restore]", "Claude background");
            return 2;
        }
        var running = FindWindow(null, HostTitle);
        if (running != IntPtr.Zero) PostMessage(running, WM_APP_COMMAND, (IntPtr)cmd, IntPtr.Zero);
        else if (cmd == CmdStudio) { requestStudioOnStart = true; Run(AppDomain.CurrentDomain.BaseDirectory); }
        else if (cmd == CmdExit) RestoreClaude();
        return 0;
    }

    // Settings, log and the copied image live next to the program, or in %LOCALAPPDATA%\ClaudeBg when
    // that folder cannot be written (Program Files, a read-only share).
    static string DataDir(string dir)
    {
        try
        {
            string probe = Path.Combine(dir, ".claude-bg-write-test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return dir;
        }
        catch (Exception)
        {
            string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudeBg");
            Directory.CreateDirectory(local);
            return local;
        }
    }

    public static void Run(string dir)
    {
        bool first;
        var mutex = new Mutex(true, "Local\\ClaudeBg", out first);
        if (!first)
        {
            // Already running: have that one point at its tray icon instead of doing nothing.
            var running = FindWindow(null, HostTitle);
            if (running != IntPtr.Zero) PostMessage(running, WM_APP_COMMAND,
                (IntPtr)(requestStudioOnStart ? CmdStudio : CmdAlready), IntPtr.Zero);
            mutex.Dispose();
            return;
        }

        // Per-monitor v2, so rects are physical pixels; Windows 10 before 1703 lacks it and falls back.
        try { SetProcessDpiAwarenessContext(new IntPtr(-4)); }
        catch (EntryPointNotFoundException) { }
        Application.EnableVisualStyles();

        appDir = DataDir(dir);
        logPath = Path.Combine(appDir, "claude-bg.log");
        bool firstRun = !File.Exists(Path.Combine(appDir, "settings.ini"));
        cfg = Settings.Load(appDir);
        claudeDark = WindowsDark(); // until Claude's own window can be read
        try { desktops = (IVirtualDesktopManager)new VirtualDesktopManager(); }
        catch (Exception) { desktops = null; }

        host = new HostWindow();
        ui = new WindowsFormsSynchronizationContext();
        tray = new NotifyIcon { Text = "Claude background", Icon = MakeDeskIcon(), Visible = true };
        tray.ContextMenuStrip = BuildMenu();
        tray.DoubleClick += (s, e) => Toggle();
        slideTimer = new Timer();
        // A visible eligible target is enough, including after the first image failed to load.
        slideTimer.Tick += (s, e) =>
        {
            if (enabled && cfg.Scene == SceneKind.Image && cfg.Mode != DeskMode.Present
                && attached.Values.Any(a => VisibleClaudeTarget(a.Target))) NextSlide();
        };
        fadeTimer = new Timer { Interval = 15 };
        fadeTimer.Tick += (s, e) => FadeStep();
        holdTimer = new Timer { Interval = 50 };
        holdTimer.Tick += (s, e) =>
        {
            SyncAll();
            int now = Environment.TickCount;
            if (!attached.Values.Any(a => unchecked(a.HoldUntil - now) > 0)) holdTimer.Stop();
        };
        UpdateTrayText();

        // At startup the first slide loads right here, so the fallbacks below know whether one did.
        if (cfg.Scene == SceneKind.Image && cfg.SlideMinutes > 0)
            for (int tries = 0; tries < 5 && currentImage == null; tries++)
            {
                var slide = TakeSlide();
                if (slide == null) break;
                TrySetImage(slide, false);
            }
        if (cfg.Scene == SceneKind.Image && currentImage == null)
        {
            // The saved image, else a bg.jpg next to the settings or the program. With none at all, Claude
            // stays opaque until one is chosen from the tray menu.
            var image = new[] { cfg.Image, Path.Combine(appDir, "bg.jpg"), Path.Combine(dir, "bg.jpg") }.FirstOrDefault(File.Exists);
            if (image != null) TrySetImage(image, true);
        }
        ApplyLook();
        ConfigureSlideshow();
        StartDeskRuntime();

        bool hotkey = RegisterHotKey(host.Handle, 1, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, (uint)Keys.B);
        if (!HasBackground())
            tray.ShowBalloonTip(8000, "Claude background needs an image",
                "Open Scene Studio to choose a living scene or your own image.", ToolTipIcon.Info);
        else if (firstRun)
            tray.ShowBalloonTip(8000, "Your Living Desk is ready",
                "Open Scene Studio to choose a scene and start a focus session" +
                (hotkey ? "; Ctrl+Alt+B turns it on and off." : "."), ToolTipIcon.Info);
        else if (!hotkey)
            tray.ShowBalloonTip(5000, "Claude background",
                "Ctrl+Alt+B is taken by another app. Use the tray icon to turn the background on and off.", ToolTipIcon.Info);
        if (firstRun) cfg.Save();

        globalHooks.Add(SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, winEventProc, 0, 0, WINEVENT_SKIPOWNPROCESS));
        globalHooks.Add(SetWinEventHook(EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZEEND, IntPtr.Zero, winEventProc, 0, 0, WINEVENT_SKIPOWNPROCESS));
        globalHooks.Add(SetWinEventHook(EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND, IntPtr.Zero, winEventProc, 0, 0, WINEVENT_SKIPOWNPROCESS));
        globalHooks.Add(SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, IntPtr.Zero, winEventProc, 0, 0, WINEVENT_SKIPOWNPROCESS));
        SystemEvents.UserPreferenceChanged += (s, e) =>
        {
            if (e.Category == UserPreferenceCategory.General) ui.Post(_ =>
            {
                if (deskClosing) return;
                UpdateDeskMotionPreference();
                CheckTheme(true);
            }, null);
        };
        // A bigger screen plugged in needs more of the image than was kept when it loaded.
        SystemEvents.DisplaySettingsChanged += (s, e) => ui.Post(_ =>
        {
            if (deskClosing) return;
            if (cfg.Scene == SceneKind.Image && currentImage != null && bgImage != null)
            {
                var screen = LargestScreen();
                if (bgImage.Width < screen.Width || bgImage.Height < screen.Height) SetImageAsync(currentImage, null);
            }
        }, null);

        // Every 2 seconds, and once right away: attach Claude windows that opened, and check the attached
        // ones for anything an event missed. Claude's theme is read at most once a minute from here.
        Action scan = () =>
        {
            foreach (var h in FindClaudeWindows())
                if (!attached.ContainsKey(h)) Attach(h);
            foreach (var a in attached.Values.ToList())
            {
                if (IsWindow(a.Target) && enabled) Verify(a);
                Sync(a);
            }
            SyncDeskWindows();
            if (unchecked(Environment.TickCount - lastThemeCheck) > 60000) CheckTheme(false);
            if (claudePids.Count > 64) claudePids.Clear(); // forget pids of processes long gone
        };
        var timer = new Timer { Interval = 2000 };
        timer.Tick += (s, e) => scan();

        // Restore the Claude windows on every way out the process can still see.
        Application.ApplicationExit += (s, e) =>
        {
            StopDeskRuntime();
            DetachAll();
            timer.Stop();
            timer.Dispose();
            slideTimer.Stop(); slideTimer.Dispose();
            fadeTimer.Stop(); fadeTimer.Dispose();
            holdTimer.Stop(); holdTimer.Dispose();
            foreach (var hook in globalHooks) UnhookWinEvent(hook);
            tray.Visible = false;
            var icon = tray.Icon;
            tray.Dispose();
            if (icon != null) icon.Dispose();
            if (bgImage != null) { bgImage.Dispose(); bgImage = null; }
            host.DestroyHandle();
        };
        Application.ThreadException += (s, e) => Log(e.Exception.ToString()); // keep running
        // These can run on another thread as the process ends, so they only make Claude opaque again.
        AppDomain.CurrentDomain.UnhandledException += (s, e) => { Log(e.ExceptionObject.ToString()); RestoreTargets(); };
        AppDomain.CurrentDomain.ProcessExit += (s, e) => RestoreTargets();
        SystemEvents.SessionEnding += (s, e) => { RestoreTargets(); ui.Post(_ => Application.Exit(), null); };

        scan();
        timer.Start();
        if (firstRun || requestStudioOnStart) ui.Post(_ => { if (!deskClosing) ShowStudio(); }, null);
        Application.Run();
        GC.KeepAlive(mutex);
        mutex.Dispose();
    }
}
