using System.Security.Cryptography;
using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;

internal static class Fo2ProceduralPortrait
{
    internal const string GeneratorId = "opennv-classic-green-appearance/v4";
    internal const string PortraitState = "generated-local-classic-green-appearance";
    internal const string RoundFace = "round";
    internal const string OvalFace = "oval";
    internal const string AngularFace = "angular";
    internal const string CroppedHair = "cropped";
    internal const string SweptHair = "swept";
    internal const string LongHair = "long";
    internal const string LightSkin = "light";
    internal const string MediumSkin = "medium";
    internal const string DeepSkin = "deep";
    internal const string BlackHairColor = "black";
    internal const string BrownHairColor = "brown";
    internal const string AuburnHairColor = "auburn";
    internal const string HazelEyeColor = "hazel";
    internal const string BlueEyeColor = "blue";
    internal const string GreenEyeColor = "green";
    internal const string StraightBrow = "straight";
    internal const string ArchedBrow = "arched";
    internal const string HeavyBrow = "heavy";
    internal const string NarrowNose = "narrow";
    internal const string StandardNose = "standard";
    internal const string BroadNose = "broad";
    internal const string SmallMouth = "small";
    internal const string NeutralMouth = "neutral";
    internal const string WideMouth = "wide";
    internal const int Width = 128;
    internal const int Height = 128;
    private const int HashLength = 64;
    private const int CenterX = 64;
    private const int CenterY = 61;
    private const int EyeY = 57;
    private const int LeftEyeX = 51;
    private const int RightEyeX = 77;
    private const int NeckX = 51;
    private const int NeckY = 98;
    private const int NeckWidth = 26;
    private const int NeckHeight = 21;
    private const int EyeWidth = 7;
    private const int EyeHeight = 3;
    private const int OutlineInset = 2;
    private const float FaceBoundary = 1.0f;
    private const float ShadingBias = 0.28f;
    private static Fo2ProceduralAppearanceCatalog Catalog =>
        Fo2ProceduralAppearanceCatalog.Load();

    internal static IReadOnlyList<string> Shapes => Catalog.FaceShapeIds;
    internal static IReadOnlyList<string> HairStyles => Catalog.HairStyleIds;
    internal static IReadOnlyList<string> SkinTones => Catalog.SkinToneIds;
    internal static IReadOnlyList<string> HairColors => Catalog.HairColorIds;
    internal static IReadOnlyList<string> EyeColors => Catalog.EyeColorIds;
    internal static IReadOnlyList<string> BrowStyles => Catalog.BrowStyleIds;
    internal static IReadOnlyList<string> NoseStyles => Catalog.NoseStyleIds;
    internal static IReadOnlyList<string> MouthStyles => Catalog.MouthStyleIds;

    internal static int ShapeIndex(string faceShapeId) =>
        Shapes.ToList().IndexOf(faceShapeId);
    internal static int HairStyleIndex(string hairStyleId) =>
        HairStyles.ToList().IndexOf(hairStyleId);
    internal static int SkinToneIndex(string skinToneId) =>
        SkinTones.ToList().IndexOf(skinToneId);
    internal static int HairColorIndex(string hairColorId) =>
        HairColors.ToList().IndexOf(hairColorId);
    internal static int EyeColorIndex(string eyeColorId) =>
        EyeColors.ToList().IndexOf(eyeColorId);
    internal static int BrowStyleIndex(string browStyleId) =>
        BrowStyles.ToList().IndexOf(browStyleId);
    internal static int NoseStyleIndex(string noseStyleId) =>
        NoseStyles.ToList().IndexOf(noseStyleId);
    internal static int MouthStyleIndex(string mouthStyleId) =>
        MouthStyles.ToList().IndexOf(mouthStyleId);

    internal static Image Render(
        string sex,
        string faceShapeId,
        string hairStyleId,
        string skinToneId,
        string hairColorId,
        string eyeColorId,
        string browStyleId,
        string noseStyleId,
        string mouthStyleId)
    {
        ValidateIdentity(
            sex,
            faceShapeId,
            hairStyleId,
            skinToneId,
            hairColorId,
            eyeColorId,
            browStyleId,
            noseStyleId,
            mouthStyleId);
        var face = Catalog.Face(faceShapeId);
        var hair = Catalog.HairStyle(hairStyleId);
        var skin = Catalog.SkinTone(skinToneId);
        var hairColor = Catalog.HairColor(hairColorId);
        var eyeColor = Catalog.EyeColor(eyeColorId);
        var brow = Catalog.BrowStyle(browStyleId);
        var nose = Catalog.NoseStyle(noseStyleId);
        var mouth = Catalog.MouthStyle(mouthStyleId);
        var image = Image.CreateEmpty(Width, Height, false, Image.Format.Rgba8);
        image.Fill(Catalog.Background);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                if (!InsideFace(x, y, face, out var normalizedDistance))
                    continue;
                image.SetPixel(
                    x,
                    y,
                    normalizedDistance >= FaceBoundary -
                        (float)OutlineInset / face.HalfWidth
                        ? Catalog.Outline
                        : x < CenterX - face.HalfWidth * ShadingBias
                            ? skin.PortraitShadow
                            : skin.PortraitHighlight);
            }
        }
        image.FillRect(
            new Rect2I(NeckX, NeckY, NeckWidth, NeckHeight),
            skin.PortraitShadow);
        image.FillRect(
            new Rect2I(LeftEyeX, EyeY, EyeWidth, EyeHeight),
            eyeColor.PortraitColor);
        image.FillRect(
            new Rect2I(RightEyeX, EyeY, EyeWidth, EyeHeight),
            eyeColor.PortraitColor);
        DrawBrow(image, Catalog.PortraitBrowLeftX, brow);
        DrawBrow(image, Catalog.PortraitBrowRightX, brow);
        image.FillRect(
            new Rect2I(
                CenterX - nose.PortraitWidth / 2,
                Catalog.PortraitNoseY,
                nose.PortraitWidth,
                nose.PortraitHeight),
            skin.PortraitShadow);
        image.FillRect(
            new Rect2I(
                CenterX - mouth.PortraitWidth / 2,
                Catalog.PortraitMouthY,
                mouth.PortraitWidth,
                mouth.PortraitThickness),
            Catalog.Feature);
        DrawHair(image, face, hair, hairColor);
        return image;
    }

    internal static Fo2CharacterAppearanceContract Commit(
        Fo2PremadeCharacter source,
        string sex,
        string faceShapeId,
        string hairStyleId,
        string skinToneId,
        string hairColorId,
        string eyeColorId,
        string browStyleId,
        string noseStyleId,
        string mouthStyleId)
    {
        var image = Render(
            sex,
            faceShapeId,
            hairStyleId,
            skinToneId,
            hairColorId,
            eyeColorId,
            browStyleId,
            noseStyleId,
            mouthStyleId);
        var root = DefaultRoot();
        Directory.CreateDirectory(root);
        var temporary = System.IO.Path.Combine(root, $".{Guid.NewGuid():N}.png");
        try
        {
            if (image.SavePng(temporary) != Error.Ok)
                throw new InvalidOperationException(
                    "Could not write the Fallout 2 local generated portrait.");
            var sha256 = FileSha256(temporary);
            var destination = System.IO.Path.Combine(root, $"{sha256}.png");
            if (File.Exists(destination))
            {
                if (FileSha256(destination) != sha256)
                    throw new InvalidOperationException(
                        "Fallout 2 generated portrait identity collision detected.");
                File.Delete(temporary);
            }
            else
            {
                File.Move(temporary, destination);
            }
            var contract = new Fo2CharacterAppearanceContract(
                Fo2CharacterAppearanceContract.ExpectedSchema,
                source.Id,
                source.Panel.LogicalPath,
                source.Panel.SourceSha256,
                source.Panel.PngSha256,
                Fo2CharacterAppearanceContract.GeneratedPortraitPreview,
                PortraitState,
                true,
                true,
                faceShapeId,
                hairStyleId,
                skinToneId,
                hairColorId,
                eyeColorId,
                browStyleId,
                noseStyleId,
                mouthStyleId,
                GeneratorId,
                Fo2ProceduralAppearanceCatalog.ExpectedId,
                Catalog.Sha256,
                destination,
                sha256,
                Width,
                Height);
            Validate(contract);
            return contract;
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    internal static void Validate(Fo2CharacterAppearanceContract contract)
    {
        if (contract.PreviewMode != Fo2CharacterAppearanceContract.GeneratedPortraitPreview ||
            contract.PortraitState != PortraitState ||
            !contract.CustomFaceEdited || !contract.CustomPortraitGenerated ||
            !Shapes.Contains(contract.FaceShapeId, StringComparer.Ordinal) ||
            !HairStyles.Contains(contract.HairStyleId, StringComparer.Ordinal) ||
            !SkinTones.Contains(contract.SkinToneId, StringComparer.Ordinal) ||
            !HairColors.Contains(contract.HairColorId, StringComparer.Ordinal) ||
            !EyeColors.Contains(contract.EyeColorId, StringComparer.Ordinal) ||
            !BrowStyles.Contains(contract.BrowStyleId, StringComparer.Ordinal) ||
            !NoseStyles.Contains(contract.NoseStyleId, StringComparer.Ordinal) ||
            !MouthStyles.Contains(contract.MouthStyleId, StringComparer.Ordinal) ||
            contract.PortraitGeneratorId != GeneratorId ||
            contract.AppearanceRecipeId != Fo2ProceduralAppearanceCatalog.ExpectedId ||
            contract.AppearanceRecipeSha256 != Catalog.Sha256 ||
            contract.GeneratedPortraitWidth != Width ||
            contract.GeneratedPortraitHeight != Height ||
            contract.GeneratedPortraitSha256.Length != HashLength ||
            contract.GeneratedPortraitSha256.Any(character =>
                !Uri.IsHexDigit(character) || char.IsUpper(character)))
            throw new InvalidOperationException(
                "Fallout 2 generated portrait contract is invalid.");
        var path = System.IO.Path.GetFullPath(contract.GeneratedPortraitPath);
        var root = DefaultRoot();
        if (!path.StartsWith(
                root + System.IO.Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(path) || FileSha256(path) != contract.GeneratedPortraitSha256)
            throw new InvalidOperationException(
                "Fallout 2 generated portrait is outside or differs from local user data.");
        var image = Image.LoadFromFile(path);
        if (image is null || image.IsEmpty() ||
            image.GetWidth() != Width || image.GetHeight() != Height)
            throw new InvalidOperationException(
                "Fallout 2 generated portrait dimensions drifted.");
    }

    private static bool InsideFace(
        int x,
        int y,
        Fo2FaceShapePreset face,
        out float normalizedDistance)
    {
        var dx = x - CenterX;
        var dy = y - CenterY;
        if (face.Taper > 0.0f)
        {
            var normalizedY = MathF.Abs(dy) / face.HalfHeight;
            var angularWidth = face.HalfWidth * (FaceBoundary - face.Taper * normalizedY);
            normalizedDistance = MathF.Max(
                MathF.Abs(dx) / MathF.Max(angularWidth, FaceBoundary),
                normalizedY);
            return normalizedDistance <= FaceBoundary;
        }
        var normalizedX = dx / face.HalfWidth;
        var normalizedY2 = dy / face.HalfHeight;
        normalizedDistance = MathF.Sqrt(
            normalizedX * normalizedX + normalizedY2 * normalizedY2);
        return normalizedDistance <= FaceBoundary;
    }

    private static void DrawHair(
        Image image,
        Fo2FaceShapePreset face,
        Fo2HairStylePreset hair,
        Fo2AppearanceColorPreset hairColor)
    {
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                if (!InsideFace(x, y, face, out _) || y > hair.HairLineY)
                    continue;
                image.SetPixel(x, y, hairColor.PortraitColor);
            }
        }
        if (hair.SideMode == Fo2ProceduralAppearanceCatalog.NoSideHair)
            return;
        var rightX = CenterX + (int)(face.HalfWidth - hair.SideInset);
        var leftX = CenterX - (int)(face.HalfWidth - hair.SideInset);
        for (var y = hair.HairLineY; y <= hair.BottomY; y++)
        {
            image.SetPixel(rightX, y, hairColor.PortraitColor);
            image.SetPixel(rightX - 1, y, hairColor.PortraitColor);
            if (hair.SideMode != Fo2ProceduralAppearanceCatalog.BothSideHair)
                continue;
            image.SetPixel(leftX, y, hairColor.PortraitColor);
            image.SetPixel(leftX + 1, y, hairColor.PortraitColor);
        }
    }

    private static void DrawBrow(Image image, int startX, Fo2BrowStylePreset brow)
    {
        var midpoint = (Catalog.PortraitBrowWidth - 1) / 2.0f;
        for (var x = 0; x < Catalog.PortraitBrowWidth; x++)
        {
            var distance = midpoint > 0.0f
                ? MathF.Abs(x - midpoint) / midpoint
                : 0.0f;
            var y = brow.PortraitY +
                (int)MathF.Round(distance * brow.PortraitOuterOffset);
            image.FillRect(
                new Rect2I(startX + x, y, 1, brow.PortraitThickness),
                Catalog.Feature);
        }
    }

    private static void ValidateIdentity(
        string sex,
        string faceShapeId,
        string hairStyleId,
        string skinToneId,
        string hairColorId,
        string eyeColorId,
        string browStyleId,
        string noseStyleId,
        string mouthStyleId)
    {
        if (sex is not "Male" and not "Female" ||
            ShapeIndex(faceShapeId) < 0 || HairStyleIndex(hairStyleId) < 0 ||
            SkinToneIndex(skinToneId) < 0 || HairColorIndex(hairColorId) < 0 ||
            EyeColorIndex(eyeColorId) < 0 || BrowStyleIndex(browStyleId) < 0 ||
            NoseStyleIndex(noseStyleId) < 0 || MouthStyleIndex(mouthStyleId) < 0)
            throw new ArgumentOutOfRangeException(
                nameof(faceShapeId),
                "Fallout 2 portrait sex, face, hair, or skin input is unsupported.");
    }

    private static string DefaultRoot() => System.IO.Path.GetFullPath(System.IO.Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
        "OpenNV",
        "portraits",
        "fallout2"));

    private static string FileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
