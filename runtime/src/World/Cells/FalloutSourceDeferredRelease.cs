using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutSourceDeferredRelease
{
    internal const string Schema = "opennv-source-deferred-release/v1";
    private readonly object _gate = new();
    private readonly FalloutSourceDeferredReleaseDeclaration _source;
    private readonly FalloutMainPlayerCellSource _player;
    private readonly string _stack;
    private readonly Guid _process, _constructor = Guid.NewGuid();
    private readonly Func<FalloutActorProcessFact<uint>> _mainWord;
    private readonly Action _requireNativeMainClosed;
    private sealed class Queue(FalloutDeferredReleaseRole role)
    {
        internal readonly FalloutDeferredReleaseRole Role = role;
        internal readonly List<Loan?> Slots = [];
        internal uint Capacity;
    }
    private sealed class Loan(Guid identity, IFalloutSourceDeferredObject owner)
    {
        internal readonly IFalloutSourceDeferredObject Owner = owner;
        internal FalloutDeferredReleaseObjectReceipt Receipt = new(identity, owner.Source,
            FalloutDeferredReleaseStage.Created, 0, false, false, false, false, null, null);
    }
    private readonly Dictionary<FalloutDeferredReleaseRole, Queue> _queues = [];
    private readonly Dictionary<Guid, Loan> _retained = [];
    private readonly Dictionary<Guid, IFalloutSourceDeferredObject> _objects = [];
    private readonly Stack<Loan> _enteredObjects = [];
    private int _ownerThread, _depth, _validating;
    private bool _flushing, _retired;
    private byte _enabled;
    private long _sequence, _forbiddenReentry;
    private string? _failureType, _error;
    private FalloutDeferredReleaseFlush? _last;
    private FalloutActorProcessRuntimeHandoff? _cold;

    internal FalloutSourceDeferredRelease(FalloutMainPlayerCellSource player, string stack, Guid process,
        Func<FalloutActorProcessFact<uint>> mainWord, Action requireNativeMainClosed,
        FalloutSourceDeferredReleaseSnapshot? saved = null)
    {
        player.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        ArgumentNullException.ThrowIfNull(mainWord); ArgumentNullException.ThrowIfNull(requireNativeMainClosed);
        if (process == Guid.Empty) throw new InvalidDataException("Deferred-release constructor has no actual source Main process.");
        _player = player; _source = FalloutSourceDeferredReleaseDeclaration.Read(player); _stack = stack;
        _process = process; _mainWord = mainWord; _requireNativeMainClosed = requireNativeMainClosed;
        // These are the original module's initialized enable cell and empty
        // release containers, not inferred absence from a loaded scene.
        _enabled = _source.InitialEnabled;
        foreach (var role in Enum.GetValues<FalloutDeferredReleaseRole>()) _queues.Add(role, new(role));
        if (saved is not null) Restore(saved);
    }

    internal void RequireBinding(FalloutMainPlayerCellSource player, string stack, Guid process)
    {
        lock (_gate)
        {
            Require(allowFailure: true);
            if (_player != player || _stack != stack || _process != process)
                throw new InvalidDataException("Deferred-release binding changed its actual source constructor/process.");
        }
    }

    private void Require(bool allowFailure = false, bool allowObjectChild = false)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_validating != 0 || !allowObjectChild && (_flushing || _enteredObjects.Count != 0))
        {
            _forbiddenReentry = checked(_forbiddenReentry + 1);
            throw new InvalidOperationException("Deferred-release capture, flush or retirement reentered an actual live child.");
        }
        if (!allowFailure && _error is not null)
            throw new InvalidOperationException("Source deferred-release owner retains its failed prefix: " + _error);
    }

    private void EnterOwner()
    {
        var thread = System.Environment.CurrentManagedThreadId;
        if (_depth != 0 && _ownerThread != thread)
            throw new InvalidOperationException("Recursive deferred-release owner lock changed its actual calling thread.");
        if (_depth == 0) _ownerThread = thread;
        _depth = checked(_depth + 1);
    }
    private void ExitOwner()
    {
        if (_depth < 1 || _ownerThread != System.Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Deferred-release unlock lost its current recursive owner.");
        if (--_depth == 0) _ownerThread = 0;
    }

    private void RequireObject(IFalloutSourceDeferredObject value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var source = value.Source;
        if (source is null || source.Identity == Guid.Empty || source.Process != _process ||
            source.Contract != _source.Contract || !Enum.IsDefined(source.Role) || string.IsNullOrWhiteSpace(source.Owner) ||
            _objects.TryGetValue(source.Identity, out var previous) && !ReferenceEquals(previous, value))
            throw new InvalidDataException("Deferred object lost its exact selected factory/allocation lifetime.");
        var before = _forbiddenReentry;
        _validating++;
        try { value.RequireCurrentFactory(); }
        finally { _validating--; }
        if (_forbiddenReentry != before)
            throw new InvalidOperationException("Deferred object factory swallowed a forbidden manager reentry.");
        if (value.Source != source)
            throw new InvalidDataException("Deferred object changed source identity during its actual factory validation.");
    }

    private Loan NewLoan(IFalloutSourceDeferredObject value)
    {
        var loan = new Loan(Guid.NewGuid(), value);
        loan.Receipt = loan.Receipt with { Changed = Next() };
        _objects.TryAdd(value.Source.Identity, value);
        // Registration precedes each opaque child. A callback can throw after
        // its real native increment/destructor committed; the loan stays owned.
        _retained.Add(loan.Receipt.Loan, loan); return loan;
    }

    private void Child(Loan loan, FalloutDeferredReleaseStage entered, FalloutDeferredReleaseStage returned, Action action,
        bool knownReferenceCleanup = false)
    {
        if (loan.Receipt.Error is not null && !knownReferenceCleanup)
            throw new NotSupportedException("Failed deferred child needs its real committed-ownership receipt before retry.");
        var before = _forbiddenReentry;
        var originalFailure = loan.Receipt.Error;
        loan.Receipt = loan.Receipt with { Stage = entered, Changed = Next() };
        _enteredObjects.Push(loan);
        try
        {
            action();
            if (_forbiddenReentry != before)
                throw new InvalidOperationException("Deferred child swallowed a forbidden current-owner reentry.");
            if (loan.Owner.Source != loan.Receipt.Source)
                throw new InvalidDataException("Deferred child changed the retained source allocation identity.");
            loan.Receipt = loan.Receipt with { Stage = originalFailure is null ? returned : FalloutDeferredReleaseStage.Failed, Changed = Next() };
        }
        catch (Exception failure)
        {
            loan.Receipt = loan.Receipt with
            {
                Stage = FalloutDeferredReleaseStage.Failed,
                Changed = Next(),
                FailureType = loan.Receipt.FailureType ?? Type(failure),
                Error = loan.Receipt.Error ?? Message(failure)
            };
            Retain(failure); throw;
        }
        finally
        {
            if (_enteredObjects.Pop() != loan)
                throw new InvalidOperationException("Deferred callback lost its actual recursive child order.");
        }
    }

    private bool DeferRawOrForm()
    {
        if (_enabled == 0) return false;
        // This is Main's actual forced-processing word. The separate manager
        // read in Player's post-null tail is not this producer.
        _validating++;
        try { return (_mainWord().Require() & 2u) == 0; }
        finally { _validating--; }
    }

    private long Next() => _sequence = checked(_sequence + 1);
    private void Retain(Exception failure) { _failureType ??= Type(failure); _error ??= Message(failure); }
    private static string Type(Exception failure) => failure.GetType().FullName ?? failure.GetType().Name;
    private static string Message(Exception failure) => string.IsNullOrWhiteSpace(failure.Message) ? failure.GetType().Name : failure.Message;
}
