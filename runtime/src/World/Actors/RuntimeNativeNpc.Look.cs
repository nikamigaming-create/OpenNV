using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private FalloutHeadTrackingState? _headTargets;
    private FalloutBodyPartLook? _headPart;
    private FalloutLookSettings? _headSettings;
    private Func<FalloutFormKey, Vector3?>? _headTargetPoint;
    private NativeHeadTrackingPose? _headPose;
    private string? _headOverrideName;
    private string? _headError;

    internal object HeadTrackingState => new
    {
        owner = _headTargets is null ? "unbound" : "source-script-target-and-head-pose",
        bodyPart = _headPart,
        settings = _headSettings,
        slots = _headTargets?.Slots.Select(value => new
        {
            value.Priority,
            target = value.Target?.ToString(),
            value.Enabled,
        }).ToArray(),
        selected = _headTargets?.SelectedTarget?.ToString(),
        cached = _headTargets?.CachedTarget?.ToString(),
        defaultHoldSeconds = _headTargets?.DefaultHoldSeconds,
        revision = _headTargets?.Revision,
        targetRevision = _headTargets?.TargetRevision,
        animationOverride = _headOverrideName,
        pose = _headPose?.State,
        error = _headError,
        unbound = new[] { "automatic-default-acquisition", "combat-targets", "eye-aiming", "full-body-look", "target-save-restoration", "matched-native-pose-and-frame" },
    };

    internal void ConfigureHeadTracking(FalloutPluginStack records, RuntimeLiveContentSource source,
        Func<FalloutFormKey, Vector3?> targetPoint)
    {
        if (source.Game != RuntimeLiveContentSource.FalloutNewVegasGame)
            throw new NotSupportedException("This engine's humanoid head-tracking bootstrap is unbound.");
        // Ordinary NPCs use the engine's default humanoid body-part form. The
        // winning BPTD owns the actual node, flags and cone, including overrides.
        _headPart = FalloutBodyPartLook.Read(records.GetEffective(records.RuntimeFormKey(0x1d)));
        _headSettings = FalloutLookSettings.Read(FalloutInstallationSettings.Read(source));
        _headTargets = new(FalloutGameSettingFloats.Read(records, "fAIHoldDefaultHeadTrackTimer"));
        _headTargetPoint = targetPoint;
        if (_headPart is null) return;
        var bone = Skeleton.BoneIndex(_headPart.TargetNode);
        _headPose = new(Skeleton.Node, bone, _headPart, _headSettings, Skeleton.UnitsToMetres);
        var block = Skeleton.Node.GetBoneMeta(bone, "opennv_nif_block").AsInt32();
        var controllers = Skeleton.Source.Blocks.Where(value => value.TypeName == "NiFloatExtraDataController")
            .Select(value => (FalloutNifFloatExtraDataController)Skeleton.Source.ReadObject(value.Index))
            .Where(value => value.Time.Target == block).ToArray();
        if (controllers.Length > 1) throw new NotSupportedException("Head float-controller selection is ambiguous.");
        if (controllers.SingleOrDefault() is not { } controller) return;
        _ = Skeleton.FloatExtraData.Get(_headPart.TargetNode, controller.ExtraDataName);
        _headOverrideName = controller.ExtraDataName;
    }

    internal Vector3? HeadTargetPoint => _headPose?.WorldPosition;

    internal void ApplyBoundHeadTrackingCommand(FalloutBoundLookCommand command)
    {
        var actor = command.Actor == Appearance.Reference ? this :
            GetTree().Root.FindChildren("*", "", true, false).OfType<RuntimeNativeNpc>()
                .SingleOrDefault(value => value.Appearance.Reference == command.Actor);
        if (actor is null && _aiStack is not null && _aiCell is not null &&
            !FalloutHeadTrackingPrograms.RequiresProcess(_aiStack.GetEffective(command.Actor), _aiCell.Cell.FormKey, false))
        {
            GD.Print($"OPENNV_NATIVE_LOOK_RESULT actor={command.Actor} process=false");
            return;
        }
        if (actor is null) throw new NotSupportedException($"Look actor {command.Actor} has no loaded runtime owner.");
        actor.ApplyHeadTrackingCommand(command.Target);
        GD.Print($"OPENNV_NATIVE_LOOK actor={command.Actor} target={command.Target?.ToString() ?? "none"} sourceLine={command.Line} owner=script-head-tracking");
    }

    internal void ApplyHeadTrackingCommand(FalloutFormKey? target)
    {
        if (_headTargets is null || _headTargetPoint is null || _headError is not null)
            throw new NotSupportedException(_headError ?? "Look command has no physical head-tracking owner.");
        if (target is { } reference)
        {
            // Resolve through real loaded owners before admitting the command.
            if (_headTargetPoint(reference) is null)
                throw new NotSupportedException($"Look target {reference} has no loaded target-point owner.");
            _headTargets.Look(reference);
        }
        else _headTargets.StopLook();
    }

    private void RestoreAuthoredHeadPose() => _headPose?.RestoreAuthoredPose();

    private void AdvanceHeadTracking(float delta)
    {
        if (_headTargets is null) return;
        try
        {
            _headTargets.Advance(delta, target => _headTargetPoint!(target) is not null);
            var point = _headTargets.SelectedTarget is { } target ? _headTargetPoint!(target) : null;
            var animationOverride = _headOverrideName is null ? 0 :
                Skeleton.FloatExtraData.Get(_headPart!.TargetNode, _headOverrideName);
            _headPose?.Publish(point, animationOverride);
        }
        catch (Exception error) when (error is NotSupportedException or InvalidDataException or InvalidOperationException)
        {
            _headError = error.Message;
            throw;
        }
    }
}
