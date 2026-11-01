using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal static class FalloutFollowContinuation
{
    // A completed retained Travel owns an actual stopped process position even
    // after its scene node retires. Unknown/unfinished procedures are not a stop.
    internal static float[]? CompletedTravelTarget(FalloutActorPackageAssignment? assignment, FalloutActorPackageMotion? motion)
    {
        if (assignment is not { Done: true } || motion?.Travel is not { Complete: true }) return null;
        assignment.Validate(); motion.Validate();
        if (assignment.Package != motion.Package || !assignment.Sha256.Equals(motion.PackageSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Retained Follow target has conflicting process and motion ownership.");
        if (motion.Travel.RoutePending || motion.Travel.RouteFailure is not null || motion.Travel.PortalArrival is not null ||
            motion.AccompanyPortal is not null || motion.DialogueCompleted ||
            new object?[] { motion.Patrol, motion.Escort, motion.EditorTravel, motion.Guard, motion.Wander,
                motion.Sandbox, motion.Eat, motion.Sleep, motion.EatPackage }.Any(value => value is not null)) return null;
        return (float[])motion.Position.Clone();
    }

    internal static void BindAccepted(FalloutPluginStack records, FalloutPluginRecord package,
        FalloutActorPackageAssignment assignment, FalloutActorPackageMotion motion, FalloutPackageEvents events)
    {
        ValidateMotion(package, motion);
        if (assignment.Done || assignment.Package != package.FormKey)
            throw new InvalidDataException("Follower continuation has no matching unfinished assignment.");
        assignment.Bind(records, events);
    }

    internal static FalloutFollowPackage ReadResident(FalloutPluginRecord package)
    {
        var declaration = FalloutPackageData.Read(package);
        var source = declaration.Procedure == 7 ? FalloutFollowPackage.ReadAccompany(package) : FalloutFollowPackage.Read(package);
        const uint supported = 0x200 | 0x1000 | 0x2000 | FalloutScriptPackage.HeadTrackingOffFlag | 0x400000 | 0x04000000;
        if ((declaration.Flags & ~supported) != 0 || declaration.SpecificFlags is not (null or 0) ||
            (declaration.Flags & 0x1000) == 0 && declaration.BehaviorFlags != 0 ||
            package.ReadSubrecords().Any(field => field.Signature is "PTD2" or "PKFD") ||
            FalloutScriptPackage.Read(package).Idles.Count != 0)
            throw new NotSupportedException("Resident follower has an unowned behavior, target or waiting-idle declaration.");
        return source;
    }

    internal static void ValidateMotion(FalloutPluginRecord package, FalloutActorPackageMotion motion)
    {
        _ = ReadResident(package);
        motion.Validate();
        if (motion.Package != package.FormKey || !motion.PackageSha256.Equals(Convert.ToHexString(SHA256.HashData(package.ReadData())), StringComparison.OrdinalIgnoreCase) ||
            new object?[] { motion.Patrol, motion.Escort, motion.EditorTravel, motion.Travel, motion.Guard, motion.Wander,
                motion.Sandbox, motion.Eat, motion.Sleep, motion.EatPackage }.Any(value => value is not null) || motion.DialogueCompleted)
            throw new InvalidDataException("Follower motion differs from its source or retains another procedure.");
    }
}
