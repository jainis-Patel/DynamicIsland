using System.Windows;
using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland.Core;

public sealed partial class VirtualPetControl
{
    private void RenderCat(DrawingContext dc, PetMood mood, double t)
    {
        bool sleeping = mood == PetMood.Sleeping;

        // 1. Tail rooted at the rump
        DrawCatTail(dc, mood, t);

        // 2. Seated Hind Haunches (thighs) & Back Paws
        dc.DrawEllipse(CatCoatBrush, CatOutlinePen, new Point(-4.9, 6.7), 2.2, 2.5);
        dc.DrawEllipse(CatCoatBrush, CatOutlinePen, new Point(4.9, 6.7), 2.2, 2.5);
        if (!sleeping || IsScampering || IsPeeking)
        {
            double hindL = IsScampering ? Math.Sin(t * 24.0 + 2.0) * 1.5 : 0.0;
            double hindR = IsScampering ? -Math.Sin(t * 24.0 + 2.0) * 1.5 : 0.0;
            dc.DrawEllipse(CatChestBrush, CatOutlinePen, new Point(-5.5, 8.9 + hindL), 1.65, 0.95);
            dc.DrawEllipse(CatChestBrush, CatOutlinePen, new Point(5.5, 8.9 + hindR), 1.65, 0.95);
        }

        // 3. Full Torso + Volumetric Shading + Tuxedo Chest Patch
        var torso = sleeping && !IsScampering && !IsPeeking ? LoafTorsoGeo : TorsoGeo;
        dc.DrawGeometry(CatCoatBrush, CatOutlinePen, torso);
        ShadeTorso(dc, torso);
        dc.DrawGeometry(CatChestBrush, null, ChestPatchGeo);

        // Tabby flank stripes on the body & haunches
        dc.DrawLine(CatBodyTabbyPen, new Point(-5.5, 5.2), new Point(-3.9, 5.6));
        dc.DrawLine(CatBodyTabbyPen, new Point(-5.8, 6.6), new Point(-4.1, 6.9));
        dc.DrawLine(CatBodyTabbyPen, new Point(5.5, 5.2), new Point(3.9, 5.6));
        dc.DrawLine(CatBodyTabbyPen, new Point(5.8, 6.6), new Point(4.1, 6.9));

        // 4. Front Legs & Paws
        DrawFrontLegsAndPaws(dc, mood, t, CatCoatBrush, CatChestBrush, CatOutlinePen, ToeLinePen, true, IsScampering, IsPeeking);

        // 5. Collar & Jingle Bell (at the neck junction)
        double collarY = sleeping ? 1.6 : 0.0;
        Push(dc, new Matrix(1, 0, 0, 1, 0, collarY));
        dc.DrawGeometry(null, CatCollarPen, CatCollarGeo);
        double bellSwing = Math.Clamp(_headShiftX * 0.6 + Math.Sin(t * 9.0) * (_bass * 0.8), -1.0, 1.0);
        var bellPos = new Point(bellSwing, 1.45);
        dc.DrawEllipse(GoldBellBrush, GoldBellOutlinePen, bellPos, 1.15, 1.15);
        dc.DrawLine(GoldBellOutlinePen, new Point(bellPos.X - 0.85, bellPos.Y + 0.1), new Point(bellPos.X + 0.85, bellPos.Y + 0.1));
        dc.DrawEllipse(BellSlotBrush, null, new Point(bellPos.X, bellPos.Y + 0.45), 0.32, 0.28);
        dc.DrawEllipse(WhiteSpecularBrush, null, new Point(bellPos.X - 0.35, bellPos.Y - 0.35), 0.32, 0.25);
        dc.Pop();

        // 6. Articulated Head (with secondary bob, tilt, and gaze tracking)
        PushHeadTransform(dc);

        double ea = _earAng + _earTwitch;
        DrawEar(dc, CatEarL, CatEarInnerL, CatCoatBrush, CatOutlinePen, CatEarInnerBrush, CatPivotL, -(ea + _earFlick));
        DrawEar(dc, CatEarR, CatEarInnerR, CatCoatBrush, CatOutlinePen, CatEarInnerBrush, CatPivotR, ea);
        dc.DrawEllipse(WhiteSpecularBrush, null, new Point(-4.2, -4.0), 0.9, 0.7);
        dc.DrawEllipse(WhiteSpecularBrush, null, new Point(4.2, -4.0), 0.9, 0.7);

        dc.DrawGeometry(CatCoatBrush, CatOutlinePen, CatHeadGeo);
        ShadeHead(dc, CatHeadGeo, CatHeadRect);
        dc.DrawEllipse(CatChestBrush, null, new Point(0, 4.0), 4.6, 3.0);

        // Forehead "M" + cheek tabby markings
        dc.DrawLine(CatTabbyPen, new Point(-2.8, -3.2), new Point(-1.5, -1.0));
        dc.DrawLine(CatTabbyPen, new Point(-1.5, -1.0), new Point(0.0, -2.8));
        dc.DrawLine(CatTabbyPen, new Point(0.0, -2.8), new Point(1.5, -1.0));
        dc.DrawLine(CatTabbyPen, new Point(1.5, -1.0), new Point(2.8, -3.2));
        dc.DrawLine(CatTabbyPen, new Point(0.0, -4.0), new Point(0.0, -2.8));
        dc.DrawLine(CatTabbyPen, new Point(-8.4, 2.4), new Point(-6.6, 2.9));
        dc.DrawLine(CatTabbyPen, new Point(-8.6, 4.0), new Point(-6.8, 4.1));
        dc.DrawLine(CatTabbyPen, new Point(8.4, 2.4), new Point(6.6, 2.9));
        dc.DrawLine(CatTabbyPen, new Point(8.6, 4.0), new Point(6.8, 4.1));

        if (mood == PetMood.Happy) DrawBlush(dc, 1.05);

        // Whiskers
        double wf = _isNearCursor ? 11.0 : 6.0;
        double w1 = Math.Sin(t * wf) * 0.5, w2 = Math.Sin(t * wf + 1.2) * 0.5;
        dc.DrawLine(WhiskerPen, new Point(-6.2, 1.2), new Point(-11.6, 0.4 + w1));
        dc.DrawLine(WhiskerPen, new Point(-6.2, 2.8), new Point(-11.2, 3.8 + w2));
        dc.DrawLine(WhiskerPen, new Point(6.2, 1.2), new Point(11.6, 0.4 - w1));
        dc.DrawLine(WhiskerPen, new Point(6.2, 2.8), new Point(11.2, 3.8 - w2));

        // Nose + mouth
        dc.DrawGeometry(PinkNoseBrush, null, CatNoseGeo);
        if (mood == PetMood.Happy || mood == PetMood.Dancing)
        {
            dc.DrawEllipse(DeepPupilBrush, null, new Point(0, 3.5), 1.25, 1.0);
            dc.DrawEllipse(DogTongueBrush, null, new Point(0, 4.0), 0.75, 0.55);
        }
        else
        {
            dc.DrawLine(MouthPen, new Point(0.0, 1.8), new Point(0.0, 2.8));
            dc.DrawLine(MouthPen, new Point(0.0, 2.8), new Point(-1.2, 3.5));
            dc.DrawLine(MouthPen, new Point(0.0, 2.8), new Point(1.2, 3.5));
        }

        DrawEyes(dc, mood, t, CatIrisBrush, 0);
        DrawAccessory(dc, t);

        dc.Pop(); // head
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // \U0001F436 Dog (Shiba Inu â€“ Full Seated Body + Bandana + Curled Rump Tail)
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    private void RenderDog(DrawingContext dc, PetMood mood, double t)
    {
        bool sleeping = mood == PetMood.Sleeping;

        // 1. Spiral cinnamon-roll Shiba tail rooted at the rump
        double wagFreq = mood == PetMood.Happy ? 18.0 : (mood == PetMood.Dancing ? 14.0 : 4.2);
        double wagDeg = Math.Sin(t * wagFreq) * (mood == PetMood.Happy ? 18.0 : (mood == PetMood.Dancing ? 13.0 : (sleeping ? 2.5 : 6.0)));
        var tm = Matrix.Identity;
        tm.RotateAt(wagDeg, DogTailBase.X, DogTailBase.Y);
        Push(dc, tm);
        dc.DrawGeometry(null, DogTailOutlinePen, DogTailGeo);
        dc.DrawGeometry(null, DogTailPen, DogTailGeo);
        dc.DrawGeometry(null, DogTailInnerPen, DogTailGeo);
        dc.DrawEllipse(DogUrajiroBrush, null, DogTailTip, 1.25, 1.25);
        dc.Pop();

        // 2. Seated Hind Haunches & Urajiro Back Paws
        dc.DrawEllipse(DogCoatBrush, DogOutlinePen, new Point(-4.9, 6.7), 2.25, 2.5);
        dc.DrawEllipse(DogCoatBrush, DogOutlinePen, new Point(4.9, 6.7), 2.25, 2.5);
        if (!sleeping || IsScampering || IsPeeking)
        {
            double hindL = IsScampering ? Math.Sin(t * 24.0 + 2.0) * 1.5 : 0.0;
            double hindR = IsScampering ? -Math.Sin(t * 24.0 + 2.0) * 1.5 : 0.0;
            dc.DrawEllipse(DogUrajiroBrush, DogOutlinePen, new Point(-5.5, 8.9 + hindL), 1.7, 0.95);
            dc.DrawEllipse(DogUrajiroBrush, DogOutlinePen, new Point(5.5, 8.9 + hindR), 1.7, 0.95);
        }

        // 3. Full Torso + Cream Urajiro Chest Shield
        var torso = sleeping && !IsScampering && !IsPeeking ? LoafTorsoGeo : TorsoGeo;
        dc.DrawGeometry(DogCoatBrush, DogOutlinePen, torso);
        ShadeTorso(dc, torso);
        dc.DrawGeometry(DogUrajiroBrush, null, ChestPatchGeo);

        // 4. Front Legs & Cream Socks
        DrawFrontLegsAndPaws(dc, mood, t, DogCoatBrush, DogUrajiroBrush, DogOutlinePen, ToeLinePen, false, IsScampering, IsPeeking);

        // 5. Shiba Neckerchief / Bandana + Gold Tag
        double bandanaY = sleeping ? 1.5 : 0.0;
        Push(dc, new Matrix(1, 0, 0, 1, 0, bandanaY));
        dc.DrawGeometry(CollarRedBrush, GoldBellOutlinePen, DogBandanaGeo);
        dc.DrawEllipse(GoldBellBrush, GoldBellOutlinePen, new Point(0, 1.25), 0.75, 0.75);
        dc.Pop();

        // 6. Articulated Head
        PushHeadTransform(dc);

        double ea = _earAng + _earTwitch;
        DrawEar(dc, DogEarL, DogEarInnerL, DogEarDarkBrush, DogOutlinePen, DogUrajiroBrush, DogPivotL, -(ea + _earFlick * 0.6));
        DrawEar(dc, DogEarR, DogEarInnerR, DogEarDarkBrush, DogOutlinePen, DogUrajiroBrush, DogPivotR, ea);

        dc.DrawGeometry(DogCoatBrush, DogOutlinePen, DogHeadGeo);
        ShadeHead(dc, DogHeadGeo, DogHeadRect);
        dc.DrawGeometry(DogUrajiroBrush, null, DogUrajiroGeo);

        // Maro-mayu eyebrow dots
        double browTilt = mood == PetMood.Happy ? -0.4 : (_isNearCursor ? -0.6 : 0.0);
        dc.DrawEllipse(DogTanDotBrush, null, new Point(-4.2, -3.8 + browTilt), 1.35, 1.05);
        dc.DrawEllipse(DogTanDotBrush, null, new Point(4.2, -3.8 + browTilt), 1.35, 1.05);

        if (mood == PetMood.Happy) DrawBlush(dc, 0.95);

        // Mouth / happy tongue blep
        if (mood == PetMood.Happy || mood == PetMood.Dancing)
        {
            double tb = Math.Sin(t * 14.0) * 0.55;
            dc.DrawEllipse(DeepPupilBrush, null, new Point(0, 4.0), 2.2, 1.8);
            dc.DrawEllipse(DogTongueBrush, null, new Point(0, 4.8 + tb), 1.9, 2.2);
            dc.DrawEllipse(WhiteSpecularBrush, null, new Point(-0.5, 4.3 + tb), 0.5, 0.5);
        }
        else
        {
            dc.DrawLine(MouthPen, new Point(-1.6, 3.8), new Point(0.0, 3.0));
            dc.DrawLine(MouthPen, new Point(0.0, 3.0), new Point(1.6, 3.8));
        }

        // Glossy nose
        dc.DrawEllipse(DarkNoseBrush, null, new Point(0, 1.0), 1.55, 1.15);
        dc.DrawEllipse(WhiteSpecularBrush, null, new Point(-0.4, 0.7), 0.45, 0.35);

        DrawEyes(dc, mood, t, DogIrisBrush, 1);
        DrawAccessory(dc, t);

        dc.Pop(); // head
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // \U0001F430 Bunny (Full Plump Body + Thumper Hind Feet + Neck Ribbon Bow)
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    private void RenderBunny(DrawingContext dc, PetMood mood, double t)
    {
        bool sleeping = mood == PetMood.Sleeping;

        // 1. Fluffy cotton pom-pom tail at the lower rump (wiggles when happy)
        double tailWiggle = (mood == PetMood.Happy || mood == PetMood.Dancing) ? Math.Sin(t * 18.0) * 0.65 : 0.0;
        dc.DrawEllipse(BunnyCoatBrush, BunnyOutlinePen, new Point(5.8 + tailWiggle, 7.3), 2.35, 2.35);
        dc.DrawEllipse(WhiteSpecularBrush, null, new Point(5.4 + tailWiggle, 6.7), 0.95, 0.95);

        // 2. Plump Hind Haunches & Long "Thumper" Feet
        dc.DrawEllipse(BunnyCoatBrush, BunnyOutlinePen, new Point(-5.1, 6.5), 2.45, 2.65);
        dc.DrawEllipse(BunnyCoatBrush, BunnyOutlinePen, new Point(5.1, 6.5), 2.45, 2.65);
        if (!sleeping || IsScampering || IsPeeking)
        {
            double hindL = IsScampering ? Math.Sin(t * 24.0 + 2.0) * 1.5 : 0.0;
            double hindR = IsScampering ? -Math.Sin(t * 24.0 + 2.0) * 1.5 : 0.0;
            dc.DrawEllipse(BunnyCoatBrush, BunnyOutlinePen, new Point(-5.7, 8.95 + hindL), 2.15, 1.05);
            dc.DrawEllipse(PawPadBrush, null, new Point(-5.9, 9.05 + hindL), 0.75, 0.42);
            dc.DrawEllipse(BunnyCoatBrush, BunnyOutlinePen, new Point(5.7, 8.95 + hindR), 2.15, 1.05);
            dc.DrawEllipse(PawPadBrush, null, new Point(5.9, 9.05 + hindR), 0.75, 0.42);
        }

        // 3. Full Torso + Fluffy Tummy Patch
        var torso = sleeping && !IsScampering && !IsPeeking ? LoafTorsoGeo : TorsoGeo;
        dc.DrawGeometry(BunnyCoatBrush, BunnyOutlinePen, torso);
        ShadeTorso(dc, torso);
        dc.DrawGeometry(BunnyBellyBrush, null, ChestPatchGeo);

        // 4. Front Legs & Paws
        DrawFrontLegsAndPaws(dc, mood, t, BunnyCoatBrush, BunnyCoatBrush, BunnyOutlinePen, BunnyToeLinePen, true, IsScampering, IsPeeking);

        // 5. Pastel Neck Ribbon Bow
        double bowY = sleeping ? 1.5 : 0.0;
        Push(dc, new Matrix(1, 0, 0, 1, 0, bowY));
        dc.DrawGeometry(BunnyBowBrush, BunnyBowPen, BunnyBowGeo);
        dc.DrawEllipse(BunnyBowBrush, BunnyBowPen, new Point(0, 1.15), 0.75, 0.68);
        dc.Pop();

        // 6. Articulated Head
        PushHeadTransform(dc);

        double ea = (_earAng + _earTwitch) * 1.25;
        DrawEar(dc, BunnyEarL, BunnyEarInnerL, BunnyCoatBrush, BunnyOutlinePen, BunnyEarInnerBrush, BunnyPivotL, -(ea + _earFlick * 0.8));
        DrawEar(dc, BunnyEarR, BunnyEarInnerR, BunnyCoatBrush, BunnyOutlinePen, BunnyEarInnerBrush, BunnyPivotR, ea);

        dc.DrawGeometry(BunnyCoatBrush, BunnyOutlinePen, BunnyHeadGeo);
        ShadeHead(dc, BunnyHeadGeo, BunnyHeadRect);
        dc.DrawEllipse(BunnyBellyBrush, null, new Point(0, 3.8), 4.2, 2.8);

        bool bloom = mood == PetMood.Happy || _squashVelocity < -0.05;
        DrawBlush(dc, bloom ? 1.35 : 1.0);

        // Micro whiskers
        dc.DrawLine(WhiskerPen, new Point(-4.8, 1.8), new Point(-9.2, 1.2));
        dc.DrawLine(WhiskerPen, new Point(-4.8, 2.8), new Point(-9.0, 3.5));
        dc.DrawLine(WhiskerPen, new Point(4.8, 1.8), new Point(9.2, 1.2));
        dc.DrawLine(WhiskerPen, new Point(4.8, 2.8), new Point(9.0, 3.5));

        // Twitching nose + Y mouth (+ buck teeth when happy)
        double twitch = Math.Sin(t * 18.0) > 0.6 ? 0.35 : 0.0;
        dc.DrawEllipse(PinkNoseBrush, null, new Point(0, 1.2 - twitch), 1.2, 0.98);
        dc.DrawEllipse(WhiteSpecularBrush, null, new Point(-0.35, 0.9 - twitch), 0.35, 0.25);
        dc.DrawLine(MouthPen, new Point(0.0, 2.2), new Point(0.0, 2.9));
        dc.DrawLine(MouthPen, new Point(0.0, 2.9), new Point(-1.0, 3.4));
        dc.DrawLine(MouthPen, new Point(0.0, 2.9), new Point(1.0, 3.4));
        if (mood == PetMood.Happy)
            dc.DrawRoundedRectangle(WhiteSpecularBrush, null, new Rect(-0.9, 3.1, 1.8, 1.2), 0.3, 0.3);

        DrawEyes(dc, mood, t, BunnyIrisBrush, 2);
        DrawAccessory(dc, t);

        dc.Pop(); // head
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 🦊 Fox (Kitsune – Bushy White-Tipped Tail + Diamond Cheek Ruff + Scarf)
    // ─────────────────────────────────────────────────────────────────────────────
    private void RenderFox(DrawingContext dc, PetMood mood, double t)
    {
        bool sleeping = mood == PetMood.Sleeping;

        // 1. Oversized bushy Kitsune tail with snow-white tip
        double wagFreq = mood == PetMood.Happy || mood == PetMood.Eating ? 15.0 : (mood == PetMood.Dancing ? 12.0 : 3.4);
        double wagDeg = Math.Sin(t * wagFreq) * (mood == PetMood.Happy ? 18.0 : (mood == PetMood.Dancing ? 14.0 : (sleeping ? 3.5 : 8.5)));
        var tm = Matrix.Identity;
        tm.RotateAt(wagDeg, FoxTailBase.X, FoxTailBase.Y);
        Push(dc, tm);
        dc.DrawGeometry(FoxCoatBrush, FoxOutlinePen, FoxTailGeo);
        dc.DrawGeometry(FoxCreamBrush, null, FoxTailTipGeo);
        dc.Pop();

        // 2. Seated Hind Haunches & Dark Chocolate Socks
        dc.DrawEllipse(FoxCoatBrush, FoxOutlinePen, new Point(-4.9, 6.7), 2.2, 2.5);
        dc.DrawEllipse(FoxCoatBrush, FoxOutlinePen, new Point(4.9, 6.7), 2.2, 2.5);
        if (!sleeping || IsScampering || IsPeeking)
        {
            double hindL = IsScampering ? Math.Sin(t * 24.0 + 2.0) * 1.5 : 0.0;
            double hindR = IsScampering ? -Math.Sin(t * 24.0 + 2.0) * 1.5 : 0.0;
            dc.DrawEllipse(FoxDarkBrush, FoxOutlinePen, new Point(-5.5, 8.9 + hindL), 1.65, 0.95);
            dc.DrawEllipse(FoxDarkBrush, FoxOutlinePen, new Point(5.5, 8.9 + hindR), 1.65, 0.95);
        }

        // 3. Full Torso + Snow-White Chest Ruff
        var torso = sleeping && !IsScampering && !IsPeeking ? LoafTorsoGeo : TorsoGeo;
        dc.DrawGeometry(FoxCoatBrush, FoxOutlinePen, torso);
        ShadeTorso(dc, torso);
        dc.DrawGeometry(FoxCreamBrush, null, ChestPatchGeo);

        // 4. Front Legs & Dark Vulpine Paws
        DrawFrontLegsAndPaws(dc, mood, t, FoxCoatBrush, FoxDarkBrush, FoxOutlinePen, ToeLinePen, true, IsScampering, IsPeeking);

        // 5. Forest-Emerald Kitsune Kerchief + Gold Leaf Pin
        double scarfY = sleeping ? 1.5 : 0.0;
        Push(dc, new Matrix(1, 0, 0, 1, 0, scarfY));
        dc.DrawGeometry(FoxScarfBrush, GoldBellOutlinePen, DogBandanaGeo);
        dc.DrawEllipse(GoldBellBrush, GoldBellOutlinePen, new Point(0, 1.25), 0.72, 0.72);
        dc.Pop();

        // 6. Articulated Head
        PushHeadTransform(dc);

        double ea = _earAng + _earTwitch;
        DrawEar(dc, FoxEarL, FoxEarInnerL, FoxDarkBrush, FoxOutlinePen, CatEarInnerBrush, FoxPivotL, -(ea + _earFlick));
        DrawEar(dc, FoxEarR, FoxEarInnerR, FoxDarkBrush, FoxOutlinePen, CatEarInnerBrush, FoxPivotR, ea);

        dc.DrawGeometry(FoxCoatBrush, FoxOutlinePen, FoxHeadGeo);
        ShadeHead(dc, FoxHeadGeo, CatHeadRect);
        dc.DrawGeometry(FoxCreamBrush, null, FoxMaskGeo);

        if (mood == PetMood.Happy || mood == PetMood.Eating) DrawBlush(dc, 1.1);

        // Whiskers
        double wf = _isNearCursor ? 11.0 : 6.0;
        double w1 = Math.Sin(t * wf) * 0.45;
        dc.DrawLine(WhiskerPen, new Point(-6.5, 2.2), new Point(-11.4, 1.5 + w1));
        dc.DrawLine(WhiskerPen, new Point(6.5, 2.2), new Point(11.4, 1.5 - w1));

        // Dark vulpine nose + mouth
        dc.DrawEllipse(DarkNoseBrush, null, new Point(0, 1.3), 1.35, 0.95);
        dc.DrawEllipse(WhiteSpecularBrush, null, new Point(-0.35, 1.05), 0.38, 0.25);
        if (mood == PetMood.Happy || mood == PetMood.Dancing || mood == PetMood.Eating)
        {
            dc.DrawEllipse(DeepPupilBrush, null, new Point(0, 3.6), 1.3, 1.05);
            dc.DrawEllipse(DogTongueBrush, null, new Point(0, 4.1), 0.85, 0.65);
        }
        else
        {
            dc.DrawLine(MouthPen, new Point(0.0, 2.2), new Point(-1.3, 3.3));
            dc.DrawLine(MouthPen, new Point(0.0, 2.2), new Point(1.3, 3.3));
        }

        DrawEyes(dc, mood, t, FoxIrisBrush, 0);
        DrawAccessory(dc, t);

        dc.Pop(); // head
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 🐼 Panda (Bamboo Giant Panda – Black Saddle, Eye Patches & Bamboo Charm)
    // ─────────────────────────────────────────────────────────────────────────────
    private void RenderPanda(DrawingContext dc, PetMood mood, double t)
    {
        bool sleeping = mood == PetMood.Sleeping;

        // 1. Round fluffy bobtail at rump
        double tailWiggle = (mood == PetMood.Happy || mood == PetMood.Dancing) ? Math.Sin(t * 16.0) * 0.55 : 0.0;
        dc.DrawEllipse(PandaDarkBrush, PandaDarkOutlinePen, new Point(5.6 + tailWiggle, 7.4), 1.95, 1.95);

        // 2. Chubby Dark Hind Haunches & Paws
        dc.DrawEllipse(PandaDarkBrush, PandaDarkOutlinePen, new Point(-5.0, 6.7), 2.35, 2.55);
        dc.DrawEllipse(PandaDarkBrush, PandaDarkOutlinePen, new Point(5.0, 6.7), 2.35, 2.55);
        if (!sleeping || IsScampering || IsPeeking)
        {
            double hindL = IsScampering ? Math.Sin(t * 24.0 + 2.0) * 1.5 : 0.0;
            double hindR = IsScampering ? -Math.Sin(t * 24.0 + 2.0) * 1.5 : 0.0;
            dc.DrawEllipse(PandaDarkBrush, PandaDarkOutlinePen, new Point(-5.6, 8.95 + hindL), 1.85, 1.05);
            dc.DrawEllipse(PawPadBrush, null, new Point(-5.6, 9.0 + hindL), 0.7, 0.4);
            dc.DrawEllipse(PandaDarkBrush, PandaDarkOutlinePen, new Point(5.6, 8.95 + hindR), 1.85, 1.05);
            dc.DrawEllipse(PawPadBrush, null, new Point(5.6, 9.0 + hindR), 0.7, 0.4);
        }

        // 3. Cream-White Torso + Iconic Black Shoulder Saddle Band
        var torso = sleeping && !IsScampering && !IsPeeking ? LoafTorsoGeo : TorsoGeo;
        dc.DrawGeometry(PandaWhiteBrush, PandaOutlinePen, torso);
        ShadeTorso(dc, torso);
        dc.DrawGeometry(PandaDarkBrush, null, PandaSaddleGeo);

        // 4. Black Forelegs & Paws with Pink Toe Beans
        DrawFrontLegsAndPaws(dc, mood, t, PandaDarkBrush, PandaDarkBrush, PandaDarkOutlinePen, ToeLinePen, true, IsScampering, IsPeeking);

        // 5. Collar + Bamboo Leaf Charm
        double collarY = sleeping ? 1.5 : 0.0;
        Push(dc, new Matrix(1, 0, 0, 1, 0, collarY));
        dc.DrawGeometry(null, CatCollarPen, CatCollarGeo);
        dc.DrawEllipse(SproutLeafBrush, GoldBellOutlinePen, new Point(0, 1.45), 1.05, 0.75);
        dc.Pop();

        // 6. Articulated Head with Round Mickey-style Panda Ears & Eye Patches
        PushHeadTransform(dc);

        double ea = (_earAng + _earTwitch) * 0.65;
        Push(dc, new Matrix(1, 0, 0, 1, 0, -ea * 0.04));
        dc.DrawEllipse(PandaDarkBrush, PandaDarkOutlinePen, new Point(-6.4, -5.2), 2.75, 2.65);
        dc.DrawEllipse(PandaDarkBrush, PandaDarkOutlinePen, new Point(6.4, -5.2), 2.75, 2.65);
        dc.Pop();

        dc.DrawGeometry(PandaWhiteBrush, PandaOutlinePen, DogHeadGeo);
        ShadeHead(dc, DogHeadGeo, DogHeadRect);

        // Tilted black panda eye patches
        dc.DrawGeometry(PandaDarkBrush, null, PandaPatchLGeo);
        dc.DrawGeometry(PandaDarkBrush, null, PandaPatchRGeo);

        if (mood == PetMood.Happy || mood == PetMood.Eating) DrawBlush(dc, 1.15);

        // Nose & mouth
        dc.DrawEllipse(DarkNoseBrush, null, new Point(0, 1.25), 1.45, 1.05);
        dc.DrawEllipse(WhiteSpecularBrush, null, new Point(-0.35, 0.95), 0.4, 0.28);
        if (mood == PetMood.Happy || mood == PetMood.Dancing || mood == PetMood.Eating)
        {
            dc.DrawEllipse(DeepPupilBrush, null, new Point(0, 3.6), 1.4, 1.1);
            dc.DrawEllipse(DogTongueBrush, null, new Point(0, 4.1), 0.95, 0.7);
        }
        else
        {
            dc.DrawLine(MouthPen, new Point(0.0, 2.2), new Point(0.0, 2.9));
            dc.DrawLine(MouthPen, new Point(0.0, 2.9), new Point(-1.2, 3.5));
            dc.DrawLine(MouthPen, new Point(0.0, 2.9), new Point(1.2, 3.5));
        }

        DrawEyes(dc, mood, t, PandaIrisBrush, 1);
        DrawAccessory(dc, t);

        dc.Pop(); // head
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 🐲 Dragon (Celestial Baby Dragon – Flapping Wings, Horns & Spade Tail)
    // ─────────────────────────────────────────────────────────────────────────────
    private void RenderDragon(DrawingContext dc, PetMood mood, double t)
    {
        bool sleeping = mood == PetMood.Sleeping;

        // 1. Articulated Dorsal Wings (flap faster when happy, dancing, or scampering)
        double flapFreq = (mood == PetMood.Happy || mood == PetMood.Dancing || IsScampering) ? 18.0 : (sleeping ? 2.1 : 5.5);
        double flapAmp = (mood == PetMood.Happy || mood == PetMood.Dancing || IsScampering) ? 22.0 : (sleeping ? 4.0 : 9.0);
        double flapAng = Math.Sin(t * flapFreq) * flapAmp;

        var wl = Matrix.Identity;
        wl.RotateAt(-flapAng, DragonWingPivotL.X, DragonWingPivotL.Y);
        Push(dc, wl);
        dc.DrawGeometry(DragonWingBrush, DragonOutlinePen, DragonWingLGeo);
        dc.Pop();

        var wr = Matrix.Identity;
        wr.RotateAt(flapAng, DragonWingPivotR.X, DragonWingPivotR.Y);
        Push(dc, wr);
        dc.DrawGeometry(DragonWingBrush, DragonOutlinePen, DragonWingRGeo);
        dc.Pop();

        // 2. Whipping Dragon Tail with Spade Tip
        DrawCatTail(dc, mood, t);
        var tip = _tail[CatTailSegs];
        Push(dc, new Matrix(1, 0, 0, 1, tip.X, tip.Y));
        dc.DrawGeometry(DragonWingBrush, DragonOutlinePen, DragonTailSpadeGeo);
        dc.Pop();

        // 3. Seated Hind Haunches & Golden-Clawed Feet
        dc.DrawEllipse(DragonCoatBrush, DragonOutlinePen, new Point(-4.9, 6.7), 2.2, 2.5);
        dc.DrawEllipse(DragonCoatBrush, DragonOutlinePen, new Point(4.9, 6.7), 2.2, 2.5);
        if (!sleeping || IsScampering || IsPeeking)
        {
            double hindL = IsScampering ? Math.Sin(t * 24.0 + 2.0) * 1.5 : 0.0;
            double hindR = IsScampering ? -Math.Sin(t * 24.0 + 2.0) * 1.5 : 0.0;
            dc.DrawEllipse(DragonBellyBrush, DragonOutlinePen, new Point(-5.5, 8.9 + hindL), 1.65, 0.95);
            dc.DrawEllipse(DragonBellyBrush, DragonOutlinePen, new Point(5.5, 8.9 + hindR), 1.65, 0.95);
        }

        // 4. Scaled Torso + Ridged Golden Belly Plates
        var torso = sleeping && !IsScampering && !IsPeeking ? LoafTorsoGeo : TorsoGeo;
        dc.DrawGeometry(DragonCoatBrush, DragonOutlinePen, torso);
        ShadeTorso(dc, torso);
        dc.DrawGeometry(DragonBellyBrush, null, ChestPatchGeo);
        dc.DrawLine(DragonBellyLinePen, new Point(-2.4, 3.2), new Point(2.4, 3.2));
        dc.DrawLine(DragonBellyLinePen, new Point(-2.8, 5.2), new Point(2.8, 5.2));
        dc.DrawLine(DragonBellyLinePen, new Point(-2.4, 7.2), new Point(2.4, 7.2));

        // 5. Front Legs & Claws
        DrawFrontLegsAndPaws(dc, mood, t, DragonCoatBrush, DragonBellyBrush, DragonOutlinePen, ToeLinePen, false, IsScampering, IsPeeking);

        // 6. Articulated Dragon Head with Curved Ivory Horns & Ear Frills
        PushHeadTransform(dc);

        double ea = _earAng + _earTwitch;
        DrawEar(dc, DragonFrillLGeo, DragonFrillLGeo, DragonWingBrush, DragonOutlinePen, DragonBellyBrush, CatPivotL, -ea * 0.7);
        DrawEar(dc, DragonFrillRGeo, DragonFrillRGeo, DragonWingBrush, DragonOutlinePen, DragonBellyBrush, CatPivotR, ea * 0.7);

        dc.DrawGeometry(DragonHornBrush, GoldBellOutlinePen, DragonHornLGeo);
        dc.DrawGeometry(DragonHornBrush, GoldBellOutlinePen, DragonHornRGeo);

        dc.DrawGeometry(DragonCoatBrush, DragonOutlinePen, DogHeadGeo);
        ShadeHead(dc, DogHeadGeo, DogHeadRect);
        dc.DrawEllipse(DragonBellyBrush, null, new Point(0, 4.1), 4.2, 2.6);

        if (mood == PetMood.Happy || mood == PetMood.Eating) DrawBlush(dc, 1.05);

        // Nostril puffs & cute fangs
        dc.DrawEllipse(DeepPupilBrush, null, new Point(-1.3, 1.3), 0.55, 0.42);
        dc.DrawEllipse(DeepPupilBrush, null, new Point(1.3, 1.3), 0.55, 0.42);
        if (mood == PetMood.Happy || mood == PetMood.Dancing || mood == PetMood.Eating)
        {
            dc.DrawEllipse(DeepPupilBrush, null, new Point(0, 3.6), 1.4, 1.05);
            dc.DrawEllipse(DogTongueBrush, null, new Point(0, 4.05), 0.9, 0.6);
        }
        else
        {
            dc.DrawLine(MouthPen, new Point(-1.8, 3.2), new Point(0.0, 2.7));
            dc.DrawLine(MouthPen, new Point(0.0, 2.7), new Point(1.8, 3.2));
        }

        DrawEyes(dc, mood, t, DragonIrisBrush, 0);
        DrawAccessory(dc, t);

        dc.Pop(); // head
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Wearable Head Accessories (Rendered in Head Space -> Zero Allocations)
    // ─────────────────────────────────────────────────────────────────────────────
    private void DrawAccessory(DrawingContext dc, double t)
    {
        string acc = Accessory ?? "None";
        switch (acc)
        {
            case "Crown":
            {
                dc.DrawGeometry(CrownGoldBrush, GoldBellOutlinePen, CrownGeo);
                dc.DrawEllipse(HeartBrushes[15], null, new Point(0.0, -6.4), 0.65, 0.65);
                dc.DrawEllipse(WaterDropBrush, null, new Point(-2.2, -6.2), 0.48, 0.48);
                dc.DrawEllipse(WaterDropBrush, null, new Point(2.2, -6.2), 0.48, 0.48);
                break;
            }
            case "WizardHat":
            {
                dc.DrawGeometry(WizardHatBrush, DragonOutlinePen, WizardHatGeo);
                dc.DrawLine(HaloGoldPen, new Point(-4.0, -5.3), new Point(4.0, -5.3));
                dc.DrawEllipse(CrownGoldBrush, null, new Point(0.5, -8.6), 0.7, 0.7);
                break;
            }
            case "Headphones":
            {
                double pulse = 1.0 + _bass * 0.18;
                dc.DrawGeometry(null, HeadphoneBandPen, HeadphoneBandGeo);
                dc.DrawEllipse(HeadphoneCupBrush, DragonOutlinePen, new Point(-8.6, 0.4), 1.65 * pulse, 2.35 * pulse);
                dc.DrawEllipse(HeadphoneCupBrush, DragonOutlinePen, new Point(8.6, 0.4), 1.65 * pulse, 2.35 * pulse);
                dc.DrawEllipse(WhiteSpecularBrush, null, new Point(-8.9, -0.3), 0.45, 0.75);
                dc.DrawEllipse(WhiteSpecularBrush, null, new Point(8.3, -0.3), 0.45, 0.75);
                break;
            }
            case "Sunglasses":
            {
                dc.DrawGeometry(SunglassesLensBrush, SunglassesFramePen, SunglassesLGeo);
                dc.DrawGeometry(SunglassesLensBrush, SunglassesFramePen, SunglassesRGeo);
                dc.DrawLine(SunglassesFramePen, new Point(-1.2, -1.8), new Point(1.2, -1.8));
                dc.DrawLine(SunglassesFramePen, new Point(-6.5, -2.2), new Point(-8.4, -1.5));
                dc.DrawLine(SunglassesFramePen, new Point(6.5, -2.2), new Point(8.4, -1.5));
                dc.DrawLine(WhiskerPen, new Point(-5.2, -1.8), new Point(-3.8, -0.2));
                dc.DrawLine(WhiskerPen, new Point(2.4, -1.8), new Point(3.8, -0.2));
                break;
            }
            case "Halo":
            {
                double bob = Math.Sin(t * 3.8) * 0.55;
                dc.DrawEllipse(null, HaloGlowPen, new Point(0, -8.8 + bob), 4.6, 1.35);
                dc.DrawEllipse(null, HaloGoldPen, new Point(0, -8.8 + bob), 4.6, 1.35);
                break;
            }
            case "Sprout":
            {
                double sway = Math.Sin(t * 4.5) * 8.0 - _headShiftX * 6.0;
                var sm = Matrix.Identity;
                sm.RotateAt(sway, 0, -5.2);
                Push(dc, sm);
                dc.DrawLine(SproutStemPen, new Point(0, -5.2), new Point(0, -8.6));
                dc.DrawGeometry(SproutLeafBrush, null, SproutLeafLGeo);
                dc.DrawGeometry(SproutLeafBrush, null, SproutLeafRGeo);
                dc.Pop();
                break;
            }
        }
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Eyes (kind: 0 cat, 1 dog, 2 bunny) \u2014 rendered in Head Space
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    private static double BlinkAmount(double p)
    {
        if (p < 0.0 || p >= 0.18) return 0.0;
        if (p < 0.07) return p / 0.07;
        if (p < 0.11) return 1.0;
        return 1.0 - (p - 0.11) / 0.07;
    }

    private void DrawEyes(DrawingContext dc, PetMood mood, double t, Brush iris, int kind)
    {
        if (mood == PetMood.Sleeping)
        {
            dc.DrawGeometry(null, ClosedEyePen, SleepArcL);
            dc.DrawGeometry(null, ClosedEyePen, SleepArcR);
            return;
        }
        if (mood == PetMood.Happy || mood == PetMood.Eating)
        {
            dc.DrawGeometry(null, ClosedEyePen, HappyArcL);
            dc.DrawGeometry(null, ClosedEyePen, HappyArcR);
            return;
        }

        double ph = t % 3.9;
        double blink = BlinkAmount(ph);
        if (((int)(t / 3.9)) % 3 == 0) blink = Math.Max(blink, BlinkAmount(ph - 0.26));

        if (blink >= 0.75)
        {
            dc.DrawLine(ClosedEyePen, new Point(-5.4, -1.2), new Point(-1.8, -1.2));
            dc.DrawLine(ClosedEyePen, new Point(1.8, -1.2), new Point(5.4, -1.2));
            return;
        }

        double gx = Math.Clamp(_gazeX + (_isNearCursor ? 0.0 : Math.Sin(t * 0.9) * 0.25 + Math.Sin(t * 2.3 + 1.1) * 0.15), -1.0, 1.0);
        double gy = Math.Clamp(_gazeY + (_isNearCursor ? 0.0 : Math.Sin(t * 1.3 + 2.0) * 0.25), -1.0, 1.0);

        double lid = 1.0 - blink * 0.85;
        double baseH = mood == PetMood.Thirsty ? 2.05 : (_isNearCursor ? 2.6 : 2.35);
        double eyeH = baseH * lid;
        const double eyeW = 1.95;

        double px = gx * 0.65;
        double py = gy * 0.45 + (mood == PetMood.Thirsty ? 0.3 : 0.0);

        double pupilW = kind == 0 ? 0.46 + 0.85 * _dilate : 1.02 + 0.32 * _dilate;
        double pupilH = kind == 0 ? eyeH * 0.95 : eyeH * (0.64 + 0.12 * _dilate);
        double glintScale = Math.Clamp(1.0 - blink * 1.5, 0.0, 1.0);

        for (int side = -1; side <= 1; side += 2)
        {
            var e = new Point(side * 3.6, -1.1);

            dc.DrawEllipse(DeepPupilBrush, null, e, eyeW + 0.18, eyeH + 0.18);
            dc.DrawEllipse(iris, null, e, eyeW, eyeH);

            var p = new Point(e.X + px, e.Y + py * (kind == 0 ? 0.5 : 1.0));
            dc.DrawEllipse(DeepPupilBrush, null, p, pupilW, pupilH);

            dc.DrawEllipse(EyeRimBrush, null, new Point(e.X + px * 0.2, e.Y + eyeH * 0.62), eyeW * 0.52, 0.3);

            if (glintScale > 0.05)
            {
                double gcx = e.X + px * 0.35, gcy = e.Y + py * 0.35;
                dc.DrawEllipse(WhiteSpecularBrush, null, new Point(gcx - 0.55, gcy - 0.65 * lid), 0.68 * glintScale, 0.68 * glintScale);
                dc.DrawEllipse(SoftSecondarySpecularBrush, null, new Point(gcx + 0.55, gcy + 0.55 * lid), 0.38 * glintScale, 0.38 * glintScale);
            }
        }
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Particles (table-driven, zero per-frame brushes / geometries)
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    private void RenderParticles(DrawingContext dc, PetMood mood, double t, double cx, double cy,
        bool isScampering = false, bool isPeeking = false)
    {
        if (isScampering)
        {
            double sc = (t * 16.0) % 8.0;
            double alpha = 1.0 - sc / 8.0;
            DrawStar(dc, cx - 11.0 - sc * 1.2, cy + 7.5 - sc * 0.4, 1.4, alpha * 0.85, t * 180.0);
            // Red laser pointer dot darting ahead of the pet during scamper chase
            double laserJitterY = Math.Sin(t * 32.0) * 1.4;
            dc.DrawEllipse(LaserDotBrush, null, new Point(cx + 12.5, cy + 8.5 + laserJitterY), 2.8, 1.8);
            return;
        }
        if (isPeeking)
        {
            double sc = (t * 5.0) % 7.0;
            double alpha = 1.0 - sc / 7.0;
            DrawStar(dc, cx + 5.5, cy - 8.5 - sc * 0.6, 1.6, alpha * 0.9, t * 90.0);
            return;
        }

        switch (mood)
        {
            case PetMood.Sleeping:
            {
                double c1 = (t * 6.5) % 20.0;
                double c2 = ((t * 6.5) + 10.0) % 20.0;
                double a1 = 1.0 - c1 / 20.0;
                double a2 = 1.0 - c2 / 20.0;

                if (c1 < 8.0)
                {
                    double r = (c1 / 8.0) * 2.6;
                    var bp = new Point(cx + 3.8 + Math.Sin(t * 3.0) * 1.2, cy - 3.5 - c1 * 0.7);
                    dc.DrawEllipse(SnoreBubbleBrush, SnoreBubblePen, bp, r, r);
                    dc.DrawEllipse(WhiteSpecularBrush, null, new Point(bp.X - r * 0.35, bp.Y - r * 0.35), r * 0.2, r * 0.2);
                }
                DrawZ(dc, cx + 5.5 + Math.Sin(t * 2.2) * 2.2, cy - 4.0 - c1 * 0.85, 5.5, a1);
                DrawZ(dc, cx + 7.5 + Math.Cos(t * 2.6) * 2.2, cy - 4.0 - c2 * 0.85, 4.2, a2);
                break;
            }
            case PetMood.Happy:
            case PetMood.Eating:
            {
                for (int i = 0; i < 3; i++)
                {
                    double oc = (t * 11.0 + i * 6.5) % 18.0;
                    double alpha = 1.0 - oc / 18.0;
                    double spreadX = ((i - 1) * 6.0) + Math.Sin(t * 3.0 + i) * 3.0;
                    double hy = cy - 5.0 - oc * 0.85;
                    double hs = (1.0 - oc / 24.0) * 3.2;
                    DrawHeart(dc, cx + spreadX, hy, hs, alpha);

                    double sc = (t * 8.0 + i * 4.0) % 14.0;
                    DrawStar(dc, cx - spreadX * 0.8, cy - 3.0 - sc * 0.85, 1.7, 1.0 - sc / 14.0, t * 180.0 + i * 40.0);
                }
                break;
            }
            case PetMood.Dancing:
            {
                double n1 = (t * 8.5) % 18.0;
                double n2 = ((t * 8.5) + 9.0) % 18.0;
                DrawNote(dc, cx + 6.5 + Math.Sin(t * 3.5) * 2.8, cy - 4.5 - n1 * 0.85, 1.0 - n1 / 18.0, false);
                DrawNote(dc, cx - 6.5 + Math.Cos(t * 3.5) * 2.8, cy - 4.5 - n2 * 0.85, 1.0 - n2 / 18.0, true);
                break;
            }
            case PetMood.Thirsty:
                DrawWaterDrop(dc, cx + 7.8, cy - 4.5 + Math.Sin(t * 5.0) * 1.5);
                break;
            case PetMood.Stretch:
            {
                double ac = (t * 4.0) % 12.0;
                double alpha = 1.0 - ac / 12.0;
                if (alpha > 0.05)
                    dc.DrawEllipse(null, AuraPens[Lvl(alpha)], new Point(cx, cy), 12.0 + ac, 10.5 + ac * 0.8);
                break;
            }
        }
    }

    private static void DrawZ(DrawingContext dc, double x, double y, double size, double opacity)
    {
        int l = Lvl(opacity);
        if (l == 0) return;
        var pen = ZPens[l];
        double half = size * 0.42;
        dc.DrawLine(pen, new Point(x - half, y - half), new Point(x + half, y - half));
        dc.DrawLine(pen, new Point(x + half, y - half), new Point(x - half, y + half));
        dc.DrawLine(pen, new Point(x - half, y + half), new Point(x + half, y + half));
    }

    private void DrawHeart(DrawingContext dc, double x, double y, double size, double opacity)
    {
        int l = Lvl(opacity);
        if (l == 0 || size <= 0.1) return;
        Push(dc, new Matrix(size, 0, 0, size, x, y));
        dc.DrawGeometry(HeartBrushes[l], null, HeartGeo);
        dc.Pop();
    }

    private void DrawStar(DrawingContext dc, double x, double y, double size, double opacity, double rotDeg)
    {
        int l = Lvl(opacity);
        if (l == 0 || size <= 0.1) return;
        var m = Matrix.Identity;
        m.Rotate(rotDeg);
        m.Scale(size, size);
        m.Translate(x, y);
        Push(dc, m);
        dc.DrawGeometry(StarBrushes[l], null, StarGeo);
        dc.Pop();
    }

    private static void DrawNote(DrawingContext dc, double x, double y, double opacity, bool pair)
    {
        int l = Lvl(opacity);
        if (l == 0) return;
        var brush = NoteBrushes[l];
        var pen = NotePens[l];
        if (!pair)
        {
            dc.DrawEllipse(brush, null, new Point(x, y), 1.6, 1.25);
            dc.DrawLine(pen, new Point(x + 1.2, y), new Point(x + 1.2, y - 5.2));
            dc.DrawLine(pen, new Point(x + 1.2, y - 5.2), new Point(x + 3.4, y - 4.0));
        }
        else
        {
            dc.DrawEllipse(brush, null, new Point(x - 2.5, y), 1.4, 1.1);
            dc.DrawEllipse(brush, null, new Point(x + 2.5, y - 1.0), 1.4, 1.1);
            dc.DrawLine(pen, new Point(x - 1.3, y), new Point(x - 1.3, y - 5.5));
            dc.DrawLine(pen, new Point(x + 3.7, y - 1.0), new Point(x + 3.7, y - 6.5));
            dc.DrawLine(pen, new Point(x - 1.3, y - 5.5), new Point(x + 3.7, y - 6.5));
        }
    }

    private void DrawWaterDrop(DrawingContext dc, double x, double y)
    {
        Push(dc, new Matrix(1, 0, 0, 1, x, y));
        dc.DrawGeometry(WaterDropBrush, null, DropGeo);
        dc.DrawEllipse(WhiteSpecularBrush, null, new Point(-0.7, -0.5), 0.65, 0.95);
        dc.Pop();
        dc.DrawEllipse(null, RipplePen, new Point(x, y + 4.8), 3.2, 1.2);
    }

    private void DrawSakuraPetal(DrawingContext dc, double x, double y, double rotDeg)
    {
        var m = Matrix.Identity;
        m.Rotate(rotDeg);
        m.Translate(x, y);
        Push(dc, m);
        dc.DrawGeometry(SakuraPetalBrush, null, SakuraPetalGeo);
        dc.Pop();
    }

    private void DrawBambooLeaf(DrawingContext dc, double x, double y, double rotDeg)
    {
        var m = Matrix.Identity;
        m.Rotate(rotDeg);
        m.Translate(x, y);
        Push(dc, m);
        dc.DrawGeometry(BambooLeafBrush, null, BambooLeafGeo);
        dc.Pop();
    }

    private void RenderSpecialAction(DrawingContext dc, double t, double cx, double cy)
    {
        double elapsed = t - _specialActionStartTime;
        const double ActionDuration = 1.8;
        if (elapsed < 0.0 || elapsed > ActionDuration) return;
        double p = Math.Clamp(elapsed / ActionDuration, 0.0, 1.0);

        switch (_specialActionKind)
        {
            case PetSpecialActionKind.LaserHunt:
            {
                // Scurrying red laser pointer dot with glowing outer corona & specular core
                double lx = cx + Math.Sin(p * 18.0) * 9.5;
                double ly = 23.5 + Math.Cos(p * 14.0) * 2.2;
                dc.DrawEllipse(LaserDotBrush, null, new Point(lx, ly), 2.5, 2.0);
                dc.DrawEllipse(WhiteSpecularBrush, null, new Point(lx, ly), 0.8, 0.65);
                if (p > 0.45)
                {
                    double sparkAlpha = Math.Clamp(1.0 - (p - 0.45) / 0.4, 0.0, 1.0);
                    DrawStar(dc, lx + 2.0, ly - 2.0, 0.8, sparkAlpha, p * 180.0);
                }
                break;
            }
            case PetSpecialActionKind.BallFetch:
            {
                // Bouncing tennis ball with dynamic parabolic bounce trajectory & felt seams
                double bx = cx - 13.0 + p * 26.0;
                double by = p < 0.5 ? 23.0 - Math.Sin(p / 0.5 * Math.PI) * 14.0 : (p < 0.82 ? 18.0 + (p - 0.5) * 8.0 : 24.2);
                dc.DrawEllipse(TennisBallBrush, GoldBellOutlinePen, new Point(bx, by), 2.3, 2.3);
                dc.DrawLine(TennisBallSeamPen, new Point(bx - 1.3, by - 1.1), new Point(bx + 1.3, by + 1.1));
                dc.DrawLine(TennisBallSeamPen, new Point(bx - 1.3, by + 1.1), new Point(bx + 1.3, by - 1.1));
                break;
            }
            case PetSpecialActionKind.BinkyFlip:
            {
                // Swirl of pastel sakura blossom petals & hearts celebrating mid-air 180° twist
                for (int i = 0; i < 4; i++)
                {
                    double ang = (i * Math.PI * 0.5) + t * 4.2;
                    double dist = 7.0 + p * 11.0;
                    DrawSakuraPetal(dc, cx + Math.Cos(ang) * dist, cy - 2.0 + Math.Sin(ang) * (dist * 0.6), p * 240.0 + i * 45.0);
                }
                DrawHeart(dc, cx + 6.0, cy - 7.5, 1.2, 1.0 - p);
                break;
            }
            case PetSpecialActionKind.FoxfireDance:
            {
                // 3 ethereal Kitsune spirit flames in 3D orbit around the fox
                for (int i = 0; i < 3; i++)
                {
                    double ang = t * 9.5 + (i * Math.PI * 2.0 / 3.0);
                    double rx = 13.5 * (1.0 - p * 0.35);
                    double ry = 6.0 * (1.0 - p * 0.35);
                    double ox = cx + Math.Cos(ang) * rx;
                    double oy = cy + Math.Sin(ang) * ry;
                    dc.DrawEllipse(FoxfireBrush, null, new Point(ox, oy), 2.8, 2.8);
                    dc.DrawEllipse(WhiteSpecularBrush, null, new Point(ox, oy), 0.8, 0.8);
                }
                break;
            }
            case PetSpecialActionKind.KungFuRoll:
            {
                // Tumbling green bamboo leaves fluttering around the somersaulting panda
                for (int i = 0; i < 4; i++)
                {
                    double lx = cx - 11.0 + (i * 7.5) + Math.Sin(t * 7.0 + i) * 2.5;
                    double ly = 23.5 - Math.Sin((p + i * 0.22) * Math.PI) * 9.5;
                    DrawBambooLeaf(dc, lx, ly, p * 360.0 + i * 40.0);
                }
                DrawStar(dc, cx + 7.0, cy - 5.5, 0.95, 1.0 - p, p * 120.0);
                break;
            }
            case PetSpecialActionKind.DragonBreath:
            {
                // Inhale sparkle then celestial heart stardust flame burst
                if (p < 0.32)
                {
                    DrawStar(dc, cx - 6.0, cy - 4.0, 0.7, p / 0.32, -p * 180.0);
                }
                else
                {
                    double fp = (p - 0.32) / 0.68;
                    double fx = cx + 5.0 + fp * 15.0;
                    double fy = cy - 2.8 + Math.Sin(fp * 8.5) * 1.6;
                    double fSize = 2.0 + fp * 3.6;
                    dc.DrawEllipse(DragonFlameBrush, null, new Point(fx, fy), fSize, fSize * 0.72);
                    DrawStar(dc, fx + 1.4, fy - 0.8, 0.85, 1.0 - fp, fp * 220.0);
                    DrawStar(dc, fx - 1.0, fy + 1.5, 0.65, 1.0 - fp, -fp * 180.0);
                }
                break;
            }
        }
    }
}
