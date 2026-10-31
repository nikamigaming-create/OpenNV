using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

// A package's animation clock and observed pose survive presentation eviction.
// This is separate from both ambient idles and combat engagement state.
internal sealed record FalloutActorPackageMotion(FalloutFormKey Package, string PackageSha256,
    string Animation, string AnimationSha256, double Seconds, bool StartPending,
    float[] Position, float[] Rotation, FalloutPatrolProgress? Patrol = null, FalloutEscortProgress? Escort = null,
    FalloutEditorTravelProgress? EditorTravel = null, bool DialogueCompleted = false, FalloutTravelProgress? Travel = null,
    FalloutTravelProgress? Guard = null, FalloutAccompanyPortalArrival? AccompanyPortal = null,
    FalloutWanderProgress? Wander = null, FalloutSandboxActivityProgress? Sandbox = null, FalloutActorEatProgress? Eat = null,
    FalloutActorSleepProgress? Sleep = null, FalloutActorEatPackageProgress? EatPackage = null,
    FalloutActorPathTurn? PathTurn = null, FalloutActorPackageAnimationLayers? AnimationLayers = null)
{
    // Captures and restores must not share mutable route, pose or activity
    // buffers with the live owner or a caller retaining the snapshot.
    internal FalloutActorPackageMotion Copy() => this with
    {
        Position = (float[])Position.Clone(),
        AnimationLayers = AnimationLayers?.Copy(),
        Rotation = (float[])Rotation.Clone(),
        EditorTravel = EditorTravel is { } editor ? editor with { Location = (float[])editor.Location.Clone() } : null,
        Travel = Travel is { } travel ? CopyTravel(travel) : null,
        Guard = Guard is { } guard ? CopyTravel(guard) : null,
        Wander = Wander is { } wander ? wander with { Destination = (float[])wander.Destination.Clone() } : null,
        Sandbox = Sandbox is { } sandbox ? sandbox with { Preferences = (byte[])sandbox.Preferences.Clone() } : null,
        Eat = Eat is { } eat ? eat with { Objects = eat.Objects?.ToArray() } : null
    };

    private static FalloutTravelProgress CopyTravel(FalloutTravelProgress travel) => travel with
    {
        Location = (float[])travel.Location.Clone(),
        RouteTarget = travel.RouteTarget is null ? null : (float[])travel.RouteTarget.Clone(),
        RouteWaypoints = travel.RouteWaypoints?.Select(point => (float[])point.Clone()).ToArray()
    };

    internal void Validate()
    {
        PathTurn?.Validate();
        AnimationLayers?.Validate();
        Patrol?.Validate();
        Escort?.Validate();
        EditorTravel?.Validate();
        Travel?.Validate();
        Guard?.Validate();
        AccompanyPortal?.Validate();
        Wander?.Validate();
        Sandbox?.Validate();
        Eat?.Validate();
        Sleep?.Validate();
        EatPackage?.Validate();
        if (Sleep is not null && (new object?[] { Patrol, Escort, EditorTravel, Guard, AccompanyPortal, Wander, Sandbox, Eat, EatPackage }
            .Any(value => value is not null) || DialogueCompleted || Sleep.Furniture && Travel is not null))
            throw new InvalidDataException("Sleep continuation conflicts with another procedure owner.");
        if (EatPackage is not null && (new object?[] { Patrol, Escort, EditorTravel, Guard, AccompanyPortal, Sandbox }
            .Any(value => value is not null) || DialogueCompleted || Eat is not null && (Travel is not null || Wander is not null) ||
            EatPackage.Furniture && (Travel is not null || Wander is not null)))
            throw new InvalidDataException("Standalone Eat conflicts with another procedure owner.");
        if (Eat is not null && (EatPackage is null && (Sandbox?.Activity != FalloutSandboxActivity.Eat || Sandbox.Target != Eat.WorldReference) || Wander is not null))
            throw new InvalidDataException("Eat continuation conflicts with its parent procedure.");
        if (Sandbox is not null && (Sandbox.Activity is not (FalloutSandboxActivity.Wander or FalloutSandboxActivity.Dialogue or FalloutSandboxActivity.Eat or FalloutSandboxActivity.Furniture or FalloutSandboxActivity.Sleep or FalloutSandboxActivity.IdleMarker) ||
            Wander is not null && Sandbox.Activity != FalloutSandboxActivity.Wander ||
            new object?[] { Patrol, Escort, EditorTravel, Travel, Guard, AccompanyPortal }.Any(value => value is not null) || DialogueCompleted))
            throw new InvalidDataException("Saved Sandbox activity conflicts with its procedure continuation.");
        if (new object?[] { Patrol, Escort, EditorTravel, Travel, Guard, AccompanyPortal, Wander }.Count(value => value is not null) + (DialogueCompleted ? 1 : 0) > 1)
            throw new InvalidDataException("Saved package has conflicting procedure progress.");
        static bool Hash(string hash) => hash is { Length: 64 } && hash.All(Uri.IsHexDigit);
        if (string.IsNullOrWhiteSpace(Package.OwnerPlugin) || Package.ObjectId == 0 || !Hash(PackageSha256) ||
            string.IsNullOrWhiteSpace(Animation) || !Hash(AnimationSha256) || !double.IsFinite(Seconds) || Seconds < 0 ||
            StartPending && Seconds != 0 || Position is not { Length: 3 } || Rotation is not { Length: 4 } ||
            Position.Concat(Rotation).Any(value => !float.IsFinite(value)) ||
            Math.Abs(Rotation.Sum(value => value * value) - 1) > .001)
            throw new InvalidDataException("Saved actor package motion is invalid.");
    }
}
