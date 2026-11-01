using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private static void ExerciseScalarSnapshots()
    {
        var source = FalloutNifFile.Read(MorphFixture(managed: true));
        // The managed fixture's controller weights are NiBlendFloat slots,
        // not standalone float samplers. Bind its sequence to the ordinary
        // source skeleton declaration, as a separate KF binds actor targets.
        var skeleton = new RuntimeNativeNifSkeleton(FalloutNifFile.Read(MorphFixture()), 1);
        try
        {
            skeleton.FloatExtraData.Add("SyntheticRoot", "ArbitraryControl", .6f);
            var sequence = (FalloutNifControllerSequence)source.ReadObject(16);
            var floatLink = new FalloutNifControllerLink("SyntheticRoot", "", "NiFloatExtraDataController", "ArbitraryControl", "", 13, -1, 50);
            sequence = sequence with { ControlledBlocks = [.. sequence.ControlledBlocks, floatLink] };
            var incoming = new RuntimeNativeNifAnimation(source, sequence, skeleton);
            incoming.RequireSnapshotTransitionOver(null);
            if (incoming.UnboundChannels.Count != 0 || incoming.ScalarChannelCount != 4)
                throw new InvalidDataException("Typed synthetic float/morph channels were not bound.");
            incoming.ApplySourceTime(1);
            skeleton.FloatExtraData.Restore("SyntheticRoot", "ArbitraryControl", .6f);
            var snapshot = incoming.CaptureSnapshotScalars();
            var cold = JsonSerializer.Deserialize<FalloutNifSnapshotScalar[]>(JsonSerializer.Serialize(snapshot))!;
            var pose = new FalloutNifAnimationSample[skeleton.Node.GetBoneCount()];
            skeleton.ResetMorphPublication();
            incoming.ApplySnapshotTransition(0, pose, 1, 0, scalars: cold);
            Check(.6f, .75f, 1.2f);
            skeleton.ResetMorphPublication();
            incoming.ApplySnapshotTransition(0, pose, .75f, .25f, scalars: cold);
            Check(.45f, .5625f, .95f);
            skeleton.ResetMorphPublication();
            incoming.ApplySnapshotTransition(0, pose, 0, 1, scalars: cold);
            Check(0, 0, .2f);
            Reject(() => incoming.ApplySnapshotTransition(0, pose, 1, 0));
            Reject(() => incoming.ValidateSnapshotScalars([.. cold.Skip(1), cold[1]]));
            Reject(() => incoming.ValidateSnapshotScalars(cold.Select((value, index) => index == 0 ? value with { Priority = 99 } : value).ToArray()));
            Reject(() => incoming.ValidateSnapshotScalars(cold.Select((value, index) => index == 0 ? value with
            { Target = value.Target with { Name = "AnotherTarget" } } : value).ToArray()));
            Reject(() => incoming.ValidateSnapshotScalars(cold.Select(value => value with { Value = float.NaN }).ToArray()));
            Reject(() => incoming.ValidateSnapshotScalars(cold.Select(value => value.Target.Name == "Base" ? value with { Value = 0 } : value).ToArray()));
            var unknown = new RuntimeNativeNifAnimation(source, sequence with
            { ControlledBlocks = [.. sequence.ControlledBlocks, floatLink with { ControllerType = "UnknownController" }] },
                skeleton, link => time => { });
            Reject(() => unknown.RequireSnapshotTransitionOver(null));
            var background = new RuntimeNativeNifAnimation(source, sequence with { Weight = 1 }, skeleton);
            var weighted = new RuntimeNativeNifAnimation(source, sequence with { Weight = 2 }, skeleton);
            RuntimeNativeNifAnimation.ApplyWeightedLayers([(background, 0, 1), (weighted, 1, .5f)]);
            Check(.375f, .375f, .7f);
            var lower = new RuntimeNativeNifAnimation(source, sequence with
            { ControlledBlocks = sequence.ControlledBlocks.Select(link => link with { Priority = 1 }).ToArray() }, skeleton);
            RuntimeNativeNifAnimation.ApplyWeightedLayers([(lower, 0, 1), (incoming, 1, 1)]);
            // Morph priorities remain zero in the incoming source, below the
            // replacement's one; the float source retains priority fifty.
            Check(.75f, 0, .2f);
            GD.Print("OPENNV_SCALAR_SNAPSHOT_PASS currentConstants=true heldIncoming=true partialBlend=true coldConstants=true typedIdentityAndPriority=true relativeTargetZero=true ordinaryWeightsAndEase=true priorities=true missingAndUnknownRefused=true");
            ExerciseVisibilitySnapshots(source, skeleton, sequence);

            void Check(float extra, float a, float b)
            {
                Near(skeleton.FloatExtraData.Get("SyntheticRoot", "ArbitraryControl"), extra);
                var morphs = skeleton.NamedMorphWeights.ToDictionary(value => value.Name, value => value.Weight);
                Near(morphs["Base"], 1); Near(morphs["ExpressionA"], a); Near(morphs["ExpressionB"], b);
            }
        }
        finally { skeleton.Node.Free(); }
    }

    private static void ExerciseOwnedScalarSnapshots(string game)
    {
        RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        FalloutNifFile Read(string path) => content.TryRead(path, null, out var bytes, out _)
            ? FalloutNifFile.Read(bytes) : throw new FileNotFoundException(path);
        var skeleton = new RuntimeNativeNifSkeleton(Read("meshes/Characters/_Male/skeleton.nif"), .0142875f);
        try
        {
            var source = Read("meshes/Characters/_Male/IdleAnims/SpecialIdle_BitingNails.kf");
            var sequence = source.Blocks.Where(block => block.TypeName == "NiControllerSequence")
                .Select(block => (FalloutNifControllerSequence)source.ReadObject(block.Index)).Single();
            var animation = new RuntimeNativeNifAnimation(source, sequence, skeleton);
            animation.RequireSnapshotTransitionOver(null);
            if (animation.UnboundChannels.Count != 0 || animation.ScalarChannelCount != 9)
                throw new InvalidDataException("Reached owned idle lost its nine typed scalar bindings.");
            animation.ApplySourceTime(sequence.StopTime * .4f);
            skeleton.FloatExtraData.Restore("Bip01 Head", "HeadTrack", .63f);
            var captured = animation.CaptureSnapshotScalars();
            var cold = JsonSerializer.Deserialize<FalloutNifSnapshotScalar[]>(JsonSerializer.Serialize(captured))!;
            var pose = Enumerable.Range(0, skeleton.Node.GetBoneCount()).Select(index =>
            {
                var p = skeleton.Node.GetBonePosePosition(index) / skeleton.UnitsToMetres;
                var q = skeleton.Node.GetBonePoseRotation(index); var scale = skeleton.Node.GetBonePoseScale(index).X;
                return new FalloutNifAnimationSample(new(p.X, -p.Z, p.Y), new(q.W, q.X, -q.Z, q.Y), scale);
            }).ToArray();
            skeleton.ResetMorphPublication();
            animation.ApplySnapshotTransition(sequence.StartTime, pose, 1, 0, scalars: cold);
            foreach (var current in animation.CaptureSnapshotScalars()) Near(current.Value, cold.Single(value => value.Target == current.Target).Value);
            var held = new RuntimeNativeNifAnimation(source, sequence, skeleton);
            held.ValidateSnapshotScalars(cold);
            held.ApplySourceTime(sequence.StartTime);
            var incoming = held.CaptureSnapshotScalars();
            skeleton.ResetMorphPublication();
            animation.ApplySnapshotTransition(sequence.StartTime, pose, .75f, .25f, scalars: cold);
            var denominator = .75f + .25f * sequence.Weight;
            foreach (var current in animation.CaptureSnapshotScalars())
                Near(current.Value, (.75f * cold.Single(value => value.Target == current.Target).Value +
                    .25f * sequence.Weight * incoming.Single(value => value.Target == current.Target).Value) / denominator);
            GD.Print($"OPENNV_OWNED_SCALAR_SNAPSHOT_PASS source={source.Sha256} sequence={sequence.Name} scalars={cold.Length} currentHeadTrack=true morphConstants=true coldConstants=true partialBlend=true sourcePhase=held actualSelectedIdentity=inferred gameplay=unverified");
            ExerciseOwnedAnimationObjectSnapshot(content, skeleton);
        }
        finally { skeleton.Node.Free(); }
    }

    private sealed record ConstantVisibility(bool Value) : IFalloutNifScalarSampler
    {
        public float Sample(float sourceTime) => FalloutNifBoolAnimation.Encode(Value);
    }

    private static void ExerciseVisibilitySnapshots(FalloutNifFile source, RuntimeNativeNifSkeleton skeleton,
        FalloutNifControllerSequence sequence)
    {
        var visible = true;
        var target = new FalloutNifScalarTarget(FalloutNifScalarKind.Visibility, "ExternalObject", "NiVisController");
        var link = new FalloutNifControllerLink(target.Node, "", target.Name, "", "", -1, -1, 50);
        RuntimeNativeNifAnimation Clip(bool value, byte priority = 50, float weight = 1) => new(source,
            sequence with { ControlledBlocks = [link with { Priority = priority }], Weight = weight }, skeleton,
            bindScalarChannel: current => new(target, current.Priority, new ConstantVisibility(value),
                () => FalloutNifBoolAnimation.Encode(visible), value => visible = FalloutNifBoolAnimation.BlendedValue(value)));
        var incoming = Clip(false);
        incoming.RequireSnapshotTransitionOver(null);
        var cold = JsonSerializer.Deserialize<FalloutNifSnapshotScalar[]>(JsonSerializer.Serialize(incoming.CaptureSnapshotScalars()))!;
        var pose = new FalloutNifAnimationSample[skeleton.Node.GetBoneCount()];
        incoming.ApplySnapshotTransition(0, pose, 1, 0, scalars: cold);
        Require(visible, "Held visibility resampled the incoming false flag.");
        incoming.ApplySnapshotTransition(0, pose, .75f, .25f, scalars: cold);
        Require(visible, "Visibility ignored its snapshot majority.");
        incoming.ApplySnapshotTransition(0, pose, .5f, .5f, scalars: cold);
        Require(visible, "The native equal-weight visibility vote must be true.");
        incoming.ApplySnapshotTransition(0, pose, .25f, .75f, scalars: cold);
        Require(!visible, "Visibility ignored its incoming majority.");
        RuntimeNativeNifAnimation.ApplyWeightedLayers([(Clip(true), 0, .25f), (Clip(false), 0, .75f)]);
        Require(!visible, "Ordinary visibility layers ignored authored contribution.");
        RuntimeNativeNifAnimation.ApplyWeightedLayers([(Clip(true), 0, .5f), (Clip(false), 0, .5f)]);
        Require(visible, "Ordinary visibility layers lost the equal-weight vote.");
        RuntimeNativeNifAnimation.ApplyWeightedLayers([(Clip(true, 1), 0, 1), (Clip(false), 0, 1)]);
        Require(!visible, "Visibility ignored source channel priority.");
        visible = true;
        RuntimeNativeNifAnimation.ApplyWeightedLayers([(Clip(false), 0, 0)]);
        Require(!visible, "The native single attached interpolator fast path lost its source value.");
        var bone = skeleton.BoneIndex("SyntheticRoot");
        var originalPosition = skeleton.Node.GetBonePosePosition(bone);
        var retainedPosition = new Vector3(1, 2, 3);
        skeleton.Node.SetBonePosePosition(bone, retainedPosition);
        visible = true;
        RuntimeNativeNifAnimation.ApplyWeightedLayers([(Clip(false, weight: 0), 0, 0)]);
        Require(!visible && skeleton.Node.GetBonePosePosition(bone) == retainedPosition,
            "Single attached reduction lost zero-weight scalar semantics or reset an uncovered bone.");
        skeleton.Node.SetBonePosePosition(bone, originalPosition);
        Reject(() => RuntimeNativeNifAnimation.ApplyWeightedLayers([(Clip(false), float.NaN, 1)]));
        Reject(() => RuntimeNativeNifAnimation.ApplyWeightedLayers([(Clip(false), 0, float.NaN)]));
        Reject(() => incoming.ValidateSnapshotScalars([]));
        Reject(() => incoming.ValidateSnapshotScalars([cold[0], cold[0]]));
        Reject(() => (cold[0] with { Value = .5f }).Validate());
        GD.Print("OPENNV_VISIBILITY_SNAPSHOT_PASS held=true majority=true equalHalfVisible=true ordinaryLayers=true priority=true singleAttachedFastPath=true coldConstants=true malformedRefused=true");
    }

    private static void ExerciseOwnedAnimationObjectSnapshot(RuntimeLiveContentSource content, RuntimeNativeNifSkeleton skeleton)
    {
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var idle = FalloutActorIdleSource.Resolve(records, records.GetEffective(records.RuntimeFormKey(0x059444)));
        FalloutNifFile Read(string path) => content.TryRead(path, null, out var bytes, out _)
            ? FalloutNifFile.Read(bytes) : throw new FileNotFoundException(path);
        var source = Read(idle.AnimationPath);
        var sequence = source.Roots.Select(source.ReadObject).OfType<FalloutNifControllerSequence>().Single();
        var roots = new List<Node3D>();
        try
        {
            var controlled = sequence.ControlledBlocks.Select(link => link.NodeName).ToHashSet(StringComparer.Ordinal);
            var visibility = sequence.ControlledBlocks.Single(link => link.ControllerType == "NiVisController");
            RuntimeNativeNifAnimation Bind(out Node3D visibilityTarget)
            {
                foreach (var attachment in idle.Objects)
                    roots.Add(NativeNifMeshBuilder.AddActorPart(Read(attachment.ModelPath), skeleton,
                        externalTransformTargets: controlled).Root);
                var nodes = roots.SelectMany(root => root.FindChildren("*", "", true, false).OfType<Node3D>().Prepend(root)).ToArray();
                var transforms = new List<(FalloutNifControllerLink Link, Node3D Node)>();
                Node3D? Target(FalloutNifControllerLink link) => nodes.SingleOrDefault(node => node.Name.ToString() == link.NodeName);
                var animation = new RuntimeNativeNifAnimation(source, sequence, skeleton, link =>
                {
                    if (link.ControllerType != "NiTransformController" || Target(link) is not { } node) return null;
                    transforms.Add((link, node)); return _ => { }; // Replaced by the production typed transform binding below.
                }, externalObjectTargets: nodes.Select(node => node.Name.ToString()).ToHashSet(StringComparer.Ordinal),
                    bindScalarChannel: link => link.ControllerType == "NiVisController" && Target(link) is { } node
                        ? RuntimeNifScalarBinding.Visibility(source, link, () => node.Visible, value => node.Visible = value) : null);
                foreach (var target in transforms) animation.RegisterSnapshotTransformTarget(target.Link, target.Node);
                visibilityTarget = Target(visibility) ?? throw new InvalidDataException("Owned animation-object visibility target is absent.");
                return animation;
            }
            var animation = Bind(out var node);
            var baseSource = Read("meshes/Characters/_Male/Locomotion/mtidle.kf");
            var background = new RuntimeNativeNifAnimation(baseSource,
                baseSource.Roots.Select(baseSource.ReadObject).OfType<FalloutNifControllerSequence>().Single(), skeleton);
            animation.RequireSnapshotTransitionOver(background);
            var sampler = new FalloutNifBoolAnimation(source, visibility.Interpolator);
            var authored = sampler.Sample(sequence.StartTime);
            foreach (var root in roots) root.Visible = false;
            node.Visible = true;
            Require(animation.CaptureSnapshotScalars().Single(value => value.Target.Kind == FalloutNifScalarKind.Visibility).Value == 1,
                "A hidden parent replaced the animation object's current local visibility flag.");
            foreach (var root in roots) root.Visible = true;
            node.Visible = !authored;
            var cold = JsonSerializer.Deserialize<FalloutNifSnapshotScalar[]>(JsonSerializer.Serialize(animation.CaptureSnapshotScalars()))!;
            Require(cold.Count(value => value.Target.Kind == FalloutNifScalarKind.Visibility) == 1,
                "Owned handoff lost its typed local visibility constant.");
            var pose = new FalloutNifAnimationSample[skeleton.Node.GetBoneCount()];
            var external = animation.CaptureSnapshotTransforms();
            animation.ApplySnapshotTransition(sequence.StartTime, pose, 1, 0, external: external, scalars: cold);
            Require(node.Visible != authored, "Owned held transition exposed the incoming visibility.");
            animation.ApplySnapshotTransition(sequence.StartTime, pose, .5f, .5f, external: external, scalars: cold);
            Require(node.Visible, "Owned equal-weight visibility vote differed from native.");
            animation.ApplySnapshotTransition(sequence.StartTime, pose, 0, 1, external: external, scalars: cold);
            Require(node.Visible == authored, "Owned completed transition did not publish its authored visibility.");
            foreach (var time in sampler.BoundaryTimes.Append(sequence.StartTime).Append(sequence.StopTime))
            {
                animation.ApplySourceTime(time);
                Require(node.Visible == sampler.Sample(time), "Owned ordinary visibility missed a source key boundary.");
            }
            foreach (var root in roots) root.Free();
            roots.Clear();
            var restored = Bind(out var restoredNode);
            restoredNode.Visible = authored;
            restored.RequireSnapshotTransitionOver(background);
            restored.ValidateSnapshotScalars(cold);
            restored.ApplySnapshotTransition(sequence.StartTime, pose, 1, 0, external: external, scalars: cold);
            Require(restoredNode.Visible != authored, "Cold animation-object rebind resampled instead of restoring its constant.");
            restored.ApplySnapshotTransition(sequence.StartTime, pose, 0, 1, external: external, scalars: cold);
            Require(restoredNode.Visible == authored, "Cold animation-object rebind lost its incoming visibility.");
            GD.Print($"OPENNV_OWNED_OBJECT_VISIBILITY_PASS idle={idle.Form} source={source.Sha256} objects={roots.Count} currentFlag=true hiddenParentLocalFlag=true held=true equalHalfVisible=true completed=true sourceBoundaries=true coldConstants=true coldExternalRebind=true gameplay=separate");
        }
        finally { foreach (var root in roots) root.Free(); }
    }

    private static void Near(float actual, float expected)
    {
        if (!float.IsFinite(actual) || MathF.Abs(actual - expected) > .00001f)
            throw new InvalidDataException($"Scalar publication differs: actual={actual:R} expected={expected:R}.");
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Invalid typed scalar snapshot was admitted.");
    }
}
