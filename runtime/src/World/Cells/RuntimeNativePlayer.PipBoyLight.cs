using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.InputSystem;
using OpenNV.Runtime.Presentation.Rendering;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private FalloutPipBoyLight? _pipBoyLightSource;
    private readonly FalloutPipBoyLightState _pipBoyLight = new();
    private OmniLight3D? _pipBoyWorldLight;
    private FalloutFormKey _pipBoyDevice;
    private object? _pipBoyLightBody;
    private FalloutNifBound? _pipBoyLightBounds;
    private string? _pipBoyLightError;
    internal object PipBoyLightObservation => new
    {
        enabled = _pipBoyLight.Enabled,
        held = _pipBoyLight.Pressed,
        error = _pipBoyLightError,
        spell = _pipBoyLightSource?.Spell.ToString(),
        effect = _pipBoyLightSource?.Effect.ToString(),
        light = _pipBoyLightSource?.Light.ToString(),
        position = _pipBoyWorldLight?.IsInsideTree() == true ? _pipBoyWorldLight.GlobalPosition.ToString() : null,
        radius = _pipBoyWorldLight?.OmniRange,
        xrControllerBinding = "unbound;existing-combat-wheel-preserved"
    };
    private bool PipBoyLightAvailable => _presentationInventory is { } inventory &&
        inventory.Equipped.Contains(_presentationRecords!.RuntimeFormId(_pipBoyDevice));
    private bool CanUsePipBoyButton => !_modalInput && _furniturePhase == 0 && SourceControls.PipBoy &&
        _sourceCamera is null && IsDefeated?.Invoke() != true && PipBoyLightAvailable;

    private void ConfigurePipBoyLight(FalloutPluginStack records)
    {
        _pipBoyLightSource = FalloutPipBoyLight.Read(records);
        _pipBoyDevice = FalloutDialogueTopic.Find(records, "ARMO", "PipBoy").FormKey;
        _pipBoyWorldLight = new()
        {
            Name = "SourcePipBoyWorldLight",
            Visible = false,
            TopLevel = true,
            OmniAttenuation = RetailLighting.GodotOmniDecayForRetailRemap,
            ShadowEnabled = _configuration.Renderer.AuthoredPointLightShadows
        };
        var rgb = FalloutPlacedLightResolver.NormalizeLightColor(_pipBoyLightSource.Definition.ColorRgb);
        _pipBoyWorldLight.LightColor = RetailLighting.GodotLightColor(new(rgb[0], rgb[1], rgb[2]));
        _pipBoyWorldLight.LightEnergy = _pipBoyLightSource.Definition.Intensity * _configuration.Renderer.PointLightEnergyScale;
        AddChild(_pipBoyWorldLight);
    }

    private bool PipBoyButtonInput(InputEvent input)
    {
        if (_pipBoyLightSource is null) return false;
        var action = _inputControls is null ? _configuration.Player.DesktopInput.PipBoy.Action : RuntimeNativeInputControls.Action(14).ToString();
        if (input.IsActionPressed(action, allowEcho: false))
        {
            if (!CanUsePipBoyButton) return false;
            _pipBoyLight.Press(); return true;
        }
        if (input.IsActionReleased(action) && _pipBoyLight.Pressed)
        {
            if (!CanUsePipBoyButton) _pipBoyLight.Cancel();
            else if (_pipBoyLight.Release()) OpenPipBoy?.Invoke();
            return true;
        }
        return false;
    }

    private void AdvancePipBoyButton(double delta)
    {
        if (_pipBoyLightSource is null) return;
        if (!CanUsePipBoyButton || GetTree().Paused) { _pipBoyLight.Cancel(); return; }
        _pipBoyLight.Advance(delta, _presentationRecords!.NumericSettings.Float("fPlayerPipBoyLightTimer"));
    }

    private void PublishPipBoyLight()
    {
        if (_pipBoyWorldLight is not { } light || _pipBoyLightSource is not { } source) return;
        if (GetTree().Paused) _pipBoyLight.Cancel();
        light.Visible = _pipBoyLight.Enabled;
        _firstPerson?.SetWorldPipBoyLight(_pipBoyLight.Enabled && _xr?.PointAtPipBoy is null);
        _thirdPerson?.SetWorldPipBoyLight(_pipBoyLight.Enabled);
        if (!_pipBoyLight.Enabled) return;
        if (!PipBoyLightAvailable || _thirdPerson is null)
            throw new NotSupportedException("Active Pip-Boy light has no equipped device/body owner.");
        var actor = _thirdPerson.Actor;
        if (!ReferenceEquals(_pipBoyLightBody, actor))
        {
            var nif = actor.Skeleton.Source;
            _pipBoyLightBounds = nif.Roots.Select(nif.ReadNode).SelectMany(node => node.ExtraData).Where(index => index >= 0)
                .Select(nif.ReadObject).OfType<FalloutNifBound>().Single(value => value.Name == "BBX");
            _pipBoyLightBody = actor;
        }
        var bound = _pipBoyLightBounds!;
        var settings = _presentationRecords!.NumericSettings;
        var forward = (bound.Center.Y + bound.Dimensions.Y) * actor.Appearance.Height + settings.Float("fMagicLightForwardOffset");
        var height = actor.SourceHeight + settings.Float("fMagicLightHeightOffset");
        var local = new Vector3(settings.Float("fMagicLightSideOffset"), height, -forward) * UnitsToMeters;
        light.GlobalTransform = new(Basis.Identity, ToGlobal(local));
        light.OmniRange = source.Radius(_presentationRecords) * UnitsToMeters;
    }

    private void PublishPipBoyLightSafely()
    {
        // A weapon or pose failure must not freeze an earlier visible light.
        // Its actual on/off owner publishes even when presentation exits early.
        try
        {
            PublishPipBoyLight();
            _pipBoyLightError = null;
            RemoveMeta("opennv_pipboy_light_error");
        }
        catch (Exception error)
        {
            if (_pipBoyWorldLight is not null) _pipBoyWorldLight.Visible = false;
            if (_pipBoyLightError != error.Message) GD.PushError("OPENNV_PIPBOY_LIGHT_UNBOUND " + error.Message);
            _pipBoyLightError = error.Message;
            SetMeta("opennv_pipboy_light_error", error.Message);
        }
    }

    internal FalloutPipBoyLightSnapshot? CapturePipBoyLight() => _pipBoyLightSource is { } source ? _pipBoyLight.Capture(source.SourceHash) : null;
    internal void RestorePipBoyLight(FalloutPipBoyLightSnapshot? state)
    {
        if (state is not null) _pipBoyLight.Restore(state, (_pipBoyLightSource ??
            throw new InvalidOperationException("Pip-Boy light restoration has no source owner.")).SourceHash);
    }
}
