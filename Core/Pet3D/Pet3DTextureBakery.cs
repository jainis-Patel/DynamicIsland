using System;
using System.Collections.Concurrent;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace DynamicIsland.Core.Pet3D;

/// <summary>
/// Procedurally bakes lightweight, frozen fur, scale, iris, and wing textures
/// for realistic 3D pet materials. Lazy-cached per species so RAM stays minimal.
/// </summary>
internal static class Pet3DTextureBakery
{
    private static readonly ConcurrentDictionary<string, Material> MaterialCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, BitmapSource> TextureCache = new(StringComparer.Ordinal);

    public static void ClearCache(string? keepSpecies = null)
    {
        if (string.IsNullOrEmpty(keepSpecies))
        {
            MaterialCache.Clear();
            TextureCache.Clear();
            return;
        }

        foreach (var k in MaterialCache.Keys)
        {
            if (!k.Contains(keepSpecies, StringComparison.OrdinalIgnoreCase))
                MaterialCache.TryRemove(k, out _);
        }
        foreach (var k in TextureCache.Keys)
        {
            if (!k.Contains(keepSpecies, StringComparison.OrdinalIgnoreCase))
                TextureCache.TryRemove(k, out _);
        }
    }

    public static Material GetCoatMaterial(string species, bool heroLod) =>
        MaterialCache.GetOrAdd($"coat_{species}_{heroLod}", _ =>
        {
            var bmp = GetCoatTexture(species, heroLod);
            var brush = new ImageBrush(bmp) { Stretch = Stretch.Fill };
            brush.Freeze();

            var group = new MaterialGroup();
            group.Children.Add(new DiffuseMaterial(brush));

            if (species == "Dragon")
            {
                // Iridescent dragon scale sheen
                group.Children.Add(new SpecularMaterial(FreezeBrush(Color.FromArgb(145, 160, 230, 255)), 28.0));
            }
            else
            {
                // Soft plush fur sheen
                group.Children.Add(new SpecularMaterial(FreezeBrush(Color.FromArgb(42, 255, 248, 240)), 9.0));
            }
            group.Freeze();
            return group;
        });

    public static Material GetHeadMaterial(string species, bool heroLod) =>
        MaterialCache.GetOrAdd($"head_{species}_{heroLod}", _ =>
        {
            var bmp = GetHeadTexture(species, heroLod);
            var brush = new ImageBrush(bmp) { Stretch = Stretch.Fill };
            brush.Freeze();

            var group = new MaterialGroup();
            group.Children.Add(new DiffuseMaterial(brush));
            group.Children.Add(new SpecularMaterial(
                FreezeBrush(species == "Dragon" ? Color.FromArgb(145, 160, 230, 255) : Color.FromArgb(46, 255, 248, 240)),
                species == "Dragon" ? 28.0 : 10.0));
            group.Freeze();
            return group;
        });

    public static Material GetIrisMaterial(string species) =>
        MaterialCache.GetOrAdd($"iris_{species}", _ =>
        {
            var bmp = GetIrisTexture(species);
            var brush = new ImageBrush(bmp) { Stretch = Stretch.Fill };
            brush.Freeze();

            var group = new MaterialGroup();
            group.Children.Add(new DiffuseMaterial(brush));
            if (species == "Dragon")
            {
                group.Children.Add(new EmissiveMaterial(FreezeBrush(Color.FromArgb(45, 0, 225, 210))));
            }
            group.Children.Add(new SpecularMaterial(FreezeBrush(Color.FromArgb(220, 255, 255, 255)), 95.0));
            group.Freeze();
            return group;
        });

    public static Material GetCorneaShellMaterial() =>
        MaterialCache.GetOrAdd("cornea_shell", _ =>
        {
            var group = new MaterialGroup();
            group.Children.Add(new DiffuseMaterial(FreezeBrush(Color.FromArgb(14, 255, 255, 255))));
            group.Children.Add(new SpecularMaterial(FreezeBrush(Color.FromArgb(245, 255, 255, 255)), 120.0));
            group.Freeze();
            return group;
        });

    public static Material GetWingMaterial() =>
        MaterialCache.GetOrAdd("dragon_wing", _ =>
        {
            var bmp = GetWingMembraneTexture();
            var brush = new ImageBrush(bmp) { Stretch = Stretch.Fill };
            brush.Freeze();
            var group = new MaterialGroup();
            group.Children.Add(new DiffuseMaterial(brush));
            group.Children.Add(new SpecularMaterial(FreezeBrush(Color.FromArgb(120, 210, 180, 255)), 22.0));
            group.Children.Add(new EmissiveMaterial(FreezeBrush(Color.FromArgb(30, 110, 60, 255))));
            group.Freeze();
            return group;
        });

    public static Material GetPlushSolidMaterial(Color color, double specPower = 10.0, byte specAlpha = 45) =>
        MaterialCache.GetOrAdd($"solid_{color.A}_{color.R}_{color.G}_{color.B}_{specPower:F0}_{specAlpha}", _ =>
        {
            var group = new MaterialGroup();
            group.Children.Add(new DiffuseMaterial(FreezeBrush(color)));
            if (specAlpha > 0)
            {
                group.Children.Add(new SpecularMaterial(FreezeBrush(Color.FromArgb(specAlpha, 255, 255, 255)), specPower));
            }
            group.Freeze();
            return group;
        });

    public static Material GetEmissiveSolidMaterial(Color diffuse, Color emissive, double specPower = 40.0) =>
        MaterialCache.GetOrAdd($"emissive_{diffuse.R}_{diffuse.G}_{diffuse.B}_{emissive.R}_{emissive.G}_{emissive.B}", _ =>
        {
            var group = new MaterialGroup();
            group.Children.Add(new DiffuseMaterial(FreezeBrush(diffuse)));
            group.Children.Add(new EmissiveMaterial(FreezeBrush(emissive)));
            group.Children.Add(new SpecularMaterial(FreezeBrush(Color.FromArgb(200, 255, 255, 255)), specPower));
            group.Freeze();
            return group;
        });

    // ─────────────────────────────────────────────────────────────────────────
    // Procedural Texture Generation
    // ─────────────────────────────────────────────────────────────────────────
    public static BitmapSource GetCoatTexture(string species, bool heroLod) =>
        TextureCache.GetOrAdd($"tex_coat_{species}_{heroLod}", _ =>
        {
            int size = heroLod ? 256 : 128;
            return BakeFurOrScaleBitmap(species, size, isHead: false);
        });

    public static BitmapSource GetHeadTexture(string species, bool heroLod) =>
        TextureCache.GetOrAdd($"tex_head_{species}_{heroLod}", _ =>
        {
            int size = heroLod ? 256 : 128;
            return BakeFurOrScaleBitmap(species, size, isHead: true);
        });

    public static BitmapSource GetIrisTexture(string species) =>
        TextureCache.GetOrAdd($"tex_iris_{species}", _ =>
        {
            const int size = 64;
            byte[] px = new byte[size * size * 4];
            (Color inner, Color mid, Color outer) = species switch
            {
                "Dog" => (Color.FromRgb(186, 124, 62), Color.FromRgb(102, 56, 20), Color.FromRgb(38, 18, 6)),
                "Bunny" => (Color.FromRgb(206, 78, 122), Color.FromRgb(124, 28, 62), Color.FromRgb(42, 6, 18)),
                "Fox" => (Color.FromRgb(255, 218, 72), Color.FromRgb(217, 130, 12), Color.FromRgb(88, 42, 0)),
                "Panda" => (Color.FromRgb(120, 224, 143), Color.FromRgb(40, 150, 90), Color.FromRgb(14, 64, 36)),
                "Dragon" => (Color.FromRgb(40, 250, 225), Color.FromRgb(0, 168, 232), Color.FromRgb(0, 48, 88)),
                _ => (Color.FromRgb(110, 245, 178), Color.FromRgb(36, 189, 118), Color.FromRgb(12, 92, 56)),
            };

            for (int y = 0; y < size; y++)
            {
                double v = (y + 0.5) / size;
                for (int x = 0; x < size; x++)
                {
                    double u = (x + 0.5) / size;
                    // Front of eyeball sits around u = 0 or 1 (theta=0), v=0.5
                    double dx = Math.Sin((u) * Math.PI * 2.0);
                    double dy = (v - 0.5) * 2.0;
                    double r = Math.Sqrt(dx * dx + dy * dy);
                    double ang = Math.Atan2(dy, dx);

                    double fiber = 0.88 + 0.12 * Math.Sin(ang * 28.0 + Hash(x, y) * 1.4);
                    Color c;
                    if (r < 0.35)
                    {
                        c = Lerp(inner, mid, r / 0.35);
                    }
                    else if (r < 0.82)
                    {
                        c = Lerp(mid, outer, (r - 0.35) / 0.47);
                    }
                    else
                    {
                        c = Color.FromRgb(14, 10, 18); // Dark limbal ring
                    }

                    // Lower-iris corneal light bounce
                    if (dy > 0.15 && r < 0.72)
                    {
                        c = Lerp(c, Colors.White, 0.22 * (1.0 - r / 0.72));
                    }

                    int idx = (y * size + x) * 4;
                    px[idx + 0] = ClampByte(c.B * fiber);
                    px[idx + 1] = ClampByte(c.G * fiber);
                    px[idx + 2] = ClampByte(c.R * fiber);
                    px[idx + 3] = 255;
                }
            }

            return CreateFrozenBitmap(size, size, px);
        });

    private static BitmapSource GetWingMembraneTexture() =>
        TextureCache.GetOrAdd("tex_wing", _ =>
        {
            const int size = 128;
            byte[] px = new byte[size * size * 4];
            Color c0 = Color.FromRgb(191, 90, 242);
            Color c1 = Color.FromRgb(86, 52, 235);
            Color c2 = Color.FromRgb(32, 18, 96);

            for (int y = 0; y < size; y++)
            {
                double v = y / (double)(size - 1);
                for (int x = 0; x < size; x++)
                {
                    double u = x / (double)(size - 1);
                    Color baseCol = u < 0.5 ? Lerp(c0, c1, u * 2.0) : Lerp(c1, c2, (u - 0.5) * 2.0);
                    double vein = Math.Exp(-Math.Pow((v - 0.35 * u) * 18.0, 2)) +
                                  Math.Exp(-Math.Pow((v - 0.72 * u) * 18.0, 2));
                    Color finalCol = Lerp(baseCol, Color.FromRgb(215, 190, 255), Math.Clamp(vein * 0.45, 0, 0.6));
                    int idx = (y * size + x) * 4;
                    px[idx + 0] = finalCol.B;
                    px[idx + 1] = finalCol.G;
                    px[idx + 2] = finalCol.R;
                    px[idx + 3] = 235;
                }
            }
            return CreateFrozenBitmap(size, size, px);
        });

    private static BitmapSource BakeFurOrScaleBitmap(string species, int size, bool isHead)
    {
        byte[] px = new byte[size * size * 4];

        for (int y = 0; y < size; y++)
        {
            double v = y / (double)(size - 1); // 0 top, 1 bottom
            for (int x = 0; x < size; x++)
            {
                double u = x / (double)(size - 1); // 0..1 around Y (u=0 or 1 is front +Z)
                double frontness = Math.Cos(u * Math.PI * 2.0); // +1 front, -1 back
                double sideAbs = Math.Abs(Math.Sin(u * Math.PI * 2.0));

                // Directional anisotropic hair strand noise
                double grain = species == "Dragon"
                    ? ScalePattern(u * 26.0, v * 20.0)
                    : FurStrandGrain(x, y);

                Color c = species switch
                {
                    "Cat" => SampleCatColor(frontness, sideAbs, v, isHead),
                    "Dog" => SampleDogColor(frontness, sideAbs, v, isHead),
                    "Bunny" => SampleBunnyColor(frontness, sideAbs, v, isHead),
                    "Fox" => SampleFoxColor(frontness, sideAbs, v, isHead),
                    "Panda" => SamplePandaColor(frontness, sideAbs, v, isHead),
                    "Dragon" => SampleDragonColor(frontness, sideAbs, v, isHead),
                    _ => SampleCatColor(frontness, sideAbs, v, isHead),
                };

                // Ambient occlusion at underside + subtle fur shading
                double ao = 1.0 - 0.16 * Math.Clamp((v - 0.72) / 0.28, 0, 1);
                double shade = grain * ao;

                int idx = (y * size + x) * 4;
                px[idx + 0] = ClampByte(c.B * shade);
                px[idx + 1] = ClampByte(c.G * shade);
                px[idx + 2] = ClampByte(c.R * shade);
                px[idx + 3] = 255;
            }
        }

        return CreateFrozenBitmap(size, size, px);
    }

    private static Color SampleCatColor(double front, double side, double v, bool isHead)
    {
        Color gingerLight = Color.FromRgb(255, 178, 102);
        Color gingerMid = Color.FromRgb(230, 136, 54);
        Color gingerDark = Color.FromRgb(166, 76, 20);
        Color cream = Color.FromRgb(255, 245, 232);

        Color baseCol = Lerp(gingerLight, gingerMid, Math.Clamp(v * 0.9, 0, 1));
        if (isHead)
        {
            // Soft cream muzzle/chin
            if (front > 0.35 && v > 0.56)
            {
                double m = Math.Clamp((front - 0.35) * 1.6 * (v - 0.56) * 3.5, 0, 1);
                baseCol = Lerp(baseCol, cream, m);
            }
            // Forehead tabby "M" & cheek stripes
            if (front > 0.45 && v is > 0.22 and < 0.38)
            {
                double stripe = Math.Abs(Math.Sin(side * 18.0));
                if (stripe > 0.82) baseCol = Lerp(baseCol, gingerDark, 0.7);
            }
            if (side > 0.65 && v is > 0.48 and < 0.68)
            {
                double cheekStripe = Math.Sin(v * 44.0);
                if (cheekStripe > 0.65) baseCol = Lerp(baseCol, gingerDark, 0.65);
            }
        }
        else
        {
            // Front cream tummy patch + flank tabby stripes
            if (front > 0.55 && v > 0.18 && v < 0.90)
            {
                double t = Math.Clamp((front - 0.55) * 3.0, 0, 1);
                baseCol = Lerp(baseCol, cream, t);
            }
            else if (side > 0.55 && v is > 0.32 and < 0.78)
            {
                if (Math.Sin(v * 34.0) > 0.65) baseCol = Lerp(baseCol, gingerDark, 0.55);
            }
        }
        return baseCol;
    }

    private static Color SampleDogColor(double front, double side, double v, bool isHead)
    {
        Color shibaGold = Color.FromRgb(245, 182, 112);
        Color shibaRich = Color.FromRgb(218, 146, 72);
        Color urajiro = Color.FromRgb(255, 248, 238);

        Color baseCol = Lerp(shibaGold, shibaRich, Math.Clamp(v * 0.85, 0, 1));
        if (isHead)
        {
            // Shiba urajiro lower cheeks & muzzle
            if (front > 0.20 && v > 0.52)
            {
                double m = Math.Clamp((front - 0.20) * 1.8 * (v - 0.52) * 3.4, 0, 1);
                baseCol = Lerp(baseCol, urajiro, m);
            }
        }
        else
        {
            if (front > 0.48 && v > 0.16 && v < 0.92)
            {
                baseCol = Lerp(baseCol, urajiro, Math.Clamp((front - 0.48) * 3.2, 0, 1));
            }
        }
        return baseCol;
    }

    private static Color SampleBunnyColor(double front, double side, double v, bool isHead)
    {
        Color white = Color.FromRgb(255, 255, 255);
        Color lavenderTint = Color.FromRgb(224, 224, 242);
        Color blush = Color.FromRgb(255, 168, 192);

        Color baseCol = Lerp(white, lavenderTint, Math.Clamp(v * 0.6, 0, 1));
        if (isHead && front > 0.35 && side > 0.35 && v is > 0.54 and < 0.70)
        {
            double b = Math.Exp(-Math.Pow((v - 0.62) * 16.0, 2)) * 0.35;
            baseCol = Lerp(baseCol, blush, b);
        }
        return baseCol;
    }

    private static Color SampleFoxColor(double front, double side, double v, bool isHead)
    {
        Color foxOrange = Color.FromRgb(248, 118, 48);
        Color foxDeep = Color.FromRgb(186, 64, 14);
        Color foxCream = Color.FromRgb(255, 248, 240);

        Color baseCol = Lerp(foxOrange, foxDeep, Math.Clamp(v * 0.85, 0, 1));
        if (isHead)
        {
            // Crisp Kitsune white cheek-and-muzzle mask
            if (front > 0.15 && v > 0.52)
            {
                double mask = Math.Clamp((front - 0.15) * 2.2 * (v - 0.52) * 3.8, 0, 1);
                baseCol = Lerp(baseCol, foxCream, mask);
            }
        }
        else
        {
            if (front > 0.52 && v > 0.16 && v < 0.90)
            {
                baseCol = Lerp(baseCol, foxCream, Math.Clamp((front - 0.52) * 3.0, 0, 1));
            }
        }
        return baseCol;
    }

    private static Color SamplePandaColor(double front, double side, double v, bool isHead)
    {
        Color ivory = Color.FromRgb(252, 250, 246);
        Color ivoryShadow = Color.FromRgb(222, 217, 210);
        Color charcoal = Color.FromRgb(32, 33, 40);

        if (isHead)
        {
            return Lerp(ivory, ivoryShadow, Math.Clamp(v * 0.7, 0, 1));
        }

        // Iconic black panda shoulder saddle band
        if (v is > 0.16 and < 0.44)
        {
            double edge = Math.Min((v - 0.16) * 14.0, (0.44 - v) * 14.0);
            return Lerp(ivory, charcoal, Math.Clamp(edge, 0, 1));
        }
        return Lerp(ivory, ivoryShadow, Math.Clamp(v * 0.7, 0, 1));
    }

    private static Color SampleDragonColor(double front, double side, double v, bool isHead)
    {
        Color sky = Color.FromRgb(100, 210, 255);
        Color cobalt = Color.FromRgb(54, 118, 255);
        Color gold = Color.FromRgb(255, 214, 102);

        Color baseCol = Lerp(sky, cobalt, Math.Clamp(v * 0.9, 0, 1));
        if (isHead && front > 0.45 && v > 0.62)
        {
            baseCol = Lerp(baseCol, gold, Math.Clamp((front - 0.45) * 2.2 * (v - 0.62) * 3.5, 0, 1));
        }
        else if (!isHead && front > 0.56 && v > 0.18 && v < 0.90)
        {
            // Golden ridged belly plates
            double plate = Math.Abs(Math.Sin(v * 26.0)) < 0.15 ? 0.8 : 1.0;
            Color belly = Color.FromRgb(ClampByte(gold.R * plate), ClampByte(gold.G * plate), ClampByte(gold.B * plate));
            baseCol = Lerp(baseCol, belly, Math.Clamp((front - 0.56) * 3.0, 0, 1));
        }
        return baseCol;
    }

    private static double FurStrandGrain(int x, int y)
    {
        // Vertical directional fur fiber modulation
        double strand = Hash(x, y / 4) * 0.65 + Hash(x, y) * 0.35;
        return 0.87 + 0.16 * strand;
    }

    private static double ScalePattern(double u, double v)
    {
        int row = (int)Math.Floor(v);
        double fu = u + ((row & 1) == 0 ? 0.0 : 0.5);
        double du = fu - Math.Floor(fu) - 0.5;
        double dv = v - Math.Floor(v) - 0.5;
        double d = Math.Sqrt(du * du + dv * dv);
        return d > 0.44 ? 0.84 : 1.02 - d * 0.25;
    }

    private static double Hash(int x, int y)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xFFFF) / 65535.0;
        }
    }

    private static Color Lerp(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }

    private static byte ClampByte(double v) => (byte)Math.Clamp((int)Math.Round(v), 0, 255);

    private static SolidColorBrush FreezeBrush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static BitmapSource CreateFrozenBitmap(int w, int h, byte[] bgra)
    {
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
        bmp.Freeze();
        return bmp;
    }
}
