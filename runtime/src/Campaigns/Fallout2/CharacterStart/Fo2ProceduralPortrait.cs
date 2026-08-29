using System.Security.Cryptography;
using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;

internal static class Fo2ProceduralPortrait
{
    internal const string GeneratorId = "opennv-classic-green-face-shape/v1";
    internal const string PortraitState = "generated-local-classic-green-face-shape";
    internal const string RoundFace = "round";
    internal const string OvalFace = "oval";
    internal const string AngularFace = "angular";
    internal const int Width = 128;
    internal const int Height = 128;
    private const int HashLength = 64;
    private const int CenterX = 64;
    private const int CenterY = 61;
    private const int EyeY = 57;
    private const int LeftEyeX = 51;
    private const int RightEyeX = 77;
    private const int NoseX = 63;
    private const int NoseY = 69;
    private const int MouthX = 53;
    private const int MouthY = 82;
    private const int MouthWidth = 22;
    private const int NeckX = 51;
    private const int NeckY = 98;
    private const int NeckWidth = 26;
    private const int NeckHeight = 21;
    private const int EyeWidth = 7;
    private const int EyeHeight = 3;
    private const int HairLineY = 42;
    private const int LongHairBottomY = 100;
    private const int OutlineInset = 2;
    private const float RoundHalfHeight = 42.0f;
    private const float RoundHalfWidth = 41.0f;
    private const float OvalHalfHeight = 48.0f;
    private const float OvalHalfWidth = 35.0f;
    private const float AngularHalfHeight = 46.0f;
    private const float AngularHalfWidth = 38.0f;
    private const float AngularTaper = 0.42f;
    private const float FaceBoundary = 1.0f;
    private const float ShadingBias = 0.28f;
    private static readonly string[] FaceShapes = [RoundFace, OvalFace, AngularFace];
    private static readonly Color Background = new("061006");
    private static readonly Color Outline = new("163e1a");
    private static readonly Color FaceDark = new("286b2d");
    private static readonly Color FaceLight = new("78e781");
    private static readonly Color Feature = new("0b240d");
    private static readonly Color Hair = new("123b16");

    internal static IReadOnlyList<string> Shapes => FaceShapes;

    internal static int ShapeIndex(string faceShapeId) =>
        Array.IndexOf(FaceShapes, faceShapeId);

    internal static Image Render(string sex, string faceShapeId)
    {
        ValidateSexAndShape(sex, faceShapeId);
        var image = Image.CreateEmpty(Width, Height, false, Image.Format.Rgba8);
        image.Fill(Background);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                if (!InsideFace(x, y, faceShapeId, out var normalizedDistance))
                    continue;
                image.SetPixel(
                    x,
                    y,
                    normalizedDistance >= FaceBoundary -
                        (float)OutlineInset / RoundHalfWidth
                        ? Outline
                        : x < CenterX - RoundHalfWidth * ShadingBias
                            ? FaceDark
                            : FaceLight);
            }
        }
        image.FillRect(new Rect2I(NeckX, NeckY, NeckWidth, NeckHeight), FaceDark);
        image.FillRect(new Rect2I(LeftEyeX, EyeY, EyeWidth, EyeHeight), Feature);
        image.FillRect(new Rect2I(RightEyeX, EyeY, EyeWidth, EyeHeight), Feature);
        image.FillRect(new Rect2I(NoseX, NoseY, OutlineInset, EyeWidth), FaceDark);
        image.FillRect(new Rect2I(MouthX, MouthY, MouthWidth, OutlineInset), Feature);
        DrawHair(image, sex);
        return image;
    }

    internal static Fo2CharacterAppearanceContract Commit(
        Fo2PremadeCharacter source,
        string sex,
        string faceShapeId)
    {
        var image = Render(sex, faceShapeId);
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
                GeneratorId,
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
            !FaceShapes.Contains(contract.FaceShapeId, StringComparer.Ordinal) ||
            contract.PortraitGeneratorId != GeneratorId ||
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
        string faceShapeId,
        out float normalizedDistance)
    {
        var dx = x - CenterX;
        var dy = y - CenterY;
        if (faceShapeId == AngularFace)
        {
            var normalizedY = MathF.Abs(dy) / AngularHalfHeight;
            var angularWidth = AngularHalfWidth * (FaceBoundary - AngularTaper * normalizedY);
            normalizedDistance = MathF.Max(
                MathF.Abs(dx) / MathF.Max(angularWidth, FaceBoundary),
                normalizedY);
            return normalizedDistance <= FaceBoundary;
        }
        var halfWidth = faceShapeId == RoundFace ? RoundHalfWidth : OvalHalfWidth;
        var halfHeight = faceShapeId == RoundFace ? RoundHalfHeight : OvalHalfHeight;
        var normalizedX = dx / halfWidth;
        var normalizedY2 = dy / halfHeight;
        normalizedDistance = MathF.Sqrt(
            normalizedX * normalizedX + normalizedY2 * normalizedY2);
        return normalizedDistance <= FaceBoundary;
    }

    private static void DrawHair(Image image, string sex)
    {
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                if (!InsideFace(x, y, OvalFace, out _) || y > HairLineY)
                    continue;
                image.SetPixel(x, y, Hair);
            }
        }
        if (sex != "Female")
            return;
        for (var y = HairLineY; y <= LongHairBottomY; y++)
        {
            if (InsideFace(CenterX, y, OvalFace, out _))
            {
                image.SetPixel(CenterX - (int)OvalHalfWidth, y, Hair);
                image.SetPixel(CenterX + (int)OvalHalfWidth, y, Hair);
            }
        }
    }

    private static void ValidateSexAndShape(string sex, string faceShapeId)
    {
        if (sex is not "Male" and not "Female" ||
            !FaceShapes.Contains(faceShapeId, StringComparer.Ordinal))
            throw new ArgumentOutOfRangeException(
                nameof(faceShapeId),
                "Fallout 2 portrait sex or face-shape input is unsupported.");
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
