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
        }
        finally { skeleton.Node.Free(); }
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
