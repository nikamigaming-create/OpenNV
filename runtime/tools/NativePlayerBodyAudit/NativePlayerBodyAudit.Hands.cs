using Godot;
using OpenNV.Runtime.World.Actors;

public partial class NativePlayerBodyAudit
{
    private static void CheckTrackedHands(RuntimeNativePlayerActor actor)
    {
        var skeleton = actor.Skeleton.Node;
        foreach (var left in new[] { true, false })
        {
            var prefix = left ? "Bip01 L " : "Bip01 R ";
            var arm = left ? actor.XrLeftArm : actor.XrRightArm;
            var grip = new Transform3D(new Basis(Vector3.Up, .4f) * new Basis(Vector3.Right, -.6f),
                new(left ? -.22f : .22f, 1.35f, -.3f));
            var digits = new[] { "Thumb1", "Finger1", "Finger2", "Finger3", "Finger4" };
            var ends = digits.Select(name => actor.Skeleton.BoneIndex(prefix + name + "2")).ToArray();
            var joints = Enumerable.Range(0, skeleton.GetBoneCount()).Where(bone =>
                skeleton.GetBoneName(bone).ToString().StartsWith(prefix + "Thumb", StringComparison.Ordinal) ||
                skeleton.GetBoneName(bone).ToString().StartsWith(prefix + "Finger", StringComparison.Ordinal)).ToArray();
            for (var input = 0; input < 3; input++)
            {
                arm.Publish(grip, true, 1, 0, 0, false, false);
                var open = joints.Select(skeleton.GetBonePose).ToArray();
                var start = ends.Select(bone => skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(bone).Origin).ToArray();
                var wrist = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(actor.Skeleton.BoneIndex(prefix + "Hand"));
                arm.Publish(grip, true, 1, input == 0 ? 1 : 0, input == 1 ? 1 : 0, input == 2, false);
                if (!(skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(actor.Skeleton.BoneIndex(prefix + "Hand"))).IsEqualApprox(wrist))
                    throw new InvalidOperationException("Finger input moved the tracked wrist.");
                for (var digit = 0; digit < digits.Length; digit++)
                {
                    var expectedInput = digit == 0 ? 2 : digit == 1 ? 1 : 0;
                    var movement = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(ends[digit]).Origin - start[digit];
                    if (expectedInput != input && movement.Length() > .0001f || expectedInput == input && movement.Length() < .005f)
                        throw new InvalidOperationException($"{actor.Name} {prefix}{digits[digit]} lost independent grip/trigger/thumb input.");
                    // The authored closing animation supplies an independent
                    // palmar direction. A dorsal grip frame passes wrist-distance
                    // checks but bends all four fingers toward the wrong XR side.
                    if (expectedInput == input && digit > 0 && movement.Dot(grip.Basis.X * (left ? 1 : -1)) < .01f)
                        throw new InvalidOperationException($"{actor.Name} {prefix}{digits[digit]} curls away from the OpenXR palm normal.");
                }
                for (var index = 0; index < joints.Length; index++)
                    if (skeleton.GetBonePose(joints[index]).Origin.DistanceTo(open[index].Origin) > .0001f)
                        throw new InvalidOperationException("Finger input stretched a source joint.");
                arm.Publish(grip, true, 1, 0, 0, false, false);
                for (var index = 0; index < joints.Length; index++)
                    if (!skeleton.GetBonePose(joints[index]).IsEqualApprox(open[index]))
                        throw new InvalidOperationException("Finger release did not restore its open pose.");
            }
        }
        actor.Advance(0, Vector3.Zero, true, false);
        GD.Print($"OPENNV_XR_HAND_POSE_PASS actor={actor.Name} palms=source-curl-direction controls=independent-grip-trigger-thumb release=restored visual-acceptance=separate");
    }
}
