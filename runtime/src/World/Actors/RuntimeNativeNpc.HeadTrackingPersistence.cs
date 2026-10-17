using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private FalloutPluginStack? _headRecords;
    private Func<FalloutActorHeadTrackingSnapshot>? _headCapture;
    private FalloutActorHeadTrackingSnapshot? _headRestore;
    private FalloutReferenceInstance? _headReferenceState;

    private FalloutHeadTrackingBinding HeadTrackingBinding()
    {
        var records = _headRecords ?? throw new NotSupportedException("Head capture has no source records.");
        var actor = Appearance.Reference ?? throw new NotSupportedException("Head capture has no placed actor.");
        var body = records.GetEffective(records.RuntimeFormKey(0x1d));
        var bone = _headPart is null ? (int?)null : Skeleton.BoneIndex(_headPart.TargetNode);
        var parent = bone is { } index ? Skeleton.Node.GetBoneParent(index) : (int?)null;
        var block = bone is { } target ? Skeleton.Node.GetBoneMeta(target, "opennv_nif_block").AsInt32() : (int?)null;
        var controller = block is { } sourceBlock ? Skeleton.Source.Blocks.Where(value => value.TypeName == "NiFloatExtraDataController")
            .Select(value => (FalloutNifFloatExtraDataController)Skeleton.Source.ReadObject(value.Index))
            .SingleOrDefault(value => value.Time.Target == sourceBlock) : null;
        return new(actor, FalloutActorHeadTrackingSource.Hash(records.GetEffective(actor)), body.FormKey,
            FalloutActorHeadTrackingSource.Hash(body), _headPart, _headSettings!, Appearance.SkeletonPath.Replace('\\', '/'),
            Skeleton.Source.Sha256, Skeleton.UnitsToMetres, bone, bone is { } b ? Skeleton.Node.GetBoneName(b).ToString() : null,
            parent, parent is { } p ? Skeleton.Node.GetBoneName(p).ToString() : null, block, controller?.Block.Index, _headOverrideName);
    }

    internal FalloutActorHeadTrackingSnapshot CaptureHeadTracking()
    {
        if (_headTargets is null || _headSettings is null || _headRestore is not null || !GodotObject.IsInstanceValid(Skeleton.Node))
            throw new NotSupportedException("Head tracking has no live source-bound capture owner.");
        if (_conversationTarget is not null)
            throw new NotSupportedException("Head tracking has an active conversation-facing continuation.");
        var saved = new FalloutActorHeadTrackingSnapshot(HeadTrackingBinding(), _headTargets.Capture(), _headPose?.Capture(),
            _headOverrideName is null ? null : Skeleton.FloatExtraData.Get(_headPart!.TargetNode, _headOverrideName), _headError);
        saved.Validate(); return saved;
    }

    private void BindHeadTrackingPersistence(FalloutReferenceInstance? referenceState = null)
    {
        if ((referenceState ?? _aiReferenceState) is not { } state || _headTargets is null) return;
        if (state.Reference != Appearance.Reference) throw new InvalidDataException("Head capture reference differs from its actual actor.");
        if (state.HeadTrackingCaptureBlocker is { } blocker)
            throw new NotSupportedException($"Head tracking cannot rematerialize its stopped capture: {blocker}");
        _headReferenceState = state;
        if (_headCapture is not null) return;
        if (state.CaptureHeadTracking is not null)
            throw new InvalidOperationException("Reference already has a live head capture owner.");
        if (state.HeadTracking is { } saved)
        {
            ValidateHeadTrackingRestore(saved);
            _headRestore = saved.Copy();
        }
        state.HeadTrackingRequired = true;
        state.CaptureHeadTracking = _headCapture = CaptureHeadTracking;
        Skeleton.Node.TreeExiting += RetainHeadTracking;
        if (IsInsideTree()) RestorePendingHeadTracking();
    }

    public override void _Ready() => RestorePendingHeadTracking();

    private void ValidateHeadTrackingRestore(FalloutActorHeadTrackingSnapshot saved)
    {
        FalloutActorHeadTrackingSource.Validate(_headRecords!, Appearance.Reference!.Value, saved);
        if (saved.Binding != HeadTrackingBinding())
            throw new InvalidDataException("Saved head tracking differs from this actual actor rig or units.");
        _headTargets!.ValidateRestore(saved.Targets);
        if (_headOverrideName is { } name) _ = Skeleton.FloatExtraData.Get(_headPart!.TargetNode, name);
    }

    private void RestorePendingHeadTracking()
    {
        if (_headRestore is not { } saved) return;
        // Children publish their saved ragdoll/combat pose before the NPC's
        // ready callback. Rejoin the raw head history afterwards, with no
        // Look replay, target invalidation, animation advance or random draw.
        ValidateHeadTrackingRestore(saved);
        _headTargets!.Restore(saved.Targets);
        if (_headOverrideName is { } name) Skeleton.FloatExtraData.Restore(_headPart!.TargetNode, name, saved.OverrideValue!.Value);
        if (saved.Pose is { } pose) _headPose!.Restore(pose);
        _headError = saved.Error; _headRestore = null;
    }

    private void RetainHeadTracking()
    {
        if (_headCapture is null || _headReferenceState is not { } state) return;
        try
        {
            if (_headRestore is not null) throw new NotSupportedException("Head restoration has not reached native readiness.");
            state.HeadTracking = CaptureHeadTracking().Copy();
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or IOException)
        {
            state.HeadTrackingCaptureBlocker = error.Message;
            GD.PushError($"OPENNV_NATIVE_HEAD_RETIRE_DIVERGENCE reference={state.Reference}: {error.Message}");
        }
        finally
        {
            if (ReferenceEquals(state.CaptureHeadTracking, _headCapture)) state.CaptureHeadTracking = null;
            Skeleton.Node.TreeExiting -= RetainHeadTracking;
            _headCapture = null;
        }
    }
}
