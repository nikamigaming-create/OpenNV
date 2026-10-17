using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutPlayerModelPipBoyRead(string Source, Guid Process, Guid Constructor, long Revision,
    Guid? Manager, byte? FirstFlag, bool Refresh);

internal sealed partial class FalloutStandaloneInterfaceState
{
    internal FalloutPlayerModelPipBoyRead ReadPlayerModelLoadRefresh(FalloutReferenceModelDataInvocation load,
        Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        RequireCurrent(native: false); requireActual(load);
        if (load.ThreadId != Environment.CurrentManagedThreadId || load.Actor.ReferenceSha256 != Source.Main.EngineSha256)
            throw new InvalidOperationException("Player loader Pip-Boy read changed its actual source or current thread.");
        if (_manager is { } manager && (manager.Retired is not null || !_native.TryGetValue(manager.Identity, out var living) || !ReadNative(living)))
            throw new InvalidOperationException("Player loader Pip-Boy gate lost its actual retained native manager.");
        if ((_manager is null) != (_managerFields is null))
            throw new InvalidDataException("Player loader Pip-Boy gate lost its independent manager constructor fields.");
        var flag = _managerFields?.FirstFlag;
        var result = new FalloutPlayerModelPipBoyRead(Source.Identity, _process, _constructor, _sequence, _manager?.Identity,
            flag, _manager is not null && flag != 0);
        RequireCurrent(native: false); requireActual(load); return result;
    }
}
