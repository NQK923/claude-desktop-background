using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security.AccessControl;
using System.Threading;
using System.Windows.Forms;

// Tests the parts of ClaudeBg.exe that need no Claude window: image loading, settings, the slideshow
// deck, theme sampling and process checks. Run with tests\run-tests.ps1; exits with the failure count.
static class Tests
{
    const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance;
    static Type app;
    static int failures;
    static string dir;

    static void Check(string label, bool ok)
    {
        Console.WriteLine((ok ? "PASS " : "FAIL ") + label);
        if (!ok) failures++;
    }

    static object Call(string name, params object[] args) { return app.GetMethod(name, Any, null, args.Select(a => a.GetType()).ToArray(), null).Invoke(null, args); }
    static object Get(string field) { return app.GetField(field, Any).GetValue(null); }
    static void Set(string field, object value) { app.GetField(field, Any).SetValue(null, value); }

    [STAThread]
    static int Main(string[] args)
    {
        app = Assembly.LoadFrom(args[0]).GetType("ClaudeBg");
        dir = Path.Combine(Path.GetTempPath(), "ClaudeBgTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Orientation();
            SettingsFile();
            DataFolder();
            SlideDeck();
            ThemeSampling();
            AsyncLoading();
            Processes();
        }
        catch (Exception ex) { Check("no exception: " + ex, false); }
        finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
        Console.WriteLine(failures == 0 ? "All tests passed" : failures + " test(s) failed");
        return failures;
    }

    // A 300 x 100 red image with a blue square in its top-left corner, tagged with an EXIF orientation.
    static string TaggedJpeg(string name, int orientation)
    {
        using (var b = new Bitmap(300, 100))
        {
            using (var g = Graphics.FromImage(b))
            {
                g.Clear(Color.Red);
                g.FillRectangle(Brushes.Blue, 0, 0, 20, 20);
            }
            var tag = (PropertyItem)FormatterServices.GetUninitializedObject(typeof(PropertyItem));
            tag.Id = 0x0112; tag.Type = 3; tag.Len = 2; tag.Value = BitConverter.GetBytes((ushort)orientation);
            b.SetPropertyItem(tag);
            string path = Path.Combine(dir, name);
            b.Save(path, ImageFormat.Jpeg);
            return path;
        }
    }

    static void Orientation()
    {
        for (int o = 1; o <= 8; o++)
        {
            string path = TaggedJpeg("o" + o + ".jpg", o);
            Bitmap wic = (Bitmap)Call("LoadImage", path), gdi;
            using (var raw = Image.FromFile(path)) gdi = (Bitmap)Call("Fit", raw);
            foreach (var pair in new[] { Tuple.Create("WIC", wic), Tuple.Create("GDI+", gdi) })
            {
                var img = pair.Item2;
                int r = img.Width - 5, b = img.Height - 5;
                // Where the stored top-left corner belongs once the image is upright, per the EXIF spec.
                var corner = new[] { null, new[] { 4, 4 }, new[] { r, 4 }, new[] { r, b }, new[] { 4, b },
                                     new[] { 4, 4 }, new[] { r, 4 }, new[] { r, b }, new[] { 4, b } }[o];
                var c = img.GetPixel(corner[0], corner[1]);
                Check(pair.Item1 + " orientation " + o + " shape", (img.Height > img.Width) == (o >= 5));
                Check(pair.Item1 + " orientation " + o + " corner", c.B > 150 && c.R < 100);
                img.Dispose();
            }
        }
    }

    static Type SettingsType() { return app.GetNestedType("Settings", Any); }

    static object LoadSettings(string folder) { return SettingsType().GetMethod("Load").Invoke(null, new object[] { folder }); }

    static object Field(object settings, string name) { return SettingsType().GetField(name).GetValue(settings); }

    static void SettingsFile()
    {
        string folder = Path.Combine(dir, "settings");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "settings.ini"),
            "image=pic.png\nalignX=7\nalignY=-1\nslideFolder=slides\ntint=notacolor\nfocused=0.9\nblur=99\nhideInCapture=true\n");
        var s = LoadSettings(folder);
        Check("relative image path", (string)Field(s, "Image") == Path.Combine(folder, "pic.png"));
        Check("relative slide folder", (string)Field(s, "SlideFolder") == Path.Combine(folder, "slides"));
        Check("alignment clamped", (double)Field(s, "AlignX") == 1 && (double)Field(s, "AlignY") == 0);
        Check("blur clamped", (int)Field(s, "Blur") == 3);
        Check("bad tint ignored", ((Color)Field(s, "Tint")).ToArgb() == Color.Black.ToArgb());
        Check("boolean parsed", (bool)Field(s, "HideInCapture"));

        SettingsType().GetMethod("Save").Invoke(s, null);
        SettingsType().GetMethod("Save").Invoke(s, null); // the second save replaces the first
        var again = LoadSettings(folder);
        Check("save round-trips", (double)Field(again, "Focused") == 0.9 && (string)Field(again, "Image") == Path.Combine(folder, "pic.png"));
        Check("no temporary file left", !File.Exists(Path.Combine(folder, "settings.ini.tmp")));

        var fresh = LoadSettings(Path.Combine(dir, "missing"));
        Check("defaults without a file", (double)Field(fresh, "Focused") == 0.88 && (string)Field(fresh, "SlideFolder") == "");
    }

    static void DataFolder()
    {
        Check("writable folder is used", (string)Call("DataDir", dir) == dir);
        string locked = Path.Combine(dir, "locked");
        var info = Directory.CreateDirectory(locked);
        var deny = new FileSystemAccessRule(Environment.UserName, FileSystemRights.CreateFiles, AccessControlType.Deny);
        var acl = info.GetAccessControl();
        acl.AddAccessRule(deny);
        info.SetAccessControl(acl);
        string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudeBg");
        bool existed = Directory.Exists(local);
        try { Check("read-only folder falls back to LOCALAPPDATA", (string)Call("DataDir", locked) == local); }
        finally
        {
            acl.RemoveAccessRule(deny);
            info.SetAccessControl(acl);
            if (!existed && Directory.Exists(local) && !Directory.EnumerateFileSystemEntries(local).Any()) Directory.Delete(local);
        }
    }

    static void UseSlideFolder(string folder)
    {
        var s = LoadSettings(Path.Combine(dir, "missing"));
        SettingsType().GetField("SlideFolder").SetValue(s, folder);
        Set("cfg", s);
        ((List<string>)Get("slideDeck")).Clear();
    }

    static void SlideDeck()
    {
        string folder = Path.Combine(dir, "slides");
        Directory.CreateDirectory(folder);
        var files = Enumerable.Range(0, 5).Select(i => Path.Combine(folder, "s" + i + ".png")).ToList();
        foreach (var f in files) File.WriteAllText(f, "");
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "");
        UseSlideFolder(folder);
        Set("currentImage", null);

        var taken = new List<string>();
        for (int i = 0; i < 10; i++)
        {
            var next = (string)Call("TakeSlide");
            Check("deck never repeats the image on screen (" + i + ")", next != null && next != (string)Get("currentImage"));
            Set("currentImage", next);
            taken.Add(next);
        }
        Check("first round deals every image once", taken.Take(5).OrderBy(f => f).SequenceEqual(files));
        Check("second round deals every image once", taken.Skip(5).OrderBy(f => f).SequenceEqual(files));

        File.Delete(files[4]);
        var seen = Enumerable.Range(0, 8).Select(i => (string)Call("TakeSlide")).ToList();
        Check("deleted images are dropped", !seen.Contains(files[4]));

        string single = Path.Combine(dir, "single");
        Directory.CreateDirectory(single);
        File.WriteAllText(Path.Combine(single, "only.jpg"), "");
        UseSlideFolder(single);
        Set("currentImage", Path.Combine(single, "only.jpg"));
        Check("one image already on screen: nothing new", Call("TakeSlide") == null);
        UseSlideFolder(Path.Combine(dir, "nowhere"));
        Check("missing folder: nothing", Call("TakeSlide") == null);
    }

    static bool? Sample(Color fill, Color text)
    {
        using (var b = new Bitmap(400, 300, PixelFormat.Format32bppRgb))
        {
            using (var g = Graphics.FromImage(b))
            {
                g.Clear(fill);
                g.FillRectangle(new SolidBrush(text), 50, 50, 100, 40); // some "content" that must not win the vote
            }
            var data = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, b.PixelFormat);
            try { return (bool?)Call("DarkFromSamples", data.Scan0, data.Stride, b.Width, b.Height); }
            finally { b.UnlockBits(data); }
        }
    }

    static void ThemeSampling()
    {
        Check("dark page is dark", Sample(Color.FromArgb(0x26, 0x26, 0x24), Color.White) == true);
        Check("light page is light", Sample(Color.FromArgb(0xFA, 0xF9, 0xF5), Color.Black) == false);
        Check("failed capture is unknown", Sample(Color.Black, Color.Black) == null);
    }

    // Pumps messages until the condition holds or five seconds pass.
    static bool WaitFor(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition() && clock.ElapsedMilliseconds < 5000) { Application.DoEvents(); Thread.Sleep(10); }
        return condition();
    }

    static void AsyncLoading()
    {
        var context = new WindowsFormsSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(context);
        Set("ui", context);
        using (var tray = new NotifyIcon())
        {
            Set("tray", tray);
            string a = TaggedJpeg("a.jpg", 1), b = TaggedJpeg("b.jpg", 6);
            Set("currentImage", null);

            bool? result = null;
            app.GetMethod("SetImageAsync", Any).Invoke(null, new object[] { a, (Action<bool>)(ok => result = ok) });
            Check("async load reports success", WaitFor(() => result.HasValue) && result == true);
            Check("async load shows the image", (string)Get("currentImage") == a && Get("bgImage") != null && tray.Icon != null);

            // Two loads racing: the later choice wins even if the earlier one finishes last.
            app.GetMethod("SetImageAsync", Any).Invoke(null, new object[] { b, null });
            app.GetMethod("SetImageAsync", Any).Invoke(null, new object[] { a, (Action<bool>)(ok => result = ok) });
            result = null;
            WaitFor(() => result.HasValue);
            var settle = Stopwatch.StartNew(); // give the earlier load time to land too
            while (settle.ElapsedMilliseconds < 1500) { Application.DoEvents(); Thread.Sleep(10); }
            Check("latest of two racing loads wins", (string)Get("currentImage") == a && ((Image)Get("bgImage")).Width > ((Image)Get("bgImage")).Height);

            string broken = Path.Combine(dir, "broken.jpg");
            File.WriteAllText(broken, "not an image");
            result = null;
            app.GetMethod("SetImageAsync", Any).Invoke(null, new object[] { broken, (Action<bool>)(ok => result = ok) });
            Check("broken image reports failure", WaitFor(() => result.HasValue) && result == false);
            Check("broken image keeps the last one", (string)Get("currentImage") == a);
            Set("tray", null);
        }
    }

    static void Processes()
    {
        Check("this process is not Claude", !(bool)Call("IsClaudePid", (uint)Process.GetCurrentProcess().Id));
        Check("a pid nobody has is not Claude", !(bool)Call("IsClaudePid", (uint)0x7FFFFFF0));
        var claude = Process.GetProcessesByName("claude").FirstOrDefault();
        if (claude != null) Check("running Claude is found", (bool)Call("IsClaudePid", (uint)claude.Id));
    }
}
