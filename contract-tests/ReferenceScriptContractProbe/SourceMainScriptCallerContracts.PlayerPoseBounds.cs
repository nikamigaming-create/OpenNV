using OpenNV.Runtime.Formats.Gamebryo;

internal static partial class SourceMainScriptCallerContracts
{
    private static void RunPlayerPoseBounds()
    {
        var identity = new FalloutNifTransform(new(0, 0, 0), [1, 0, 0, 0, 1, 0, 0, 0, 1], 1);
        var first = new FalloutNifSkinBoneData(identity, new(0, 0, 0), 2, []);
        var second = first with { BoundingSphereRadius = 1 };
        var reads = 0;
        var result = FalloutNifPoseBounds.Skin([first, second], ["first", "second"], name =>
        {
            ++reads;
            return name == "first" ? identity : identity with { Translation = new(8, 0, 0), Scale = 2 };
        });
        Require(reads == 2 && result.Radius == 6 && result.Center == new System.Numerics.Vector3(4, 0, 0),
            "Posed source bounds skipped a bone, borrowed bind pose, or applied inverse binds twice.");
        var rotated = identity with { RotationRowMajor = [0, -1, 0, 1, 0, 0, 0, 0, 1], Scale = 2 };
        result = FalloutNifPoseBounds.Skin([first with { BoundingSphereCenter = new(1, 0, 0) }], ["first"], _ => rotated);
        Require(result.Center == new System.Numerics.Vector3(0, 2, 0) && result.Radius == 4,
            "Current skin bound lost its rotated center or uniform pose scale.");
        result = FalloutNifPoseBounds.Skin([first, first], ["first", "first"], _ => identity);
        Require(result.Radius == 2 && result.Center == System.Numerics.Vector3.Zero,
            "Coincident source spheres introduced a division by zero.");
        Reject(() => FalloutNifPoseBounds.Skin([], [], _ => identity));
        Reject(() => FalloutNifPoseBounds.Skin([first], [], _ => identity));
        Reject(() => FalloutNifPoseBounds.Skin([first], [""], _ => identity));
        Reject(() => FalloutNifPoseBounds.Skin([first with { BoundingSphereRadius = -1 }], ["first"], _ => identity));
        Reject(() => FalloutNifPoseBounds.Skin([first with { BoundingSphereRadius = float.NaN }], ["first"], _ => identity));
        Reject(() => FalloutNifPoseBounds.Skin([first], ["first"], _ => identity with { Translation = new(float.NaN, 0, 0) }));
        Reject(() => FalloutNifPoseBounds.Skin([first], ["first"], _ => throw new NotSupportedException("Actual current bone is missing.")));
        Console.WriteLine("OPENNV_PLAYER_POSE_BOUNDS_PASS currentBoneSpheres=true rotation=true scale=true coincident=true malformedRefused=true missingCurrentBoneRefused=true");
    }
}
