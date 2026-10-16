using System.Runtime.ExceptionServices;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Cells;

internal enum FalloutReferenceModelDataIngress { ReferenceConstructor, ThreeDLoad, ModelSetter, UnownedWriter, ColdBinding }
internal sealed record FalloutReferenceModelDataInvocation(Guid Call, FalloutCombatActorIdentity Actor,
    Guid ReferenceProcess, Guid ReferenceConstructor, Guid CommonProcess, long Epoch, int ThreadId,
    FalloutReferenceModelDataIngress Ingress);
internal sealed record FalloutReferenceModelDataValues(uint? FirstWord, uint? SecondWord,
    uint? WaterBits, uint? ScalarBits, uint? ThirdWord, FalloutSourceSceneNode? Model, FalloutSourceSceneNode? OtherModel);
internal sealed record FalloutReferenceModelDataStore(long Changed, string Field, uint? Bits = null,
    Guid? Data = null, FalloutSourceSceneNode? Node = null);
internal sealed record FalloutReferenceModelDataCall(FalloutReferenceModelDataInvocation Invocation,
    Guid Factory, long Entered, long Changed, Guid? BeforeData, Guid? AfterData,
    IReadOnlyList<FalloutReferenceModelDataStore> Stores, bool Returned, string? Failure,
    FalloutSourceSceneNode? SuppliedModel = null);
internal sealed record FalloutReferenceModelDataPublication(Guid Process, Guid CommonProcess, Guid Factory, Guid Constructor,
    long Published, Guid? PreviousProcess = null, Guid? PreviousFactory = null,
    Guid? PreviousData = null, Guid? CurrentData = null);
internal sealed record FalloutReferenceModelDataRead(string Source, string Stack, Guid Process, Guid Factory,
    Guid Constructor, FalloutCombatActorIdentity Actor, long Epoch, long Sequence, Guid? Data,
    FalloutReferenceModelDataValues? Fields);
internal sealed record FalloutReferenceModelColdBinding(FalloutReferenceModelDataInvocation Invocation,
    Guid Factory, long Entered, long Changed, Guid Data, FalloutSourceSceneNode PreviousModel,
    FalloutActorLogicalSceneSnapshot PreviousScene, FalloutSourceSceneNode SuppliedModel,
    Guid? LogicalLifetime, long? LogicalSequence, bool ModelStored, bool ModelRetained, bool Returned, string? Failure);
internal sealed record FalloutReferenceModelDataSnapshot(FalloutPlayerMoverDeltaSource Source, string Stack,
    Guid Process, Guid CommonProcess, Guid Factory, Guid Constructor, FalloutCombatActorIdentity Actor, long Epoch, long Sequence,
    Guid? Data, FalloutReferenceModelDataValues? Fields,
    IReadOnlyList<FalloutReferenceModelDataPublication> Publications,
    IReadOnlyList<FalloutReferenceModelDataCall> Calls, string? WriterBoundary, string? Failure, bool Retired,
    Guid? PreviousData = null, FalloutActorLogicalSceneSnapshot? ModelScene = null,
    IReadOnlyList<FalloutReferenceModelColdBinding>? ColdBindings = null,
    bool RetirementStarted = false, string? RetirementFailure = null);

// A lease retains the same returned source graph and native factory. Merely
// naming a native root is insufficient; every read/retain rechecks its owner.
internal enum FalloutReferenceModelBindingRetirementState
{ Retained, SourceReleaseEntered, SourceReleaseFailed, BindingPending, BindingRetirementEntered, Retired }

internal sealed class FalloutReferenceModelDataNode
{
    private readonly FalloutActorLogicalScene _logical;
    private readonly Action _requireActual;
    private readonly Action _requireRetirement;
    private readonly Action _released;
    private readonly Action? _retireBinding;
    private int _references;
    private bool _bindingRetired, _releaseEntered, _bindingRetirementStarted, _bindingRetirementEntered;
    private ExceptionDispatchInfo? _sourceReleaseFailure;
    internal FalloutReferenceModelBindingRetirementState RetirementState =>
        _releaseEntered ? FalloutReferenceModelBindingRetirementState.SourceReleaseEntered :
        _bindingRetirementEntered ? FalloutReferenceModelBindingRetirementState.BindingRetirementEntered :
        _sourceReleaseFailure is not null ? FalloutReferenceModelBindingRetirementState.SourceReleaseFailed :
        _bindingRetired ? FalloutReferenceModelBindingRetirementState.Retired :
        _references == 0 ? FalloutReferenceModelBindingRetirementState.BindingPending : FalloutReferenceModelBindingRetirementState.Retained;
    internal FalloutSourceSceneNode Node { get; }
    internal FalloutActorLogicalScene LogicalScene { get { RequireCurrent(); return _logical; } }
    internal string? SaveBlocker => _releaseEntered ? "source-reference-model-release-entered" :
        _sourceReleaseFailure is not null ? "source-reference-model-release-failed" :
        _bindingRetired ? "source-reference-model-binding-retired" :
        _bindingRetirementStarted ? "source-reference-model-binding-retirement-incomplete" : _logical.SaveBlocker;
    internal FalloutActorLogicalSceneSnapshot CaptureScene() => _logical.Capture();
    internal FalloutReferenceModelDataNode(FalloutSourceSceneNode node, FalloutActorLogicalScene logical,
        Action requireActual, Action released, Action? retireBinding = null, Action? requireRetirement = null)
    {
        ArgumentNullException.ThrowIfNull(logical); ArgumentNullException.ThrowIfNull(requireActual); ArgumentNullException.ThrowIfNull(released);
        if (node.Process == Guid.Empty || node.Factory == Guid.Empty || node.NativeRoot == 0 || node.Factory != logical.Factory)
            throw new InvalidDataException("Reference model publication changed its actual returned graph/native factory.");
        Node = node; _logical = logical; _requireActual = requireActual; _released = released;
        _requireRetirement = requireRetirement ?? requireActual;
        _retireBinding = retireBinding; RequireCurrent();
    }
    internal void RequireCurrent()
    {
        if (_bindingRetired || _bindingRetirementStarted || _releaseEntered || _sourceReleaseFailure is not null)
            throw new InvalidOperationException("Reference model binding entered retirement or retained a failed source release.");
        _requireActual();
        if (_logical.SaveBlocker is { } failure) throw new NotSupportedException(failure);
        if (_logical.Factory != Node.Factory) throw new InvalidOperationException("Reference model changed its retained source graph.");
    }
    internal void Retain(Action committed)
    { RequireCurrent(); _references = checked(_references + 1); committed(); RequireCurrent(); }
    internal void Release(Action committed)
    {
        if (_releaseEntered || _bindingRetirementEntered)
            throw new InvalidOperationException("Reference model release reentered its actual lifecycle callback.");
        _releaseEntered = true;
        try
        {
            _requireRetirement(); _sourceReleaseFailure?.Throw();
            if (_bindingRetired || _logical.Factory != Node.Factory || _references < 1)
                throw new InvalidOperationException("Reference model released a foreign or retired binding.");
            // The source callback runs once, before its retained count commits.
            // An exception has an unknown source prefix and cannot be replayed.
            if (_references == 1)
                try { _released(); }
                catch (Exception error) { _sourceReleaseFailure = ExceptionDispatchInfo.Capture(error); throw; }
            --_references;
            if (_references == 0) _bindingRetirementStarted = true;
            committed();
        }
        finally { _releaseEntered = false; }
        if (_references == 0) RetireBinding();
    }
    internal void RetireBinding()
    {
        if (_releaseEntered || _bindingRetirementEntered)
            throw new InvalidOperationException("Reference model binding retirement reentered its actual lifecycle callback.");
        if (_bindingRetired) return;
        if (_references != 0) throw new InvalidOperationException("Reference model binding retains source references.");
        // Binding ownership is independent of source reference count. A failed
        // final callback retains this zero-reference binding for a real retry.
        _bindingRetirementStarted = _bindingRetirementEntered = true;
        try { _retireBinding?.Invoke(); _bindingRetired = true; }
        finally { _bindingRetirementEntered = false; }
    }
}
