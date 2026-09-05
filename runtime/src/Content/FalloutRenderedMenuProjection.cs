namespace OpenNV.Runtime.Content;

internal readonly record struct FalloutRenderedMenuProjection(float HorizontalSlope, float VerticalSlope,
    float Near, float Far, float ModelScale, float Depth, float HorizontalOffset, float VerticalOffset)
{
    internal static (int Width, int Height) RenderTargetSize(int backBufferWidth, int backBufferHeight)
    {
        if (backBufferWidth <= 0 || backBufferHeight <= 0) throw new ArgumentOutOfRangeException(nameof(backBufferWidth));
        // Rendered texture slots use a fixed 1280-wide target at the actual
        // back-buffer aspect. The menu's logical orthographic canvas is separate.
        var height = (int)(1280f * backBufferHeight / backBufferWidth);
        if (height <= 0) throw new InvalidDataException("Rendered-menu target height is empty.");
        return (1280, height);
    }

    internal static FalloutRenderedMenuProjection Read(FalloutInstallationSettings settings, float aspect,
        bool characterCreation)
    {
        var fov = settings.Number("Display", "fDefaultFOV");
        var factor = settings.Number("RenderedTerminal", "fRenderedTerminalFOV");
        var near = settings.Number("Display", "fNearDistance");
        var scale = settings.Number("RenderedTerminal", "fRaceSexMenuScale");
        var depth = characterCreation ? 80 : settings.Number("RenderedTerminal", "fRaceSexMenuZoom");
        var horizontal = settings.Number("RenderedTerminal", "fRaceSexMenuHPos");
        var vertical = characterCreation ? 0 : settings.Number("RenderedTerminal", "fRaceSexMenuVPos");
        if (!float.IsFinite(aspect) || aspect <= 0 || !float.IsFinite(fov) ||
            !float.IsFinite(factor) || fov * factor is <= 0 or >= 90 ||
            !float.IsFinite(near) || near <= 0 || !float.IsFinite(scale) || scale <= 0 ||
            !float.IsFinite(depth) || depth <= near || !float.IsFinite(horizontal) || !float.IsFinite(vertical))
            throw new InvalidDataException("Owned rendered-menu projection settings are invalid.");
        // Rendered-menu policy uses the configured world FOV times the menu
        // multiplier, then the engine's 4:3 reference factor. Script/console
        // creation uses the engine's standard 80-unit plane; barber/surgery
        // modes use the INI zoom and vertical offset instead.
        var slope = MathF.Tan(fov * factor * (MathF.PI / 180)) * (3f / 4f);
        return new(slope, slope / aspect, near, 5000, scale, depth, horizontal, vertical);
    }
}
