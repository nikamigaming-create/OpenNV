using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class RuntimeNativePlayerPackage
{
    private readonly Guid _idlePackageFactory = Guid.NewGuid();

    internal void RequireIdlePackageWorld(FalloutReferenceWorld world)
    {
        RequireHealthy();
        if (!ReferenceEquals(world, _world))
            throw new InvalidOperationException("Player idle package query cannot borrow another actual reference world.");
    }

    internal FalloutIdlePackageRead ReadIdlePackage(FalloutReferenceWorld world, FalloutProcessSceneQuery process)
    {
        RequireIdlePackageWorld(world);
        if (!process.Actor.EnginePlayer || process.Actor.Reference != _stack.RuntimeFormKey(0x14) ||
            process.Level != FalloutDetectionProcessLevel.High || process.Epoch < 1 ||
            process.Process == Guid.Empty || process.GameplayOwnership == Guid.Empty ||
            _executingResults.Count != 0)
            throw new InvalidOperationException("Player idle package query lost its actual High Player scope or entered a result callback.");
        // Outgoing assignment remains current during POCA; a pending next
        // package is not the source current-package slot before commitment.
        FalloutIdleSelectedObject? result = null;
        if (_package is { } package)
        {
            if (_packageHash is null)
                throw new InvalidOperationException("Player idle query entered an incomplete package assignment/result callback.");
            result = FalloutIdlePackageSelection.From(package, new(_idlePackageFactory, process.Process,
                process.GameplayOwnership, process.Actor.Reference, process.Stack, package.Form,
                _packageHash, _assignmentRevision));
        }
        return new(_idlePackageFactory, process.Process, process.GameplayOwnership,
            process.Actor.Reference, process.Stack, _assignmentRevision, result);
    }
}
