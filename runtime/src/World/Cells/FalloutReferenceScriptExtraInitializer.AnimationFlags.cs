using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutReferenceAnimationFlagWrite(FalloutReferenceModelDataInvocation Invocation,
    Guid Call, string Source, string LoadedSource, Guid Factory, Guid FieldsFactory, long Entered, long Changed,
    uint Before, uint After, bool Enabled, FalloutSourceAnimationContextConstruction? Construction,
    bool Returned, string? Failure);

internal sealed partial class FalloutReferenceScriptExtraInitializer
{
    private readonly List<FalloutReferenceAnimationFlagWrite> _animationFlagWrites = [];
    internal FalloutReferenceFlagRead ReadLoadedAnimationFlags(FalloutReferenceModelDataInvocation invocation,
        Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        if (_busy) { ++_fault; throw new InvalidOperationException("Animation flag read reentered its actual source word."); }
        requireActual(invocation);
        if (invocation.Actor != Actor || invocation.CommonProcess != Process || invocation.ReferenceProcess != _threadContext.Process ||
            invocation.ReferenceConstructor != Constructor || invocation.Epoch != Epoch || invocation.ThreadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Animation flag read changed its actual reference/source constructor.");
        if (_calls.LastOrDefault(IsReferenceFlagWriter)?.WriterBoundary is { } boundary) throw new NotSupportedException(boundary);
        return new(Source.Identity, _fields.Stack, Process, Factory, FieldsFactory, Constructor, Actor, Epoch, _sequence, _referenceFlags);
    }
    internal FalloutReferenceAnimationFlagWrite SetLoadedAnimationFlag(FalloutLoadedAnimationSource source,
        FalloutReferenceModelDataInvocation invocation, bool enabled, FalloutSourceAnimationContextState? context,
        Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        if (_busy) { ++_fault; throw new InvalidOperationException("Reference animation flag reentered its actual flag owner."); }
        source.Validate(); requireActual(invocation);
        if (source.Tail.Movement.Mover.Controller.Fields.Identity != Source.Fields.Identity ||
            invocation.Actor != Actor || invocation.CommonProcess != Process || invocation.ReferenceProcess != _threadContext.Process ||
            invocation.ReferenceConstructor != Constructor || invocation.Epoch != Epoch ||
            invocation.Ingress != FalloutReferenceModelDataIngress.ThreeDLoad || invocation.ThreadId != Environment.CurrentManagedThreadId ||
            enabled && (context is null || context.Construction.Role != FalloutLoadedAnimationRole.Common ||
                !context.Construction.Returned || context.Construction.Invocation != invocation))
            throw new InvalidOperationException("Reference animation flag lost its real loader, constructor or shared current word.");
        if (_calls.LastOrDefault(IsReferenceFlagWriter)?.WriterBoundary is { } boundary) throw new NotSupportedException(boundary);
        var call = Guid.NewGuid(); _invocations.Add(call); var entered = ++_sequence; var before = _referenceFlags;
        var fault = _fault; string? failure = null; _busy = true;
        try
        {
            requireActual(invocation);
            _referenceFlags = enabled ? before | FalloutLoadedAnimationSource.AnimationReferenceFlag : before & ~FalloutLoadedAnimationSource.AnimationReferenceFlag;
            requireActual(invocation);
            if (_fault != fault) throw new InvalidOperationException("Animation flag setter swallowed an actual owner reentry.");
        }
        catch (Exception error) { failure = error.Message; _failure ??= failure; throw; }
        finally
        {
            _animationFlagWrites.Add(new(invocation, call, Source.Identity, source.Identity, Factory, FieldsFactory,
                entered, ++_sequence, before, _referenceFlags, enabled, context?.Construction, failure is null, failure));
            _busy = false;
        }
        return _animationFlagWrites[^1];
    }
}
