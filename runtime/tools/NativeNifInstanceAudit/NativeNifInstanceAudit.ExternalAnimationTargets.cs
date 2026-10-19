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
            GD.Print("OPENNV_EXTERNAL_ANIMATION_TARGET_PASS absentAlternative=true bodyAdvances=true declaredUnboundRefused=true");
        }
        finally { skeleton.Node.Free(); }
    }
}
