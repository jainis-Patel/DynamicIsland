using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace DynamicIsland.Core;

public sealed partial class IslandNatureThemeControl
{
    private void RenderIvyUnderlay(DrawingContext dc, Rect p, double t)
    {
        EnsureIvyGeometry(p);
        if (_cachedVines == null || _cachedBranches == null) return;

        double cyc = t % 30.0;
        double fade = cyc > 27.0 ? (30.0 - cyc) / 3.0 : 1.0;
        if (fade <= 0.01) return;

        if (fade < 0.99) dc.PushOpacity(fade);

        _flowersBuffer.Clear();
        int[] ks = new int[_cachedVines.Count];
        for (int j = 0; j < _cachedVines.Count; j++)
        {
            double delay = j > 1 ? 1.2 : 0.0;
            ks[j] = (int)(_cachedVines[j].Length * EaseOutCubic((cyc - delay) / 10.0));
        }

        for (int j = 0; j < _cachedVines.Count; j++)
        {
            if (ks[j] > 2)
            {
                DrawVine(dc, _cachedVines[j], ks[j], j > 1 ? 1.8 : 2.6, t, _flowersBuffer, j, isBranch: false);
            }
        }

        for (int q = 0; q < _cachedBranches.Count; q++)
        {
            var b = _cachedBranches[q];
            int n = (int)(b.Points.Length * Clamp((ks[b.ParentIdx] - b.AttachIdx) / 32.0));
            if (n > 2)
            {
                DrawVine(dc, b.Points, n, 1.4, t, _flowersBuffer, q + 9, isBranch: true);
            }
        }

        // Draw botanical blooms along ivy vines
        foreach (var fl in _flowersBuffer)
        {
            DrawFlower(dc, fl.X, fl.Y, fl.S, fl.R, fl.Ty);
        }

        if (fade < 0.99) dc.Pop();
    }

    private void DrawVine(DrawingContext dc, VinePoint[] pts, int n, double w, double t, List<FlowerData> fl, int sd, bool isBranch)
    {
        if (n < 2) return;

        // 1. Draw Vine Stems (two-tone: dark bark base + light sap highlight)
        var stemGeom = new StreamGeometry();
        using (var ctx = stemGeom.Open())
        {
            ctx.BeginFigure(new Point(pts[0].X, pts[0].Y), isFilled: false, isClosed: false);
            for (int i = 1; i < n; i++)
            {
                ctx.LineTo(new Point(pts[i].X, pts[i].Y), isStroked: true, isSmoothJoin: true);
            }
        }
        stemGeom.Freeze();

        Pen darkPen = isBranch ? BranchDarkPen : (sd > 1 ? SecVineDarkPen : MainVineDarkPen);
        Pen lightPen = isBranch ? BranchLightPen : (sd > 1 ? SecVineLightPen : MainVineLightPen);
        dc.DrawGeometry(null, darkPen, stemGeom);
        dc.DrawGeometry(null, lightPen, stemGeom);

        // 2. Draw English Ivy Leaves alternating along the stem
        for (int i = 3; i < n; i += 3)
        {
            var pt = pts[i];
            double sd2 = ((i / 3) % 2 == 1) ? 1.0 : -1.0;
            double sc = EaseOutCubic((n - i) / 22.0);
            double h = ((uint)((i + sd * 31) * 2654435761u)) / 4294967296.0;
            double len = (10.5 + h * 8.0) * sc * _scale * (isBranch ? 0.8 : 1.0);
            if (len < 1.5) continue;

            double ang = Math.Atan2(pt.Ty, pt.Tx) + sd2 * (0.95 + Math.Sin(t * 1.2 + i * 0.7) * 0.1);

            // CRITICAL: Prevent top-edge clipping and squashing when pill is flush with monitor top!
            if (pt.Y < 14.0 && Math.Sin(ang) < 0)
            {
                // Align leaf smoothly along capsule contour (tangential or outwards/downwards)
                double cosSign = Math.Cos(ang) >= 0 ? 1.0 : -1.0;
                ang = cosSign > 0 ? 0.18 + h * 0.15 : Math.PI - 0.18 - h * 0.15;
            }

            double lx = pt.X + Math.Cos(ang) * len * 0.25;
            double ly = Math.Max(1.5, pt.Y + Math.Sin(ang) * len * 0.25);

            dc.DrawLine(MeadowStemDarkPen, new Point(pt.X, pt.Y), new Point(lx, ly));
            DrawIvyLeaf(dc, lx, ly, ang, len, h > 0.50);
        }

        // 3. Flower bud candidates spaced according to _bloomDensity
        int flowerSpacing = Math.Max(9, (int)(16.0 / Math.Max(0.25, _bloomDensity)));
        for (int i = 8; i < n; i += flowerSpacing + ((i * 7) % 5))
        {
            var pt = pts[i];
            double h = (((uint)(i * 40503 + sd * 977)) % 1000) / 1000.0;
            double k = EaseOutCubic((n - i) / 24.0);
            if (k > 0.05)
            {
                double fx = pt.X + pt.Nx * (5.0 + h * 5.0) * _scale;
                double fy = Math.Max(2.0, pt.Y + Math.Abs(pt.Ny) * (4.0 + h * 4.0) * _scale);
                // Cycle flower types: 0=Morning Glory, 1=Jasmine, 2=Lilac, 3=Pink Camellia
                int flowerType = ((i / 14) + sd) % 4;
                fl.Add(new FlowerData(
                    fx,
                    fy,
                    k * (0.85 + h * 0.4) * _scale,
                    h * 6.283 + Math.Sin(t + i) * 0.08,
                    flowerType));
            }
        }

        // Branch tips flower bud
        if (isBranch && n == pts.Length && n > 10)
        {
            var pt = pts[n - 10];
            fl.Add(new FlowerData(pt.X, Math.Max(2.0, pt.Y + 2.0), 0.85 * _scale, 0.5, (sd + n) % 4));
        }
    }

    private static void DrawIvyLeaf(DrawingContext dc, double x, double y, double ang, double len, bool isLight)
    {
        if (len <= 0.1) return;
        double w = len * 0.55;

        var mat = Matrix.Identity;
        mat.Rotate(ang * 180.0 / Math.PI);
        mat.Translate(x, y);

        dc.PushTransform(new MatrixTransform(mat));

        var scaleMat = Matrix.Identity;
        scaleMat.Scale(len, len);
        dc.PushTransform(new MatrixTransform(scaleMat));
        int penIdx = Math.Clamp((int)Math.Round(len) - 1, 0, IvyLeafOutlinePens.Length - 1);
        dc.DrawGeometry(isLight ? IvyLeafLightBrush : IvyLeafDarkBrush, IvyLeafOutlinePens[penIdx], UnitIvyLeafGeometry);
        dc.Pop();

        // Draw delicate palmate veins
        dc.DrawLine(IvyLeafVeinPen, new Point(0, 0), new Point(len * 0.9, 0));
        dc.DrawLine(IvyLeafVeinPen, new Point(len * 0.05, 0), new Point(len * 0.33, -w * 0.92));
        dc.DrawLine(IvyLeafVeinPen, new Point(len * 0.05, 0), new Point(len * 0.33, w * 0.92));
        dc.DrawLine(IvyLeafVeinPen, new Point(len * 0.5, 0), new Point(len * 0.64, -w * 0.24));
        dc.DrawLine(IvyLeafVeinPen, new Point(len * 0.5, 0), new Point(len * 0.64, w * 0.24));

        dc.Pop();
    }

    private static void DrawFlower(DrawingContext dc, double x, double y, double s, double r, int ty)
    {
        if (s <= 0.02) return;
        switch (ty)
        {
            case 0: DrawMorningGlory(dc, x, y, s, r); break;
            case 1: DrawJasmine(dc, x, y, s, r); break;
            case 2: DrawBlossom(dc, x, y, s * 1.05, r, LilacPetalBrush, StamenPurplePen); break;
            case 3:
            default: DrawBlossom(dc, x, y, s, r, RosePetalBrush, StamenPinkPen); break;
        }
    }

    private static void DrawMorningGlory(DrawingContext dc, double x, double y, double s, double r)
    {
        if (s <= 0.02) return;

        var mat = Matrix.Identity;
        mat.Scale(s, s);
        mat.Rotate(r * 180.0 / Math.PI);
        mat.Translate(x, y);

        dc.PushTransform(new MatrixTransform(mat));
        dc.DrawGeometry(GloryRadialBrush, null, MorningGloryGeometry);

        for (int i = 0; i < 5; i++)
        {
            double a = i * 1.2566 + 0.6283;
            dc.DrawLine(GloryRaysPen, new Point(Math.Cos(a) * 1.5, Math.Sin(a) * 1.5), new Point(Math.Cos(a) * 9.0, Math.Sin(a) * 9.0));
        }
        dc.DrawEllipse(GloryCenterBrush, null, new Point(0, 0), 1.8, 1.8);

        dc.Pop();
    }

    private static void DrawJasmine(DrawingContext dc, double x, double y, double s, double r)
    {
        if (s <= 0.02) return;

        var mat = Matrix.Identity;
        mat.Scale(s, s);
        mat.Rotate(r * 180.0 / Math.PI);
        mat.Translate(x, y);

        dc.PushTransform(new MatrixTransform(mat));

        // 5 outer ivory petals
        for (int i = 0; i < 5; i++)
        {
            double a = i * (Math.PI * 2.0 / 5.0);
            DrawPetal(dc, 10.0, 3.4, JasminePetal1Brush, a, notched: false);
        }
        // 5 inner white petals offset by 36 deg
        for (int i = 0; i < 5; i++)
        {
            double a = i * (Math.PI * 2.0 / 5.0) + 0.63;
            DrawPetal(dc, 7.0, 2.8, JasminePetal2Brush, a, notched: false);
        }

        dc.DrawEllipse(JasmineCenterBrush, null, new Point(0, 0), 1.8, 1.8);

        dc.Pop();
    }

    private static void DrawBlossom(DrawingContext dc, double x, double y, double s, double r, Brush petalBrush, Pen stamenPen)
    {
        if (s <= 0.02) return;

        var mat = Matrix.Identity;
        mat.Scale(s, s);
        mat.Rotate(r * 180.0 / Math.PI);
        mat.Translate(x, y);

        dc.PushTransform(new MatrixTransform(mat));

        // 5 notched petals
        for (int i = 0; i < 5; i++)
        {
            double a = i * (Math.PI * 2.0 / 5.0);
            DrawPetal(dc, 8.0, 4.6, petalBrush, a, notched: true);
        }

        // 9 stamens with golden anthers
        for (int i = 0; i < 9; i++)
        {
            double a = i * (Math.PI * 2.0 / 9.0) + 0.3;
            Point pEnd = new(Math.Cos(a) * 3.4, Math.Sin(a) * 3.4);
            Point pAnther = new(Math.Cos(a) * 3.7, Math.Sin(a) * 3.7);
            dc.DrawLine(stamenPen, new Point(0, 0), pEnd);
            dc.DrawEllipse(GoldenPollenBrush, null, pAnther, 0.7, 0.7);
        }

        dc.Pop();
    }

    private static void DrawPetal(DrawingContext dc, double len, double wd, Brush brush, double rot, bool notched)
    {
        if (len <= 0.05 || wd <= 0.05) return;

        var mat = Matrix.Identity;
        mat.Rotate(rot * 180.0 / Math.PI);

        dc.PushTransform(new MatrixTransform(mat));

        var scaleMat = Matrix.Identity;
        scaleMat.Scale(len, wd);
        dc.PushTransform(new MatrixTransform(scaleMat));
        dc.DrawGeometry(brush, null, notched ? UnitNotchedPetalGeometry : UnitSmoothPetalGeometry);
        dc.Pop();

        dc.DrawLine(PetalSepPen, new Point(len * 0.1, 0), new Point(len * 0.8, 0));
        dc.Pop();
    }

    private void RenderIvyOverlay(DrawingContext dc, Rect p, double t)
    {
        // 1. Shimmering golden pollen / firefly specks drifting past the island in the visible airspace
        if (_pollenEnabled && _pollenCount > 0)
        {
            int pollenMax = Math.Clamp(_pollenCount, 0, 30);
            for (int j = 0; j < pollenMax; j++)
            {
                double px = p.X - 35.0 + (((j * 97) % 100) / 100.0) * (p.Width + 70.0) + Math.Sin(t * 0.5 + j) * 12.0;
                double py = p.Y + p.Height + 10.0 + (((j * 53) % 100) / 100.0) * 110.0 - ((t * 6.0 + j * 8.0) % 40.0);
                double rawSin = Math.Abs(Math.Sin(t + j));
                if (rawSin * 0.55 > 0.04)
                {
                    int alphaIdx = Math.Clamp((int)(rawSin * GoldenPollenAlphaBrushes.Length) - 1, 0, GoldenPollenAlphaBrushes.Length - 1);
                    dc.DrawEllipse(GoldenPollenAlphaBrushes[alphaIdx], null, new Point(px, py), 1.5 * _scale, 1.5 * _scale);
                }
            }
        }

        // 2. Fluttering Butterflies orbiting gracefully around the cascading foliage
        int count = Math.Clamp(_butterflyCount, 0, 4);
        if (count > 0)
        {
            for (int i = 0; i < count; i++)
            {
                Point FlightPath(double u)
                {
                    double bx, by;
                    if (i == 0) // Monarch fluttering near right cascading foliage
                    {
                        double cx = p.X + p.Width * 0.82;
                        bx = cx + Math.Sin(u * 0.35) * (45.0 * _scale) + Math.Cos(u * 0.15) * 15.0;
                        by = p.Y + p.Height + 28.0 + Math.Sin(u * 0.52) * 22.0;
                    }
                    else if (i == 1) // Blue Morpho fluttering near left cascading foliage
                    {
                        double cx = p.X + p.Width * 0.18;
                        bx = cx + Math.Sin(u * 0.42 + 1.5) * (45.0 * _scale) - Math.Cos(u * 0.18) * 15.0;
                        by = p.Y + p.Height + 32.0 + Math.Sin(u * 0.61 + 2.0) * 20.0;
                    }
                    else // Swallowtail looping gently across center underlay
                    {
                        double cx = p.X + p.Width * 0.50;
                        bx = cx + Math.Sin(u * 0.28 + 3.0) * (p.Width * 0.35);
                        by = p.Y + p.Height + 48.0 + Math.Sin(u * 0.45 + 1.0) * 18.0;
                    }
                    return new Point(bx, by);
                }

                Point ptA = FlightPath(t);
                Point ptB = FlightPath(t + 0.05);
                double ang = Math.Atan2(ptB.X - ptA.X, -(ptB.Y - ptA.Y));

                DrawButterfly(dc, ptA.X, ptA.Y, t + i, ang, ButterflyScales[i] * _scale, ButterflySpeeds[i], ButterflyWingBrushes[i], ButterflyWingPens[i], ButterflySpotBrushes[i]);
            }
        }
    }

    private static void DrawButterfly(DrawingContext dc, double x, double y, double t, double ang, double scale, double speed,
        Brush wingBrush, Pen wingPen, Brush spotBrush)
    {
        if (scale <= 0.02) return;

        var mat = Matrix.Identity;
        mat.Scale(scale, scale);
        mat.Rotate(ang * 180.0 / Math.PI);
        mat.Translate(x, y);

        dc.PushTransform(new MatrixTransform(mat));

        // Flapping motion: squash-and-stretch on wing X axis
        double flap = 0.25 + 0.75 * Math.Abs(Math.Sin(t * speed));

        for (int sx = -1; sx <= 1; sx += 2)
        {
            var wingMat = Matrix.Identity;
            wingMat.Scale(sx * flap, 1.0);
            dc.PushTransform(new MatrixTransform(wingMat));

            dc.DrawGeometry(wingBrush, wingPen, ButterflyForewingGeometry);
            dc.DrawGeometry(wingBrush, wingPen, ButterflyHindwingGeometry);

            foreach (var ve in ButterflyVeinEnds)
            {
                dc.DrawLine(ButterflyVeinPen, new Point(1, 1), ve);
            }

            foreach (var sp in ButterflySpots)
            {
                dc.DrawEllipse(spotBrush, null, new Point(sp.px, sp.py), sp.r, sp.r);
            }

            dc.Pop();
        }

        // Slender body & head
        dc.DrawEllipse(ButterflyBodyBrush, null, new Point(0, 1), 1.6, 7.0);
        dc.DrawEllipse(ButterflyBodyBrush, null, new Point(0, -7), 1.9, 1.9);

        // Curved antennae
        dc.DrawGeometry(null, ButterflyBodyPen, ButterflyAntennaeGeometry);

        dc.Pop();
    }

    // =========================================================================
    //  THEME 2: Meadow (Cascading Hanging Wildflower Meadow)
    // =========================================================================

    private void RenderMeadow(DrawingContext dc, Rect p, double t)
    {
        var rng = new Mulberry32(11);
        double yAnchor = p.Y + p.Height;

        // 1. Meadow grass blades draping down from the pill bottom edge
        int bladeCount = Math.Max(15, (int)(70 * _density));
        for (int i = 0; i < bladeCount; i++)
        {
            double x = p.X + 10.0 + i * (p.Width - 20.0) / Math.Max(1.0, bladeCount - 1) + (rng.NextDouble() - 0.5) * 4.0;
            double h = (14.0 + rng.NextDouble() * 28.0) * _scale;
            double ln = (rng.NextDouble() - 0.5) * 14.0 + Math.Sin(t * 1.3 + i * 0.7) * 3.0;
            double k = EaseOutCubic((t - 0.2 - rng.NextDouble() * 1.2) / 1.2);
            double tx = x + ln * k;
            double ty = yAnchor + h * k;

            var blade = new StreamGeometry();
            using (var ctx = blade.Open())
            {
                ctx.BeginFigure(new Point(x - 1.4, yAnchor), isFilled: true, isClosed: true);
                ctx.QuadraticBezierTo(new Point(x + ln * 0.1, yAnchor + h * k * 0.6), new Point(tx, ty), isStroked: false, isSmoothJoin: true);
                ctx.QuadraticBezierTo(new Point(x + ln * 0.2 + 1.0, yAnchor + h * k * 0.5), new Point(x + 1.4, yAnchor), isStroked: false, isSmoothJoin: true);
            }
            blade.Freeze();
            dc.DrawGeometry(GrassBrush, null, blade);
        }

        // 2. Wildflower stems (daisies, poppies, lavender) hanging and blooming below
        int flowerCount = Math.Max(2, (int)(8 * _bloomDensity));
        for (int i = 0; i < flowerCount; i++)
        {
            var rx = new Mulberry32((uint)(i * 7 + 3));
            double x = p.X + 24.0 + i * (p.Width - 48.0) / Math.Max(1.0, flowerCount - 1) + (rx.NextDouble() - 0.5) * 10.0;
            double h = (42.0 + rx.NextDouble() * 46.0) * _scale;
            double k = EaseOutCubic((t - 0.6 - i * 0.28) / 1.8);
            double ln = (rx.NextDouble() - 0.5) * 26.0;
            double sw = Math.Sin(t * 1.2 + i * 1.7) * 4.0 * k;
            double ex = x + ln * k + sw;
            double ey = yAnchor + h * k;
            double cx = x + ln * 0.1;
            double cy = yAnchor + h * k * 0.55;

            DrawLimb(dc, x, yAnchor, cx, cy, ex, ey, 2.8 * _scale, 1.6 * _scale, 1.0, MeadowStemDarkPen, MeadowStemLightPen);

            // Small stem leaves
            for (int leafIdx = 0; leafIdx < 2; leafIdx++)
            {
                double u = leafIdx == 0 ? 0.3 : 0.55;
                double sd = leafIdx == 0 ? -1.0 : 1.0;
                var q = Qp(x, yAnchor, cx, cy, ex, ey, u);

                var mat = Matrix.Identity;
                mat.Rotate((0.9 + sd * 0.7 + Math.Sin(t + i) * 0.05) * 180.0 / Math.PI);
                mat.Translate(q.X, q.Y);
                dc.PushTransform(new MatrixTransform(mat));
                DrawPetal(dc, 14.0 * k * _scale, 4.0 * k * _scale, MeadowLeafBrush, 0, notched: false);
                dc.Pop();
            }

            // Blossom types: 0=Daisy, 1=Poppy, 2=Lavender, 3=Daisy, 4=Poppy, 5=Lavender, 6=Daisy, 7=Poppy
            int fType = i % 3;
            double s = k * (0.85 + rx.NextDouble() * 0.3) * _scale;
            double ph = rx.NextDouble() * 6.0;

            switch (fType)
            {
                case 0: DrawDaisy(dc, ex, ey, s, t, ph); break;
                case 1: DrawPoppy(dc, ex, ey, s, t, ph); break;
                case 2:
                default: DrawLavender(dc, ex, ey, s, t, ph); break;
            }
        }
    }

    private static void DrawDaisy(DrawingContext dc, double x, double y, double s, double t, double ph)
    {
        if (s <= 0.02) return;
        var mat = Matrix.Identity;
        mat.Scale(s, s);
        mat.Rotate((Math.Sin(t + ph) * 0.06) * 180.0 / Math.PI);
        mat.Translate(x, y);

        dc.PushTransform(new MatrixTransform(mat));

        // 18 outer white petals
        for (int i = 0; i < 18; i++)
        {
            double a = i * (Math.PI * 2.0 / 18.0) + ph;
            DrawPetal(dc, 12.0, 2.3, DaisyPetal1Brush, a, notched: false);
        }
        // 14 inner white petals
        for (int i = 0; i < 14; i++)
        {
            double a = i * (Math.PI * 2.0 / 14.0) + ph + 0.2;
            DrawPetal(dc, 9.0, 2.3, DaisyPetal2Brush, a, notched: false);
        }

        // Golden center disc with florets
        dc.DrawEllipse(DaisyCenterBrush, null, new Point(0, 0), 4.5, 4.5);
        for (int i = 0; i < 8; i++)
        {
            dc.DrawRectangle(DaisyFloretBrush, null, new Rect(Math.Cos(i * 2.4) * i * 0.45, Math.Sin(i * 2.4) * i * 0.45, 0.8, 0.8));
        }

        dc.Pop();
    }

    private static void DrawPoppy(DrawingContext dc, double x, double y, double s, double t, double ph)
    {
        if (s <= 0.02) return;
        var mat = Matrix.Identity;
        mat.Scale(s, s);
        mat.Rotate(ph * 180.0 / Math.PI);
        mat.Translate(x, y);

        dc.PushTransform(new MatrixTransform(mat));

        for (int i = 0; i < 5; i++)
        {
            double a = i * (Math.PI * 2.0 / 5.0);
            DrawPetal(dc, 12.0, 8.5, PoppyPetal1Brush, a, notched: false);
        }
        for (int i = 0; i < 5; i++)
        {
            double a = i * (Math.PI * 2.0 / 5.0) + 0.6;
            DrawPetal(dc, 8.5, 5.5, PoppyPetal2Brush, a, notched: false);
        }

        dc.DrawEllipse(PoppyCenterBrush, null, new Point(0, 0), 2.8, 2.8);
        for (int i = 0; i < 12; i++)
        {
            double a = i * (Math.PI * 2.0 / 12.0);
            dc.DrawEllipse(PoppyRingBrush, null, new Point(Math.Cos(a) * 4.2, Math.Sin(a) * 4.2), 0.7, 0.7);
        }

        dc.Pop();
    }

    private static void DrawLavender(DrawingContext dc, double x, double y, double s, double t, double ph)
    {
        if (s <= 0.02) return;
        var mat = Matrix.Identity;
        mat.Scale(s, s);
        mat.Translate(x, y);

        dc.PushTransform(new MatrixTransform(mat));

        for (int i = 0; i < 14; i++)
        {
            double sw = Math.Sin(t * 1.4 + ph + i * 0.2) * 0.5;
            double flX = (i % 2 == 1 ? 2.4 : -2.4) + sw;
            double flY = i * 3.0; // Cascading downward florets

            dc.DrawEllipse(i % 2 == 1 ? LavFloret1Brush : LavFloret2Brush, null, new Point(flX, flY), 2.4, 1.7);
        }

        dc.Pop();
    }

    private void RenderMeadowButterfly(DrawingContext dc, Rect p, double t)
    {
        if (_butterflyCount <= 0) return;
        double bx = p.X + p.Width / 2.0 + Math.Sin(t * 0.45) * 180.0;
        double by = p.Y + p.Height + 50.0 + Math.Sin(t * 0.8) * 25.0;
        double ang = Math.Sin(t * 0.9) * 0.3;

        DrawButterfly(dc, bx, by, t, ang, 1.0 * _scale, 12.0, MonarchWingBrush, ButterflyMonarchPen, MonarchSpotBrush);
    }

    // =========================================================================
    //  Helper: Tapered Two-Tone Limb
    // =========================================================================

    private static Point DrawLimb(DrawingContext dc, double x0, double y0, double cx, double cy, double x1, double y1,
        double w0, double w1, double k, Pen penDark, Pen penLight)
    {
        if (k <= 0) return new Point(x0, y0);
        int n = 10;

        for (int pass = 0; pass < 2; pass++)
        {
            bool isHighlight = pass == 1;
            Point prev = new(x0, y0);
            for (int i = 1; i <= n; i++)
            {
                double u = (double)i / n * k;
                var q = Qp(x0, y0, cx, cy, x1, y1, u);
                double offset = isHighlight ? 0.6 : 0.0;

                dc.DrawLine(isHighlight ? penLight : penDark,
                    new Point(prev.X - offset, prev.Y),
                    new Point(q.X - offset, q.Y));
                prev = q;
            }
        }

        return Qp(x0, y0, cx, cy, x1, y1, k);
    }
}
