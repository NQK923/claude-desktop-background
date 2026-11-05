using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Forms;

static partial class Tests
{
    static void LivingDesk(string output)
    {
        Directory.CreateDirectory(output);
        LivingSettings();
        FocusTiming();
        CompanionPlacement();
        SceneRendering(output);
        StudioRendering(output);
        FocusProgressIntegration();
    }

    static void FocusProgressIntegration()
    {
        string folder = Path.Combine(dir, "focus-progress");
        Directory.CreateDirectory(folder);
        var settings = LoadSettings(folder);
        Set("cfg", settings);
        Set("appDir", folder);
        Call("ResetFocusSession");
        Call("CompleteFocusSession");
        Check("finish without a session earns no progress", (int)Field(settings, "CompletedSessions") == 0);
        Call("ToggleFocusSession");
        Check("runtime starts focus in Focus mode", (bool)Call("FocusRunning") && (DeskMode)Field(settings, "Mode") == DeskMode.Focus);
        Call("CompleteFocusSession");
        Call("CompleteFocusSession");
        Check("runtime completion adds progress exactly once", (int)Field(settings, "CompletedSessions") == 1);
        Check("runtime completion persists progress", (int)Field(LoadSettings(folder), "CompletedSessions") == 1);
        Call("ToggleFocusSession");
        Call("ResetFocusSession");
        Call("CompleteFocusSession");
        Check("runtime reset does not add progress", !(bool)Call("FocusHasSession") && (int)Field(settings, "CompletedSessions") == 1);
    }

    static void CompanionPlacement()
    {
        bool compact;
        var work = new Rectangle(0, 0, 1920, 1080);
        var expanded = new Size(220, 300);
        var small = new Size(84, 84);
        var target = new Rectangle(100, 100, 800, 800);
        var right = ClaudeBg.CalculateCompanionBounds(target, work, expanded, small, out compact);
        Check("companion uses free space on the right", !compact && right.Left >= target.Right && work.Contains(right));
        target = new Rectangle(1100, 100, 750, 800);
        var left = ClaudeBg.CalculateCompanionBounds(target, work, expanded, small, out compact);
        Check("companion uses free space on the left", !compact && left.Right <= target.Left && work.Contains(left));
        var maximized = ClaudeBg.CalculateCompanionBounds(work, work, expanded, small, out compact);
        Check("maximized Claude gets a compact companion", compact && work.Contains(maximized));
        var otherMonitor = new Rectangle(-1920, -100, 1920, 1080);
        var negative = ClaudeBg.CalculateCompanionBounds(new Rectangle(-1800, 50, 900, 700), otherMonitor, expanded, small, out compact);
        Check("companion placement handles negative monitor coordinates", otherMonitor.Contains(negative));
        var tinyWork = new Rectangle(0, 0, 80, 60);
        var tiny = ClaudeBg.CalculateCompanionBounds(tinyWork, tinyWork, expanded, small, out compact);
        Check("companion remains inside small working areas", compact && tiny.Width > 0 && tiny.Height > 0 && tinyWork.Contains(tiny));
    }

    static void SetSetting(object settings, string name, object value)
    {
        SettingsType().GetField(name).SetValue(settings, value);
    }

    static void LivingSettings()
    {
        string folder = Path.Combine(dir, "living-settings");
        Directory.CreateDirectory(folder);
        string ini = Path.Combine(folder, "settings.ini");

        var fresh = LoadSettings(Path.Combine(folder, "new"));
        Check("fresh install starts with Rainy Study", (SceneKind)Field(fresh, "Scene") == SceneKind.RainyStudy);
        Check("fresh install enables companion", (bool)Field(fresh, "CompanionVisible"));

        File.WriteAllText(ini, "image=old.png\nfocused=0.94\n");
        var legacy = LoadSettings(folder);
        Check("legacy settings preserve image mode", (SceneKind)Field(legacy, "Scene") == SceneKind.Image);
        Check("legacy settings do not add a companion unexpectedly", !(bool)Field(legacy, "CompanionVisible"));
        Check("legacy opacity is preserved", (double)Field(legacy, "Focused") == 0.94);

        SetSetting(legacy, "Scene", SceneKind.NightTrain);
        SetSetting(legacy, "Mode", DeskMode.Relax);
        SetSetting(legacy, "Motion", 0.7);
        SetSetting(legacy, "SceneAnimations", false);
        SetSetting(legacy, "CompanionVisible", true);
        SetSetting(legacy, "FocusMinutes", 45);
        SetSetting(legacy, "CompletedSessions", 17);
        SettingsType().GetMethod("Save").Invoke(legacy, null);
        var saved = LoadSettings(folder);
        Check("scene and mode round-trip", (SceneKind)Field(saved, "Scene") == SceneKind.NightTrain && (DeskMode)Field(saved, "Mode") == DeskMode.Relax);
        Check("animation preferences round-trip", (double)Field(saved, "Motion") == 0.7 && !(bool)Field(saved, "SceneAnimations"));
        Check("companion and focus duration round-trip", (bool)Field(saved, "CompanionVisible") && (int)Field(saved, "FocusMinutes") == 45);
        Check("completed sessions survive restart", (int)Field(saved, "CompletedSessions") == 17);

        File.WriteAllText(ini, "scene=999\nmode=bogus\nmotion=NaN\nfocused=Infinity\nfocusMinutes=1440\ncompletedSessions=-8\n");
        var invalid = LoadSettings(folder);
        Check("unknown scenes and modes are rejected", Enum.IsDefined(typeof(SceneKind), Field(invalid, "Scene")) && Enum.IsDefined(typeof(DeskMode), Field(invalid, "Mode")));
        double motion = (double)Field(invalid, "Motion"), opacity = (double)Field(invalid, "Focused");
        Check("nonfinite settings cannot reach painting", !Double.IsNaN(motion) && !Double.IsInfinity(motion) && !Double.IsNaN(opacity) && !Double.IsInfinity(opacity));
        Check("focus duration is bounded", (int)Field(invalid, "FocusMinutes") >= 5 && (int)Field(invalid, "FocusMinutes") <= 120);
        Check("negative progress is rejected", (int)Field(invalid, "CompletedSessions") == 0);
        File.WriteAllText(ini, "scene=aQuArIuM\nmode=rElAx\nmotion=-2\n");
        var mixed = LoadSettings(folder);
        Check("scene and mode names ignore case", (SceneKind)Field(mixed, "Scene") == SceneKind.Aquarium && (DeskMode)Field(mixed, "Mode") == DeskMode.Relax);
        Check("motion is bounded", (double)Field(mixed, "Motion") == 0);
    }

    static void FocusTiming()
    {
        var focus = new FocusSession();
        Check("focus starts idle", !focus.HasSession && !focus.Running);
        focus.Toggle(1500, 10);
        Check("focus starts at requested duration", focus.HasSession && focus.Running && focus.RemainingSeconds(10) == 1500);
        Check("focus consumes elapsed time", focus.RemainingSeconds(70) == 1440);
        focus.Toggle(1500, 70);
        Check("focus pauses without losing the session", focus.HasSession && !focus.Running && focus.RemainingSeconds(1070) == 1440);
        Check("paused session cannot expire", !focus.TryComplete(1070, false));
        focus.Toggle(3000, 1070);
        Check("resume retains original duration", focus.Running && focus.RemainingSeconds(1130) == 1380);
        Check("focus does not finish early", !focus.TryComplete(2509.5, false) && focus.RemainingSeconds(2509.5) == 1);
        Check("focus completes at the deadline", focus.TryComplete(2510, false));
        Check("automatic completion is counted once", !focus.HasSession && !focus.Running && !focus.TryComplete(2511, false) && !focus.TryComplete(2511, true));

        focus.Toggle(600, 3000);
        Check("manual finish completes an existing session", focus.TryComplete(3001, true));
        Check("manual finish is counted once", !focus.TryComplete(3002, true));
        focus.Toggle(600, 3100);
        focus.Reset();
        Check("reset cancels without completion", !focus.HasSession && !focus.Running && !focus.TryComplete(4000, true));

        var clock = new FocusSession();
        clock.Toggle(600, 100);
        int remaining = clock.RemainingSeconds(150);
        Check("backwards clock cannot extend a session", clock.RemainingSeconds(140) <= remaining);
        Check("invalid clock cannot complete a session", !clock.TryComplete(Double.NaN, false) && !clock.TryComplete(Double.PositiveInfinity, false));
    }

    static SceneFrame PreviewFrame(SceneKind scene, DeskMode mode, double seconds, float motion)
    {
        return new SceneFrame { Scene = scene, Mode = mode, Seconds = seconds, Motion = motion,
            Dark = true, Pointer = new PointF(0.5f, 0.5f), CompletedSessions = 4, Hour = 21 };
    }

    static Bitmap PaintScene(SceneFrame frame, int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap)) ScenePainter.Paint(graphics, new Rectangle(0, 0, width, height), frame);
        return bitmap;
    }

    static string Fingerprint(Bitmap bitmap)
    {
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[Math.Abs(data.Stride) * bitmap.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(bytes));
        }
        finally { bitmap.UnlockBits(data); }
    }

    static void SceneRendering(string output)
    {
        var growing = PreviewFrame(SceneKind.RainyStudy, DeskMode.Focus, 0, 0);
        growing.CompletedSessions = 0;
        using (var before = PaintScene(growing, 960, 600))
        {
            growing.CompletedSessions = 8;
            using (var after = PaintScene(growing, 960, 600)) Check("completed sessions visibly grow the study plant", Fingerprint(before) != Fingerprint(after));
        }
        string previous = null;
        foreach (var scene in new[] { SceneKind.RainyStudy, SceneKind.Aquarium, SceneKind.NightTrain })
        {
            var frame = PreviewFrame(scene, DeskMode.Relax, 12, 0.7f);
            using (var first = PaintScene(frame, 1280, 800))
            using (var repeat = PaintScene(frame, 1280, 800))
            {
                string signature = Fingerprint(first);
                Check(scene + " rendering is deterministic", signature == Fingerprint(repeat));
                Check(scene + " has a distinct composition", signature != previous);
                previous = signature;
                Check(scene + " fills its canvas", first.GetPixel(0, 0).A == 255 && first.GetPixel(1279, 799).A == 255);
                first.Save(Path.Combine(output, scene + ".png"), ImageFormat.Png);
                frame.Seconds = 14;
                using (var next = PaintScene(frame, 1280, 800)) Check(scene + " animates with time", signature != Fingerprint(next));
            }

            frame.Motion = 0;
            frame.Seconds = 0;
            using (var still = PaintScene(frame, 320, 200))
            {
                frame.Seconds = 99;
                frame.Pointer = new PointF(0.9f, 0.1f);
                using (var later = PaintScene(frame, 320, 200)) Check(scene + " zero motion freezes time and parallax", Fingerprint(still) == Fingerprint(later));
            }
            frame.Mode = DeskMode.Present;
            frame.Motion = 1;
            frame.Seconds = 1;
            using (var still = PaintScene(frame, 320, 200))
            {
                frame.Seconds = 90;
                frame.Pointer = new PointF(0, 1);
                using (var later = PaintScene(frame, 320, 200)) Check(scene + " presentation preview stays still", Fingerprint(still) == Fingerprint(later));
            }

            bool smallBounds = true;
            foreach (var size in new[] { new Size(1, 1), new Size(2, 40), new Size(50, 2), new Size(240, 360) })
                using (var tiny = PaintScene(frame, size.Width, size.Height)) smallBounds &= tiny.GetPixel(0, 0).A == 255;
            Check(scene + " supports small and portrait previews", smallBounds);

            frame.Mode = DeskMode.Focus;
            frame.Dark = false;
            frame.Hour = 12;
            using (var light = PaintScene(frame, 960, 600)) light.Save(Path.Combine(output, scene + "-light.png"), ImageFormat.Png);
        }

        using (var bitmap = new Bitmap(200, 160))
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Magenta);
            graphics.TranslateTransform(5, 7);
            graphics.SetClip(new Rectangle(10, 10, 120, 100));
            graphics.SmoothingMode = SmoothingMode.None;
            float[] transform = graphics.Transform.Elements;
            RectangleF clip = graphics.ClipBounds;
            ScenePainter.Paint(graphics, new Rectangle(20, 20, 80, 60), PreviewFrame(SceneKind.Aquarium, DeskMode.Relax, 3, 0.5f));
            Check("scene painter restores caller transform", transform.SequenceEqual(graphics.Transform.Elements));
            Check("scene painter restores caller clip and quality", graphics.ClipBounds == clip && graphics.SmoothingMode == SmoothingMode.None);
            Check("scene painter stays within its bounds", bitmap.GetPixel(0, 0).ToArgb() == Color.Magenta.ToArgb() && bitmap.GetPixel(190, 150).ToArgb() == Color.Magenta.ToArgb());
            ScenePainter.Paint(graphics, Rectangle.Empty, PreviewFrame(SceneKind.RainyStudy, DeskMode.Focus, 0, 0));
            Check("empty scene bounds are safe", true);
        }
    }

    static Form CreateDeskForm(string name)
    {
        return (Form)Activator.CreateInstance(app.GetNestedType(name, Any), true);
    }

    static void PrepareSnapshot(Form form)
    {
        // Hidden Forms do not create visible native child controls. Show off-screen and pump their
        // layout/paint messages before WM_PRINT, otherwise a blank background can pass a smoke test.
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-10000, -10000);
        form.ShowInTaskbar = false;
        form.Show();
        Application.DoEvents();
        form.PerformLayout();
        Application.DoEvents();
    }

    static bool HasSnapshotContent(Bitmap bitmap)
    {
        var colors = new HashSet<int>();
        // Ignore the window frame/title bar; those can be present while every child is still hidden.
        for (int y = 45; y < bitmap.Height - 8; y += 5)
            for (int x = 8; x < bitmap.Width - 8; x += 5)
            {
                colors.Add(bitmap.GetPixel(x, y).ToArgb());
                if (colors.Count > 20) return true;
            }
        return false;
    }

    static void StudioRendering(string output)
    {
        string folder = Path.Combine(dir, "studio-settings");
        Directory.CreateDirectory(folder);
        var settings = LoadSettings(folder);
        Set("cfg", settings);
        Set("appDir", folder);
        Set("claudeDark", true);
        Call("ApplyLook");
        var changed = app.GetField("DeskChanged", Any);
        var beforeEvents = (Delegate)changed.GetValue(null);
        int beforeCount = beforeEvents == null ? 0 : beforeEvents.GetInvocationList().Length;
        using (var studioForm = CreateDeskForm("StudioForm"))
        {
            studioForm.Size = new Size(960, 680);
            PrepareSnapshot(studioForm);
            using (var bitmap = new Bitmap(studioForm.Width, studioForm.Height))
            {
                studioForm.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(output, "SceneStudio.png"), ImageFormat.Png);
                Check("Scene Studio snapshot contains its rendered controls", HasSnapshotContent(bitmap));
            }
            var livePreview = studioForm.Controls.Find("ScenePreview", true).Single();
            Check("Scene Studio creates a visible live preview", livePreview.Visible && livePreview.IsHandleCreated && livePreview.Width > 100 && livePreview.Height > 40);
            Check("Scene Studio creates a resizable native UI", studioForm.Controls.Count > 0 && studioForm.MinimumSize.Width >= 700);
            studioForm.Size = studioForm.MinimumSize;
            studioForm.PerformLayout();
            Application.DoEvents();
            using (var bitmap = new Bitmap(studioForm.Width, studioForm.Height))
            {
                studioForm.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(output, "SceneStudio-small.png"), ImageFormat.Png);
                Check("small Studio snapshot contains its rendered controls", HasSnapshotContent(bitmap));
            }
            Check("Scene Studio paints at its minimum size", true);
        }
        using (var companionForm = CreateDeskForm("CompanionForm"))
        {
            PrepareSnapshot(companionForm);
            using (var bitmap = new Bitmap(companionForm.Width, companionForm.Height))
            {
                companionForm.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(output, "Nep.png"), ImageFormat.Png);
                Check("companion snapshot contains the character and controls", HasSnapshotContent(bitmap));
            }
            Check("companion draws without Claude or a running timer", companionForm.Width > 0 && companionForm.Height > 0);
            companionForm.ClientSize = new Size(94, 110);
            companionForm.GetType().GetMethod("SetCompactLayout", Any).Invoke(companionForm, new object[] { true });
            companionForm.PerformLayout();
            Application.DoEvents();
            using (var bitmap = new Bitmap(companionForm.Width, companionForm.Height))
            {
                companionForm.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(output, "Nep-compact.png"), ImageFormat.Png);
                Check("compact snapshot contains the character", HasSnapshotContent(bitmap));
            }
            Check("compact companion paints inside its dock", companionForm.Width == 94 && companionForm.Height == 110);
        }
        var afterEvents = (Delegate)changed.GetValue(null);
        Check("disposed Studio and companion release subscriptions", (afterEvents == null ? 0 : afterEvents.GetInvocationList().Length) == beforeCount);
    }
}
