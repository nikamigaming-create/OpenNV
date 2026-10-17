using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Physics;

internal sealed record FalloutControllerModelWorldRead(FalloutReferenceModelDataInvocation Load, Guid Controller,
    Guid Proxy, Guid Phantom, ulong NativePhantom, string? World, long Revision);

internal sealed partial class FalloutEarlyController
{
    internal FalloutControllerModelWorldRead ReadModelLoadWorld(FalloutReferenceModelDataInvocation load,
        Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        RequireCurrent();
        if (_readingPostProcess) { ++_postProcessReadFault; throw new InvalidOperationException("Controller world query reentered its actual owner."); }
        var fault = _postProcessReadFault; var revision = _sequence; _readingPostProcess = true;
        try
        {
            Check();
            var proxy = Proxy.Capture(); var phantom = Phantom.Capture();
            if (proxy.Phantom != phantom.Identity || phantom.NativeId == 0)
                throw new InvalidDataException("Controller world getter lost its same actual proxy/phantom object.");
            var result = new FalloutControllerModelWorldRead(load, Identity, proxy.Identity, phantom.Identity, phantom.NativeId, phantom.CurrentWorld, revision);
            Check(); return result;
        }
        finally { _readingPostProcess = false; }

        void Check()
        {
            RequireCurrent(); requireActual(load);
            if (_postProcessReadFault != fault || _sequence != revision || load.Actor != Lineage.Actor || load.CommonProcess != Lineage.CommonProcess ||
                load.Epoch != Lineage.Epoch || load.ReferenceConstructor != Lineage.Constructor || load.ReferenceProcess != Lineage.ThreadProcess ||
                load.ThreadId != Lineage.Thread || load.ThreadId != Environment.CurrentManagedThreadId || load.Ingress != FalloutReferenceModelDataIngress.ThreeDLoad)
                throw new InvalidOperationException("Controller world query crossed its actual Player/Common/current-thread owner.");
        }
    }
}
