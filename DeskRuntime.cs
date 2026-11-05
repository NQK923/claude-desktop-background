using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

// The timer owns no UI or wall clock. A monotonic time supplied by the caller makes pause,
// resume and completion deterministic, including when a machine sleeps or its clock changes.
internal sealed class FocusSession
{
    public bool HasSession { get; private set; }
    public bool Running { get; private set; }
    double secondsLeft, beganAt, lastNow;

    double Clock(double now)
    {
        if (!double.IsNaN(now) && !double.IsInfinity(now)) lastNow = Math.Max(lastNow, now);
        return lastNow;
    }

    double Remaining(double now)
    {
        return !HasSession ? 0 : Math.Max(0, secondsLeft - (Running ? Math.Max(0, now - beganAt) : 0));
    }

    public int RemainingSeconds(double now)
    {
        return (int)Math.Ceiling(Remaining(Clock(now)));
    }

    public void Toggle(int durationSeconds, double now)
    {
        now = Clock(now);
        if (!HasSession)
        {
            secondsLeft = Math.Max(1, durationSeconds);
            beganAt = now;
            HasSession = Running = true;
        }
        else if (Running)
        {
            secondsLeft = Remaining(now);
            Running = false;
        }
        else
        {
            beganAt = now;
            Running = true;
        }
    }

    public bool TryComplete(double now, bool finishEarly)
    {
        now = Clock(now);
        if (!HasSession || (!finishEarly && Remaining(now) > 0)) return false;
        Reset();
        return true;
    }

    public void Reset()
    {
        HasSession = Running = false;
        secondsLeft = 0;
    }
}

public static partial class ClaudeBg
{
    static readonly Stopwatch deskClock = Stopwatch.StartNew();
    static readonly FocusSession focusSession = new FocusSession();
    static Timer sceneTimer, deskHeartbeat;
    static bool deskRuntimeStarted, deskClosing, systemMotion = true, requestStudioOnStart;
    static SceneKind appliedScene;
    static double sceneSeconds, sceneTickAt;
    static IntPtr lastCompanionTarget;
    static event Action DeskChanged;

    static bool HasBackground()
    {
        return cfg != null && (cfg.Scene != SceneKind.Image || bgImage != null);
    }

    static SceneFrame CurrentSceneFrame(PointF normalizedPointer)
    {
        float x = normalizedPointer.X, y = normalizedPointer.Y;
        if (float.IsNaN(x) || float.IsInfinity(x)) x = 0.5f;
        if (float.IsNaN(y) || float.IsInfinity(y)) y = 0.5f;
        return new SceneFrame {
            Scene = cfg == null ? SceneKind.RainyStudy : cfg.Scene,
            Mode = cfg == null ? DeskMode.Focus : cfg.Mode,
            Seconds = sceneSeconds,
            Motion = cfg != null && cfg.SceneAnimations && systemMotion && cfg.Mode != DeskMode.Present ? (float)cfg.Motion : 0,
            Dark = claudeDark,
            Pointer = new PointF(Math.Max(0, Math.Min(1, x)), Math.Max(0, Math.Min(1, y))),
            CompletedSessions = cfg == null ? 0 : cfg.CompletedSessions,
            Hour = DateTime.Now.Hour
        };
    }

    static int FocusSecondsRemaining()
    {
        return focusSession.HasSession ? focusSession.RemainingSeconds(deskClock.Elapsed.TotalSeconds)
            : (cfg == null ? 25 : cfg.FocusMinutes) * 60;
    }

    static bool FocusRunning() { return focusSession.Running; }
    static bool FocusHasSession() { return focusSession.HasSession; }

    static void NotifyDeskChanged()
    {
        var changed = DeskChanged;
        if (changed != null) changed();
        InvalidateDeskUi();
    }

    static void RecordFocusCompletion()
    {
        if (cfg == null) return;
        if (cfg.CompletedSessions < int.MaxValue) cfg.CompletedSessions++;
        cfg.Save();
        foreach (var a in attached.Values) if (!a.Form.IsDisposed) a.Form.Invalidate();
        NotifyDeskChanged();
    }

    static bool CheckFocusCompletion()
    {
        if (!focusSession.TryComplete(deskClock.Elapsed.TotalSeconds, false)) return false;
        RecordFocusCompletion();
        return true;
    }

    static void ToggleFocusSession()
    {
        if (cfg == null || deskClosing || CheckFocusCompletion()) return;
        focusSession.Toggle(cfg.FocusMinutes * 60, deskClock.Elapsed.TotalSeconds);
        cfg.Mode = DeskMode.Focus;
        ChangeDeskPreferences();
    }

    static void CompleteFocusSession()
    {
        if (cfg == null || deskClosing) return;
        if (focusSession.TryComplete(deskClock.Elapsed.TotalSeconds, true)) RecordFocusCompletion();
    }

    static void ResetFocusSession()
    {
        focusSession.Reset();
        NotifyDeskChanged();
    }

    static void ChangeDeskPreferences()
    {
        if (cfg == null || deskClosing) return;
        cfg.Normalize();
        if (appliedScene != cfg.Scene)
        {
            // A pending slideshow decode must never replace a newer scene selection.
            ++loadSeq;
            appliedScene = cfg.Scene;
            if (cfg.Scene == SceneKind.Image && bgImage == null && File.Exists(cfg.Image))
                SetImageAsync(cfg.Image, null);
        }
        cfg.Save();
        ApplyLook();
        ConfigureSlideshow();
        SyncAll();
        NotifyDeskChanged();
    }

    // IsWindowVisible alone is also true for windows on other virtual desktops. DWM cloak and
    // the supported desktop-manager interface keep their scenes and companion asleep there.
    static bool VisibleClaudeTarget(IntPtr target)
    {
        if (!IsWindow(target) || !IsWindowVisible(target) || IsIconic(target)) return false;
        int cloaked;
        if (DwmGetWindowAttributeInt(target, DWMWA_CLOAKED, out cloaked, sizeof(int)) == 0 && cloaked != 0) return false;
        if (desktops != null)
        {
            try
            {
                int current;
                if (desktops.IsWindowOnCurrentVirtualDesktop(target, out current) == 0 && current == 0) return false;
            }
            catch (COMException) { } // Explorer may be restarting; DWM still provides the cloak check.
        }
        return true;
    }

    static bool DeskOwnsForeground()
    {
        IntPtr fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        IntPtr root = GetAncestor(fg, GA_ROOTOWNER);
        return (studio != null && !studio.IsDisposed && studio.IsHandleCreated && (fg == studio.Handle || root == studio.Handle))
            || (companion != null && !companion.IsDisposed && companion.IsHandleCreated && (fg == companion.Handle || root == companion.Handle));
    }

    static void SyncDeskWindows()
    {
        if (!deskRuntimeStarted || deskClosing || cfg == null) return;
        Attachment chosen = null;
        if (enabled && cfg.CompanionVisible && cfg.Mode != DeskMode.Present)
        {
            var eligible = attached.Values.Where(a => a.Eligible && !a.Moving && !a.Landing && !a.Last.IsEmpty).ToList();
            chosen = eligible.FirstOrDefault(Focused);
            if (chosen == null && DeskOwnsForeground())
                chosen = eligible.FirstOrDefault(a => a.Target == lastCompanionTarget) ?? eligible.FirstOrDefault();
        }
        if (chosen != null) lastCompanionTarget = chosen.Target;
        UpdateCompanionWindow(chosen == null ? (Rectangle?)null : chosen.Last);
        RefreshSceneTimer();
    }

    static bool NeedsSceneFrames()
    {
        if (!deskRuntimeStarted || deskClosing || cfg == null || !cfg.SceneAnimations || !systemMotion
            || cfg.Motion <= 0 || cfg.Mode == DeskMode.Present) return false;
        return DeskUiNeedsFrames() || (enabled && cfg.Scene != SceneKind.Image
            && attached.Values.Any(a => a.Eligible && !a.Form.IsDisposed && IsWindowVisible(a.Form.Handle)));
    }

    static void RefreshSceneTimer()
    {
        if (sceneTimer == null) return;
        bool run = NeedsSceneFrames();
        if (run && !sceneTimer.Enabled)
        {
            sceneTickAt = deskClock.Elapsed.TotalSeconds;
            sceneTimer.Start();
        }
        else if (!run && sceneTimer.Enabled) sceneTimer.Stop();
    }

    static void PaintDeskFrame()
    {
        if (!NeedsSceneFrames()) { RefreshSceneTimer(); return; }
        double now = deskClock.Elapsed.TotalSeconds;
        // A stalled message loop or resume from sleep must not jump an entire scene across the screen.
        sceneSeconds += Math.Max(0, Math.Min(0.25, now - sceneTickAt));
        sceneTickAt = now;
        if (cfg.Scene != SceneKind.Image && enabled)
            foreach (var a in attached.Values)
                if (a.Eligible && !a.Form.IsDisposed && IsWindowVisible(a.Form.Handle)) a.Form.Invalidate();
        InvalidateDeskUi();
    }

    static void UpdateDeskMotionPreference()
    {
        systemMotion = Animations();
        RefreshSceneTimer();
        foreach (var a in attached.Values) if (!a.Form.IsDisposed) a.Form.Invalidate();
        NotifyDeskChanged();
    }

    static void StartDeskRuntime()
    {
        deskClosing = false;
        deskRuntimeStarted = true;
        appliedScene = cfg.Scene;
        systemMotion = Animations();
        sceneTimer = new Timer { Interval = 33 };
        sceneTimer.Tick += (s, e) => PaintDeskFrame();
        deskHeartbeat = new Timer { Interval = 1000 };
        deskHeartbeat.Tick += (s, e) =>
        {
            bool hadSession = focusSession.HasSession;
            bool completed = CheckFocusCompletion();
            if (hadSession && !completed) NotifyDeskChanged();
            else if (!completed) InvalidateDeskUi(); // timed greetings also expire when animation is disabled
            // Also observes our own Studio activation/closure, which the global WinEvent hook skips.
            SyncDeskWindows();
        };
        deskHeartbeat.Start();
        SyncDeskWindows();
    }

    static void StopDeskRuntime()
    {
        deskClosing = true;
        deskRuntimeStarted = false;
        ++loadSeq;
        if (sceneTimer != null) { sceneTimer.Stop(); sceneTimer.Dispose(); sceneTimer = null; }
        if (deskHeartbeat != null) { deskHeartbeat.Stop(); deskHeartbeat.Dispose(); deskHeartbeat = null; }
        focusSession.Reset();
        CloseDeskUi();
    }

    // An owned icon is present even before a photo is loaded or a Claude window exists.
    static Icon MakeDeskIcon()
    {
        using (var bitmap = new Bitmap(32, 32))
        {
            using (var g = Graphics.FromImage(bitmap))
            using (var ground = new SolidBrush(Color.FromArgb(44, 48, 48)))
            using (var pot = new SolidBrush(Color.FromArgb(218, 146, 110)))
            using (var leaf = new SolidBrush(Color.FromArgb(156, 190, 145)))
            using (var stem = new Pen(Color.FromArgb(156, 190, 145), 2))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillEllipse(ground, 0, 0, 31, 31);
                g.DrawLine(stem, 16, 23, 16, 9);
                g.FillEllipse(leaf, 7, 7, 10, 6);
                g.FillEllipse(leaf, 16, 4, 9, 6);
                g.FillPolygon(pot, new[] { new Point(10, 20), new Point(22, 20), new Point(20, 28), new Point(12, 28) });
            }
            IntPtr handle = bitmap.GetHicon();
            try { return (Icon)Icon.FromHandle(handle).Clone(); }
            finally { DestroyIcon(handle); }
        }
    }
}
