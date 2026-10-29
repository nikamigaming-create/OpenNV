using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private bool CanCaptureIdleRequestContinuation() => !_bindingInitialBase && AnimationError is null &&
        (_currentIdleRequest?.State != 2 || _animation is not null && _idlePlayback is not null && CanCaptureIdleObjects()) &&
        _outgoingIdles.All(outgoing => outgoing.Bindings is { } bindings && bindings.Count == outgoing.Objects.Count &&
            outgoing.Objects.All(node => GodotObject.IsInstanceValid(node) && !node.IsQueuedForDeletion()));

    private void BindIdleRequestCapture()
    {
        if (_aiReferenceState is not { } state) return;
        state.CanCaptureIdleRequests = _idleRequestsReady = CanCaptureIdleRequestContinuation;
        state.CaptureIdleRequests = _idleRequestsCapture = CaptureIdleRequestContinuation;
    }

    private FalloutActorResidualPose CaptureCompleteIdlePose()
    {
        var node = Skeleton.Node;
        return new(Appearance.SkeletonPath, Skeleton.Source.Sha256,
            Enumerable.Range(0, node.GetBoneCount()).Select(index =>
            {
                var p = node.GetBonePosePosition(index); var q = node.GetBonePoseRotation(index); var s = node.GetBonePoseScale(index);
                return new FalloutActorResidualBonePose(index, node.GetBoneName(index).ToString(),
                    [p.X, p.Y, p.Z], [q.X, q.Y, q.Z, q.W], [s.X, s.Y, s.Z]);
            }).ToArray());
    }

    private void RestoreCompleteIdlePose(FalloutActorResidualPose pose)
    {
        pose.ValidateBinding(Appearance.SkeletonPath, Skeleton.Source.Sha256,
            Enumerable.Range(0, Skeleton.Node.GetBoneCount()).Select(index =>
                (Skeleton.Node.GetBoneName(index).ToString(), FalloutNifTransformComponents.None)).ToArray());
        foreach (var bone in pose.Bones)
        {
            var p = bone.Position!; var q = bone.Rotation!; var s = bone.Scale!;
            Skeleton.Node.SetBonePosePosition(bone.Index, new(p[0], p[1], p[2]));
            Skeleton.Node.SetBonePoseRotation(bone.Index, new(q[0], q[1], q[2], q[3]));
            Skeleton.Node.SetBonePoseScale(bone.Index, new(s[0], s[1], s[2]));
        }
    }

    private FalloutActorIdleRequestsSnapshot? CaptureIdleRequestContinuation()
    {
        if (_currentIdleRequest is null && _queuedIdleRequest is null && _outgoingIdles.Count == 0 && !_idleRetirementPending && _idleReplays.Remaining.Count == 0) return null;
        if (!CanCaptureIdleRequestContinuation()) throw new NotSupportedException("Idle requests still have unowned sequence/object bindings.");
        var pose = CaptureCompleteIdlePose();
        FalloutActorPackageIdleAnimation Animation(IdleRequest request, FalloutIdleAnimationPlayback clock, long revision,
            IReadOnlyList<FalloutActorIdleObjectBinding>? bindings) => new(request.Idle,
                FalloutActorFurnitureContinuation.RecordHash(request.Stack.GetEffective(request.Idle)), request.Resource!, request.Sha256!,
                clock.Capture(), revision, pose.Copy(), bindings?.ToArray());
        FalloutActorIdleRequestSnapshot Pending(IdleRequest request) => new(request.Idle,
            FalloutActorFurnitureContinuation.RecordHash(request.Stack.GetEffective(request.Idle)), request.Owner, request.State,
            request.SequenceState, request.Resource, request.Sha256);
        var current = _currentIdleRequest is { } active ? Pending(active) : null;
        if (current is { State: 2 })
        {
            var counterpart = _idleEaseIn is null ? null : _outgoingIdles.LastOrDefault(outgoing => outgoing.Incoming?.Idle == current.Idle);
            current = current with
            {
                Animation = Animation(_currentIdleRequest!, _idlePlayback!, _idleRevision, _idleObjectBindings),
                Ease = _currentIdleEaseOut?.Capture() ?? _idleEaseIn?.Capture(),
                SnapshotBlend = CaptureIdleSnapshot(),
                TransitionOther = counterpart is null ? null : new(counterpart.Request.Idle,
                    FalloutActorFurnitureContinuation.RecordHash(counterpart.Request.Stack.GetEffective(counterpart.Request.Idle)),
                    counterpart.Request.Resource!, counterpart.Request.Sha256!)
            };
        }
        var outgoing = _outgoingIdles.Select(owner => Pending(owner.Request) with
        {
            Animation = Animation(owner.Request, owner.Clock, owner.Revision, owner.Bindings),
            Ease = owner.Deactivation.Capture(),
            TransitionOther = owner.Incoming
        }).ToArray();
        var saved = new FalloutActorIdleRequestsSnapshot(current, _queuedIdleRequest is { } queued ? Pending(queued) : null,
            outgoing, _idleRetirementPending, pose, IdleTransitionSettings(), _idleReplays.Remaining.Select(value =>
                new FalloutIdleReplayCooldown(value.Key, FalloutActorFurnitureContinuation.RecordHash(_aiStack!.GetEffective(value.Key)), value.Value)).ToArray());
        saved.Validate(_aiStack);
        return saved;
    }

    private FalloutIdleTransition.Metadata ReadIdleTransitionMetadata(FalloutFurnitureClipSnapshot source)
    {
        source.Validate();
        var record = _aiStack!.GetEffective(source.Idle);
        var content = RuntimeLiveContentSource.Current!;
        if (record.Signature != "IDLE" || !FalloutActorFurnitureContinuation.RecordHash(record).Equals(source.IdleSha256, StringComparison.OrdinalIgnoreCase) ||
            !FalloutActorIdleSource.Resolve(_aiStack, record).AnimationPath.Equals(source.Resource, StringComparison.OrdinalIgnoreCase) ||
            !content.TryRead(source.Resource, null, out var bytes, out _))
            throw new InvalidDataException("Idle transition counterpart differs from its winning source.");
        var nif = FalloutNifFile.Read(bytes);
        if (!nif.Sha256.Equals(source.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Idle transition counterpart KF differs from its retained hash.");
        var sequence = nif.Roots.Select(nif.ReadObject).OfType<FalloutNifControllerSequence>().Single();
        var keys = sequence.TextKeys < 0 ? Array.Empty<FalloutNifTextKey>() :
            ((FalloutNifTextKeyExtraData)nif.ReadObject(sequence.TextKeys)).Keys;
        return FalloutIdleGroupTextKeys.Read(sequence.Name, keys).Blend;
    }

    private void RestoreIdleRequestContinuation(FalloutActorIdleRequestsSnapshot saved)
    {
        saved.Validate(_aiStack);
        if (saved.Settings != IdleTransitionSettings()) throw new InvalidDataException("Idle transition settings differ from their retained source.");
        if (_animation is not null || _outgoingIdles.Count != 0) throw new InvalidOperationException("Idle requests already have live pose owners.");
        _idleReplays.Restore(saved.Cooldowns.ToDictionary(value => value.Idle, value => value.Remaining));
        foreach (var outgoing in saved.Outgoing)
        {
            PlayIdle(_aiStack!, outgoing.Idle, outgoing.Owner, outgoing.Animation, restoreCompletePose: true);
            var own = FalloutIdleGroupTextKeys.Read(_animation!.Sequence.Name, _animation.TextKeys).Blend;
            var counterpart = outgoing.TransitionOther is { } other ? ReadIdleTransitionMetadata(other) : (FalloutIdleTransition.Metadata?)null;
            var fade = new FalloutIdleDeactivation(own, saved.Settings, counterpart);
            fade.Restore(outgoing.Ease!);
            _currentIdleRequest!.EaseOut();
            _outgoingIdles.Add(new(_currentIdleRequest, _animation, _idlePlayback!, _idleData!, _idleRevision,
                _animationObjects.ToArray(), _idleObjectBindings, fade, outgoing.TransitionOther));
            _animationObjects.Clear(); _idleObjectBindings = [];
            _animation = null; _idlePlayback = null; _idleData = null; _idleForm = null; _idleOwner = null;
            _currentIdleRequest = null; _idleAnimationResource = null; _idleAnimationSha256 = null;
        }
        if (saved.Current is { State: 2 } current)
        {
            PlayIdle(_aiStack!, current.Idle, current.Owner, current.Animation, restoreCompletePose: true);
            if (current.Ease is { } phase)
            {
                var incoming = FalloutIdleGroupTextKeys.Read(_animation!.Sequence.Name, _animation.TextKeys).Blend;
                if (current.SequenceState == 3)
                {
                    _currentIdleEaseOut = new(incoming, saved.Settings); _currentIdleEaseOut.Restore(phase); _currentIdleRequest!.EaseOut();
                }
                else
                {
                    var previous = ReadIdleTransitionMetadata(current.TransitionOther ??
                        throw new InvalidDataException("Incoming ease lost its outgoing source."));
                    _idleEaseIn = new(previous, saved.Settings, incoming); _idleEaseIn.Restore(phase); _currentIdleRequest!.EaseIn();
                }
            }
            if (current.SnapshotBlend is { } snapshot) RestoreIdleSnapshot(snapshot);
        }
        else if (saved.Current is { State: 3 } terminal)
        {
            var content = RuntimeLiveContentSource.Current!;
            if (!content.TryRead(terminal.Resource!, null, out var bytes, out _)) throw new FileNotFoundException("Terminal idle sequence is absent.", terminal.Resource);
            var source = FalloutNifFile.Read(bytes);
            if (!source.Sha256.Equals(terminal.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Terminal idle sequence hash differs.");
            var sequence = source.Roots.Select(source.ReadObject).OfType<FalloutNifControllerSequence>().Single();
            _currentIdleRequest = new(_aiStack!, terminal.Idle, terminal.Owner);
            _currentIdleRequest.RestoreTerminal(sequence, terminal.Resource!, source.Sha256);
        }
        if (saved.Queued is { } queued) _queuedIdleRequest = new(_aiStack!, queued.Idle, queued.Owner);
        _idleRetirementPending = saved.RetirementPending;
        RestoreCompleteIdlePose(saved.Pose);
    }

    private void RetainIdleRequestContinuation()
    {
        if (_aiReferenceState is { } state && ReferenceEquals(state.CaptureIdleRequests, _idleRequestsCapture))
            state.IdleRequests = CaptureIdleRequestContinuation()?.Copy();
    }
}
