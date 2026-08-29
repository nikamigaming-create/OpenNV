using System.Security.Cryptography;
using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout1;

internal sealed record Fo1CharacterAppearance(
    string FaceShapeId,
    string HairStyleId,
    string SkinToneId,
    string HairColorId,
    string EyeColorId,
    string RecipeId,
    string RecipeSha256,
    string GeneratorId,
    string PortraitPath,
    string PortraitSha256,
    int PortraitWidth,
    int PortraitHeight)
{
    internal const string ExpectedSchema = "opennv-fo1-character-appearance/v1";

    internal void Validate(string sex) => Fo1ProceduralPortrait.Validate(this, sex);

    internal object Report() => new
    {
        schema = ExpectedSchema,
        mode = "hex-local-procedural-custom",
        faceShapeId = FaceShapeId,
        hairStyleId = HairStyleId,
        skinToneId = SkinToneId,
        hairColorId = HairColorId,
        eyeColorId = EyeColorId,
        recipeId = RecipeId,
        recipeSha256 = RecipeSha256,
        generatorId = GeneratorId,
        portraitPath = PortraitPath,
        portraitSha256 = PortraitSha256,
        portraitWidth = PortraitWidth,
        portraitHeight = PortraitHeight,
        boundary = "local-procedural-preview-not-retail-head-geometry",
    };
}

internal static class Fo1ProceduralPortrait
{
    internal const string GeneratorId = "opennv-fo1-classic-green-portrait/v1";
    internal const int Width = 128;
    internal const int Height = 128;
    private const int HashLength = 64;
    private const int CenterX = 64;
    private const int CenterY = 61;
    private const int EyeY = 57;
    private const int LeftEyeX = 51;
    private const int RightEyeX = 77;
    private const int EyeWidth = 7;
    private const int EyeHeight = 3;
    private const int NeckX = 51;
    private const int NeckY = 98;
    private const int NeckWidth = 26;
    private const int NeckHeight = 21;
    private const int NoseY = 69;
    private const int MouthY = 82;
    private const int OutlineInset = 2;
    private const float FaceBoundary = 1.0f;
    private const float MaleWidthScale = 1.025f;
    private const float FemaleWidthScale = 0.975f;
    private const float ShadingBias = 0.28f;
    private static Fo1ProceduralAppearanceCatalog Catalog =>
        Fo1ProceduralAppearanceCatalog.Load();

    internal static IReadOnlyList<string> FaceShapes =>
        Catalog.FaceShapes.Select(row => row.Id).ToArray();
    internal static IReadOnlyList<string> HairStyles =>
        Catalog.HairStyles.Select(row => row.Id).ToArray();
    internal static IReadOnlyList<string> SkinTones =>
        Catalog.SkinTones.Select(row => row.Id).ToArray();
    internal static IReadOnlyList<string> HairColors =>
        Catalog.HairColors.Select(row => row.Id).ToArray();
    internal static IReadOnlyList<string> EyeColors =>
        Catalog.EyeColors.Select(row => row.Id).ToArray();

    internal static Image Render(
        string sex,
        string faceShapeId,
        string hairStyleId,
        string skinToneId,
        string hairColorId,
        string eyeColorId)
    {
        ValidateIdentity(
            sex, faceShapeId, hairStyleId, skinToneId, hairColorId, eyeColorId);
        var face = Catalog.Face(faceShapeId);
        var hair = Catalog.Hair(hairStyleId);
        var skin = Catalog.Skin(skinToneId);
        var hairColor = Catalog.HairColor(hairColorId);
        var eyeColor = Catalog.EyeColor(eyeColorId);
        var widthScale = sex == "Male" ? MaleWidthScale : FemaleWidthScale;
        var image = Image.CreateEmpty(Width, Height, false, Image.Format.Rgba8);
        image.Fill(Catalog.Background);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                if (!InsideFace(x, y, face, widthScale, out var distance))
                    continue;
                image.SetPixel(
                    x,
                    y,
                    distance >= FaceBoundary - (float)OutlineInset / face.HalfWidth
                        ? Catalog.Outline
                        : x < CenterX - face.HalfWidth * ShadingBias
                            ? skin.PortraitShadow
                            : skin.PortraitHighlight);
            }
        }
        image.FillRect(new Rect2I(NeckX, NeckY, NeckWidth, NeckHeight), skin.PortraitShadow);
        image.FillRect(new Rect2I(LeftEyeX, EyeY, EyeWidth, EyeHeight), eyeColor.PortraitColor);
        image.FillRect(new Rect2I(RightEyeX, EyeY, EyeWidth, EyeHeight), eyeColor.PortraitColor);
        image.FillRect(new Rect2I(CenterX - 1, NoseY, 2, 7), skin.PortraitShadow);
        image.FillRect(new Rect2I(CenterX - 11, MouthY, 22, 2), Catalog.Feature);
        DrawHair(image, face, hair, hairColor, widthScale);
        return image;
    }

    internal static Fo1CharacterAppearance Commit(
        string sex,
        string faceShapeId,
        string hairStyleId,
        string skinToneId,
        string hairColorId,
        string eyeColorId)
    {
        var image = Render(
            sex, faceShapeId, hairStyleId, skinToneId, hairColorId, eyeColorId);
        var root = PortraitRoot();
        Directory.CreateDirectory(root);
        var temporary = System.IO.Path.Combine(root, $".{Guid.NewGuid():N}.png");
        try
        {
            if (image.SavePng(temporary) != Error.Ok)
                throw new InvalidOperationException(
                    "Could not write the Fallout 1 local generated portrait.");
            var sha256 = FileSha256(temporary);
            var destination = System.IO.Path.Combine(root, $"{sha256}.png");
            if (File.Exists(destination))
            {
                if (FileSha256(destination) != sha256)
                    throw new InvalidOperationException(
                        "Fallout 1 generated portrait identity collision detected.");
                File.Delete(temporary);
            }
            else
            {
                File.Move(temporary, destination);
            }
            var appearance = new Fo1CharacterAppearance(
                faceShapeId,
                hairStyleId,
                skinToneId,
                hairColorId,
                eyeColorId,
                Fo1ProceduralAppearanceCatalog.ExpectedId,
                Catalog.Sha256,
                GeneratorId,
                destination,
                sha256,
                Width,
                Height);
            Validate(appearance, sex);
            return appearance;
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    internal static void Validate(Fo1CharacterAppearance appearance, string sex)
    {
        ValidateIdentity(
            sex,
            appearance.FaceShapeId,
            appearance.HairStyleId,
            appearance.SkinToneId,
            appearance.HairColorId,
            appearance.EyeColorId);
        if (appearance.RecipeId != Fo1ProceduralAppearanceCatalog.ExpectedId ||
            appearance.RecipeSha256 != Catalog.Sha256 ||
            appearance.GeneratorId != GeneratorId ||
            appearance.PortraitWidth != Width || appearance.PortraitHeight != Height ||
            appearance.PortraitSha256.Length != HashLength ||
            appearance.PortraitSha256.Any(character =>
                !Uri.IsHexDigit(character) || char.IsUpper(character)))
            throw new InvalidOperationException(
                "Fallout 1 generated portrait contract is invalid.");
        var path = System.IO.Path.GetFullPath(appearance.PortraitPath);
        var root = PortraitRoot();
        if (!path.StartsWith(
                root + System.IO.Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(path) || FileSha256(path) != appearance.PortraitSha256)
            throw new InvalidOperationException(
                "Fallout 1 generated portrait is outside or differs from local user data.");
        var image = Image.LoadFromFile(path);
        if (image is null || image.IsEmpty() ||
            image.GetWidth() != Width || image.GetHeight() != Height)
            throw new InvalidOperationException(
                "Fallout 1 generated portrait dimensions drifted.");
    }

    private static bool InsideFace(
        int x,
        int y,
        Fo1FaceShape face,
        float widthScale,
        out float distance)
    {
        var dx = x - CenterX;
        var dy = y - CenterY;
        var width = face.HalfWidth * widthScale;
        if (face.Taper > 0.0f)
        {
            var normalizedY = MathF.Abs(dy) / face.HalfHeight;
            var angularWidth = width * (FaceBoundary - face.Taper * normalizedY);
            distance = MathF.Max(
                MathF.Abs(dx) / MathF.Max(angularWidth, FaceBoundary),
                normalizedY);
            return distance <= FaceBoundary;
        }
        var normalizedX = dx / width;
        var normalizedY2 = dy / face.HalfHeight;
        distance = MathF.Sqrt(normalizedX * normalizedX + normalizedY2 * normalizedY2);
        return distance <= FaceBoundary;
    }

    private static void DrawHair(
        Image image,
        Fo1FaceShape face,
        Fo1HairStyle hair,
        Fo1AppearanceColor color,
        float widthScale)
    {
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                if (y <= hair.HairLineY && InsideFace(x, y, face, widthScale, out _))
                    image.SetPixel(x, y, color.PortraitColor);
            }
        }
        if (hair.SideMode == Fo1ProceduralAppearanceCatalog.NoSideHair)
            return;
        var side = (int)(face.HalfWidth * widthScale);
        var rightX = CenterX + side;
        var leftX = CenterX - side;
        for (var y = hair.HairLineY; y <= hair.BottomY; y++)
        {
            image.SetPixel(rightX, y, color.PortraitColor);
            image.SetPixel(rightX - 1, y, color.PortraitColor);
            if (hair.SideMode != Fo1ProceduralAppearanceCatalog.BothSideHair)
                continue;
            image.SetPixel(leftX, y, color.PortraitColor);
            image.SetPixel(leftX + 1, y, color.PortraitColor);
        }
    }

    private static void ValidateIdentity(
        string sex,
        string faceShapeId,
        string hairStyleId,
        string skinToneId,
        string hairColorId,
        string eyeColorId)
    {
        if (sex is not "Male" and not "Female" ||
            !FaceShapes.Contains(faceShapeId, StringComparer.Ordinal) ||
            !HairStyles.Contains(hairStyleId, StringComparer.Ordinal) ||
            !SkinTones.Contains(skinToneId, StringComparer.Ordinal) ||
            !HairColors.Contains(hairColorId, StringComparer.Ordinal) ||
            !EyeColors.Contains(eyeColorId, StringComparer.Ordinal))
            throw new InvalidOperationException(
                "Fallout 1 custom appearance identity is unsupported.");
    }

    private static string PortraitRoot() => System.IO.Path.GetFullPath(
        System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "OpenNV",
            "portraits",
            "fallout1"));

    private static string FileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
