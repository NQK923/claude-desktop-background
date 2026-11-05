using System;
using System.Drawing;
using System.Drawing.Drawing2D;

// Original, asset-free scenery with soft anime background colors and cel-painted
// foreground details. A logical 1200 x 800 canvas keeps objects in proportion.
internal enum SceneKind { Image, RainyStudy, Aquarium, NightTrain }
internal enum DeskMode { Focus, Relax, Present }

internal sealed class SceneFrame
{
    public SceneKind Scene;
    public DeskMode Mode;
    public double Seconds;
    public float Motion;
    public bool Dark;
    public PointF Pointer;
    public int CompletedSessions;
    public int Hour;
}

internal static class ScenePainter
{
    const float Tau = (float)(Math.PI * 2.0);

    public static void Paint(Graphics g, Rectangle bounds, SceneFrame frame)
    {
        if (g == null) throw new ArgumentNullException("g");
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        if (frame == null) frame = new SceneFrame();

        GraphicsState saved = g.Save();
        try
        {
            g.SetClip(bounds, CombineMode.Intersect);
            float scale = Math.Min(bounds.Width / 1200f, bounds.Height / 800f);
            g.TranslateTransform(bounds.Left, bounds.Top);
            g.ScaleTransform(scale, scale);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighSpeed;

            float w = bounds.Width / scale, h = bounds.Height / scale;
            double time = AnimationSeconds(frame);
            float night = NightAmount(frame.Dark, frame.Hour);
            float motion = MotionAmount(frame);
            float px = (FiniteUnit(frame.Pointer.X, .5f) - .5f) * 12f * motion;
            float py = (FiniteUnit(frame.Pointer.Y, .5f) - .5f) * 8f * motion;

            switch (frame.Scene)
            {
                case SceneKind.RainyStudy:
                    RainyStudy(g, w, h, frame, time, night, px, py);
                    break;
                case SceneKind.Aquarium:
                    Aquarium(g, w, h, frame, time, night, px, py);
                    break;
                case SceneKind.NightTrain:
                    NightTrain(g, w, h, frame, time, night, px, py);
                    break;
                default:
                    FillGradient(g, new RectangleF(0, 0, w, h),
                        Mix(Color.FromArgb(227, 225, 214), Color.FromArgb(31, 40, 43), night),
                        Mix(Color.FromArgb(196, 207, 199), Color.FromArgb(20, 29, 35), night), 90);
                    Glow(g, new RectangleF(-w * .2f, -h * .4f, w * .85f, h * 1.2f),
                        Color.FromArgb(26, 194, 171, 124));
                    break;
            }
        }
        finally { g.Restore(saved); }
    }

    // Pure helpers are also useful to verify reduced-motion and growth behavior.
    internal static double AnimationSeconds(SceneFrame frame)
    {
        if (frame == null || frame.Mode == DeskMode.Present) return 0;
        double seconds = frame.Seconds;
        if (Double.IsNaN(seconds) || Double.IsInfinity(seconds) || seconds < 0) return 0;
        float motion = FiniteUnit(frame.Motion, 0);
        if (motion <= 0) return 0;
        // Retain precision after long uptime, without unbounded coordinates.
        return (seconds % 604800.0) * motion * (frame.Mode == DeskMode.Focus ? .34 : 1.0);
    }

    internal static int PlantLeaves(int completedSessions)
    {
        return 4 + Math.Min(12, Math.Max(0, completedSessions));
    }

    internal static float NightAmount(bool dark, int hour)
    {
        int wrapped = ((hour % 24) + 24) % 24;
        float daylight = Math.Max(0, (float)Math.Sin((wrapped - 6) * Math.PI / 12.0));
        return dark ? 1f - daylight * .18f : 1f - daylight;
    }

    static float MotionAmount(SceneFrame frame)
    {
        if (frame.Mode == DeskMode.Present) return 0;
        return FiniteUnit(frame.Motion, 0) * (frame.Mode == DeskMode.Focus ? .5f : 1f);
    }

    static void RainyStudy(Graphics g, float w, float h, SceneFrame frame,
        double t, float night, float px, float py)
    {
        Color wallTop = Mix(Color.FromArgb(228, 211, 191), Color.FromArgb(49, 43, 62), night);
        Color wallBottom = Mix(Color.FromArgb(188, 180, 174), Color.FromArgb(36, 42, 57), night);
        FillGradient(g, new RectangleF(0, 0, w, h), wallTop, wallBottom, 90);

        float deskY = h * .835f;
        RectangleF outer = new RectangleF(w * .055f, h * .065f, w * .89f, h * .68f);
        RectangleF window = Inset(outer, 16);
        FillRounded(g, new RectangleF(outer.X - 7, outer.Y - 5, outer.Width + 14, outer.Height + 19),
            13, Mix(Color.FromArgb(133, 126, 130), Color.FromArgb(25, 30, 45), night));
        FillRounded(g, outer, 9, Mix(Color.FromArgb(188, 169, 149), Color.FromArgb(88, 79, 89), night));

        GraphicsState glassState = g.Save();
        try
        {
            using (GraphicsPath clip = RoundedPath(window, 3)) g.SetClip(clip, CombineMode.Intersect);
            Color skyTop = Mix(Color.FromArgb(139, 185, 200), Color.FromArgb(36, 47, 82), night);
            Color skyMiddle = Mix(Color.FromArgb(180, 197, 204), Color.FromArgb(68, 76, 110), night);
            Color skyBottom = Mix(Color.FromArgb(226, 198, 181), Color.FromArgb(114, 105, 133), night);
            SkyGradient(g, window, skyTop, skyMiddle, skyBottom);
            Glow(g, new RectangleF(window.Right - 330 + px, window.Y - 100 + py, 470, 380),
                Mix(Color.FromArgb(85, 255, 228, 184), Color.FromArgb(49, 180, 193, 229), night));
            DrawCloud(g, window.X - 98 + px * .4f, window.Y + 48 + py * .4f,
                Math.Min(480, window.Width * .44f), 84, t, 1,
                Mix(Color.FromArgb(86, 237, 234, 226), Color.FromArgb(37, 147, 156, 192), night));
            DrawCloud(g, window.Right - 325 + px * .5f, window.Y + 119 + py * .4f,
                410, 76, t, 4,
                Mix(Color.FromArgb(76, 246, 225, 214), Color.FromArgb(34, 177, 163, 199), night));

            float horizon = window.Y + window.Height * .78f;
            Ridge(g, window, horizon - 42, 50, t * .18 + px * .7, 3,
                Mix(Color.FromArgb(153, 172, 188), Color.FromArgb(77, 84, 118), night));
            Ridge(g, window, horizon + 8, 35, px * 1.1, 6,
                Mix(Color.FromArgb(137, 159, 177), Color.FromArgb(57, 74, 102), night));
            DrawCity(g, window, horizon + 51, night, px * 1.3, false);

            // The middle pane stays broad and uncluttered; brighter drops gather at the edges.
            int rainCount = frame.Mode == DeskMode.Relax ? 84 : 48;
            using (Pen rain = new Pen(Color.FromArgb(frame.Mode == DeskMode.Relax ? 48 : 30, 214, 233, 232), 1.15f))
            using (Pen middleRain = new Pen(Color.FromArgb(15, 218, 235, 232), .8f))
            {
                for (int i = 0; i < rainCount; i++)
                {
                    float x = window.X + window.Width * Fraction(Noise(i + 70) + t * .006);
                    float y = window.Y + window.Height * Fraction(Noise(i + 220) + t * (.058 + Noise(i + 650) * .033));
                    float length = 12 + Noise(i + 130) * 20;
                    Pen pen = x > w * .32f && x < w * .68f ? middleRain : rain;
                    g.DrawLine(pen, x, y, x - length * .19f, y + length);
                }
            }
            using (Pen trail = new Pen(Color.FromArgb(28, 221, 235, 230), 1.8f))
            using (Pen bead = new Pen(Color.FromArgb(71, 221, 237, 232), 1.1f))
            {
                for (int i = 0; i < 12; i++)
                {
                    float side = i < 6 ? .04f + Noise(i + 750) * .18f : .78f + Noise(i + 750) * .18f;
                    float x = window.X + window.Width * side;
                    float y = window.Y + window.Height * Fraction(Noise(i + 830) + t * .008);
                    g.DrawBezier(trail, x, y - 38, x - 2, y - 22, x + 2, y - 12, x, y);
                    g.DrawEllipse(bead, x - 1.8f, y, 3.6f, 5.5f);
                }
            }
            GlassReflection(g, window, Color.FromArgb(10, 244, 232, 219));
        }
        finally { g.Restore(glassState); }

        Color trim = Mix(Color.FromArgb(171, 155, 143), Color.FromArgb(82, 77, 90), night);
        Color trimLight = Mix(Color.FromArgb(222, 197, 165), Color.FromArgb(132, 111, 111), night);
        for (int i = 0; i < 2; i++)
        {
            float x = window.X + window.Width * (i == 0 ? .205f : .795f);
            FillRect(g, trim, x - 5, window.Y, 10, window.Height);
            FillRect(g, WithAlpha(trimLight, 150), x - 5, window.Y, 1.7f, window.Height);
        }
        FillRect(g, trim, window.X, window.Bottom - 34, window.Width, 7);
        FillGradient(g, new RectangleF(outer.X - 12, outer.Bottom, outer.Width + 24, 18),
            trimLight, trim, 90);
        FillRect(g, Color.FromArgb(24, 0, 0, 0), outer.X - 7, outer.Bottom + 18, outer.Width + 14, 6);

        DrawCurtain(g, w, h, false, night, t);
        DrawCurtain(g, w, h, true, night, t);
        DrawStringLights(g, w * .10f, w * .90f, h * .024f, night);

        // A low pool of lamplight sits under the text area instead of bleaching it.
        Glow(g, new RectangleF(-w * .08f, deskY - 290, w * .52f, 510), Color.FromArgb(50, 245, 179, 111));
        FillGradient(g, new RectangleF(0, deskY, w, h - deskY),
            Mix(Color.FromArgb(202, 164, 124), Color.FromArgb(132, 100, 84), night),
            Mix(Color.FromArgb(152, 121, 102), Color.FromArgb(68, 66, 76), night), 90);
        FillRect(g, Mix(Color.FromArgb(244, 206, 154), Color.FromArgb(190, 144, 101), night), 0, deskY, w, 2.5f);
        FillRect(g, Color.FromArgb(28, 0, 0, 0), 0, deskY + 39, w, 7);
        using (Pen grain = new Pen(Color.FromArgb(14, 240, 213, 169), 1))
        {
            for (int i = 0; i < 8; i++)
            {
                float y = deskY + 15 + i * 15;
                g.DrawBezier(grain, 0, y, w * .27f, y - 7, w * .67f, y + 8, w, y - 3);
            }
        }

        DrawLamp(g, w * .105f, deskY + 15, night);
        DrawBooks(g, w * .18f, deskY + 20, 146, night);
        DrawCup(g, w * .315f, deskY + 19, .84f,
            Mix(Color.FromArgb(246, 218, 187), Color.FromArgb(222, 186, 152), night), t, true);
        DrawPlant(g, w * .875f, deskY + 13, PlantLeaves(frame.CompletedSessions), t, night, px);
        // Small paper coaster and pencil at the edge complete the inhabited desk.
        FillRounded(g, new RectangleF(w * .72f, deskY + 19, 78, 34), 5,
            Mix(Color.FromArgb(205, 202, 166), Color.FromArgb(130, 149, 132), night));
        Stroke(g, Color.FromArgb(170, 192, 146, 86), 3.2f,
            w * .725f + 4, deskY + 38, w * .725f + 66, deskY + 25);

        ReadingVeil(g, w, h, frame.Mode, night,
            Mix(Color.FromArgb(186, 193, 198), Color.FromArgb(51, 64, 91), night));
    }

    static void Aquarium(Graphics g, float w, float h, SceneFrame frame,
        double t, float night, float px, float py)
    {
        Color top = Mix(Color.FromArgb(111, 181, 185), Color.FromArgb(36, 85, 109), night);
        Color bottom = Mix(Color.FromArgb(55, 115, 137), Color.FromArgb(22, 43, 71), night);
        FillGradient(g, new RectangleF(0, 0, w, h), top, bottom, 90);
        Glow(g, new RectangleF(-w * .25f + px, -h * .5f + py, w * .9f, h * 1.15f),
            Mix(Color.FromArgb(92, 225, 239, 182), Color.FromArgb(64, 136, 200, 189), night));
        Glow(g, new RectangleF(w * .65f, h * .25f, w * .65f, h), Color.FromArgb(32, 127, 135, 192));

        int rayAlpha = frame.Mode == DeskMode.Relax ? 38 : 25;
        for (int i = 0; i < 6; i++)
        {
            float x = w * (-.2f + i * .205f) + Wave(t, .16, i) * 9 + px * 2;
            DrawLightShaft(g, x, h, 115 + i * 12, Color.FromArgb(rayAlpha, 200, 236, 205));
        }
        using (Pen ripple = new Pen(Color.FromArgb(13, 191, 225, 205), 1.3f))
        {
            for (int i = 0; i < 5; i++)
            {
                float y = 16 + i * 10 + Wave(t, .27, i * .8) * 4;
                g.DrawBezier(ripple, -20, y, w * .25f, y - 17, w * .7f, y + 14, w + 20, y - 2);
            }
        }

        RectangleF water = new RectangleF(0, 0, w, h);
        Ridge(g, water, h * .91f, 90, px * .8, 4,
            Mix(Color.FromArgb(47, 96, 103), Color.FromArgb(21, 59, 72), night));
        // Distant, translucent eelgrass gives depth without drawing across the reading lane.
        for (int i = 0; i < 8; i++)
        {
            bool right = i > 3;
            float x = right ? w - 35 - (i - 4) * 44 : 18 + i * 42;
            DrawRibbon(g, x + px * .5f, h * .94f, 195 + Noise(i + 10) * 145,
                15, Wave(t, .31, i * 1.7) * 24, Color.FromArgb(65, 62, 130, 119));
        }

        // Small schools travel through the upper and lower bands, leaving the middle calm.
        int schoolCount = frame.Mode == DeskMode.Relax ? 9 : 6;
        for (int i = 0; i < schoolCount; i++)
        {
            bool upper = (i & 1) == 0;
            float cycle = Fraction(Noise(i + 1000) + t * (upper ? .011 : -.008));
            float x = -65 + cycle * (w + 130);
            float y = h * (upper ? .18f : .705f) + Wave(t, .48, i * 1.9) * 16;
            Color color = i % 3 == 0 ? Color.FromArgb(223, 202, 159) : Color.FromArgb(143, 200, 194);
            DrawFish(g, x + px * .6f, y + py * .6f, 25 + Noise(i + 24) * 13,
                upper ? 1 : -1, color, t, i, .65f);
        }

        float leftX = w * .17f + Wave(t, .24, .3) * 48 + px;
        float rightX = w * .84f + Wave(t, .21, 2.1) * 43 + px;
        DrawFish(g, leftX, h * .39f + Wave(t, .39, .8) * 24 + py, 70, 1,
            Mix(Color.FromArgb(249, 191, 129), Color.FromArgb(235, 174, 115), night), t, 2, 1);
        DrawFish(g, rightX, h * .49f + Wave(t, .35, 2.2) * 21 + py, 60, -1,
            Mix(Color.FromArgb(238, 183, 179), Color.FromArgb(217, 153, 165), night), t, 5, 1);
        if (frame.Mode == DeskMode.Relax)
            DrawFish(g, w * .82f + Wave(t, .3, 4) * 38, h * .29f + Wave(t, .4, 1) * 17,
                35, -1, Color.FromArgb(155, 212, 189), t, 7, .85f);

        float floorY = h * .905f;
        using (GraphicsPath sand = new GraphicsPath())
        {
            sand.AddBezier(0, floorY + 8, w * .2f, floorY - 36, w * .69f, floorY + 27, w, floorY - 12);
            sand.AddLine(w, floorY - 12, w, h);
            sand.AddLine(w, h, 0, h);
            sand.CloseFigure();
            using (LinearGradientBrush sandColor = new LinearGradientBrush(new RectangleF(0, floorY - 40, w, h - floorY + 40),
                Mix(Color.FromArgb(174, 177, 139), Color.FromArgb(86, 108, 113), night),
                Mix(Color.FromArgb(96, 142, 135), Color.FromArgb(41, 68, 87), night), 90f))
                g.FillPath(sandColor, sand);
        }
        for (int i = 0; i < 27; i++)
        {
            float x = Noise(i + 1500) * w;
            float y = floorY + 9 + Noise(i + 1600) * Math.Max(1, h - floorY - 15);
            FillEllipse(g, Color.FromArgb(45, 159, 168, 141), x, y, 2 + Noise(i + 12) * 4, 1.5f);
        }

        Rock(g, 45, floorY + 37, 164, 96, Mix(Color.FromArgb(76, 111, 108), Color.FromArgb(35, 65, 72), night));
        Rock(g, w - 252, floorY + 43, 190, 130, Mix(Color.FromArgb(83, 113, 105), Color.FromArgb(36, 67, 70), night));
        Rock(g, w - 150, floorY + 48, 116, 64, Mix(Color.FromArgb(115, 131, 110), Color.FromArgb(51, 80, 77), night));

        Color kelp = Mix(Color.FromArgb(86, 161, 129), Color.FromArgb(52, 125, 117), night);
        Color kelpLight = Mix(Color.FromArgb(162, 194, 133), Color.FromArgb(111, 169, 135), night);
        DrawKelp(g, 65, floorY + 26, 310, 7, t, 1, kelp, kelpLight);
        DrawKelp(g, 126, floorY + 30, 242, 6, t, 3, kelp, kelpLight);
        DrawKelp(g, 24, floorY + 34, 206, 5, t, 5, kelp, kelpLight);
        DrawKelp(g, w - 79, floorY + 23, 354, 8, t, 8, kelp, kelpLight);
        DrawKelp(g, w - 146, floorY + 32, 226, 6, t, 9, kelp, kelpLight);
        DrawKelp(g, w - 26, floorY + 30, 246, 6, t, 12, kelp, kelpLight);
        DrawCoral(g, 223, floorY + 36, 79, t, Mix(Color.FromArgb(223, 165, 159), Color.FromArgb(174, 133, 159), night));
        DrawCoral(g, w - 290, floorY + 34, 53, t, Mix(Color.FromArgb(183, 197, 143), Color.FromArgb(122, 161, 145), night));

        int bubbleCount = frame.Mode == DeskMode.Relax ? 34 : 20;
        using (Pen bubble = new Pen(Color.FromArgb(55, 171, 220, 206), 1.2f))
        using (Pen glint = new Pen(Color.FromArgb(92, 213, 237, 215), 1.4f))
        {
            for (int i = 0; i < bubbleCount; i++)
            {
                bool right = (i & 1) == 0;
                float x = w * (right ? .865f : .125f) + (Noise(i + 1900) - .5f) * 105;
                float y = h * (1.08f - Fraction(Noise(i + 1800) + t * (.024 + Noise(i + 1850) * .019)) * 1.17f);
                float r = 2 + Noise(i + 1910) * 4;
                x += Wave(t, .8, i) * 9;
                g.DrawEllipse(bubble, x - r, y - r, r * 2, r * 2);
                g.DrawArc(glint, x - r + .8f, y - r + .8f, r * 1.5f, r * 1.5f, 200, 70);
            }
        }
        ReadingVeil(g, w, h, frame.Mode, night, Mix(top, bottom, .55f));
    }

    static void NightTrain(Graphics g, float w, float h, SceneFrame frame,
        double t, float night, float px, float py)
    {
        Color cabin = Mix(Color.FromArgb(133, 113, 115), Color.FromArgb(63, 47, 67), night);
        FillGradient(g, new RectangleF(0, 0, w, h), cabin,
            Mix(Color.FromArgb(78, 83, 103), Color.FromArgb(34, 39, 61), night), 90);
        RectangleF outer = new RectangleF(w * .055f, h * .095f, w * .89f, h * .685f);
        RectangleF window = Inset(outer, 17);
        FillRounded(g, new RectangleF(outer.X - 9, outer.Y - 9, outer.Width + 18, outer.Height + 24), 36,
            Mix(Color.FromArgb(79, 73, 88), Color.FromArgb(26, 27, 44), night));
        FillRounded(g, outer, 30, Mix(Color.FromArgb(179, 151, 132), Color.FromArgb(120, 96, 104), night));

        GraphicsState windowState = g.Save();
        try
        {
            using (GraphicsPath clip = RoundedPath(window, 19)) g.SetClip(clip, CombineMode.Intersect);
            Color skyTop = Mix(Color.FromArgb(99, 105, 160), Color.FromArgb(35, 37, 78), night);
            Color skyMiddle = Mix(Color.FromArgb(162, 151, 185), Color.FromArgb(68, 68, 116), night);
            Color skyBottom = Mix(Color.FromArgb(237, 187, 166), Color.FromArgb(135, 112, 150), night);
            SkyGradient(g, window, skyTop, skyMiddle, skyBottom);
            Glow(g, new RectangleF(window.Right - 330 + px, window.Y - 115 + py, 430, 370),
                Color.FromArgb(51, 187, 183, 224));
            DrawCloud(g, window.X - 85 + px * .3f, window.Y + window.Height * .17f,
                Math.Min(470, window.Width * .43f), 64, t, 3,
                Mix(Color.FromArgb(66, 234, 198, 210), Color.FromArgb(30, 171, 148, 194), night));
            DrawCloud(g, window.Right - 295 + px * .5f, window.Y + window.Height * .42f,
                410, 51, t, 5,
                Mix(Color.FromArgb(73, 239, 208, 210), Color.FromArgb(30, 185, 155, 201), night));

            using (SolidBrush star = new SolidBrush(Color.FromArgb(frame.Mode == DeskMode.Relax ? 100 : 61, 214, 221, 217)))
            using (SolidBrush dimStar = new SolidBrush(Color.FromArgb(33, 212, 225, 225)))
            {
                for (int i = 0; i < 48; i++)
                {
                    float x = window.X + window.Width * Noise(i + 2300) + px * .25f;
                    float y = window.Y + window.Height * Noise(i + 2350) * .48f + py * .25f;
                    float r = Noise(i + 2400) > .8f ? 2 : 1.2f;
                    g.FillEllipse(x > w * .34f && x < w * .67f ? dimStar : star, x, y, r, r);
                }
            }
            float moonX = window.X + window.Width * .83f + px * .25f;
            float moonY = window.Y + window.Height * .20f + py * .25f;
            Glow(g, new RectangleF(moonX - 62, moonY - 62, 124, 124), Color.FromArgb(61, 230, 219, 199));
            FillEllipse(g, Color.FromArgb(233, 247, 229, 188), moonX - 15, moonY - 15, 30, 30);
            FillEllipse(g, Mix(skyTop, skyMiddle, .25f), moonX - 6, moonY - 18, 28, 28);
            Sparkle(g, window.X + window.Width * .15f + px * .25f, window.Y + 76,
                4.4f, Color.FromArgb(105, 236, 224, 223));
            Sparkle(g, window.Right - 82 + px * .25f, window.Y + window.Height * .36f,
                3.1f, Color.FromArgb(95, 227, 228, 238));

            Ridge(g, window, window.Y + window.Height * .68f, 91, t * 5 + px * .3, 10,
                Mix(Color.FromArgb(131, 125, 165), Color.FromArgb(76, 75, 119), night));
            Ridge(g, window, window.Y + window.Height * .79f, 68, t * 13 + px * .8, 17,
                Mix(Color.FromArgb(96, 111, 148), Color.FromArgb(49, 64, 103), night));
            DrawCity(g, window, window.Y + window.Height * .84f, night, -t * 15 + px, true);
            Ridge(g, window, window.Y + window.Height * .93f, 48, t * 35 + px * 1.8, 26,
                Mix(Color.FromArgb(65, 92, 116), Color.FromArgb(31, 48, 78), night));

            float ground = window.Bottom + 9;
            Color tree = Mix(Color.FromArgb(45, 71, 96), Color.FromArgb(24, 38, 63), night);
            for (int i = 0; i < 19; i++)
            {
                float x = window.X - 130 + Fraction(i / 19.0 - t * .034) * (window.Width + 260);
                float height = 29 + Noise(i + 2700) * 64;
                DrawPine(g, x, ground, height, tree);
            }
            for (int i = 0; i < 5; i++)
            {
                float x = window.X - 90 + Fraction(i / 5.0 - t * .047) * (window.Width + 180);
                float y = window.Bottom - 37 + Noise(i + 2750) * 18;
                Stroke(g, Color.FromArgb(94, 245, 182, 122), 1.8f, x, y, x + 22, y);
                FillEllipse(g, Color.FromArgb(183, 255, 213, 151), x + 15, y - 1.6f, 5, 3.2f);
            }

            // Reflections are broad and faint, like warm carriage lights in glass.
            GlassReflection(g, window, Color.FromArgb(9, 255, 220, 184));
            Glow(g, new RectangleF(window.X - 115, window.Y - 85, 355, 220), Color.FromArgb(48, 255, 190, 135));
            Glow(g, new RectangleF(window.Right - 140, window.Y + 15, 190, window.Height * .84f), Color.FromArgb(28, 249, 191, 153));
            FillRounded(g, new RectangleF(window.X + 35, window.Y + 32, 160, 5), 2,
                Color.FromArgb(47, 255, 222, 166));
            using (Pen reflection = new Pen(Color.FromArgb(14, 218, 230, 231), 1))
            {
                for (int i = 0; i < 9; i++)
                {
                    float y = window.Y + window.Height * (.13f + Noise(i + 2800) * .75f);
                    float x = window.X + window.Width * Noise(i + 2850);
                    g.DrawLine(reflection, x, y, x + 25 + Noise(i + 2870) * 55, y - 1);
                }
            }
        }
        finally { g.Restore(windowState); }

        using (GraphicsPath outline = RoundedPath(Inset(outer, 4), 26))
        using (Pen rim = new Pen(Color.FromArgb(134, 220, 176, 143), 1.6f)) g.DrawPath(rim, outline);
        FillGradient(g, new RectangleF(outer.X - 8, outer.Bottom + 5, outer.Width + 16, 24),
            Mix(Color.FromArgb(174, 144, 124), Color.FromArgb(112, 91, 99), night), cabin, 90);

        // Overhead light and seat edges establish the carriage without boxing in the center.
        Glow(g, new RectangleF(w * .1f, -68, w * .8f, 190), Color.FromArgb(58, 249, 184, 118));
        FillRounded(g, new RectangleF(w * .19f, 25, w * .62f, 7), 3, Color.FromArgb(222, 240, 198, 145));
        FillRounded(g, new RectangleF(w * .20f, 26, w * .60f, 2), 1, Color.FromArgb(210, 255, 231, 183));
        FillRect(g, Color.FromArgb(28, 9, 16, 22), w * .12f, 43, w * .76f, 3);
        DrawSeat(g, -43, h * .67f, 211, h * .41f, false, night);
        DrawSeat(g, w - 155, h * .70f, 210, h * .39f, true, night);

        float tableY = h * .868f;
        Glow(g, new RectangleF(w * .62f, tableY - 142, w * .37f, 300), Color.FromArgb(36, 247, 176, 129));
        using (GraphicsPath table = new GraphicsPath())
        {
            table.AddPolygon(new PointF[] {
                new PointF(w * .715f, tableY), new PointF(w * .94f, tableY - 9),
                new PointF(w * .98f, tableY + 38), new PointF(w * .69f, tableY + 44)
            });
            using (SolidBrush wood = new SolidBrush(Mix(Color.FromArgb(185, 147, 119), Color.FromArgb(138, 104, 95), night)))
                g.FillPath(wood, table);
            using (Pen contour = new Pen(Color.FromArgb(175, 44, 41, 60), 2.1f)) g.DrawPath(contour, table);
        }
        Stroke(g, Color.FromArgb(183, 226, 173, 128), 2, w * .69f, tableY + 42, w * .98f, tableY + 36);
        DrawCup(g, w * .855f, tableY + 18, .73f, Color.FromArgb(228, 202, 184), t, true);
        DrawBooks(g, w * .735f, tableY + 28, 105, night);
        ReadingVeil(g, w, h, frame.Mode, night, Color.FromArgb(55, 60, 94));
    }

    static void DrawCity(Graphics g, RectangleF view, float baseline, float night, double offset, bool train)
    {
        int count = 30;
        float cell = (view.Width + 120) / count;
        Color silhouette = train
            ? Mix(Color.FromArgb(87, 100, 137), Color.FromArgb(43, 56, 90), night)
            : Mix(Color.FromArgb(127, 151, 174), Color.FromArgb(49, 66, 99), night);
        using (SolidBrush buildings = new SolidBrush(silhouette))
        using (SolidBrush lights = new SolidBrush(Color.FromArgb((int)(35 + night * 100), 255, 210, 153)))
        using (Pen roofInk = new Pen(WithAlpha(Mix(silhouette, Color.FromArgb(32, 39, 65), .45f), 90), 1.2f))
        {
            for (int i = 0; i < count; i++)
            {
                float x = view.X - 60 + Fraction(i / (double)count + offset / (view.Width + 120)) * (view.Width + 120);
                float bh = (train ? 12 : 28) + Noise(i + 3100) * (train ? 41 : 91);
                float bw = cell * (.51f + Noise(i + 3200) * .4f);
                g.FillRectangle(buildings, x, baseline - bh, bw, bh + 80);
                if (i % 4 == 0) g.FillRectangle(buildings, x + bw * .3f, baseline - bh - 6, bw * .17f, 6);
                if (!train && i % 4 == 1)
                {
                    PointF[] roof = new PointF[] { new PointF(x - 2, baseline - bh),
                        new PointF(x + bw * .48f, baseline - bh - 11), new PointF(x + bw + 2, baseline - bh) };
                    g.FillPolygon(buildings, roof);
                    g.DrawLines(roofInk, roof);
                }
                if (!train && i % 6 == 0)
                    g.DrawLine(roofInk, x + bw * .65f, baseline - bh, x + bw * .65f, baseline - bh - 13);
                // Fewer lights in the middle pane make conversations easier to read.
                if (x > view.X + view.Width * .30f && x < view.X + view.Width * .70f && i % 3 != 0) continue;
                for (int j = 0; j < 3; j++)
                {
                    if (Noise(i * 5 + j + 3300) < .38f) continue;
                    float yy = baseline - bh + 9 + j * (train ? 7 : 16);
                    if (yy < baseline - 4) g.FillRectangle(lights, x + 5 + (j % 2) * Math.Max(1, bw - 13), yy, 3, train ? 2 : 4);
                }
            }
        }
    }

    static void DrawCurtain(Graphics g, float w, float h, bool right, float night, double t)
    {
        float edge = right ? w : 0;
        float direction = right ? -1 : 1;
        float width = Math.Min(w * .069f, 106);
        float sway = Wave(t, .18, right ? 1 : 0) * 3;
        using (GraphicsPath curtain = new GraphicsPath())
        {
            curtain.AddLine(edge, 0, edge + direction * width, 0);
            curtain.AddBezier(edge + direction * width, 0,
                edge + direction * (width - 9 + sway), h * .27f,
                edge + direction * (width * .45f), h * .54f,
                edge + direction * (width * .72f), h * .79f);
            curtain.AddLine(edge + direction * width * .72f, h * .79f, edge, h * .80f);
            curtain.CloseFigure();
            RectangleF rect = new RectangleF(right ? w - width : 0, 0, width, h * .81f);
            using (LinearGradientBrush cloth = new LinearGradientBrush(rect,
                Mix(Color.FromArgb(156, 167, 170), Color.FromArgb(48, 67, 86), night),
                Mix(Color.FromArgb(202, 197, 180), Color.FromArgb(85, 104, 119), night), right ? 180f : 0f))
                g.FillPath(cloth, curtain);
        }
        using (Pen seam = new Pen(Color.FromArgb(37, 225, 217, 190), 1.6f))
        {
            for (int i = 1; i < 4; i++)
            {
                float x = edge + direction * width * (i / 4f);
                g.DrawBezier(seam, x, 0, x + direction * sway, h * .27f,
                    x * (right ? 1 : .8f) + (right ? (w - x) * .2f : 0), h * .5f,
                    edge + direction * width * (i / 5.6f), h * .78f);
            }
        }
    }

    static void DrawLamp(Graphics g, float x, float y, float night)
    {
        Color metal = Mix(Color.FromArgb(125, 174, 153), Color.FromArgb(92, 139, 130), night);
        Color ink = Color.FromArgb(211, 40, 48, 63);
        Glow(g, new RectangleF(x - 115, y - 290, 310, 300), Color.FromArgb(71, 255, 185, 113));
        Glow(g, new RectangleF(x - 80, y - 42, 320, 105), Color.FromArgb(65, 255, 202, 132));
        using (GraphicsPath pool = new GraphicsPath())
        {
            pool.AddPolygon(new PointF[] {
                new PointF(x + 7, y - 203), new PointF(x + 83, y - 199),
                new PointF(x + 167, y + 3), new PointF(x - 51, y + 3)
            });
            using (LinearGradientBrush light = new LinearGradientBrush(new RectangleF(x - 55, y - 205, 230, 215),
                Color.FromArgb(47, 255, 218, 156), Color.FromArgb(5, 255, 207, 132), 90f)) g.FillPath(light, pool);
        }
        FillEllipse(g, Color.FromArgb(43, 35, 29, 46), x - 51, y - 2, 110, 19);
        FillEllipse(g, metal, x - 43, y - 10, 88, 19);
        OutlineEllipse(g, new RectangleF(x - 43, y - 10, 88, 19), ink, 2.1f);
        using (Pen baseLight = new Pen(Color.FromArgb(117, 223, 228, 173), 1.5f))
            g.DrawArc(baseLight, x - 36, y - 8, 72, 13, 195, 137);
        Stroke(g, ink, 9, x, y - 8, x - 11, y - 155);
        Stroke(g, ink, 8, x - 11, y - 155, x + 35, y - 231);
        Stroke(g, metal, 5.5f, x, y - 8, x - 11, y - 155);
        Stroke(g, metal, 5, x - 11, y - 155, x + 35, y - 231);
        FillEllipse(g, Mix(metal, Color.FromArgb(228, 215, 158), .25f), x - 18, y - 162, 14, 14);
        OutlineEllipse(g, new RectangleF(x - 18, y - 162, 14, 14), ink, 1.7f);
        using (GraphicsPath shade = new GraphicsPath())
        {
            shade.AddBezier(x + 9, y - 238, x + 29, y - 254, x + 59, y - 250, x + 73, y - 233);
            shade.AddLine(x + 73, y - 233, x + 94, y - 199);
            shade.AddBezier(x + 94, y - 199, x + 61, y - 190, x + 24, y - 190, x - 9, y - 201);
            shade.CloseFigure();
            using (LinearGradientBrush finish = new LinearGradientBrush(new RectangleF(x - 10, y - 252, 106, 63),
                Mix(metal, Color.FromArgb(204, 217, 173), .28f), metal, 90f)) g.FillPath(finish, shade);
            using (GraphicsPath shadow = new GraphicsPath())
            {
                shadow.AddBezier(x + 48, y - 247, x + 59, y - 246, x + 67, y - 241, x + 73, y - 233);
                shadow.AddLine(x + 73, y - 233, x + 94, y - 199);
                shadow.AddBezier(x + 94, y - 199, x + 77, y - 194, x + 65, y - 194, x + 54, y - 194);
                shadow.AddBezier(x + 54, y - 194, x + 64, y - 212, x + 59, y - 233, x + 48, y - 247);
                using (SolidBrush brush = new SolidBrush(Mix(metal, Color.FromArgb(45, 66, 82), .35f))) g.FillPath(brush, shadow);
            }
            Outline(g, shade, ink, 2.2f);
        }
        using (Pen shadeLight = new Pen(Color.FromArgb(143, 223, 234, 189), 2.2f))
            g.DrawBezier(shadeLight, x + 10, y - 232, x + 23, y - 245, x + 41, y - 246, x + 51, y - 241);
        FillEllipse(g, Color.FromArgb(252, 250, 213, 143), x - 7, y - 205, 100, 12);
        OutlineEllipse(g, new RectangleF(x - 7, y - 205, 100, 12), Color.FromArgb(175, 117, 91, 66), 1.4f);
        FillEllipse(g, Color.FromArgb(233, 255, 242, 192), x + 18, y - 203, 48, 7);
        Stroke(g, Color.FromArgb(151, 218, 225, 169), 1.2f, x - 2, y - 15, x - 12, y - 148);
    }

    static void DrawBooks(Graphics g, float x, float y, float width, float night)
    {
        Color pages = Mix(Color.FromArgb(246, 228, 188), Color.FromArgb(215, 195, 163), night);
        Color ink = Color.FromArgb(170, 57, 52, 68);
        FillRounded(g, new RectangleF(x - 5, y - 16, width + 9, 16), 2,
            Mix(Color.FromArgb(121, 172, 168), Color.FromArgb(91, 143, 143), night));
        using (GraphicsPath cover = RoundedPath(new RectangleF(x - 5, y - 16, width + 9, 16), 2))
            Outline(g, cover, ink, 1.5f);
        FillRect(g, pages, x - 2, y - 13, width + 4, 10);
        Stroke(g, Color.FromArgb(55, 84, 91, 76), 1, x + 2, y - 8, x + width - 3, y - 8);
        FillRounded(g, new RectangleF(x + 7, y - 35, width - 14, 18), 2,
            Mix(Color.FromArgb(224, 154, 132), Color.FromArgb(193, 128, 121), night));
        using (GraphicsPath cover = RoundedPath(new RectangleF(x + 7, y - 35, width - 14, 18), 2))
            Outline(g, cover, ink, 1.5f);
        FillRect(g, pages, x + 12, y - 31, width - 24, 10);
        Stroke(g, Color.FromArgb(63, 127, 100, 96), 1, x + 16, y - 25, x + width - 18, y - 25);
        FillRect(g, Color.FromArgb(223, 239, 188, 112), x + width * .73f, y - 33, 8, 22);
        FillRect(g, Color.FromArgb(72, 0, 0, 0), x + 7, y - 19, width - 14, 2);
    }

    static void DrawCup(Graphics g, float x, float y, float size, Color ceramic, double t, bool steam)
    {
        GraphicsState saved = g.Save();
        try
        {
            g.TranslateTransform(x, y);
            g.ScaleTransform(size, size);
            Color ink = Color.FromArgb(190, 73, 59, 76);
            FillEllipse(g, Color.FromArgb(40, 34, 27, 46), -47, -2, 98, 16);
            FillEllipse(g, Mix(ceramic, Color.FromArgb(104, 110, 128), .23f), -47, -8, 96, 16);
            OutlineEllipse(g, new RectangleF(-47, -8, 96, 16), ink, 1.8f);
            FillEllipse(g, ceramic, -42, -11, 85, 14);
            using (Pen handleInk = new Pen(ink, 8.5f)) g.DrawArc(handleInk, 19, -47, 28, 32, -91, 181);
            using (Pen handle = new Pen(ceramic, 5.4f)) g.DrawArc(handle, 19, -47, 28, 32, -91, 181);
            using (GraphicsPath body = new GraphicsPath())
            {
                body.AddLine(-31, -54, 29, -54);
                body.AddBezier(29, -54, 27, -8, 23, -4, 0, -4);
                body.AddBezier(0, -4, -24, -4, -28, -9, -31, -54);
                using (LinearGradientBrush glaze = new LinearGradientBrush(new RectangleF(-32, -55, 64, 52),
                    Mix(ceramic, Color.FromArgb(255, 242, 206), .22f), ceramic, 0f))
                    g.FillPath(glaze, body);
                using (GraphicsPath shadow = new GraphicsPath())
                {
                    shadow.AddLine(11, -54, 29, -54);
                    shadow.AddBezier(29, -54, 27, -8, 23, -4, 0, -4);
                    shadow.AddBezier(0, -4, 20, -14, 16, -32, 11, -54);
                    using (SolidBrush brush = new SolidBrush(Mix(ceramic, Color.FromArgb(107, 109, 142), .24f)))
                        g.FillPath(brush, shadow);
                }
                Outline(g, body, ink, 2);
            }
            FillEllipse(g, Mix(ceramic, Color.FromArgb(255, 244, 211), .52f), -31, -60, 60, 13);
            OutlineEllipse(g, new RectangleF(-31, -60, 60, 13), ink, 1.4f);
            FillEllipse(g, Color.FromArgb(90, 67, 66), -26, -57, 50, 9);
            using (Pen teaLight = new Pen(Color.FromArgb(138, 190, 146, 111), 1.2f))
                g.DrawArc(teaLight, -23, -55, 42, 5, 185, 115);
            Sparkle(g, -4, -28, 6.3f, Color.FromArgb(191, 255, 244, 213));
            using (Pen shine = new Pen(Color.FromArgb(165, 255, 248, 224), 2.2f))
                g.DrawBezier(shine, -23, -46, -23, -34, -22, -24, -19, -18);
            if (steam)
            {
                using (Pen plume = new Pen(Color.FromArgb(61, 255, 236, 216), 2.2f))
                using (Pen thin = new Pen(Color.FromArgb(35, 244, 234, 223), 1.4f))
                {
                    for (int i = 0; i < 3; i++)
                    {
                        float sx = -13 + i * 14;
                        float sway = Wave(t, .65, i * 1.9) * 10;
                        g.DrawBezier(i == 1 ? plume : thin,
                            sx, -65, sx - 12 + sway, -87, sx + 14 + sway, -101, sx + sway * .6f, -128 - i * 6);
                    }
                }
            }
        }
        finally { g.Restore(saved); }
    }

    static void DrawPlant(Graphics g, float x, float y, int leaves, double t, float night, float px)
    {
        float height = 97 + (leaves - 4) * 5.5f;
        float rootY = y - 56;
        float bend = Wave(t, .32, 2) * 4 + px * .15f;
        Color stem = Mix(Color.FromArgb(94, 140, 100), Color.FromArgb(77, 124, 104), night);
        using (Pen stalk = new Pen(stem, 3))
            g.DrawBezier(stalk, x, rootY, x - 7, rootY - height * .4f, x + bend, rootY - height * .7f, x + bend, rootY - height);
        for (int i = 0; i < leaves; i++)
        {
            float fraction = (i + 1f) / (leaves + 1f);
            float yy = rootY - 10 - fraction * height * .87f;
            float side = (i & 1) == 0 ? -1 : 1;
            float length = 35 + (1 - fraction) * 22;
            float sway = Wave(t, .32, 2 + i * .1) * 3 * fraction;
            PointF from = new PointF(x + bend * fraction, yy);
            PointF tip = new PointF(from.X + side * length + sway, yy - 20 - fraction * 9);
            Color leaf = Mix(Color.FromArgb(147, 185, 121), Color.FromArgb(106, 158, 126), night);
            if ((i & 1) != 0) leaf = Mix(leaf, Color.FromArgb(206, 209, 145), .16f);
            Leaf(g, from, tip, 12 + (1 - fraction) * 5, leaf, WithAlpha(Color.FromArgb(228, 227, 166), 75));
        }
        Leaf(g, new PointF(x + bend, rootY - height + 7),
            new PointF(x + bend + 8, rootY - height - 31), 11,
            Mix(Color.FromArgb(180, 200, 133), Color.FromArgb(142, 179, 129), night), Color.FromArgb(70, 234, 232, 174));
        FillEllipse(g, Color.FromArgb(44, 40, 28, 46), x - 47, y - 5, 95, 16);
        using (GraphicsPath pot = new GraphicsPath())
        {
            pot.AddPolygon(new PointF[] { new PointF(x - 38, rootY), new PointF(x + 38, rootY),
                new PointF(x + 28, y), new PointF(x - 28, y) });
            using (LinearGradientBrush clay = new LinearGradientBrush(new RectangleF(x - 39, rootY, 78, 57),
                Mix(Color.FromArgb(232, 174, 142), Color.FromArgb(206, 144, 123), night),
                Mix(Color.FromArgb(209, 140, 123), Color.FromArgb(172, 117, 112), night), 0f)) g.FillPath(clay, pot);
            using (GraphicsPath shadow = new GraphicsPath())
            {
                shadow.AddPolygon(new PointF[] { new PointF(x + 15, rootY), new PointF(x + 38, rootY),
                    new PointF(x + 28, y), new PointF(x + 5, y) });
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(53, 77, 65, 105))) g.FillPath(brush, shadow);
            }
            Outline(g, pot, Color.FromArgb(186, 75, 55, 69), 2.1f);
        }
        FillRounded(g, new RectangleF(x - 41, rootY - 5, 82, 12), 3,
            Mix(Color.FromArgb(246, 192, 157), Color.FromArgb(226, 164, 135), night));
        using (GraphicsPath lip = RoundedPath(new RectangleF(x - 41, rootY - 5, 82, 12), 3))
            Outline(g, lip, Color.FromArgb(170, 88, 61, 73), 1.7f);
        FillEllipse(g, Color.FromArgb(83, 66, 67), x - 35, rootY - 8, 70, 9);
        Stroke(g, Color.FromArgb(155, 255, 222, 170), 1.7f, x - 34, rootY + 3, x + 26, rootY + 3);
        Stroke(g, Color.FromArgb(111, 255, 219, 173), 2.2f, x - 28, rootY + 13, x - 22, y - 9);
        if (leaves >= 10)
        {
            // A tiny new blossom rewards sustained focus; it is deterministic and local.
            float bx = x + bend + 8, by = rootY - height - 33;
            for (int i = 0; i < 5; i++)
            {
                float a = i * Tau / 5;
                FillEllipse(g, Color.FromArgb(250, 210, 141), bx + (float)Math.Cos(a) * 5 - 4,
                    by + (float)Math.Sin(a) * 5 - 4, 8, 8);
            }
            FillEllipse(g, Color.FromArgb(173, 123, 87), bx - 2.5f, by - 2.5f, 5, 5);
        }
    }

    static void DrawFish(Graphics g, float x, float y, float size, int direction,
        Color color, double t, int seed, float opacity)
    {
        GraphicsState saved = g.Save();
        try
        {
            g.TranslateTransform(x, y);
            g.ScaleTransform(direction * size / 60f, size / 60f);
            g.RotateTransform(Wave(t, .55, seed) * 3);
            float tail = Wave(t, 2.4, seed) * 7;
            Color fin = WithAlpha(Mix(color, Color.FromArgb(243, 219, 181), .19f), (int)(209 * opacity));
            Color bodyColor = WithAlpha(color, (int)(249 * opacity));
            Color ink = Color.FromArgb((int)(164 * opacity), 49, 69, 91);
            using (GraphicsPath tailPath = new GraphicsPath())
            {
                tailPath.AddBezier(-31, 0, -43, -8, -50, -18 - tail * .4f, -57, -24 - tail * .3f);
                tailPath.AddBezier(-57, -24 - tail * .3f, -62 + tail * .5f, -9, -52 - tail * .5f, 8, -57, 24 + tail * .3f);
                tailPath.AddBezier(-57, 24 + tail * .3f, -44, 15, -41, 6, -31, 0);
                using (SolidBrush brush = new SolidBrush(fin)) g.FillPath(brush, tailPath);
                Outline(g, tailPath, ink, 1.2f);
            }
            using (GraphicsPath topFin = new GraphicsPath())
            {
                topFin.AddBezier(-20, -12, -18, -35, 8, -37, 20, -10);
                topFin.AddLine(20, -10, -20, -12);
                using (SolidBrush brush = new SolidBrush(fin)) g.FillPath(brush, topFin);
                Outline(g, topFin, ink, 1.1f);
            }
            using (GraphicsPath body = new GraphicsPath())
            {
                body.AddBezier(-35, 0, -13, -28, 20, -29, 39, -5);
                body.AddBezier(39, -5, 45, 1, 37, 8, 27, 11);
                body.AddBezier(27, 11, 6, 26, -19, 19, -35, 0);
                using (LinearGradientBrush scales = new LinearGradientBrush(new RectangleF(-37, -27, 79, 53),
                    WithAlpha(Mix(color, Color.FromArgb(255, 237, 193), .22f), bodyColor.A), bodyColor, 90f))
                    g.FillPath(scales, body);
                using (GraphicsPath shadow = new GraphicsPath())
                {
                    shadow.AddBezier(-33, 1, -11, 13, 12, 12, 40, -1);
                    shadow.AddBezier(40, -1, 42, 3, 34, 9, 27, 11);
                    shadow.AddBezier(27, 11, 6, 26, -19, 19, -33, 1);
                    using (SolidBrush brush = new SolidBrush(WithAlpha(Mix(color, Color.FromArgb(75, 104, 139), .30f), (int)(152 * opacity))))
                        g.FillPath(brush, shadow);
                }
                Outline(g, body, ink, 1.45f);
            }
            using (GraphicsPath finPath = new GraphicsPath())
            {
                finPath.AddBezier(2, 4, -7, 18, 5, 32 + tail * .3f, 13, 9);
                finPath.CloseFigure();
                using (SolidBrush brush = new SolidBrush(fin)) g.FillPath(brush, finPath);
                Outline(g, finPath, WithAlpha(ink, (int)(97 * opacity)), 1);
            }
            using (Pen gill = new Pen(WithAlpha(Mix(color, Color.FromArgb(42, 70, 73), .45f), (int)(100 * opacity)), 1.3f))
                g.DrawArc(gill, 8, -13, 18, 27, 102, 141);
            using (Pen sheen = new Pen(Color.FromArgb((int)(137 * opacity), 255, 246, 214), 1.7f))
            {
                g.DrawBezier(sheen, -20, -9, -6, -20, 9, -19, 19, -13);
                g.DrawLine(sheen, -36, -1, -51, -14 - tail * .2f);
            }
            // The two bright eye highlights and a tiny smile keep the fish friendly.
            FillEllipse(g, Color.FromArgb((int)(242 * opacity), 255, 244, 221), 23, -12, 10, 12);
            OutlineEllipse(g, new RectangleF(23, -12, 10, 12), ink, 1.1f);
            FillEllipse(g, Color.FromArgb((int)(249 * opacity), 41, 61, 86), 26.8f, -9.5f, 5.8f, 8);
            FillEllipse(g, Color.FromArgb((int)(251 * opacity), 255, 252, 236), 27.3f, -9.2f, 2.7f, 2.8f);
            FillEllipse(g, Color.FromArgb((int)(205 * opacity), 232, 245, 242), 30.1f, -4.5f, 1.2f, 1.2f);
            FillEllipse(g, Color.FromArgb((int)(65 * opacity), 252, 161, 157), 22, 2, 8, 3.6f);
            using (Pen smile = new Pen(WithAlpha(ink, (int)(147 * opacity)), 1.1f))
                g.DrawArc(smile, 35, -1, 5, 4, 15, 125);
        }
        finally { g.Restore(saved); }
    }

    static void DrawRibbon(Graphics g, float x, float y, float height, float width, float sway, Color color)
    {
        using (GraphicsPath blade = new GraphicsPath())
        {
            blade.AddBezier(x - width * .3f, y, x + sway - width, y - height * .43f,
                x - sway * .5f, y - height * .77f, x + sway, y - height);
            blade.AddBezier(x + sway, y - height, x + width + sway * .4f, y - height * .67f,
                x + width + sway, y - height * .35f, x + width * .3f, y);
            blade.CloseFigure();
            using (SolidBrush brush = new SolidBrush(color)) g.FillPath(brush, blade);
        }
    }

    static void DrawKelp(Graphics g, float x, float y, float height, int leaves,
        double t, int seed, Color dark, Color light)
    {
        float sway = Wave(t, .42, seed * 1.4) * 18;
        using (Pen stem = new Pen(dark, 3.6f))
            g.DrawBezier(stem, x, y, x - sway, y - height * .3f, x + sway * .8f, y - height * .6f, x + sway, y - height);
        for (int i = 0; i < leaves; i++)
        {
            float f = (i + .6f) / leaves;
            float yy = y - height * f;
            float side = (i & 1) == 0 ? -1 : 1;
            float bend = Wave(t, .42, seed * 1.4 + f) * 18 * f;
            float length = 34 + (1 - f) * 20;
            PointF start = new PointF(x + sway * f, yy);
            PointF tip = new PointF(start.X + side * length + bend, yy - 22 - 19 * f);
            Leaf(g, start, tip, 10 + (1 - f) * 7, (i & 1) == 0 ? dark : light,
                Color.FromArgb(30, 186, 213, 152));
        }
        Leaf(g, new PointF(x + sway, y - height + 24), new PointF(x + sway * 1.3f, y - height - 25),
            10, light, Color.FromArgb(26, 186, 213, 152));
    }

    static void DrawCoral(Graphics g, float x, float y, float height, double t, Color color)
    {
        using (Pen branch = new Pen(color, 7))
        using (Pen tip = new Pen(Mix(color, Color.FromArgb(192, 186, 146), .23f), 4))
        {
            branch.StartCap = branch.EndCap = LineCap.Round;
            tip.StartCap = tip.EndCap = LineCap.Round;
            for (int i = 0; i < 5; i++)
            {
                float side = (i - 2) * height * .24f;
                float top = y - height * (.65f + Noise(i + 4000) * .35f);
                float sway = Wave(t, .38, i * 1.3) * 2.5f;
                g.DrawBezier(branch, x, y, x + side * .5f, y - height * .25f,
                    x + side, top + 15, x + side + sway, top);
                g.DrawBezier(tip, x + side * .7f, y - height * .42f,
                    x + side * .7f + 9, y - height * .58f,
                    x + side + 17 + sway, top + 17, x + side + 17 + sway, top + 3);
            }
        }
    }

    static void Rock(Graphics g, float x, float bottom, float width, float height, Color color)
    {
        using (GraphicsPath rock = new GraphicsPath())
        {
            rock.AddBezier(x, bottom - 7, x + width * .06f, bottom - height * .67f,
                x + width * .23f, bottom - height, x + width * .49f, bottom - height * .94f);
            rock.AddBezier(x + width * .49f, bottom - height * .94f, x + width * .82f, bottom - height * .93f,
                x + width * .94f, bottom - height * .35f, x + width, bottom - 4);
            rock.AddBezier(x + width, bottom - 4, x + width * .7f, bottom + 11, x + width * .19f, bottom + 12, x, bottom - 7);
            using (LinearGradientBrush stone = new LinearGradientBrush(new RectangleF(x, bottom - height, width, height + 15),
                Mix(color, Color.FromArgb(193, 198, 164), .24f), Mix(color, Color.FromArgb(42, 58, 87), .18f), 75f))
                g.FillPath(stone, rock);
            Outline(g, rock, Color.FromArgb(91, 35, 66, 86), 1.5f);
        }
        using (GraphicsPath facet = new GraphicsPath())
        {
            facet.AddBezier(x + width * .13f, bottom - height * .43f,
                x + width * .22f, bottom - height * .78f, x + width * .33f, bottom - height * .89f,
                x + width * .49f, bottom - height * .86f);
            facet.AddLine(x + width * .49f, bottom - height * .86f, x + width * .72f, bottom - height * .64f);
            facet.AddLine(x + width * .72f, bottom - height * .64f, x + width * .39f, bottom - height * .56f);
            facet.CloseFigure();
            using (SolidBrush light = new SolidBrush(Color.FromArgb(24, 210, 226, 181))) g.FillPath(light, facet);
        }
        using (Pen highlight = new Pen(Color.FromArgb(26, 182, 198, 161), 2))
            g.DrawBezier(highlight, x + width * .16f, bottom - height * .42f,
                x + width * .2f, bottom - height * .65f, x + width * .38f, bottom - height * .83f,
                x + width * .55f, bottom - height * .77f);
    }

    static void DrawPine(Graphics g, float x, float y, float height, Color color)
    {
        using (GraphicsPath tree = new GraphicsPath())
        {
            tree.AddPolygon(new PointF[] {
                new PointF(x, y - height), new PointF(x + height * .18f, y - height * .57f),
                new PointF(x + height * .09f, y - height * .58f), new PointF(x + height * .29f, y - height * .24f),
                new PointF(x + height * .16f, y - height * .25f), new PointF(x + height * .38f, y),
                new PointF(x - height * .38f, y), new PointF(x - height * .16f, y - height * .25f),
                new PointF(x - height * .29f, y - height * .24f), new PointF(x - height * .09f, y - height * .58f),
                new PointF(x - height * .18f, y - height * .57f)
            });
            using (SolidBrush brush = new SolidBrush(color)) g.FillPath(brush, tree);
        }
    }

    static void DrawSeat(Graphics g, float x, float y, float width, float height, bool right, float night)
    {
        Color fabric = Mix(Color.FromArgb(115, 140, 160), Color.FromArgb(66, 91, 125), night);
        FillRounded(g, new RectangleF(x - 6, y - 3, width + 12, height + 5), 43, Color.FromArgb(31, 33, 53));
        using (GraphicsPath seat = RoundedPath(new RectangleF(x, y, width, height), 38))
        using (LinearGradientBrush cloth = new LinearGradientBrush(new RectangleF(x, y, width, height),
            Mix(fabric, Color.FromArgb(192, 169, 158), .18f), fabric, right ? 180f : 0f)) g.FillPath(cloth, seat);
        FillRounded(g, new RectangleF(x + 17, y + 12, width - 34, 67), 23,
            Mix(fabric, Color.FromArgb(200, 178, 159), .19f));
        FillRounded(g, new RectangleF(x + width * .68f, y + 97, width * .23f, Math.Max(1, height - 118)), 16,
            WithAlpha(Mix(fabric, Color.FromArgb(38, 43, 75), .25f), 80));
        using (GraphicsPath headrest = RoundedPath(new RectangleF(x + 17, y + 12, width - 34, 67), 23))
            Outline(g, headrest, Color.FromArgb(99, 41, 53, 81), 1.6f);
        using (Pen warmEdge = new Pen(Color.FromArgb(77, 248, 202, 154), 1.8f))
            g.DrawBezier(warmEdge, x + 28, y + 28, x + 35, y + 13, x + width - 35, y + 13, x + width - 28, y + 28);
        using (Pen seam = new Pen(Color.FromArgb(70, 176, 183, 180), 1.4f))
        {
            g.DrawLine(seam, x + 26, y + 107, x + 26, y + height - 15);
            g.DrawLine(seam, x + width - 26, y + 107, x + width - 26, y + height - 15);
            g.DrawLine(seam, x + 28, y + 83, x + width - 28, y + 83);
        }
        float armX = right ? x - 17 : x + width - 22;
        FillRounded(g, new RectangleF(armX, y + height * .61f, 47, 26), 10,
            Mix(Color.FromArgb(85, 93, 115), Color.FromArgb(45, 55, 82), night));
    }

    static void Ridge(Graphics g, RectangleF area, float baseline, float amplitude,
        double offset, int seed, Color color)
    {
        const int segments = 32;
        using (GraphicsPath ridge = new GraphicsPath())
        {
            PointF[] points = new PointF[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                float x = area.X - 4 + (area.Width + 8) * i / segments;
                double sample = (x + offset) / 172.0;
                float shape = .42f + .25f * (float)Math.Sin(sample + seed)
                    + .14f * (float)Math.Sin(sample * 1.87 + seed * .3)
                    + .075f * (float)Math.Sin(sample * 3.7 + seed * 1.8);
                points[i] = new PointF(x, baseline - amplitude * shape);
            }
            ridge.AddCurve(points, .38f);
            ridge.AddLine(points[segments], new PointF(area.Right + 4, area.Bottom + 5));
            ridge.AddLine(area.Right + 4, area.Bottom + 5, area.X - 4, area.Bottom + 5);
            ridge.CloseFigure();
            using (SolidBrush brush = new SolidBrush(color)) g.FillPath(brush, ridge);
        }
    }

    static void SkyGradient(Graphics g, RectangleF rect, Color top, Color middle, Color bottom)
    {
        using (LinearGradientBrush sky = new LinearGradientBrush(rect, top, bottom, 90f))
        {
            ColorBlend blend = new ColorBlend(3);
            blend.Colors = new Color[] { top, middle, bottom };
            blend.Positions = new float[] { 0f, .57f, 1f };
            sky.InterpolationColors = blend;
            g.FillRectangle(sky, rect);
        }
    }

    static void DrawCloud(Graphics g, float x, float y, float width, float height,
        double t, int seed, Color color)
    {
        x += Wave(t, .055, seed) * 8;
        y += Wave(t, .041, seed * .7) * 2;
        using (GraphicsPath cloud = new GraphicsPath())
        {
            cloud.AddBezier(x, y + height * .64f, x + width * .02f, y + height * .37f,
                x + width * .10f, y + height * .30f, x + width * .17f, y + height * .46f);
            cloud.AddBezier(x + width * .17f, y + height * .46f, x + width * .19f, y - height * .10f,
                x + width * .36f, y - height * .11f, x + width * .43f, y + height * .29f);
            cloud.AddBezier(x + width * .43f, y + height * .29f, x + width * .55f, y + height * .10f,
                x + width * .67f, y + height * .14f, x + width * .74f, y + height * .47f);
            cloud.AddBezier(x + width * .74f, y + height * .47f, x + width * .86f, y + height * .31f,
                x + width * .98f, y + height * .46f, x + width, y + height * .69f);
            cloud.AddBezier(x + width, y + height * .69f, x + width * .82f, y + height,
                x + width * .17f, y + height, x, y + height * .64f);
            using (LinearGradientBrush wash = new LinearGradientBrush(new RectangleF(x, y, width, height),
                color, WithAlpha(color, 0), 90f)) g.FillPath(wash, cloud);
        }
        using (Pen rim = new Pen(WithAlpha(Mix(color, Color.FromArgb(255, 235, 224), .25f), color.A / 3), 1.5f))
            g.DrawBezier(rim, x + width * .17f, y + height * .46f,
                x + width * .19f, y - height * .10f, x + width * .36f, y - height * .11f,
                x + width * .43f, y + height * .29f);
    }

    static void DrawLightShaft(Graphics g, float x, float height, float width, Color color)
    {
        GraphicsState saved = g.Save();
        try
        {
            g.TranslateTransform(x, 0);
            g.RotateTransform(-18);
            Glow(g, new RectangleF(-width * .5f, -height * .65f, width, height * 1.65f), color);
        }
        finally { g.Restore(saved); }
    }

    static void DrawStringLights(Graphics g, float left, float right, float y, float night)
    {
        float width = right - left;
        Color wire = Mix(Color.FromArgb(122, 117, 132), Color.FromArgb(93, 82, 108), night);
        using (Pen cord = new Pen(wire, 1.4f))
        {
            g.DrawBezier(cord, left, y, left + width * .3f, y + 23, left + width * .7f, y + 23, right, y);
            for (int i = 0; i < 9; i++)
            {
                float f = (i + .5f) / 9;
                float bx = left + width * f;
                float by = y + 69 * f * (1 - f);
                g.DrawLine(cord, bx, by, bx, by + 6);
                Glow(g, new RectangleF(bx - 15, by - 6, 30, 30), Color.FromArgb((int)(38 + 28 * night), 255, 192, 124));
                FillEllipse(g, Color.FromArgb(239, 255, 215, 159), bx - 2.1f, by + 5, 4.2f, 5.7f);
                FillEllipse(g, Color.FromArgb(240, 255, 242, 211), bx - .7f, by + 5.5f, 1.5f, 3.1f);
            }
        }
    }

    static void Sparkle(Graphics g, float x, float y, float radius, Color color)
    {
        using (SolidBrush brush = new SolidBrush(color))
            g.FillPolygon(brush, new PointF[] {
                new PointF(x, y - radius), new PointF(x + radius * .22f, y - radius * .22f),
                new PointF(x + radius, y), new PointF(x + radius * .22f, y + radius * .22f),
                new PointF(x, y + radius), new PointF(x - radius * .22f, y + radius * .22f),
                new PointF(x - radius, y), new PointF(x - radius * .22f, y - radius * .22f)
            });
    }

    static void GlassReflection(Graphics g, RectangleF glass, Color color)
    {
        using (GraphicsPath reflection = new GraphicsPath())
        {
            reflection.AddPolygon(new PointF[] {
                new PointF(glass.X + glass.Width * .035f, glass.Y),
                new PointF(glass.X + glass.Width * .13f, glass.Y),
                new PointF(glass.X + glass.Width * .40f, glass.Bottom),
                new PointF(glass.X + glass.Width * .34f, glass.Bottom)
            });
            using (SolidBrush brush = new SolidBrush(color)) g.FillPath(brush, reflection);
        }
        using (Pen edge = new Pen(WithAlpha(color, Math.Min(30, color.A + 5)), 1.4f))
            g.DrawLine(edge, glass.X + glass.Width * .95f, glass.Y,
                glass.X + glass.Width * .75f, glass.Bottom);
    }

    static void ReadingVeil(Graphics g, float w, float h, DeskMode mode, float night, Color color)
    {
        int alpha = mode == DeskMode.Relax ? 21 : 39;
        // A feathered veil, not a visible panel. Preserve the interesting outer thirds.
        Glow(g, new RectangleF(w * .22f, -h * .15f, w * .56f, h * 1.18f), WithAlpha(color, alpha));
        if (mode == DeskMode.Present)
            FillRect(g, Color.FromArgb(night > .5f ? 12 : 7, 24, 35, 43), 0, 0, w, h);
    }

    static void Leaf(Graphics g, PointF start, PointF tip, float width, Color color, Color vein)
    {
        float dx = tip.X - start.X, dy = tip.Y - start.Y;
        float length = (float)Math.Sqrt(dx * dx + dy * dy);
        if (length < .01f) return;
        float nx = -dy / length * width, ny = dx / length * width;
        using (GraphicsPath leaf = new GraphicsPath())
        {
            leaf.AddBezier(start.X, start.Y,
                start.X + dx * .24f + nx, start.Y + dy * .24f + ny,
                start.X + dx * .71f + nx, start.Y + dy * .71f + ny, tip.X, tip.Y);
            leaf.AddBezier(tip.X, tip.Y,
                start.X + dx * .66f - nx * .7f, start.Y + dy * .66f - ny * .7f,
                start.X + dx * .2f - nx * .7f, start.Y + dy * .2f - ny * .7f, start.X, start.Y);
            using (SolidBrush brush = new SolidBrush(color)) g.FillPath(brush, leaf);
            Outline(g, leaf, WithAlpha(Mix(color, Color.FromArgb(34, 66, 84), .48f), 124), 1.1f);
        }
        using (GraphicsPath face = new GraphicsPath())
        {
            face.AddBezier(start.X, start.Y,
                start.X + dx * .24f + nx, start.Y + dy * .24f + ny,
                start.X + dx * .71f + nx, start.Y + dy * .71f + ny, tip.X, tip.Y);
            face.AddLine(tip, start);
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(20, 231, 239, 178))) g.FillPath(brush, face);
        }
        using (Pen pen = new Pen(vein, 1)) g.DrawLine(pen, start, tip);
    }

    static void Outline(Graphics g, GraphicsPath path, Color color, float width)
    {
        using (Pen pen = new Pen(color, width))
        {
            pen.LineJoin = LineJoin.Round;
            g.DrawPath(pen, path);
        }
    }

    static void OutlineEllipse(Graphics g, RectangleF rect, Color color, float width)
    {
        using (Pen pen = new Pen(color, width)) g.DrawEllipse(pen, rect);
    }

    static void Glow(Graphics g, RectangleF rect, Color color)
    {
        if (rect.Width <= 0 || rect.Height <= 0 || color.A == 0) return;
        using (GraphicsPath ellipse = new GraphicsPath())
        {
            ellipse.AddEllipse(rect);
            using (PathGradientBrush brush = new PathGradientBrush(ellipse))
            {
                brush.CenterColor = color;
                brush.SurroundColors = new Color[] { Color.FromArgb(0, color.R, color.G, color.B) };
                brush.CenterPoint = new PointF(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
                g.FillEllipse(brush, rect);
            }
        }
    }

    static void FillGradient(Graphics g, RectangleF rect, Color from, Color to, float angle)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;
        using (LinearGradientBrush brush = new LinearGradientBrush(rect, from, to, angle)) g.FillRectangle(brush, rect);
    }

    static void FillRect(Graphics g, Color color, float x, float y, float width, float height)
    {
        if (width <= 0 || height <= 0) return;
        using (SolidBrush brush = new SolidBrush(color)) g.FillRectangle(brush, x, y, width, height);
    }

    static void FillEllipse(Graphics g, Color color, float x, float y, float width, float height)
    {
        if (width <= 0 || height <= 0) return;
        using (SolidBrush brush = new SolidBrush(color)) g.FillEllipse(brush, x, y, width, height);
    }

    static void FillRounded(Graphics g, RectangleF rect, float radius, Color color)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;
        using (GraphicsPath path = RoundedPath(rect, radius))
        using (SolidBrush brush = new SolidBrush(color)) g.FillPath(brush, path);
    }

    static GraphicsPath RoundedPath(RectangleF rect, float radius)
    {
        GraphicsPath path = new GraphicsPath();
        float diameter = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        if (diameter < .01f)
        {
            path.AddRectangle(rect);
            return path;
        }
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    static void Stroke(Graphics g, Color color, float width, float x1, float y1, float x2, float y2)
    {
        using (Pen pen = new Pen(color, width))
        {
            pen.StartCap = pen.EndCap = LineCap.Round;
            g.DrawLine(pen, x1, y1, x2, y2);
        }
    }

    static RectangleF Inset(RectangleF rect, float amount)
    {
        return new RectangleF(rect.X + amount, rect.Y + amount, rect.Width - amount * 2, rect.Height - amount * 2);
    }

    static Color Mix(Color a, Color b, float fraction)
    {
        float f = FiniteUnit(fraction, 0);
        return Color.FromArgb((int)(a.A + (b.A - a.A) * f), (int)(a.R + (b.R - a.R) * f),
            (int)(a.G + (b.G - a.G) * f), (int)(a.B + (b.B - a.B) * f));
    }

    static Color WithAlpha(Color color, int alpha)
    {
        return Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), color.R, color.G, color.B);
    }

    static float FiniteUnit(float value, float fallback)
    {
        if (Single.IsNaN(value) || Single.IsInfinity(value)) return fallback;
        return Math.Max(0, Math.Min(1, value));
    }

    static float Wave(double time, double frequency, double phase)
    {
        return (float)Math.Sin(time * frequency + phase);
    }

    static float Fraction(double value)
    {
        return (float)(value - Math.Floor(value));
    }

    static float Noise(int seed)
    {
        unchecked
        {
            uint value = (uint)seed * 747796405u + 2891336453u;
            value = ((value >> (int)((value >> 28) + 4)) ^ value) * 277803737u;
            value = (value >> 22) ^ value;
            return (value & 0x00ffffffu) / 16777216f;
        }
    }
}
