using System.Xml.Linq;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Ui;

internal partial class NativeOwnedGameplayHud
{
    private readonly Dictionary<XElement, FalloutCompassBlinkState> _compassBlinkStates = [];
    private FalloutCompassBlinkSettings? _compassBlinkSettings;
    private FalloutCompassHudDeclaration? _compassBlinkDeclaration;
    private float _compassBaseAlpha;
    private string? _compassBlinkError;

    private void AdvanceCompassBlink(float seconds)
    {
        if (_compassWindow is null || _lastCompass is null || _compassError is not null) return;
        if (_compassBlinkError is not null) { _tiles.Bind(_compassWindow, "visible", 0); return; }
        if (_lastCompass.Markers.Count == 0) return;
        try
        {
            if (_compassBlinkSettings is null)
            {
                var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Compass presentation source is absent.");
                var declaration = FalloutExecutableStringTable.ReadCompassHudDeclaration(source.FalloutExecutablePath);
                var opacity = FalloutInstallationSettings.Read(source).Number("Interface", "fHudOpacity");
                if (!float.IsFinite(opacity) || opacity is < 0 or > 1) throw new InvalidDataException("Source compass opacity is invalid.");
                _compassBaseAlpha = opacity * 255;
                _compassBlinkDeclaration = declaration;
                _compassBlinkSettings = FalloutCompassBlinkSettings.Read(_compassRecords);
            }
            var band = 2 * ((int)(_tiles.Number(_compassWindow, "width") / 2) - 35);
            var edge = band * _compassBlinkDeclaration!.EdgeFadeStartFraction;
            var milliseconds = unchecked((uint)Godot.Time.GetTicksMsec());
            var changed = false;
            void Alpha(XElement tile, float value)
            {
                changed |= _tiles.Number(tile, "alpha") != value;
                _tiles.Bind(tile, "alpha", value);
            }
            foreach (var marker in _lastCompass.Markers)
            {
                if (!_compassIcons.TryGetValue((marker.Kind, marker.Reference.ToString()), out var tile)) continue;
                var edgeAlpha = marker.Heading > edge ? (float)(_compassBaseAlpha * (band - (double)marker.Heading) / (band - (double)edge)) : _compassBaseAlpha;
                if (marker.Kind == "actor")
                {
                    // Native actor ticks have source color/heading-edge alpha,
                    // but never enter the distance-blink kernel.
                    Alpha(tile, edgeAlpha); continue;
                }
                if (!_compassBlinkStates.TryGetValue(tile, out var state))
                {
                    state = new(_tiles.Number(tile, "alpha"), checked((int)_tiles.Number(tile, "_AlphaDown")));
                    _compassBlinkStates.Add(tile, state);
                }
                var blink = marker.BlinkDistanceSquared is { } distance &&
                    (marker.Kind == "quest" || marker.Kind == "landmark" && marker.Heading <= edge &&
                        distance < _compassBlinkDeclaration.LandmarkBlinkSquaredLimit);
                if (blink) state.Advance(_compassBlinkSettings, marker.BlinkDistanceSquared!.Value, _compassBaseAlpha,
                    _compassBaseAlpha, seconds, milliseconds);
                else state.SetAlpha(marker.Kind == "landmark" ? edgeAlpha : _compassBaseAlpha);
                Alpha(tile, state.Alpha); _tiles.Bind(tile, "_AlphaDown", state.Phase);
            }
            if (changed) QueueRedraw();
        }
        catch (Exception error)
        {
            _compassBlinkError = error.Message;
            GD.PushError($"OPENNV_GAMEPLAY_COMPASS_FAIL {error.Message}");
            _tiles.Bind(_compassWindow, "visible", 0); QueueRedraw();
        }
    }
}
