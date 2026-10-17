using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceModelDataState
{
    internal FalloutReferenceModelDataNode? ReadForLight(FalloutProcessSceneQuery query)
    {
        RequireIdle(); _requireLifetime(); _model?.RequireCurrent();
        if (query.Actor != Actor || query.Process != CommonProcess || query.Epoch != Epoch ||
            query.Stack != Stack || _values?.Model != _model?.Node)
            throw new InvalidOperationException("Light bounds crossed the actual current Reference model field.");
        if (SaveBlocker is { } boundary) throw new NotSupportedException(boundary);
        return _model;
    }
    internal FalloutReferenceModelDataNode? ReadForPlayerLoad(FalloutReferenceModelDataInvocation invocation,
        Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        ArgumentNullException.ThrowIfNull(requireActual); RequireIdle(); var fault = _fault;
        Check(); _model?.RequireCurrent(); Check();
        if (SaveBlocker is { } boundary) throw new NotSupportedException(boundary);
        if (_calls.Count == 0 || _values?.Model != _model?.Node)
            throw new InvalidOperationException("Player loader lost the actual current Reference model field.");
        return _model;

        void Check()
        {
            requireActual(invocation); _requireLifetime();
            if (_fault != fault || invocation.ReferenceProcess != Process || invocation.ReferenceConstructor != Constructor ||
                invocation.Actor != Actor || invocation.CommonProcess != CommonProcess || invocation.Epoch != Epoch ||
                invocation.ThreadId != _thread || invocation.Ingress != FalloutReferenceModelDataIngress.ThreeDLoad)
                throw new InvalidOperationException("Reference model borrow changed its actual enclosing Player loader.");
        }
    }

    internal void RequireActorMovementData(FalloutReferenceModelDataRead borrowed,
        FalloutPlayerLateFrameInvocation invocation, Action<FalloutPlayerLateFrameInvocation> requireActual)
    {
        var current = ReadForMover(invocation, requireActual);
        if (borrowed != current || current.Data is null)
            throw new InvalidDataException("Actor movement crossed a source data/model writer or borrowed another current reference object.");
    }
}
