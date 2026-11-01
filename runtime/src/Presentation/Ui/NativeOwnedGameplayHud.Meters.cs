using System.Xml.Linq;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed record NativeHudCondition(FalloutFormKey Weapon, float Fraction);

internal partial class NativeOwnedGameplayHud
{
    private FalloutNumericIniSettings? _hudIni;
    private Func<NativeHudCondition?>? _condition;
    private NativeHudCondition? _lastCondition;
    private XElement? _conditionLabel, _conditionMeter, _conditionBackground, _conditionArrows;
    private float _conditionArrowInitialMeterX;
    private string? _conditionError;

    private void InitializeMeterDetails(FalloutPluginStack records,
        Func<string, XElement, XElement> template, Func<NativeHudCondition?>? condition, XElement? conditionArrows)
    {
        _hudIni = records.IniSettings;
        _condition = condition;
        if (_hp is null || _ap is null) return;
        var left = template("template_left_bracket", _hp);
        var right = template("template_right_bracket", _ap);
        // Match native creation order: underlay brackets precede text/meters.
        left.Remove(); _hp.AddFirst(left); right.Remove(); _ap.AddFirst(right);
        _tiles.Bind(left, "brightness", 255); _tiles.Bind(right, "brightness", 255);
        _tiles.Bind(right, "x", -37);
        if (condition is null) return;
        _conditionLabel = template("template_justify_right_text", _ap);
        _tiles.Text[_conditionLabel] = FalloutGameSettingStrings.Read(records, "sInventoryCondition");
        _tiles.Bind(_conditionLabel, "x", 80); _tiles.Bind(_conditionLabel, "y", 80);
        _conditionBackground = template("template_meter_background", _ap);
        _tiles.Bind(_conditionBackground, "y", 83); _tiles.Bind(_conditionBackground, "brightness", 100);
        _conditionMeter = template("template_meter", _ap);
        _conditionArrowInitialMeterX = _tiles.Number(_conditionMeter, "x");
        _tiles.SetFilename(_conditionMeter, "Interface/VATS/vats_bar.dds");
        _tiles.Bind(_conditionMeter, "_OriginalX", 90); _tiles.Bind(_conditionMeter, "_TotalWidth", 60);
        _tiles.Bind(_conditionMeter, "_ImageWidth", 1); _tiles.Bind(_conditionMeter, "zoom", 200);
        _tiles.Bind(_conditionMeter, "y", 83); _tiles.Bind(_conditionMeter, "brightness", 175);
        _conditionArrows = conditionArrows ?? throw new InvalidDataException("Source HUD condition arrows are unavailable.");
        // Native initializes the root arrow with the HUD palette even though
        // the shipped XML leaves its system-color declaration empty.
        if (string.IsNullOrWhiteSpace(_conditionArrows.Element("systemcolor")?.Value))
            _conditionArrows.SetElementValue("systemcolor", "entity_hudmain");
        _tiles.Bind(_conditionArrows, "height", _tiles.Number(_conditionBackground, "height"));
        BindMeter(_conditionMeter, 0, false, 0);
        ShowCondition(false);
    }

    private void LayoutMeterBranches(Vector2 pixels)
    {
        // Preserve existing normalization outside the qualified landscape
        // integration and keep that presentation gap explicit.
        SetMeta("opennv_hud_nonlandscape_normalization_unbound", pixels.X <= pixels.Y);
        var wide = (double)pixels.Y / pixels.X != .75;
        var suffix = wide ? "Wide:Interface" : ":Interface";
        var safeX = _hudIni!.Get("iSafeZoneX" + suffix);
        var safeY = _hudIni.Get("iSafeZoneY" + suffix);
        if (!double.IsFinite(safeX) || !double.IsFinite(safeY) || safeX < int.MinValue || safeX > int.MaxValue ||
            safeY < int.MinValue || safeY > int.MaxValue)
            throw new InvalidDataException("HUD safe-zone settings exceed native integer coordinates.");
        var x = (int)safeX; var y = (int)safeY;
        _tiles.Bind(_hp!, "x", 2f * x + 10);
        _tiles.Bind(_ap!, "x", (int)Size.X - 2f * x - _tiles.Number(_ap!, "width") + 30);
        _tiles.Bind(_hp!, "y", (int)Size.Y - 2f * y - _tiles.Number(_hp!, "height"));
        _tiles.Bind(_ap!, "y", (int)Size.Y - 2f * y - _tiles.Number(_ap!, "height"));
        if (_conditionArrows is not null)
        {
            // The authored root image marks the fixed 75% condition boundary.
            // Native placement uses the full background and initial meter
            // coordinate, before the filled meter receives its current width.
            _tiles.Bind(_conditionArrows, "x", (float)((double)_tiles.Number(_ap!, "x") +
                _tiles.Number(_conditionLabel!, "x") + _conditionArrowInitialMeterX +
                .75 * _tiles.Number(_conditionBackground!, "width") + 8));
            _tiles.Bind(_conditionArrows, "y", _tiles.Number(_ap!, "y") + _tiles.Number(_conditionMeter!, "y"));
        }
    }

    private void BindMeter(XElement meter, float fraction, bool right, int minimumSegments)
    {
        var authoredTotal = _tiles.Number(meter, "_TotalWidth");
        var authoredStep = _tiles.Number(meter, "_ImageWidth");
        var authoredOrigin = _tiles.Number(meter, "_OriginalX");
        if (!float.IsFinite(fraction) || !float.IsFinite(authoredTotal) || !float.IsFinite(authoredStep) ||
            !float.IsFinite(authoredOrigin) || authoredTotal < 0 || authoredTotal >= int.MaxValue ||
            authoredStep < 1 || authoredStep >= int.MaxValue || authoredOrigin < int.MinValue || authoredOrigin >= int.MaxValue)
            throw new InvalidDataException("HUD source meter metrics are invalid.");
        var total = (int)authoredTotal; var step = (int)authoredStep;
        var width = Math.Max(minimumSegments * step, (int)(total * (double)Math.Clamp(fraction, 0, 1) / step) * step);
        _tiles.Bind(meter, "width", width);
        _tiles.Bind(meter, "x", (int)authoredOrigin - (right ? width : 0));
    }

    private void ShowCondition(bool shown)
    {
        if (_conditionMeter is null) return;
        foreach (var tile in new[] { _conditionLabel!, _conditionMeter, _conditionBackground!, _conditionArrows! })
            _tiles.Bind(tile, "visible", shown ? 1 : 0);
    }

    private void UpdateCondition()
    {
        if (_conditionMeter is null || _condition is null) return;
        try
        {
            var condition = _condition();
            if (condition == _lastCondition && _conditionError is null) return;
            if (condition is { } value && (!float.IsFinite(value.Fraction) || value.Fraction is < 0 or > 1))
                throw new InvalidDataException("HUD weapon condition is outside its persistent unit interval.");
            _conditionError = null; _lastCondition = condition;
            ShowCondition(condition is not null);
            if (condition is not null) BindMeter(_conditionMeter, condition.Fraction, false, 0);
            // The native threshold is qualified; cadence/phase and alpha
            // modulation need a joined runtime observation before admission.
            SetMeta("opennv_hud_low_condition_flash_unbound", condition?.Fraction < .25f);
            QueueRedraw();
        }
        catch (Exception error)
        {
            if (_conditionError != error.Message) GD.PushError("OPENNV_HUD_CONDITION_UNBOUND " + error.Message);
            _conditionError = error.Message; _lastCondition = null; ShowCondition(false); QueueRedraw();
        }
    }
}
