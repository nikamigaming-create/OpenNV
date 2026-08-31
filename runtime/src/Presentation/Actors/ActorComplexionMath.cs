using Godot;

namespace OpenNV.Runtime.Presentation.Actors;

internal static class ActorComplexionMath
{
    private const float LeftCheekMinimumU = 0.12f;
    private const float LeftCheekMaximumU = 0.42f;
    private const float RightCheekMinimumU = 0.58f;
    private const float RightCheekMaximumU = 0.88f;
    private const float CheekMinimumV = 0.40f;
    private const float CheekMaximumV = 0.68f;
    private const float NeckMinimumU = 0.18f;
    private const float NeckMaximumU = 0.82f;
    private const float NeckMinimumV = 0.78f;
    private const float NeckMaximumV = 0.98f;
    private const float TorsoMinimumU = 0.25f;
    private const float TorsoMaximumU = 0.75f;
    private const float TorsoMinimumV = 0.25f;
    private const float TorsoMaximumV = 0.72f;
    private const float OpaqueAlphaThreshold = 0.5f;
    private const int MaximumSamplesPerAxis = 96;

    internal static Vector3 AverageFaceGenEncodedSkinColor(ShaderMaterial material)
    {
        var leftCheek = AverageFaceGenEncodedColor(
            material,
            LeftCheekMinimumU,
            LeftCheekMaximumU,
            CheekMinimumV,
            CheekMaximumV);
        var rightCheek = AverageFaceGenEncodedColor(
            material,
            RightCheekMinimumU,
            RightCheekMaximumU,
            CheekMinimumV,
            CheekMaximumV);
        return (leftCheek + rightCheek) * 0.5f;
    }

    internal static Vector3 AverageFaceGenEncodedNeckColor(ShaderMaterial material) =>
        AverageFaceGenEncodedColor(
            material,
            NeckMinimumU,
            NeckMaximumU,
            NeckMinimumV,
            NeckMaximumV);

    internal static Vector3 AverageEncodedSkinColor(
        ShaderMaterial material,
        bool centralTorso)
    {
        var image = RequiredTextureImage(material, "base_map");
        return AverageTextureColor(
            image,
            (_, _, color) => new Vector3(color.R, color.G, color.B),
            centralTorso ? TorsoMinimumU : 0.0f,
            centralTorso ? TorsoMaximumU : 1.0f,
            centralTorso ? TorsoMinimumV : 0.0f,
            centralTorso ? TorsoMaximumV : 1.0f);
    }

    internal static float Mean(Vector3 color) =>
        (color.X + color.Y + color.Z) / 3.0f;

    private static Vector3 AverageFaceGenEncodedColor(
        ShaderMaterial material,
        float minimumU,
        float maximumU,
        float minimumV,
        float maximumV)
    {
        var baseImage = RequiredTextureImage(material, "base_map");
        var detailImage = RequiredTextureImage(material, "facegen_map0");
        var neutral = material.GetShaderParameter("signed_detail_neutral").AsSingle();
        var detailScale = material.GetShaderParameter("signed_detail_scale").AsSingle();
        var tone = material.GetShaderParameter("tone_multiplier").AsVector3();
        Vector3 Sample(int x, int y, Color baseColor)
        {
            var detailX = Math.Min(
                detailImage.GetWidth() - 1,
                x * detailImage.GetWidth() / baseImage.GetWidth());
            var detailY = Math.Min(
                detailImage.GetHeight() - 1,
                y * detailImage.GetHeight() / baseImage.GetHeight());
            var detail = detailImage.GetPixel(detailX, detailY);
            return new Vector3(
                Math.Clamp(
                    (baseColor.R + detailScale * (detail.R - neutral)) * tone.X,
                    0.0f,
                    1.0f),
                Math.Clamp(
                    (baseColor.G + detailScale * (detail.G - neutral)) * tone.Y,
                    0.0f,
                    1.0f),
                Math.Clamp(
                    (baseColor.B + detailScale * (detail.B - neutral)) * tone.Z,
                    0.0f,
                    1.0f));
        }

        return AverageTextureColor(
            baseImage,
            Sample,
            minimumU,
            maximumU,
            minimumV,
            maximumV);
    }

    private static Vector3 AverageTextureColor(
        Image image,
        Func<int, int, Color, Vector3> convert,
        float minimumU,
        float maximumU,
        float minimumV,
        float maximumV)
    {
        var stepX = Math.Max(1, image.GetWidth() / MaximumSamplesPerAxis);
        var stepY = Math.Max(1, image.GetHeight() / MaximumSamplesPerAxis);
        var startX = Math.Clamp(
            (int)(image.GetWidth() * minimumU),
            0,
            image.GetWidth() - 1);
        var endX = Math.Clamp(
            (int)(image.GetWidth() * maximumU),
            startX + 1,
            image.GetWidth());
        var startY = Math.Clamp(
            (int)(image.GetHeight() * minimumV),
            0,
            image.GetHeight() - 1);
        var endY = Math.Clamp(
            (int)(image.GetHeight() * maximumV),
            startY + 1,
            image.GetHeight());
        var total = Vector3.Zero;
        var samples = 0;
        for (var y = startY; y < endY; y += stepY)
        {
            for (var x = startX; x < endX; x += stepX)
            {
                var color = image.GetPixel(x, y);
                if (color.A < OpaqueAlphaThreshold)
                    continue;
                total += convert(x, y, color);
                samples++;
            }
        }

        if (samples == 0)
            throw new InvalidOperationException(
                "Owned actor skin texture has no opaque complexion samples.");
        return total / samples;
    }

    private static Image RequiredTextureImage(
        ShaderMaterial material,
        string parameter)
    {
        if (material.GetShaderParameter(parameter).AsGodotObject() is not Texture2D texture)
            throw new InvalidOperationException(
                $"Owned actor complexion material has no {parameter} texture.");
        var image = texture.GetImage();
        if (image.IsEmpty())
            throw new InvalidOperationException(
                $"Owned actor complexion material {parameter} texture is empty.");
        return image;
    }
}
