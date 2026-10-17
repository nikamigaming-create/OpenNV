using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutReferenceControllerExtraRead(Guid Initializer, Guid Fields,
    byte Subtype, Guid? Extra, long Revision);

internal sealed partial class FalloutReferenceScriptExtraInitializer
{
    // Constructor absence belongs to this same Extra-list lifetime. An entered
    // copy, load or Script callback invalidates it even when that writer failed.
    internal FalloutReferenceControllerExtraRead ReadControllerConstructorExtra(byte subtype,
        FalloutReferenceModelDataInvocation load, Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        var fault = _fault; Check();
        if (subtype is not (FalloutPlayerModelLoadContinuationSource.ControllerAuxiliaryExtra or FalloutPlayerModelLoadContinuationSource.ControllerModelExtra or
            FalloutPlayerModelLoadContinuationSource.ControllerModelOverrideExtra or 0x6d))
            throw new NotSupportedException("source-controller-Extra-subtype-writer-contract-unbound:" + subtype);
        if (_fields.ScriptInitializationWriterBoundary is { } fieldWriter) throw new NotSupportedException(fieldWriter);
        if (!_returnedInitialization || _records.TryGetWinner(Actor.Reference, out _) ||
            _calls.Any(call => !call.Returned || call.InvalidatedWriter is
                FalloutReferenceScriptExtraWriter.ExtraCopyOrLoad or FalloutReferenceScriptExtraWriter.ScriptCallback))
            throw new NotSupportedException("source-reference-controller-Extra-current-membership-writer-unbound:" + subtype);
        Check(); return new(Factory, FieldsFactory, subtype, subtype == 0x6d ? _animationNoteExtra?.Identity : null, _sequence);

        void Check()
        {
            if (_busy) { ++_fault; throw new InvalidOperationException("Controller Extra query reentered the source list initializer."); }
            requireActual(load); _records.RequireLiveLoadReceipt(_records.LoadReceipt);
            if (_fault != fault || load.Actor != Actor || load.CommonProcess != Process || load.Epoch != Epoch ||
                load.ReferenceConstructor != Constructor || load.ReferenceProcess != _threadContext.Process ||
                load.Ingress != FalloutReferenceModelDataIngress.ThreeDLoad || load.ThreadId != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("Controller Extra read changed its actual reference/list/Player loader.");
        }
    }
}
