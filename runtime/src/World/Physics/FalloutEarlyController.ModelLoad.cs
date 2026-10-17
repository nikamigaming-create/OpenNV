using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Physics;

internal sealed record FalloutControllerModelFieldRead(FalloutReferenceModelDataInvocation Load,
    Guid Controller, FalloutControllerPostProcessField Field, Guid? Value, long Revision);

internal sealed partial class FalloutEarlyController
{
    internal FalloutControllerModelFieldRead ReadModelLoadField(FalloutReferenceModelDataInvocation load,
        FalloutControllerPostProcessField field, Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        RequireCurrent(); ArgumentNullException.ThrowIfNull(requireActual);
        if (_readingPostProcess) { ++_postProcessReadFault; throw new InvalidOperationException("Controller model field query reentered its actual owner."); }
        var fault = _postProcessReadFault; var sequence = _sequence; _readingPostProcess = true;
        try
        {
            Check();
            var (value, boundary) = field switch
            {
                FalloutControllerPostProcessField.FirstBody => (_postProcessFields.FirstBody, _postProcessFields.FirstBodyBoundary),
                FalloutControllerPostProcessField.SecondBody => (_postProcessFields.SecondBody, _postProcessFields.SecondBodyBoundary),
                _ => throw new InvalidDataException("Model helper selected no original controller body field.")
            };
            if (boundary is not null) throw new NotSupportedException(boundary);
            Check(); return new(load, Identity, field, value, sequence);
        }
        finally { _readingPostProcess = false; }

        void Check()
        {
            RequireCurrent(); requireActual(load);
            if (_postProcessReadFault != fault || _sequence != sequence || load.Call == Guid.Empty ||
                load.Actor != Lineage.Actor || load.CommonProcess != Lineage.CommonProcess || load.Epoch != Lineage.Epoch ||
                load.ReferenceConstructor != Lineage.Constructor || load.ReferenceProcess != Lineage.ThreadProcess ||
                load.ThreadId != Lineage.Thread || load.ThreadId != Environment.CurrentManagedThreadId ||
                load.Ingress != FalloutReferenceModelDataIngress.ThreeDLoad)
                throw new InvalidOperationException("Controller model field query crossed its actual Player/Common/thread owner or a writer.");
        }
    }
}
