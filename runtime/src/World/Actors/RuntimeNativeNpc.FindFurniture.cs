using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private const string FindFurnitureCaptureBlocker = "Find Furniture needs its search, reservation and physical animation continuation.";
    private FalloutFindFurniturePackage? _findFurniture;
    private double _furnitureSearchRemaining;

    private void BeginFindFurniture(FalloutPluginRecord package)
    {
        _findFurniture = FalloutFindFurniturePackage.Read(package);
        _ = _findFurniture.Candidates(_aiStack!, _aiWorld!, Appearance.Reference!.Value, _aiCell!);
        _aiPackage = package;
        _furnitureSearchRemaining = 0;
        _packageEvents!.Change(_packageIdleSource);
        _aiReferenceState!.ProcedureCaptureBlocker = FindFurnitureCaptureBlocker;
        GD.Print($"OPENNV_NATIVE_FIND_FURNITURE_READY reference={Appearance.Reference} package={package.FormKey} location={_findFurniture.Location} radius={_findFurniture.Radius}");
    }

    private void AdvanceFindFurniture(double delta)
    {
        if (_findFurniture is null || _seat is not null || _aiError is not null || Combat is null ||
            !Combat.PackageMovementReady || _conversationTarget is not null) return;
        _furnitureSearchRemaining -= delta;
        if (_furnitureSearchRemaining > 0) return;
        _furnitureSearchRemaining = .5;
        try
        {
            var candidates = _findFurniture.Candidates(_aiStack!, _aiWorld!, Appearance.Reference!.Value, _aiCell!)
                .OrderBy(reference => _referenceTransform!(reference).Origin.DistanceSquaredTo(Position));
            foreach (var reference in candidates)
                if (TryBeginFurniturePackage(_aiPackage!, reference, _aiStack!.GetEffective(reference.Base), initializing: false)) return;
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or IOException)
        {
            _aiError = error.Message; _failedPackage = _aiPackage?.FormKey;
            BlockSelectionCapture($"Find Furniture continuation is unbound: {error.Message}");
            GD.PushError($"OPENNV_NATIVE_FIND_FURNITURE_DIVERGENCE reference={Appearance.Reference}: {error.Message}");
        }
    }
}
