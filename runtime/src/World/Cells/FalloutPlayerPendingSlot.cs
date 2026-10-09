using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal enum FalloutPlayerPendingKind { MoveTo, Door, ReferenceTravel, Empty, Opaque }
internal sealed record FalloutPlayerPendingRequest(Guid Identity, long Revision, FalloutPlayerPendingKind Kind,
    FalloutPlayerMove? Move, FalloutFormKey? Door, string Owner, FalloutPlayerTransferPayload? SourcePayload = null);
internal sealed record FalloutPlayerPendingReplacement(long Revision, Guid Released, Guid Stored, string Owner);
internal sealed record FalloutPlayerPendingCompletion(Guid Identity, long Revision, string Owner);
internal sealed record FalloutPlayerPendingSlotSnapshot(long Revision, FalloutPlayerPendingRequest? Pending,
    FalloutPlayerPendingReplacement? Replacement, FalloutPlayerPendingCompletion? Completion,
    string? FailureType, string? Error);

// The original setter owns one payload. Source statements may replace a not-yet
// entered payload, but cannot append a second FIFO destination. Asynchronous
// native construction must retain that exact payload until its caller returns.
internal sealed partial class FalloutPlayerPendingSlot
{
    private long _revision;
    private FalloutPlayerPendingRequest? _pending;
    private FalloutPlayerPendingReplacement? _replacement;
    private FalloutPlayerPendingCompletion? _completion;
    private Guid? _entered;
    private Exception? _failure;
    private string? _restoredFailureType, _restoredError;
    private bool _retired;
    private bool _setting;
    internal event Action<FalloutPlayerPendingReplacement>? Replaced;
    internal FalloutPlayerPendingRequest? Next => _failure is null && _restoredError is null ? _pending : null;
    internal bool Pending => _pending is not null;
    internal string? Error => _failure is { } error ? Message(error) : _restoredError;
    internal string? SaveBlocker => _setting ? "source-Player-pending-setter-entered" : _entered is not null ? "source-Player-pending-consumer-entered" :
        Error is { } error ? "source-Player-pending:" + error :
        _pending is { SourcePayload: null } ? "source-Player-pending-raw-allocation-factory-unowned" :
        _pending?.SourcePayload?.Callback is not null ? "source-Player-pending-callback-cold-factory-unowned" : null;
    internal object State => new
    {
        revision = _revision,
        pending = _pending,
        replacement = _replacement,
        completion = _completion,
        entered = _entered,
        setting = _setting,
        error = Error,
        retired = _retired,
        blocker = SaveBlocker
    };

    internal FalloutPlayerPendingRequest Store(FalloutPlayerPendingKind kind, FalloutPlayerMove? move,
        FalloutFormKey? door, string owner, FalloutPlayerTransferPayload? sourcePayload = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_entered is not null || _setting) throw Retain(new InvalidOperationException("A pending Player payload cannot be replaced while its setter/native child is entered."));
        if (Error is { } error) throw new InvalidOperationException("Player pending setter retains a failed prefix: " + error);
        var candidate = new FalloutPlayerPendingRequest(Guid.NewGuid(), checked(_revision + 1), kind, move, door, owner, sourcePayload);
        ValidateRequest(candidate);
        var previous = _pending;
        var replacement = previous is null ? null : new FalloutPlayerPendingReplacement(candidate.Revision, previous.Identity, candidate.Identity, owner);
        _setting = true;
        try
        {
            if (replacement is not null) Replaced?.Invoke(replacement);
            if (_failure is not null) throw new InvalidOperationException("Player overlap assertion swallowed a setter/capture reentry.", _failure);
        }
        catch (Exception failure) { throw Retain(failure); }
        finally { _setting = false; }
        _revision = candidate.Revision;
        // The old payload contains only borrowed source identities and value
        // fields. Native work belongs to Enter(), which forbids replacement.
        _pending = candidate;
        if (replacement is not null) _replacement = replacement;
        return candidate;
    }
    internal IDisposable Enter(FalloutPlayerPendingRequest request)
    {
        Require(request);
        if (_entered is not null || _setting) throw Retain(new InvalidOperationException("Pending Player consumer reentered its payload/setter."));
        _entered = request.Identity;
        return new Lease(this, request.Identity);
    }
    private sealed class Lease(FalloutPlayerPendingSlot owner, Guid identity) : IDisposable
    {
        public void Dispose()
        {
            if (owner._entered != identity) return;
            owner._entered = null;
        }
    }
    internal void Complete(FalloutPlayerPendingRequest request, string owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner); Require(request);
        if (_entered is not null && _entered != request.Identity)
            throw Retain(new InvalidOperationException("Player null store has another entered native owner."));
        _completion = new(request.Identity, request.Revision, owner);
        _pending = null;
    }
    internal void Fail(FalloutPlayerPendingRequest request, Exception failure)
    {
        if (_pending != request) throw new InvalidOperationException("Player failure lost its still-owned pending payload.");
        Retain(failure);
    }
    internal void Require(FalloutPlayerPendingRequest request)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_pending != request || Error is not null)
            throw new InvalidOperationException("Player destination request is superseded, failed or already consumed.");
    }
    internal FalloutPlayerPendingSlotSnapshot Capture()
    {
        if (_entered is not null || _setting) throw Retain(new NotSupportedException("Entered Player setter/native transfer has no cold completion receipt."));
        var saved = new FalloutPlayerPendingSlotSnapshot(_revision, _pending, _replacement, _completion,
            _failure?.GetType().FullName ?? _restoredFailureType, Error);
        Validate(saved); return saved;
    }
    internal void Restore(FalloutPlayerPendingSlotSnapshot saved)
    {
        Validate(saved); RequireSourcePayloadForCold(saved);
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_revision != 0 || _pending is not null || _failure is not null || _entered is not null)
            throw new InvalidOperationException("Player cold slot cannot replace a constructed/current request.");
        _revision = saved.Revision; _pending = saved.Pending; _replacement = saved.Replacement; _completion = saved.Completion;
        _restoredFailureType = saved.FailureType; _restoredError = saved.Error;
    }
    internal void RequireSettled()
    {
        if (SaveBlocker is { } blocker) throw new NotSupportedException(blocker);
        if (Pending) throw new NotSupportedException("Player request has not reached its actual source null store.");
    }
    internal void Retire()
    {
        if (_entered is not null || _setting) throw Retain(new InvalidOperationException("Player pending setter/native work must retire before its payload owner."));
        // A failed payload is preserved diagnostically. Retirement is not a
        // successful source null store and must not clear the original error.
        _retired = true;
    }
    internal static void Validate(FalloutPlayerPendingSlotSnapshot saved)
    {
        if (saved is null || saved.Revision < 0 || (saved.Error is null) != (saved.FailureType is null) ||
            saved.Error is not null && (string.IsNullOrWhiteSpace(saved.Error) || string.IsNullOrWhiteSpace(saved.FailureType)))
            throw new InvalidDataException("Player pending slot lost its mutation/failure identity.");
        if (saved.Pending is { } pending)
        {
            ValidateRequest(pending);
            if (pending.Revision != saved.Revision) throw new InvalidDataException("Pending Player slot selected an older payload.");
        }
        if (saved.Replacement is { } replacement && (replacement.Revision < 2 || replacement.Revision > saved.Revision ||
            replacement.Released == Guid.Empty || replacement.Stored == Guid.Empty || replacement.Released == replacement.Stored ||
            string.IsNullOrWhiteSpace(replacement.Owner) || saved.Pending is { } current && replacement.Revision == current.Revision && replacement.Stored != current.Identity))
            throw new InvalidDataException("Player overlap assertion discarded the original replacement order.");
        if (saved.Completion is { } completion && (completion.Identity == Guid.Empty || completion.Revision < 1 ||
            completion.Revision > saved.Revision || string.IsNullOrWhiteSpace(completion.Owner) ||
            saved.Pending is { } outstanding && completion.Revision >= outstanding.Revision))
            throw new InvalidDataException("Player pending null store invented a payload completion.");
    }
    private static void ValidateRequest(FalloutPlayerPendingRequest request)
    {
        request.SourcePayload?.RequireRole(request.Kind);
        if (!Enum.IsDefined(request.Kind) || request.Identity == Guid.Empty || request.Revision < 1 || string.IsNullOrWhiteSpace(request.Owner) ||
            request.Kind == FalloutPlayerPendingKind.MoveTo && (request.Move is null || request.Door is not null) ||
            request.Kind == FalloutPlayerPendingKind.Door && (request.Door is null || request.Move is not null) ||
            request.Kind is FalloutPlayerPendingKind.ReferenceTravel or FalloutPlayerPendingKind.Empty or FalloutPlayerPendingKind.Opaque && (request.Move is not null || request.Door is not null))
            throw new InvalidDataException("Player pending request lacks its exact typed source payload.");
        if (request.Move is { } move && (!float.IsFinite(move.X) || !float.IsFinite(move.Y) || !float.IsFinite(move.Z) ||
            move.Source.ObjectId == 0 || move.Destination.ObjectId == 0 || string.IsNullOrWhiteSpace(move.Source.OwnerPlugin) || string.IsNullOrWhiteSpace(move.Destination.OwnerPlugin)))
            throw new InvalidDataException("Player MoveTo source/value fields are not complete.");
        if (request.Door is { } door && (door.ObjectId == 0 || string.IsNullOrWhiteSpace(door.OwnerPlugin)))
            throw new InvalidDataException("Player door request has no source reference identity.");
    }
    private Exception Retain(Exception failure)
    {
        _failure ??= failure; return failure;
    }
    private static string Message(Exception failure) => string.IsNullOrWhiteSpace(failure.Message) ? failure.GetType().Name : failure.Message;
}
