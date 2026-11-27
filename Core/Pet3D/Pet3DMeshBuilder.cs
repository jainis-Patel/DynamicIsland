using System;
using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Media.Media3D;

namespace DynamicIsland.Core.Pet3D;

/// <summary>
/// Procedural organic 3D mesh generator for Dynamic Island realistic 3D pets.
/// Produces smooth, frozen <see cref="MeshGeometry3D"/> instances cached by (key, heroLod)
/// with strict counter-clockwise (CCW) triangle winding and analytical outward normals.
/// </summary>
internal static class Pet3DMeshBuilder
{
    private static readonly ConcurrentDictionary<string, MeshGeometry3D> Cache = new(StringComparer.Ordinal);

    public static void ClearCache() => Cache.Clear();

    private static MeshGeometry3D GetOrCreate(string key, bool heroLod, Func<MeshGeometry3D> factory)
    {
        string fullKey = heroLod ? $"{key}|H" : $"{key}|C";
        return Cache.GetOrAdd(fullKey, _ =>
        {
            var mesh = factory();
            if (mesh.CanFreeze) mesh.Freeze();
            return mesh;
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Core Parametric Ellipsoid & Deformed Organic Sculpting
    // ─────────────────────────────────────────────────────────────────────────
    public static MeshGeometry3D GetUnitSphere(bool heroLod) =>
        GetOrCreate("unit_sphere", heroLod, () =>
            CreateDeformedEllipsoid(1.0, 1.0, 1.0, heroLod ? 18 : 10, heroLod ? 22 : 12, null));

    public static MeshGeometry3D GetSculptedHead(string species, bool heroLod) =>
        GetOrCreate($"head_{species}", heroLod, () =>
        {
            int lat = heroLod ? 22 : 12;
            int lon = heroLod ? 28 : 14;

            return species switch
            {
                "Dog" => CreateDeformedEllipsoid(0.76, 0.64, 0.70, lat, lon, (x, y, z, u, v) =>
                {
                    // Shiba Inu: full rounded cheeks, distinct forward snout/muzzle
                    if (y < 0.0 && z > 0.1)
                    {
                        double cheek = Math.Exp(-y * y * 4.0) * 0.09;
                        x *= 1.0 + cheek;
                    }
                    if (z > 0.25 && y < 0.15 && y > -0.45)
                    {
                        double muzzle = Math.Max(0, (z - 0.25) / 0.45);
                        z += muzzle * 0.16 * Math.Cos(Math.Clamp(x * 1.4, -1.2, 1.2));
                    }
                    return new Point3D(x, y, z);
                }),
                "Bunny" => CreateDeformedEllipsoid(0.74, 0.62, 0.68, lat, lon, (x, y, z, u, v) =>
                {
                    // Plush marshmallow cheeks, slightly tapered forehead
                    if (y < 0.05) x *= 1.0 + 0.12 * Math.Sin(Math.Clamp(-y * 2.2, 0, Math.PI));
                    if (y > 0.2) x *= 0.94;
                    return new Point3D(x, y, z);
                }),
                "Fox" => CreateDeformedEllipsoid(0.78, 0.62, 0.70, lat, lon, (x, y, z, u, v) =>
                {
                    // Kitsune: wide diamond cheek ruff + sleek tapered vulpine muzzle
                    if (y < 0.1 && y > -0.35 && Math.Abs(x) > 0.2)
                        x *= 1.0 + 0.14 * Math.Exp(-Math.Pow((y + 0.1) * 4.0, 2));
                    if (z > 0.2 && y < 0.15 && y > -0.4)
                    {
                        double sn = (z - 0.2) / 0.5;
                        z += sn * 0.22 * (1.0 - Math.Min(1.0, Math.Abs(x) * 1.3));
                        x *= 1.0 - sn * 0.18;
                    }
                    return new Point3D(x, y, z);
                }),
                "Panda" => CreateDeformedEllipsoid(0.79, 0.66, 0.71, lat, lon, (x, y, z, u, v) =>
                {
                    // Chubby broad bear head with full lower jowls
                    if (y < 0.0) x *= 1.0 + 0.08 * Math.Sin(Math.Clamp(-y * 2.4, 0, Math.PI));
                    return new Point3D(x, y, z);
                }),
                "Dragon" => CreateDeformedEllipsoid(0.73, 0.61, 0.74, lat, lon, (x, y, z, u, v) =>
                {
                    // Sleek dragon skull with subtle brow ridge & forward snout
                    if (z > 0.15 && y < 0.15 && y > -0.4)
                    {
                        double sn = (z - 0.15) / 0.55;
                        z += sn * 0.18 * (1.0 - Math.Min(1.0, Math.Abs(x) * 1.1));
                    }
                    return new Point3D(x, y, z);
                }),
                _ => CreateDeformedEllipsoid(0.77, 0.63, 0.69, lat, lon, (x, y, z, u, v) =>
                {
                    // Cat: soft cheek puffs + subtle rounded muzzle cushion
                    if (y < 0.08 && y > -0.35)
                        x *= 1.0 + 0.10 * Math.Exp(-Math.Pow((y + 0.12) * 4.2, 2));
                    if (z > 0.3 && y < 0.05 && y > -0.4)
                        z += 0.08 * Math.Max(0, 1.0 - Math.Abs(x) * 1.8);
                    return new Point3D(x, y, z);
                }),
            };
        });

    public static MeshGeometry3D GetSculptedTorso(bool loaf, bool heroLod) =>
        GetOrCreate(loaf ? "torso_loaf" : "torso_seated", heroLod, () =>
        {
            int lat = heroLod ? 20 : 12;
            int lon = heroLod ? 24 : 14;
            if (loaf)
            {
                // Cozy wide sleeping loaf: flat underside, plush rounded back
                return CreateDeformedEllipsoid(0.66, 0.42, 0.72, lat, lon, (x, y, z, u, v) =>
                {
                    if (y < -0.15) y = -0.15 + (y + 0.15) * 0.35; // flatten bottom against ground
                    return new Point3D(x, y, z);
                });
            }

            // Seated pear-shaped torso: narrower shoulders, fuller rounded hips
            return CreateDeformedEllipsoid(0.56, 0.62, 0.54, lat, lon, (x, y, z, u, v) =>
            {
                double t = Math.Clamp((0.62 - y) / 1.24, 0.0, 1.0); // 0 at neck, 1 at base
                double flare = 0.78 + 0.36 * Math.Sin(t * Math.PI * 0.72);
                x *= flare;
                z *= flare;
                if (y < -0.35) y = -0.35 + (y + 0.35) * 0.42; // sit comfortably on floor
                return new Point3D(x, y, z);
            });
        });

    public static MeshGeometry3D GetForelegMesh(bool heroLod) =>
        GetOrCreate("foreleg", heroLod, () =>
            CreateCurvedTube(
                new[]
                {
                    new Point3D(0, 0.30, 0.0),
                    new Point3D(0, 0.10, 0.03),
                    new Point3D(0, -0.15, 0.05),
                    new Point3D(0, -0.34, 0.07)
                },
                new[] { 0.145, 0.125, 0.115, 0.125 },
                heroLod ? 14 : 8,
                capEnds: true));

    public static MeshGeometry3D GetPawMesh(bool bunnyFoot, bool heroLod) =>
        GetOrCreate(bunnyFoot ? "paw_bunny" : "paw_std", heroLod, () =>
        {
            double rx = bunnyFoot ? 0.17 : 0.145;
            double ry = 0.085;
            double rz = bunnyFoot ? 0.25 : 0.175;
            return CreateDeformedEllipsoid(rx, ry, rz, heroLod ? 12 : 8, heroLod ? 16 : 10, (x, y, z, u, v) =>
            {
                if (y < 0) y *= 0.45; // flat paw pad contact
                return new Point3D(x, y, z);
            });
        });

    public static MeshGeometry3D GetHindHaunchMesh(bool heroLod) =>
        GetOrCreate("hind_haunch", heroLod, () =>
            CreateDeformedEllipsoid(0.24, 0.27, 0.28, heroLod ? 14 : 8, heroLod ? 16 : 10, null));

    public static MeshGeometry3D GetEarMesh(string species, bool inner, bool heroLod) =>
        GetOrCreate($"ear_{species}_{(inner ? "in" : "out")}", heroLod, () =>
        {
            int lat = heroLod ? 14 : 8;
            int lon = heroLod ? 16 : 10;
            double scale = inner ? 0.72 : 1.0;

            return species switch
            {
                "Bunny" => CreateDeformedEllipsoid(0.19 * scale, 0.58 * scale, 0.09 * scale, lat, lon, (x, y, z, u, v) =>
                {
                    // Long arched lop-capable bunny ear
                    double normY = (y / (0.58 * scale) + 1.0) * 0.5;
                    x *= 0.7 + 0.5 * Math.Sin(normY * Math.PI);
                    z -= Math.Pow(normY, 1.8) * 0.08;
                    if (inner) z += 0.035;
                    return new Point3D(x, y + 0.52 * scale, z);
                }),
                "Panda" => CreateDeformedEllipsoid(0.24 * scale, 0.23 * scale, 0.12 * scale, lat, lon, (x, y, z, u, v) =>
                    new Point3D(x, y + 0.16 * scale, z)),
                "Fox" => CreateDeformedEllipsoid(0.24 * scale, 0.44 * scale, 0.11 * scale, lat, lon, (x, y, z, u, v) =>
                {
                    double normY = Math.Clamp((y / (0.44 * scale) + 1.0) * 0.5, 0, 1);
                    x *= 1.0 - normY * 0.72;
                    z *= 1.0 - normY * 0.45;
                    if (inner) z += 0.04;
                    return new Point3D(x, y + 0.36 * scale, z);
                }),
                "Dog" => CreateDeformedEllipsoid(0.22 * scale, 0.34 * scale, 0.12 * scale, lat, lon, (x, y, z, u, v) =>
                {
                    double normY = Math.Clamp((y / (0.34 * scale) + 1.0) * 0.5, 0, 1);
                    x *= 1.0 - normY * 0.65;
                    z *= 1.0 - normY * 0.35;
                    if (inner) z += 0.045;
                    return new Point3D(x, y + 0.28 * scale, z);
                }),
                "Dragon" => CreateDeformedEllipsoid(0.16 * scale, 0.34 * scale, 0.06 * scale, lat, lon, (x, y, z, u, v) =>
                {
                    double normY = Math.Clamp((y / (0.34 * scale) + 1.0) * 0.5, 0, 1);
                    x *= 1.0 - normY * 0.45;
                    return new Point3D(x, y + 0.28 * scale, z);
                }),
                _ => CreateDeformedEllipsoid(0.23 * scale, 0.38 * scale, 0.11 * scale, lat, lon, (x, y, z, u, v) =>
                {
                    // Cat pointed triangular curved pinna
                    double normY = Math.Clamp((y / (0.38 * scale) + 1.0) * 0.5, 0, 1);
                    x *= 1.0 - normY * 0.76;
                    z *= 1.0 - normY * 0.45;
                    if (inner) z += 0.042;
                    return new Point3D(x, y + 0.31 * scale, z);
                }),
            };
        });

    public static MeshGeometry3D GetTailSegmentMesh(double len, double rBase, double rTip, bool heroLod) =>
        GetOrCreate($"tailseg_{len:F2}_{rBase:F2}_{rTip:F2}", heroLod, () =>
            CreateCurvedTube(
                new[]
                {
                    new Point3D(0, 0, 0),
                    new Point3D(len * 0.5, len * 0.08, 0),
                    new Point3D(len, len * 0.12, 0)
                },
                new[] { rBase, (rBase + rTip) * 0.5, rTip },
                heroLod ? 12 : 8,
                capEnds: true));

    public static MeshGeometry3D GetShibaSpiralTailMesh(bool heroLod) =>
        GetOrCreate("shiba_tail", heroLod, () =>
        {
            const int pts = 18;
            var spine = new Point3D[pts];
            var radii = new double[pts];
            for (int i = 0; i < pts; i++)
            {
                double u = i / (double)(pts - 1);
                double th = -Math.PI * 0.35 + u * 1.32 * Math.PI * 2.0;
                double r = 0.27 - 0.15 * u;
                spine[i] = new Point3D(r * Math.Cos(th) + 0.12, r * Math.Sin(th) + 0.22, -0.15 - u * 0.08);
                radii[i] = 0.115 * (1.0 - 0.25 * u);
            }
            return CreateCurvedTube(spine, radii, heroLod ? 12 : 8, capEnds: true);
        });

    public static MeshGeometry3D GetFoxBushyTailMesh(bool heroLod) =>
        GetOrCreate("fox_tail", heroLod, () =>
        {
            var spine = new[]
            {
                new Point3D(0.0, 0.0, 0.0),
                new Point3D(0.28, 0.10, -0.14),
                new Point3D(0.58, 0.36, -0.20),
                new Point3D(0.76, 0.72, -0.12),
                new Point3D(0.84, 1.02, -0.02),
            };
            var radii = new[] { 0.12, 0.23, 0.29, 0.22, 0.08 };
            return CreateCurvedTube(spine, radii, heroLod ? 14 : 9, capEnds: true);
        });

    public static MeshGeometry3D GetDragonWingMesh(bool left, bool heroLod) =>
        GetOrCreate(left ? "dragon_wing_l" : "dragon_wing_r", heroLod, () =>
        {
            double s = left ? -1.0 : 1.0;
            // Sculpted thin curved bat-wing mesh (double-sided for clean visibility from any angle)
            var p = new[]
            {
                new Point3D(0.0, 0.05, 0.0),
                new Point3D(0.38 * s, 0.48, -0.06),
                new Point3D(0.88 * s, 0.62, -0.14),
                new Point3D(0.76 * s, 0.14, -0.10),
                new Point3D(0.46 * s, -0.06, -0.05),
                new Point3D(0.20 * s, -0.14, -0.02),
            };
            var uv = new[]
            {
                new Point(0.0, 0.5),
                new Point(0.4, 0.1),
                new Point(1.0, 0.0),
                new Point(0.9, 0.7),
                new Point(0.5, 0.9),
                new Point(0.2, 1.0),
            };
            var mesh = new MeshGeometry3D();
            // Front & back faces
            for (int copy = 0; copy < 2; copy++)
            {
                foreach (var pt in p) mesh.Positions.Add(pt);
                foreach (var tc in uv) mesh.TextureCoordinates.Add(tc);
            }

            int[] tris = { 0, 1, 2, 0, 2, 3, 0, 3, 4, 0, 4, 5 };
            for (int i = 0; i < tris.Length; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                if (left) { (b, c) = (c, b); }
                mesh.TriangleIndices.Add(a); mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(c);
                // Back face
                mesh.TriangleIndices.Add(6 + a); mesh.TriangleIndices.Add(6 + c); mesh.TriangleIndices.Add(6 + b);
            }
            ComputeNormals(mesh);
            return mesh;
        });

    public static MeshGeometry3D GetDragonHornMesh(bool left, bool heroLod) =>
        GetOrCreate(left ? "dragon_horn_l" : "dragon_horn_r", heroLod, () =>
        {
            double s = left ? -1.0 : 1.0;
            return CreateCurvedTube(
                new[]
                {
                    new Point3D(0.0, 0.0, 0.0),
                    new Point3D(0.10 * s, 0.20, -0.05),
                    new Point3D(0.16 * s, 0.38, -0.16),
                    new Point3D(0.13 * s, 0.50, -0.26)
                },
                new[] { 0.085, 0.065, 0.042, 0.015 },
                heroLod ? 10 : 7,
                capEnds: true);
        });

    public static MeshGeometry3D GetTorusMesh(double majorR, double minorR, int majorSegs, int minorSegs) =>
        GetOrCreate($"torus_{majorR:F2}_{minorR:F2}_{majorSegs}_{minorSegs}", true, () =>
        {
            var mesh = new MeshGeometry3D();
            for (int i = 0; i <= majorSegs; i++)
            {
                double u = i / (double)majorSegs;
                double theta = u * 2.0 * Math.PI;
                double cosT = Math.Cos(theta), sinT = Math.Sin(theta);

                for (int j = 0; j <= minorSegs; j++)
                {
                    double v = j / (double)minorSegs;
                    double phi = v * 2.0 * Math.PI;
                    double cosP = Math.Cos(phi), sinP = Math.Sin(phi);

                    double r = majorR + minorR * cosP;
                    double x = r * cosT;
                    double y = minorR * sinP;
                    double z = r * sinT;

                    mesh.Positions.Add(new Point3D(x, y, z));
                    var n = new Vector3D(cosP * cosT, sinP, cosP * sinT);
                    n.Normalize();
                    mesh.Normals.Add(n);
                    mesh.TextureCoordinates.Add(new Point(u, v));
                }
            }

            int stride = minorSegs + 1;
            for (int i = 0; i < majorSegs; i++)
            {
                for (int j = 0; j < minorSegs; j++)
                {
                    int p00 = i * stride + j;
                    int p10 = (i + 1) * stride + j;
                    int p01 = i * stride + j + 1;
                    int p11 = (i + 1) * stride + j + 1;

                    mesh.TriangleIndices.Add(p00); mesh.TriangleIndices.Add(p01); mesh.TriangleIndices.Add(p11);
                    mesh.TriangleIndices.Add(p00); mesh.TriangleIndices.Add(p11); mesh.TriangleIndices.Add(p10);
                }
            }
            return mesh;
        });

    public static MeshGeometry3D GetConeMesh(double rBottom, double rTop, double height, int radialSegs) =>
        GetOrCreate($"cone_{rBottom:F2}_{rTop:F2}_{height:F2}_{radialSegs}", true, () =>
            CreateCurvedTube(
                new[] { new Point3D(0, 0, 0), new Point3D(0, height * 0.5, 0), new Point3D(0, height, 0) },
                new[] { rBottom, (rBottom + rTop) * 0.5, Math.Max(0.008, rTop) },
                radialSegs,
                capEnds: true));

    public static MeshGeometry3D GetCrownMesh(bool heroLod) =>
        GetOrCreate("acc_crown", heroLod, () =>
        {
            int segs = heroLod ? 20 : 12;
            var mesh = new MeshGeometry3D();
            const double rBase = 0.28;
            const double hBase = 0.0;
            const double hLow = 0.14;
            const double hPeak = 0.28;

            for (int i = 0; i <= segs; i++)
            {
                double u = i / (double)segs;
                double th = u * Math.PI * 2.0;
                double cos = Math.Cos(th), sin = Math.Sin(th);
                // 5 crown points
                double wave = Math.Pow(Math.Max(0, Math.Cos(th * 5.0)), 2.0);
                double topY = hLow + (hPeak - hLow) * wave;

                mesh.Positions.Add(new Point3D(rBase * cos, hBase, rBase * sin));
                mesh.TextureCoordinates.Add(new Point(u, 1));
                mesh.Positions.Add(new Point3D(rBase * 1.08 * cos, topY, rBase * 1.08 * sin));
                mesh.TextureCoordinates.Add(new Point(u, 0));
            }

            for (int i = 0; i < segs; i++)
            {
                int b0 = i * 2, t0 = i * 2 + 1;
                int b1 = (i + 1) * 2, t1 = (i + 1) * 2 + 1;
                // Outer
                mesh.TriangleIndices.Add(b0); mesh.TriangleIndices.Add(t1); mesh.TriangleIndices.Add(t0);
                mesh.TriangleIndices.Add(b0); mesh.TriangleIndices.Add(b1); mesh.TriangleIndices.Add(t1);
                // Inner
                mesh.TriangleIndices.Add(b0); mesh.TriangleIndices.Add(t0); mesh.TriangleIndices.Add(t1);
                mesh.TriangleIndices.Add(b0); mesh.TriangleIndices.Add(t1); mesh.TriangleIndices.Add(b1);
            }
            ComputeNormals(mesh);
            return mesh;
        });

    public static MeshGeometry3D GetHeadphoneBandMesh(bool heroLod) =>
        GetOrCreate("acc_hp_band", heroLod, () =>
        {
            const int pts = 15;
            var spine = new Point3D[pts];
            var radii = new double[pts];
            for (int i = 0; i < pts; i++)
            {
                double u = i / (double)(pts - 1);
                double th = Math.PI * (0.04 + u * 0.92);
                spine[i] = new Point3D(-0.82 * Math.Cos(th), 0.72 * Math.Sin(th) + 0.04, 0.0);
                radii[i] = 0.055;
            }
            return CreateCurvedTube(spine, radii, heroLod ? 10 : 6, capEnds: true);
        });

    public static MeshGeometry3D GetBandanaMesh(bool heroLod) =>
        GetOrCreate("neck_bandana", heroLod, () =>
        {
            var mesh = new MeshGeometry3D();
            // Curved triangular scarf hugging chest
            var pts = new[]
            {
                new Point3D(-0.38, 0.14, 0.28),
                new Point3D(0.0, 0.10, 0.44),
                new Point3D(0.38, 0.14, 0.28),
                new Point3D(-0.22, -0.08, 0.42),
                new Point3D(0.22, -0.08, 0.42),
                new Point3D(0.0, -0.30, 0.47),
            };
            foreach (var pt in pts) mesh.Positions.Add(pt);
            mesh.TextureCoordinates.Add(new Point(0, 0));
            mesh.TextureCoordinates.Add(new Point(0.5, 0));
            mesh.TextureCoordinates.Add(new Point(1, 0));
            mesh.TextureCoordinates.Add(new Point(0.2, 0.5));
            mesh.TextureCoordinates.Add(new Point(0.8, 0.5));
            mesh.TextureCoordinates.Add(new Point(0.5, 1.0));

            int[] tris = { 0, 3, 1, 1, 3, 4, 1, 4, 2, 3, 5, 4 };
            for (int i = 0; i < tris.Length; i += 3)
            {
                mesh.TriangleIndices.Add(tris[i]);
                mesh.TriangleIndices.Add(tris[i + 1]);
                mesh.TriangleIndices.Add(tris[i + 2]);
            }
            ComputeNormals(mesh);
            return mesh;
        });

    public static MeshGeometry3D GetWhiskerMesh(bool left, int index) =>
        GetOrCreate($"whisker_{(left ? "l" : "r")}_{index}", true, () =>
        {
            double s = left ? -1.0 : 1.0;
            double yOff = index == 0 ? 0.02 : -0.06;
            double yDrop = index == 0 ? 0.04 : -0.10;
            return CreateCurvedTube(
                new[]
                {
                    new Point3D(0.24 * s, yOff, 0.52),
                    new Point3D(0.55 * s, yOff + yDrop * 0.4, 0.48),
                    new Point3D(0.88 * s, yOff + yDrop, 0.36)
                },
                new[] { 0.012, 0.008, 0.003 },
                5,
                capEnds: true);
        });

    // ─────────────────────────────────────────────────────────────────────────
    // Mesh Builders
    // ─────────────────────────────────────────────────────────────────────────
    private static MeshGeometry3D CreateDeformedEllipsoid(
        double rx, double ry, double rz,
        int latSegs, int lonSegs,
        Func<double, double, double, double, double, Point3D>? deform)
    {
        var mesh = new MeshGeometry3D();

        for (int i = 0; i <= latSegs; i++)
        {
            double v = i / (double)latSegs;
            double phi = v * Math.PI;
            double sinPhi = Math.Sin(phi);
            double cosPhi = Math.Cos(phi);

            for (int j = 0; j <= lonSegs; j++)
            {
                double u = j / (double)lonSegs;
                double theta = u * 2.0 * Math.PI;
                double sinTheta = Math.Sin(theta);
                double cosTheta = Math.Cos(theta);

                double x = rx * sinPhi * sinTheta;
                double y = ry * cosPhi;
                double z = rz * sinPhi * cosTheta;

                Point3D pos = deform != null ? deform(x, y, z, u, v) : new Point3D(x, y, z);
                mesh.Positions.Add(pos);
                mesh.TextureCoordinates.Add(new Point(u, v));
            }
        }

        int stride = lonSegs + 1;
        for (int i = 0; i < latSegs; i++)
        {
            for (int j = 0; j < lonSegs; j++)
            {
                int p00 = i * stride + j;
                int p10 = (i + 1) * stride + j;
                int p01 = i * stride + j + 1;
                int p11 = (i + 1) * stride + j + 1;

                mesh.TriangleIndices.Add(p00);
                mesh.TriangleIndices.Add(p10);
                mesh.TriangleIndices.Add(p11);

                mesh.TriangleIndices.Add(p00);
                mesh.TriangleIndices.Add(p11);
                mesh.TriangleIndices.Add(p01);
            }
        }

        ComputeNormals(mesh);
        return mesh;
    }

    public static MeshGeometry3D CreateCurvedTube(
        Point3D[] spine,
        double[] radii,
        int radialSegs,
        bool capEnds)
    {
        var mesh = new MeshGeometry3D();
        int rings = spine.Length;
        if (rings < 2) return mesh;

        Vector3D prevNormal = new Vector3D(0, 0, 1);

        for (int i = 0; i < rings; i++)
        {
            Vector3D tangent;
            if (i == 0) tangent = spine[1] - spine[0];
            else if (i == rings - 1) tangent = spine[rings - 1] - spine[rings - 2];
            else tangent = spine[i + 1] - spine[i - 1];

            if (tangent.LengthSquared < 1e-10) tangent = new Vector3D(0, 1, 0);
            tangent.Normalize();

            // Parallel-transport frame
            Vector3D normal = prevNormal - Vector3D.DotProduct(prevNormal, tangent) * tangent;
            if (normal.LengthSquared < 1e-6)
            {
                Vector3D fallback = Math.Abs(tangent.Z) < 0.85 ? new Vector3D(0, 0, 1) : new Vector3D(1, 0, 0);
                normal = Vector3D.CrossProduct(tangent, fallback);
            }
            normal.Normalize();
            prevNormal = normal;

            Vector3D binormal = Vector3D.CrossProduct(tangent, normal);
            binormal.Normalize();

            double r = radii[Math.Min(i, radii.Length - 1)];
            double v = i / (double)(rings - 1);

            for (int j = 0; j <= radialSegs; j++)
            {
                double u = j / (double)radialSegs;
                double ang = u * 2.0 * Math.PI;
                Vector3D offset = Math.Cos(ang) * normal + Math.Sin(ang) * binormal;
                mesh.Positions.Add(spine[i] + r * offset);
                mesh.TextureCoordinates.Add(new Point(u, v));
            }
        }

        int stride = radialSegs + 1;
        for (int i = 0; i < rings - 1; i++)
        {
            for (int j = 0; j < radialSegs; j++)
            {
                int p00 = i * stride + j;
                int p10 = (i + 1) * stride + j;
                int p01 = i * stride + j + 1;
                int p11 = (i + 1) * stride + j + 1;

                mesh.TriangleIndices.Add(p00);
                mesh.TriangleIndices.Add(p01);
                mesh.TriangleIndices.Add(p11);

                mesh.TriangleIndices.Add(p00);
                mesh.TriangleIndices.Add(p11);
                mesh.TriangleIndices.Add(p10);
            }
        }

        if (capEnds)
        {
            // Start cap
            int startCenter = mesh.Positions.Count;
            mesh.Positions.Add(spine[0]);
            mesh.TextureCoordinates.Add(new Point(0.5, 0));
            for (int j = 0; j < radialSegs; j++)
            {
                mesh.TriangleIndices.Add(startCenter);
                mesh.TriangleIndices.Add(j + 1);
                mesh.TriangleIndices.Add(j);
            }

            // End cap
            int endCenter = mesh.Positions.Count;
            mesh.Positions.Add(spine[rings - 1]);
            mesh.TextureCoordinates.Add(new Point(0.5, 1));
            int lastRingStart = (rings - 1) * stride;
            for (int j = 0; j < radialSegs; j++)
            {
                mesh.TriangleIndices.Add(endCenter);
                mesh.TriangleIndices.Add(lastRingStart + j);
                mesh.TriangleIndices.Add(lastRingStart + j + 1);
            }
        }

        ComputeNormals(mesh);
        return mesh;
    }

    private static void ComputeNormals(MeshGeometry3D mesh)
    {
        int count = mesh.Positions.Count;
        var normals = new Vector3D[count];

        var indices = mesh.TriangleIndices;
        var pos = mesh.Positions;
        for (int i = 0; i + 2 < indices.Count; i += 3)
        {
            int i0 = indices[i];
            int i1 = indices[i + 1];
            int i2 = indices[i + 2];
            Vector3D e1 = pos[i1] - pos[i0];
            Vector3D e2 = pos[i2] - pos[i0];
            Vector3D fn = Vector3D.CrossProduct(e1, e2);
            normals[i0] += fn;
            normals[i1] += fn;
            normals[i2] += fn;
        }

        mesh.Normals.Clear();
        for (int i = 0; i < count; i++)
        {
            Vector3D n = normals[i];
            if (n.LengthSquared > 1e-12) n.Normalize();
            else n = new Vector3D(0, 1, 0);
            mesh.Normals.Add(n);
        }
    }
}
