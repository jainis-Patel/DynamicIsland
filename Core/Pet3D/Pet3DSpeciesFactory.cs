using System;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace DynamicIsland.Core.Pet3D;

/// <summary>
/// Holds a fully articulated 3D pet model hierarchy and pre-allocated transform handles
/// so per-frame animation mutates transform properties in-place with zero allocations.
/// </summary>
internal sealed class Pet3DRig
{
    public Model3DGroup RootModel { get; } = new();

    // Root transforms (squash/stretch, flip, bounce, position)
    public ScaleTransform3D RootScale { get; } = new(1, 1, 1);
    public AxisAngleRotation3D RootRotateZ { get; } = new(new Vector3D(0, 0, 1), 0);
    public AxisAngleRotation3D RootRotateX { get; } = new(new Vector3D(1, 0, 0), 0);
    public AxisAngleRotation3D RootRotateY { get; } = new(new Vector3D(0, 1, 0), 0);
    public TranslateTransform3D RootTranslate { get; } = new(0, 0, 0);

    // Seated vs Loaf torso swap (zero-allocation via ScaleTransform3D 0 or 1)
    public ScaleTransform3D SeatedTorsoScale { get; } = new(1, 1, 1);
    public ScaleTransform3D LoafTorsoScale { get; } = new(0, 0, 0);

    // Head articulation
    public TranslateTransform3D HeadTranslate { get; } = new(0, 0.72, 0.14);
    public AxisAngleRotation3D HeadYaw { get; } = new(new Vector3D(0, 1, 0), 0);
    public AxisAngleRotation3D HeadPitch { get; } = new(new Vector3D(1, 0, 0), 0);
    public AxisAngleRotation3D HeadRoll { get; } = new(new Vector3D(0, 0, 1), 0);

    // Ears
    public AxisAngleRotation3D EarLeftRot { get; } = new(new Vector3D(0, 0, 1), 0);
    public AxisAngleRotation3D EarRightRot { get; } = new(new Vector3D(0, 0, 1), 0);

    // Eyes (gaze + blink + pupil dilation + closed sleep/happy arcs)
    public TranslateTransform3D EyeLeftPupilShift { get; } = new(0, 0, 0);
    public TranslateTransform3D EyeRightPupilShift { get; } = new(0, 0, 0);
    public ScaleTransform3D EyeLeftOpenScale { get; } = new(1, 1, 1);
    public ScaleTransform3D EyeRightOpenScale { get; } = new(1, 1, 1);
    public ScaleTransform3D PupilScale { get; } = new(1, 1, 1);
    public ScaleTransform3D ClosedEyesScale { get; } = new(0, 0, 0);

    // Tongue (Happy / Dancing / Eating)
    public ScaleTransform3D TongueScale { get; } = new(0, 0, 0);
    public TranslateTransform3D TongueBob { get; } = new(0, 0, 0);

    // Limbs
    public AxisAngleRotation3D FrontLegLRot { get; } = new(new Vector3D(1, 0, 0), 0);
    public AxisAngleRotation3D FrontLegRRot { get; } = new(new Vector3D(1, 0, 0), 0);
    public TranslateTransform3D FrontLegLShift { get; } = new(0, 0, 0);
    public TranslateTransform3D FrontLegRShift { get; } = new(0, 0, 0);
    public ScaleTransform3D FrontLegsVisibleScale { get; } = new(1, 1, 1);
    public AxisAngleRotation3D HindLegLRot { get; } = new(new Vector3D(1, 0, 0), 0);
    public AxisAngleRotation3D HindLegRRot { get; } = new(new Vector3D(1, 0, 0), 0);

    // Tail chain (1 to 4 segments depending on species)
    public AxisAngleRotation3D[] TailSegRots { get; init; } = Array.Empty<AxisAngleRotation3D>();

    // Dragon Wings
    public AxisAngleRotation3D WingLeftRot { get; } = new(new Vector3D(0, 1, 0), 0);
    public AxisAngleRotation3D WingRightRot { get; } = new(new Vector3D(0, 1, 0), 0);

    // Neckwear bell / charm swing
    public AxisAngleRotation3D BellSwingRot { get; } = new(new Vector3D(0, 0, 1), 0);

    // Wearable Accessory slot
    public Model3DGroup AccessorySlot { get; } = new();
    public AxisAngleRotation3D AccessoryAnimRot { get; } = new(new Vector3D(0, 0, 1), 0);
    public TranslateTransform3D AccessoryAnimTranslate { get; } = new(0, 0, 0);
    public ScaleTransform3D AccessoryAnimScale { get; } = new(1, 1, 1);
}

internal static class Pet3DSpeciesFactory
{
    public static Pet3DRig BuildRig(string species, string accessory, bool heroLod)
    {
        var rig = new Pet3DRig
        {
            TailSegRots = species is "Cat" or "Dragon"
                ? new[]
                {
                    new AxisAngleRotation3D(new Vector3D(0, 0, 1), 0),
                    new AxisAngleRotation3D(new Vector3D(0, 0, 1), 0),
                    new AxisAngleRotation3D(new Vector3D(0, 0, 1), 0),
                    new AxisAngleRotation3D(new Vector3D(0, 0, 1), 0),
                }
                : new[]
                {
                    new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0),
                }
        };

        var rootGroup = new Transform3DGroup();
        rootGroup.Children.Add(rig.RootScale);
        rootGroup.Children.Add(new RotateTransform3D(rig.RootRotateZ, new Point3D(0, -0.78, 0)));
        rootGroup.Children.Add(new RotateTransform3D(rig.RootRotateX, new Point3D(0, -0.2, 0)));
        rootGroup.Children.Add(new RotateTransform3D(rig.RootRotateY, new Point3D(0, -0.2, 0)));
        rootGroup.Children.Add(rig.RootTranslate);
        rig.RootModel.Transform = rootGroup;

        Material coatMat = Pet3DTextureBakery.GetCoatMaterial(species, heroLod);
        Material headMat = Pet3DTextureBakery.GetHeadMaterial(species, heroLod);

        // 1. Seated Torso & Loaf Torso
        var seatedGroup = new Model3DGroup { Transform = rig.SeatedTorsoScale };
        seatedGroup.Children.Add(MakeModel(Pet3DMeshBuilder.GetSculptedTorso(loaf: false, heroLod), coatMat,
            new TranslateTransform3D(0, -0.22, -0.05)));
        rig.RootModel.Children.Add(seatedGroup);

        var loafGroup = new Model3DGroup { Transform = rig.LoafTorsoScale };
        loafGroup.Children.Add(MakeModel(Pet3DMeshBuilder.GetSculptedTorso(loaf: true, heroLod), coatMat,
            new TranslateTransform3D(0, -0.45, 0.0)));
        rig.RootModel.Children.Add(loafGroup);

        // 2. Hind Haunches & Back Paws
        var hindMat = GetLimbMaterial(species, coatMat, isPaw: false);
        var pawMat = GetLimbMaterial(species, coatMat, isPaw: true);
        bool bunnyFeet = species == "Bunny";

        AddHindLeg(rig.RootModel, -0.44, hindMat, pawMat, bunnyFeet, heroLod, rig.HindLegLRot);
        AddHindLeg(rig.RootModel, +0.44, hindMat, pawMat, bunnyFeet, heroLod, rig.HindLegRRot);

        // 3. Front Legs & Paws
        var frontLegsGroup = new Model3DGroup { Transform = rig.FrontLegsVisibleScale };
        AddFrontLeg(frontLegsGroup, -0.24, hindMat, pawMat, heroLod, rig.FrontLegLRot, rig.FrontLegLShift);
        AddFrontLeg(frontLegsGroup, +0.24, hindMat, pawMat, heroLod, rig.FrontLegRRot, rig.FrontLegRShift);
        rig.RootModel.Children.Add(frontLegsGroup);

        // 4. Tail & Wings
        BuildTail(rig, species, coatMat, heroLod);
        if (species == "Dragon")
        {
            BuildDragonWings(rig, heroLod);
        }

        // 5. Neckwear (Collar / Bandana / Bow / Scarf)
        BuildNeckwear(rig, species, heroLod);

        // 6. Articulated Head Assembly
        var headGroup = new Model3DGroup();
        var headTf = new Transform3DGroup();
        headTf.Children.Add(new RotateTransform3D(rig.HeadRoll));
        headTf.Children.Add(new RotateTransform3D(rig.HeadPitch));
        headTf.Children.Add(new RotateTransform3D(rig.HeadYaw));
        headTf.Children.Add(rig.HeadTranslate);
        headGroup.Transform = headTf;

        // Head skull mesh
        headGroup.Children.Add(MakeModel(Pet3DMeshBuilder.GetSculptedHead(species, heroLod), headMat));

        // Ears
        BuildEars(headGroup, species, coatMat, heroLod, rig.EarLeftRot, rig.EarRightRot);

        // Dragon Horns
        if (species == "Dragon")
        {
            var hornMat = Pet3DTextureBakery.GetPlushSolidMaterial(Color.FromRgb(245, 211, 140), 35, 160);
            headGroup.Children.Add(MakeModel(Pet3DMeshBuilder.GetDragonHornMesh(true, heroLod), hornMat,
                new TranslateTransform3D(-0.26, 0.48, -0.05)));
            headGroup.Children.Add(MakeModel(Pet3DMeshBuilder.GetDragonHornMesh(false, heroLod), hornMat,
                new TranslateTransform3D(0.26, 0.48, -0.05)));
        }

        // Panda Eye Patches & Shiba Brow Dots
        if (species == "Panda")
        {
            var patchMat = Pet3DTextureBakery.GetPlushSolidMaterial(Color.FromRgb(28, 29, 36), 8, 30);
            var patchMesh = Pet3DMeshBuilder.GetUnitSphere(heroLod);
            headGroup.Children.Add(MakeModel(patchMesh, patchMat, BuildScaleRotTrans(0.21, 0.17, 0.08, 20, -0.30, 0.08, 0.62)));
            headGroup.Children.Add(MakeModel(patchMesh, patchMat, BuildScaleRotTrans(0.21, 0.17, 0.08, -20, 0.30, 0.08, 0.62)));
        }
        else if (species == "Dog")
        {
            var dotMat = Pet3DTextureBakery.GetPlushSolidMaterial(Color.FromRgb(255, 250, 240), 6, 20);
            var dotMesh = Pet3DMeshBuilder.GetUnitSphere(false);
            headGroup.Children.Add(MakeModel(dotMesh, dotMat, BuildScaleRotTrans(0.085, 0.065, 0.05, 0, -0.25, 0.32, 0.56)));
            headGroup.Children.Add(MakeModel(dotMesh, dotMat, BuildScaleRotTrans(0.085, 0.065, 0.05, 0, 0.25, 0.32, 0.56)));
        }

        // Muzzle cushions, Wet 3D Nose, Tongue & Whiskers
        BuildMuzzleAndNose(headGroup, rig, species, heroLod);

        // Realistic Multi-Layer Eyes
        BuildEyes(headGroup, rig, species, heroLod);

        // Wearable Accessory Slot
        var accTf = new Transform3DGroup();
        accTf.Children.Add(rig.AccessoryAnimScale);
        accTf.Children.Add(new RotateTransform3D(rig.AccessoryAnimRot));
        accTf.Children.Add(rig.AccessoryAnimTranslate);
        rig.AccessorySlot.Transform = accTf;
        headGroup.Children.Add(rig.AccessorySlot);

        AttachAccessory(rig, accessory, heroLod);

        rig.RootModel.Children.Add(headGroup);
        return rig;
    }

    public static void AttachAccessory(Pet3DRig rig, string accessory, bool heroLod)
    {
        rig.AccessorySlot.Children.Clear();
        switch (accessory)
        {
            case "Crown":
            {
                var gold = Pet3DTextureBakery.GetPlushSolidMaterial(Color.FromRgb(255, 212, 38), 65, 230);
                rig.AccessorySlot.Children.Add(MakeModel(Pet3DMeshBuilder.GetCrownMesh(heroLod), gold,
                    new TranslateTransform3D(0, 0.52, 0.04)));
                var ruby = Pet3DTextureBakery.GetEmissiveSolidMaterial(Color.FromRgb(255, 45, 85), Color.FromRgb(120, 10, 35), 90);
                rig.AccessorySlot.Children.Add(MakeModel(Pet3DMeshBuilder.GetUnitSphere(false), ruby,
                    BuildScaleRotTrans(0.05, 0.05, 0.05, 0, 0, 0.62, 0.31)));
                break;
            }
            case "WizardHat":
            {
                var hatMat = Pet3DTextureBakery.GetPlushSolidMaterial(Color.FromRgb(94, 42, 152), 18, 80);
                var goldBand = Pet3DTextureBakery.GetPlushSolidMaterial(Color.FromRgb(255, 214, 10), 60, 210);
                rig.AccessorySlot.Children.Add(MakeModel(Pet3DMeshBuilder.GetConeMesh(0.58, 0.02, 0.05, heroLod ? 18 : 10), hatMat,
                    new TranslateTransform3D(0, 0.50, 0.02)));
                rig.AccessorySlot.Children.Add(MakeModel(Pet3DMeshBuilder.GetConeMesh(0.32, 0.02, 0.66, heroLod ? 16 : 10), hatMat,
                    new TranslateTransform3D(0, 0.52, 0.02)));
                rig.AccessorySlot.Children.Add(MakeModel(Pet3DMeshBuilder.GetTorusMesh(0.31, 0.032, 16, 8), goldBand,
                    new TranslateTransform3D(0, 0.56, 0.02)));
                break;
            }
            case "Headphones":
            {
                var bandMat = Pet3DTextureBakery.GetPlushSolidMaterial(Color.FromRgb(56, 56, 70), 30, 140);
                var cupMat = Pet3DTextureBakery.GetEmissiveSolidMaterial(Color.FromRgb(10, 132, 255), Color.FromRgb(0, 95, 165), 80);
                rig.AccessorySlot.Children.Add(MakeModel(Pet3DMeshBuilder.GetHeadphoneBandMesh(heroLod), bandMat));
                var cupMesh = Pet3DMeshBuilder.GetUnitSphere(heroLod);
                rig.AccessorySlot.Children.Add(MakeModel(cupMesh, cupMat, BuildScaleRotTrans(0.14, 0.22, 0.18, 0, -0.78, 0.05, 0.02)));
                rig.AccessorySlot.Children.Add(MakeModel(cupMesh, cupMat, BuildScaleRotTrans(0.14, 0.22, 0.18, 0, 0.78, 0.05, 0.02)));
                break;
            }
            case "Sunglasses":
            {
                var lensMat = Pet3DTextureBakery.GetPlushSolidMaterial(Color.FromRgb(18, 20, 30), 100, 250);
                var frameMat = Pet3DTextureBakery.GetPlushSolidMaterial(Color.FromRgb(24, 24, 32), 40, 160);
                var lensMesh = Pet3DMeshBuilder.GetUnitSphere(false);
                rig.AccessorySlot.Children.Add(MakeModel(lensMesh, lensMat, BuildScaleRotTrans(0.22, 0.14, 0.06, 0, -0.28, 0.08, 0.74)));
                rig.AccessorySlot.Children.Add(MakeModel(lensMesh, lensMat, BuildScaleRotTrans(0.22, 0.14, 0.06, 0, 0.28, 0.08, 0.74)));
                rig.AccessorySlot.Children.Add(MakeModel(lensMesh, frameMat, BuildScaleRotTrans(0.12, 0.028, 0.03, 0, 0, 0.10, 0.75)));
                break;
            }
            case "Halo":
            {
                var haloMat = Pet3DTextureBakery.GetEmissiveSolidMaterial(Color.FromRgb(255, 224, 80), Color.FromRgb(255, 195, 20), 90);
                rig.AccessorySlot.Children.Add(MakeModel(Pet3DMeshBuilder.GetTorusMesh(0.36, 0.045, 20, 8), haloMat,
                    new TranslateTransform3D(0, 0.86, 0.0)));
                break;
            }
            case "Sprout":
            {
                var stemMat = Pet3DTextureBakery.GetPlushSolidMaterial(Color.FromRgb(30, 138, 56), 15, 60);
                var leafMat = Pet3DTextureBakery.GetPlushSolidMaterial(Color.FromRgb(76, 226, 108), 22, 95);
                rig.AccessorySlot.Children.Add(MakeModel(Pet3DMeshBuilder.GetConeMesh(0.032, 0.024, 0.28, 8), stemMat,
                    new TranslateTransform3D(0, 0.56, 0.0)));
                var leafMesh = Pet3DMeshBuilder.GetUnitSphere(false);
                rig.AccessorySlot.Children.Add(MakeModel(leafMesh, leafMat, BuildScaleRotTrans(0.16, 0.05, 0.08, 22, -0.14, 0.82, 0.0)));
                rig.AccessorySlot.Children.Add(MakeModel(leafMesh, leafMat, BuildScaleRotTrans(0.16, 0.05, 0.08, -22, 0.14, 0.82, 0.0)));
                break;
            }
        }
    }

    private static Material GetLimbMaterial(string species, Material coatMat, bool isPaw)
    {
        return species switch
        {
            "Fox" when isPaw => Pet3DTextureBakery.GetPlushSolidMaterial(Color.FromRgb(42, 20, 12), 10, 35),
            "Panda" => Pet3DTextureBakery.GetPlushSolidMaterial(Color.FromRgb(28, 29, 36), 8, 30),
            "Cat" when isPaw => Pet3DTextureBakery.GetPlushSolidMaterial(Color.FromRgb(255, 244, 232), 8, 30),
            "Dog" when isPaw => Pet3DTextureBakery.GetPlushSolidMaterial(Color.FromRgb(255, 247, 238), 8, 30),
            "Dragon" when isPaw => Pet3DTextureBakery.GetPlushSolidMaterial(Color.FromRgb(255, 209, 92), 25, 110),
            _ => coatMat
        };
    }

    private static void AddHindLeg(Model3DGroup parent, double x, Material haunchMat, Material pawMat,
        bool bunnyFoot, bool heroLod, AxisAngleRotation3D rot)
    {
        var legGroup = new Model3DGroup();
        var tf = new Transform3DGroup();
        tf.Children.Add(new RotateTransform3D(rot, new Point3D(x, -0.58, 0.05)));
        legGroup.Transform = tf;

        legGroup.Children.Add(MakeModel(Pet3DMeshBuilder.GetHindHaunchMesh(heroLod), haunchMat,
            new TranslateTransform3D(x, -0.55, 0.04)));
        legGroup.Children.Add(MakeModel(Pet3DMeshBuilder.GetPawMesh(bunnyFoot, heroLod), pawMat,
            new TranslateTransform3D(x * 1.06, -0.78, bunnyFoot ? 0.24 : 0.18)));
        parent.Children.Add(legGroup);
    }

    private static void AddFrontLeg(Model3DGroup parent, double x, Material legMat, Material pawMat,
        bool heroLod, AxisAngleRotation3D rot, TranslateTransform3D shift)
    {
        var legGroup = new Model3DGroup();
        var tf = new Transform3DGroup();
        tf.Children.Add(new RotateTransform3D(rot, new Point3D(0, 0.25, 0)));
        tf.Children.Add(new TranslateTransform3D(x, -0.45, 0.34));
        tf.Children.Add(shift);
        legGroup.Transform = tf;

        legGroup.Children.Add(MakeModel(Pet3DMeshBuilder.GetForelegMesh(heroLod), legMat));
        legGroup.Children.Add(MakeModel(Pet3DMeshBuilder.GetPawMesh(false, heroLod), pawMat,
            new TranslateTransform3D(0, -0.35, 0.11)));
        parent.Children.Add(legGroup);
    }

    private static void BuildTail(Pet3DRig rig, string species, Material coatMat, bool heroLod)
    {
        if (species == "Dog")
        {
            var tailGroup = new Model3DGroup
            {
                Transform = new RotateTransform3D(rig.TailSegRots[0], new Point3D(0.32, -0.52, -0.30))
            };
            tailGroup.Children.Add(MakeModel(Pet3DMeshBuilder.GetShibaSpiralTailMesh(heroLod), coatMat,
                new TranslateTransform3D(0.32, -0.52, -0.30)));
            rig.RootModel.Children.Add(tailGroup);
            return;
        }

        if (species == "Fox")
        {
            var tailGroup = new Model3DGroup
            {
                Transform = new RotateTransform3D(rig.TailSegRots[0], new Point3D(0.34, -0.58, -0.28))
            };
            tailGroup.Children.Add(MakeModel(Pet3DMeshBuilder.GetFoxBushyTailMesh(heroLod), coatMat,
                new TranslateTransform3D(0.34, -0.58, -0.28)));
            var creamTip = Pet3DTextureBakery.GetPlushSolidMaterial(Color.FromRgb(255, 248, 240), 10, 35);
            tailGroup.Children.Add(MakeModel(Pet3DMeshBuilder.GetUnitSphere(heroLod), creamTip,
                BuildScaleRotTrans(0.18, 0.22, 0.18, 0, 1.15, 0.38, -0.32)));
            rig.RootModel.Children.Add(tailGroup);
            return;
        }

        if (species is "Bunny" or "Panda")
        {
            var mat = species == "Bunny"
                ? Pet3DTextureBakery.GetPlushSolidMaterial(Colors.White, 8, 25)
                : Pet3DTextureBakery.GetPlushSolidMaterial(Color.FromRgb(28, 29, 36), 8, 25);
            var tailGroup = new Model3DGroup
            {
                Transform = new RotateTransform3D(rig.TailSegRots[0], new Point3D(0.35, -0.58, -0.36))
            };
            tailGroup.Children.Add(MakeModel(Pet3DMeshBuilder.GetUnitSphere(heroLod), mat,
                BuildScaleRotTrans(0.18, 0.18, 0.18, 0, 0.40, -0.58, -0.42)));
            rig.RootModel.Children.Add(tailGroup);
            return;
        }

        // Cat & Dragon: 4-segment sinuous articulated tail chain
        Material segMat = coatMat;
        Model3DGroup parent = rig.RootModel;
        for (int i = 0; i < rig.TailSegRots.Length; i++)
        {
            var segGroup = new Model3DGroup();
            var tf = new Transform3DGroup();
            tf.Children.Add(new RotateTransform3D(rig.TailSegRots[i]));
            tf.Children.Add(i == 0
                ? new TranslateTransform3D(0.36, -0.60, -0.28)
                : new TranslateTransform3D(0.25, 0.03, 0.0));
            segGroup.Transform = tf;

            double rBase = 0.095 - i * 0.015;
            double rTip = 0.080 - i * 0.015;
            segGroup.Children.Add(MakeModel(Pet3DMeshBuilder.GetTailSegmentMesh(0.26, rBase, rTip, heroLod), segMat));

            if (species == "Dragon" && i == rig.TailSegRots.Length - 1)
            {
                var spadeMat = Pet3DTextureBakery.GetWingMaterial();
                segGroup.Children.Add(MakeModel(Pet3DMeshBuilder.GetUnitSphere(false), spadeMat,
                    BuildScaleRotTrans(0.12, 0.10, 0.04, 0, 0.28, 0.03, 0.0)));
            }

            parent.Children.Add(segGroup);
            parent = segGroup;
        }
    }

    private static void BuildDragonWings(Pet3DRig rig, bool heroLod)
    {
        var wingMat = Pet3DTextureBakery.GetWingMaterial();
        var wl = new Model3DGroup
        {
            Transform = new Transform3DGroup
            {
                Children =
                {
                    new RotateTransform3D(rig.WingLeftRot),
                    new TranslateTransform3D(-0.28, 0.15, -0.40)
                }
            }
        };
        wl.Children.Add(MakeModel(Pet3DMeshBuilder.GetDragonWingMesh(true, heroLod), wingMat));
        rig.RootModel.Children.Add(wl);

        var wr = new Model3DGroup
        {
            Transform = new Transform3DGroup
            {
                Children =
                {
                    new RotateTransform3D(rig.WingRightRot),
                    new TranslateTransform3D(0.28, 0.15, -0.40)
                }
            }
        };
        wr.Children.Add(MakeModel(Pet3DMeshBuilder.GetDragonWingMesh(false, heroLod), wingMat));
        rig.RootModel.Children.Add(wr);
    }

    private static void BuildNeckwear(Pet3DRig rig, string species, bool heroLod)
    {
        var goldMat = Pet3DTextureBakery.GetPlushSolidMaterial(Color.FromRgb(255, 208, 38), 70, 235);
        if (species == "Cat")
        {
            var redMat = Pet3DTextureBakery.GetPlushSolidMaterial(Color.FromRgb(220, 35, 58), 25, 110);
            rig.RootModel.Children.Add(MakeModel(Pet3DMeshBuilder.GetTorusMesh(0.42, 0.045, 18, 8), redMat,
                new TranslateTransform3D(0, 0.22, 0.10)));

            var bellGroup = new Model3DGroup
            {
                Transform = new Transform3DGroup
                {
                    Children =
                    {
                        new RotateTransform3D(rig.BellSwingRot, new Point3D(0, 0.08, 0)),
                        new TranslateTransform3D(0, 0.12, 0.54)
                    }
                }
            };
            bellGroup.Children.Add(MakeModel(Pet3DMeshBuilder.GetUnitSphere(false), goldMat,
                new ScaleTransform3D(0.085, 0.085, 0.085)));
            rig.RootModel.Children.Add(bellGroup);
        }
        else if (species is "Dog" or "Fox")
        {
            var clothMat = species == "Fox"
                ? Pet3DTextureBakery.GetPlushSolidMaterial(Color.FromRgb(22, 158, 82), 12, 45)
                : Pet3DTextureBakery.GetPlushSolidMaterial(Color.FromRgb(218, 34, 54), 12, 45);
            rig.RootModel.Children.Add(MakeModel(Pet3DMeshBuilder.GetBandanaMesh(heroLod), clothMat,
                new TranslateTransform3D(0, 0.14, 0.12)));
            rig.RootModel.Children.Add(MakeModel(Pet3DMeshBuilder.GetUnitSphere(false), goldMat,
                BuildScaleRotTrans(0.055, 0.055, 0.045, 0, 0, 0.18, 0.56)));
        }
        else if (species == "Bunny")
        {
            var bowMat = Pet3DTextureBakery.GetPlushSolidMaterial(Color.FromRgb(255, 92, 141), 16, 70);
            var loopMesh = Pet3DMeshBuilder.GetUnitSphere(false);
            rig.RootModel.Children.Add(MakeModel(loopMesh, bowMat, BuildScaleRotTrans(0.14, 0.08, 0.05, 18, -0.14, 0.18, 0.52)));
            rig.RootModel.Children.Add(MakeModel(loopMesh, bowMat, BuildScaleRotTrans(0.14, 0.08, 0.05, -18, 0.14, 0.18, 0.52)));
            rig.RootModel.Children.Add(MakeModel(loopMesh, bowMat, BuildScaleRotTrans(0.06, 0.06, 0.05, 0, 0, 0.18, 0.54)));
        }
        else if (species == "Panda")
        {
            var redMat = Pet3DTextureBakery.GetPlushSolidMaterial(Color.FromRgb(220, 35, 58), 25, 110);
            var leafMat = Pet3DTextureBakery.GetPlushSolidMaterial(Color.FromRgb(48, 209, 88), 22, 95);
            rig.RootModel.Children.Add(MakeModel(Pet3DMeshBuilder.GetTorusMesh(0.44, 0.042, 18, 8), redMat,
                new TranslateTransform3D(0, 0.22, 0.10)));
            rig.RootModel.Children.Add(MakeModel(Pet3DMeshBuilder.GetUnitSphere(false), leafMat,
                BuildScaleRotTrans(0.08, 0.05, 0.03, 0, 0, 0.16, 0.56)));
        }
    }

    private static void BuildEars(Model3DGroup headGroup, string species, Material coatMat, bool heroLod,
        AxisAngleRotation3D earL, AxisAngleRotation3D earR)
    {
        Material earOuter = species switch
        {
            "Dog" => Pet3DTextureBakery.GetPlushSolidMaterial(Color.FromRgb(126, 68, 28), 8, 30),
            "Fox" => Pet3DTextureBakery.GetPlushSolidMaterial(Color.FromRgb(56, 26, 16), 8, 30),
            "Panda" => Pet3DTextureBakery.GetPlushSolidMaterial(Color.FromRgb(28, 29, 36), 8, 30),
            "Dragon" => Pet3DTextureBakery.GetWingMaterial(),
            _ => coatMat
        };

        Material earInner = species switch
        {
            "Dog" => Pet3DTextureBakery.GetPlushSolidMaterial(Color.FromRgb(255, 245, 232), 6, 18),
            "Panda" => earOuter,
            "Dragon" => Pet3DTextureBakery.GetPlushSolidMaterial(Color.FromRgb(255, 209, 92), 20, 90),
            _ => Pet3DTextureBakery.GetPlushSolidMaterial(Color.FromRgb(255, 164, 188), 8, 25),
        };

        (double px, double py, double pz, double baseAng) = species switch
        {
            "Bunny" => (0.28, 0.44, -0.02, 14.0),
            "Panda" => (0.50, 0.44, 0.0, 28.0),
            "Dog" => (0.44, 0.36, 0.02, 26.0),
            _ => (0.42, 0.38, 0.0, 24.0),
        };

        var leftGroup = new Model3DGroup
        {
            Transform = new Transform3DGroup
            {
                Children =
                {
                    new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), baseAng)),
                    new RotateTransform3D(earL),
                    new TranslateTransform3D(-px, py, pz)
                }
            }
        };
        leftGroup.Children.Add(MakeModel(Pet3DMeshBuilder.GetEarMesh(species, false, heroLod), earOuter));
        if (species != "Panda")
            leftGroup.Children.Add(MakeModel(Pet3DMeshBuilder.GetEarMesh(species, true, heroLod), earInner));
        headGroup.Children.Add(leftGroup);

        var rightGroup = new Model3DGroup
        {
            Transform = new Transform3DGroup
            {
                Children =
                {
                    new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), -baseAng)),
                    new RotateTransform3D(earR),
                    new TranslateTransform3D(px, py, pz)
                }
            }
        };
        rightGroup.Children.Add(MakeModel(Pet3DMeshBuilder.GetEarMesh(species, false, heroLod), earOuter));
        if (species != "Panda")
            rightGroup.Children.Add(MakeModel(Pet3DMeshBuilder.GetEarMesh(species, true, heroLod), earInner));
        headGroup.Children.Add(rightGroup);
    }

    private static void BuildMuzzleAndNose(Model3DGroup headGroup, Pet3DRig rig, string species, bool heroLod)
    {
        if (heroLod && species is "Cat" or "Dog" or "Bunny" or "Fox" or "Panda")
        {
            Color padColor = species switch
            {
                "Dog" => Color.FromRgb(255, 247, 238),
                "Fox" => Color.FromRgb(255, 248, 240),
                "Panda" => Color.FromRgb(250, 248, 245),
                "Bunny" => Color.FromRgb(255, 246, 250),
                _ => Color.FromRgb(255, 244, 232),
            };
            var padMat = Pet3DTextureBakery.GetPlushSolidMaterial(padColor, 8, 25);
            var padMesh = Pet3DMeshBuilder.GetUnitSphere(false);
            headGroup.Children.Add(MakeModel(padMesh, padMat, BuildScaleRotTrans(0.14, 0.10, 0.11, 0, -0.11, -0.16, 0.68)));
            headGroup.Children.Add(MakeModel(padMesh, padMat, BuildScaleRotTrans(0.14, 0.10, 0.11, 0, 0.11, -0.16, 0.68)));
        }

        // Wet glossy 3D nose
        bool pinkNose = species is "Cat" or "Bunny";
        var noseMat = Pet3DTextureBakery.GetPlushSolidMaterial(
            pinkNose ? Color.FromRgb(255, 118, 152) : Color.FromRgb(32, 26, 36),
            95, 245);
        double nz = species is "Dog" or "Fox" ? 0.80 : 0.75;
        headGroup.Children.Add(MakeModel(Pet3DMeshBuilder.GetUnitSphere(false), noseMat,
            BuildScaleRotTrans(0.105, 0.072, 0.085, 0, 0, -0.08, nz)));

        // Happy / Panting Pink Tongue
        var tongueGroup = new Model3DGroup
        {
            Transform = new Transform3DGroup
            {
                Children =
                {
                    rig.TongueScale,
                    rig.TongueBob,
                    new TranslateTransform3D(0, -0.25, nz - 0.04)
                }
            }
        };
        var tongueMat = Pet3DTextureBakery.GetPlushSolidMaterial(Color.FromRgb(255, 92, 128), 65, 200);
        tongueGroup.Children.Add(MakeModel(Pet3DMeshBuilder.GetUnitSphere(false), tongueMat,
            new ScaleTransform3D(0.095, 0.11, 0.06)));
        headGroup.Children.Add(tongueGroup);

        // 3D Whiskers in Hero LOD for Cat, Fox, Bunny
        if (heroLod && species is "Cat" or "Fox" or "Bunny")
        {
            var whiskerMat = Pet3DTextureBakery.GetPlushSolidMaterial(Color.FromArgb(200, 255, 255, 255), 25, 120);
            for (int i = 0; i < 2; i++)
            {
                headGroup.Children.Add(MakeModel(Pet3DMeshBuilder.GetWhiskerMesh(true, i), whiskerMat));
                headGroup.Children.Add(MakeModel(Pet3DMeshBuilder.GetWhiskerMesh(false, i), whiskerMat));
            }
        }
    }

    private static void BuildEyes(Model3DGroup headGroup, Pet3DRig rig, string species, bool heroLod)
    {
        Material irisMat = Pet3DTextureBakery.GetIrisMaterial(species);
        Material pupilMat = Pet3DTextureBakery.GetPlushSolidMaterial(Color.FromRgb(16, 12, 20), 60, 180);
        Material corneaMat = Pet3DTextureBakery.GetCorneaShellMaterial();
        Material closedMat = Pet3DTextureBakery.GetPlushSolidMaterial(Color.FromRgb(24, 18, 28), 15, 60);

        var sphere = Pet3DMeshBuilder.GetUnitSphere(heroLod);
        bool slitPupil = species is "Cat" or "Fox" or "Dragon";
        double ez = species is "Dog" or "Fox" ? 0.62 : 0.60;

        for (int side = -1; side <= 1; side += 2)
        {
            var openScale = side < 0 ? rig.EyeLeftOpenScale : rig.EyeRightOpenScale;
            var pupilShift = side < 0 ? rig.EyeLeftPupilShift : rig.EyeRightPupilShift;

            var eyeSocket = new Model3DGroup
            {
                Transform = new Transform3DGroup
                {
                    Children =
                    {
                        openScale,
                        new TranslateTransform3D(side * 0.28, 0.08, ez)
                    }
                }
            };

            // Textured Iris Sphere
            eyeSocket.Children.Add(MakeModel(sphere, irisMat, new ScaleTransform3D(0.145, 0.165, 0.11)));

            // Dynamic Pupil (tracks cursor + dilates)
            var pupilTf = new Transform3DGroup();
            pupilTf.Children.Add(rig.PupilScale);
            pupilTf.Children.Add(new ScaleTransform3D(slitPupil ? 0.048 : 0.082, slitPupil ? 0.12 : 0.095, 0.06));
            pupilTf.Children.Add(new TranslateTransform3D(0, 0, 0.075));
            pupilTf.Children.Add(pupilShift);
            eyeSocket.Children.Add(MakeModel(sphere, pupilMat, pupilTf));

            // Glossy Corneal Specular Shell
            if (heroLod)
            {
                eyeSocket.Children.Add(MakeModel(sphere, corneaMat, new ScaleTransform3D(0.152, 0.172, 0.12)));
            }

            headGroup.Children.Add(eyeSocket);
        }

        // Closed / Happy Eyelid Arcs
        var closedGroup = new Model3DGroup { Transform = rig.ClosedEyesScale };
        closedGroup.Children.Add(MakeModel(sphere, closedMat, BuildScaleRotTrans(0.13, 0.025, 0.04, 0, -0.28, 0.08, ez + 0.08)));
        closedGroup.Children.Add(MakeModel(sphere, closedMat, BuildScaleRotTrans(0.13, 0.025, 0.04, 0, 0.28, 0.08, ez + 0.08)));
        headGroup.Children.Add(closedGroup);
    }

    private static GeometryModel3D MakeModel(MeshGeometry3D mesh, Material material, Transform3D? transform = null)
    {
        var model = new GeometryModel3D(mesh, material);
        if (transform != null) model.Transform = transform;
        return model;
    }

    private static Transform3D BuildScaleRotTrans(double sx, double sy, double sz, double rotZDeg, double tx, double ty, double tz)
    {
        var tg = new Transform3DGroup();
        tg.Children.Add(new ScaleTransform3D(sx, sy, sz));
        if (Math.Abs(rotZDeg) > 0.01)
            tg.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), rotZDeg)));
        tg.Children.Add(new TranslateTransform3D(tx, ty, tz));
        return tg;
    }
}
