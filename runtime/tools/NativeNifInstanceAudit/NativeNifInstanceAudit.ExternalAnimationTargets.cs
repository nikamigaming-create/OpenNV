using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private void ExerciseExternalAnimationTargets()
    {
        var source = FalloutNifFile.Read(Synthetic(false));
        var skeleton = new RuntimeNativeNifSkeleton(source, 1);
        try
        {
            AddChild(skeleton.Node);
            FalloutNifControllerLink Link(string name) => new(name, "", "NiTransformController", "", "", 2, -1, 0);
            var sequence = new FalloutNifControllerSequence(source.Blocks[0], "Aim",
                [Link("ArbitraryNode"), Link("OtherModelPart")], 1, -1, 0, 1, 0, 1, -1, "ArbitraryNode", -1, null);
            var selected = new RuntimeNativeNifAnimation(source, sequence, skeleton,
                externalObjectTargets: new HashSet<string>(StringComparer.Ordinal));
            if (selected.UnboundChannels.Count != 0 || selected.AbsentSourceTargets.Count != 1 || selected.TransformChannelCount != 1)
                throw new InvalidDataException("Absent model target changed the real skeleton binding.");
            selected.ApplySourceTime(0);
            var before = skeleton.Node.GetBonePoseRotation(0);
            selected.ApplySourceTime(.5f);
            if (before.IsEqualApprox(skeleton.Node.GetBonePoseRotation(0)))
                throw new InvalidDataException("Absent model target prevented the body animation from advancing.");
            var declared = new RuntimeNativeNifAnimation(source, sequence, skeleton,
                externalObjectTargets: new HashSet<string>(StringComparer.Ordinal) { "OtherModelPart" });
            if (declared.UnboundChannels.Count != 1 || declared.AbsentSourceTargets.Count != 0)
                throw new InvalidDataException("Declared external target without an owner was silently discarded.");
            try { declared.ApplySourceTime(.5f); throw new InvalidDataException("Incomplete animation was applied."); }
            catch (NotSupportedException) { }
            ExerciseAccumulationLayers();
            GD.Print("OPENNV_EXTERNAL_ANIMATION_TARGET_PASS absentAlternative=true bodyAdvances=true declaredUnboundRefused=true");
        }
        finally { skeleton.Node.Free(); }
    }
    private void ExerciseAccumulationLayers()
    {
        var movingSource = FalloutNifFile.Read(Synthetic(false, 10));
        var idleSource = FalloutNifFile.Read(Synthetic(false, 0));
        var skeleton = new RuntimeNativeNifSkeleton(movingSource, 1);
        try
        {
            AddChild(skeleton.Node);
            RuntimeNativeNifAnimation Layer(FalloutNifFile source, byte priority) => new(source,
                new(source.Blocks[0], "SourceRoot", [new("ArbitraryNode", "", "NiTransformController", "", "", 2, -1, priority)],
                    1, -1, 0, 1, 0, 1, -1, "ArbitraryNode", -1, null), skeleton, externalAccumulationRoot: true);
            var movement = Layer(movingSource, 30); var idle = Layer(idleSource, 32);
            Vector3 Step(float movementTime, float idleTime, float ease) => RuntimeNativeNifAnimation.ConsumeAccumulationLayers(
                [(movement, movementTime, 1), (idle, idleTime, ease)], []);
            movement.RestoreAccumulationTime(0); idle.RestoreAccumulationTime(0);
            var suppressed = Step(.2f, .2f, 1);
            if (!suppressed.IsZeroApprox() || movement.AccumulationTime != 0 || idle.AccumulationTime != .2f)
                throw new InvalidDataException("Higher priority authored stationary root did not own actual accumulation.");
            var faded = Step(.3f, .3f, .5f);
            if (!faded.IsEqualApprox(new Vector3(0, 0, -2)))
                throw new InvalidDataException("Accumulation did not use actual source priorities/ease and last contributing sample.");
            var cold = Layer(movingSource, 30); cold.RestoreAccumulationTime(movement.AccumulationTime);
            var coldMotion = RuntimeNativeNifAnimation.ConsumeAccumulationLayers([(cold, .4f, 1)], []);
            if (!coldMotion.IsEqualApprox(new Vector3(0, 0, -1)))
                throw new InvalidDataException("Cold accumulation repeated or lost its consumed source phase.");
            cold.RestoreAccumulationTime(.9f);
            var wrapped = RuntimeNativeNifAnimation.ConsumeAccumulationLayers([(cold, .1f, 1)], []);
            if (!wrapped.IsEqualApprox(new Vector3(0, 0, -2)) || !skeleton.Node.GetBonePosePosition(0).IsZeroApprox())
                throw new InvalidDataException("Accumulation wrap or external visual-root removal failed.");
            GD.Print("OPENNV_ACCUMULATION_LAYER_PASS sourcePriorities=true zeroMotionOwned=true easing=true coldCursor=true wrap=true externalRoot=true native=false");
        }
        finally { skeleton.Node.Free(); }
    }

}
