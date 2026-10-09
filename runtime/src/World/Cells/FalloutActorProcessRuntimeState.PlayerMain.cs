using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessRuntimeState
{
    internal const string MainPlayerCellSchema = "opennv-source-Main-Player-CELL/v3";
    private FalloutMainPlayerCellSource? _mainPlayerCellSource;
    private FalloutPlayerPendingSlot? _mainPlayerPending;
    private IFalloutMainPlayerCellConsumers? _mainPlayerCellConsumers;
    private FalloutMainPlayerCellCall? _mainPlayerCellLast;
    private FalloutMainPlayerCellInvocation? _mainPlayerCellChild;
    private FalloutMainScriptInvocation? _mainPlayerCellActiveMain;
    private Guid _mainPlayerCellLease;
    private int? _mainPlayerCellThread;
    private string? _mainPlayerCellDelivery;
    private long _mainPlayerCellCalls, _mainPlayerCellReentry;
    private FalloutActorProcessRuntimeHandoff? _mainPlayerCellCold;
    private bool _mainPlayerCellRetired;
    private bool? _mainPlayerWorldBracket, _mainPlayerMovementBracket;
    private FalloutMainPlayerRootBinding? _mainPlayerRootBinding, _mainPlayerPreviousRoot;
    internal bool MainPlayerCellConstructed => _mainPlayerCellSource is not null;
    internal string? MainPlayerCellSaveBlocker => _mainPlayerCellActiveMain is not null ? "source-Main-Player-CELL-child-entered" :
        _mainPlayerCellLast is { Disposition: FalloutMainPlayerCellDisposition.Failed } call ?
            "source-Main-Player-CELL:" + call.Children.LastOrDefault()?.Step + ":" + call.Error :
            MainPlayerColdRootToRebind is not null ? "source-Player-cold-root-awaiting-actual-native-publication" : _mainPlayerPending?.SaveBlocker ?? _mainPlayerPendingConsumers?.SaveBlocker;
    internal object? MainPlayerCellState => _mainPlayerCellSource is null ? null : new
    {
        source = _mainPlayerCellSource,
        process = _process,
        calls = _mainPlayerCellCalls,
        last = _mainPlayerCellLast,
        pending = _mainPlayerPending!.State,
        pendingConsumers = _mainPlayerPendingConsumers!.State,
        deliveryBound = _mainPlayerCellLease != Guid.Empty,
        delivery = _mainPlayerCellDelivery,
        retired = _mainPlayerCellRetired,
        cold = _mainPlayerCellCold,
        worldBracket = _mainPlayerWorldBracket,
        playerBracket = _mainPlayerMovementBracket,
        rootBinding = _mainPlayerRootBinding,
        previousRoot = _mainPlayerPreviousRoot,
        blocker = MainPlayerCellSaveBlocker,
    };
    internal void ConstructMainPlayerCell(FalloutMainPlayerCellSource source, FalloutPlayerPendingSlot pending,
        FalloutMainPlayerCellSnapshot? saved)
    {
        RequireNotBusy(); source.Validate(); ArgumentNullException.ThrowIfNull(pending);
        if (_mainPlayerCellSource is not null || source.Main != MainScriptCallerSource() || pending.Pending || pending.Error is not null)
            throw new InvalidOperationException("Player child must construct on its actual Main and initially null pending slot once.");
        if (saved is not null)
        {
            ValidateMainPlayerCell(saved);
            if (saved.Source != source || saved.Stack != _stack || saved.CapturedProcess == _process || saved.Changed > _sequence)
                throw new InvalidDataException("Player/CELL cold construction changed selected source or captured Main process.");
            pending.Restore(saved.Pending); _mainPlayerCellCalls = saved.Calls; _mainPlayerCellLast = saved.LastCall;
            _mainPlayerWorldBracket = saved.WorldBracket; _mainPlayerMovementBracket = saved.PlayerBracket;
            _mainPlayerPreviousRoot = saved.RootBinding;
            _mainPlayerCellCold = new(saved.CapturedProcess, _process, Next());
        }
        _mainPlayerCellSource = source; _mainPlayerPending = pending;
        ConstructMainPlayerPendingConsumers(source, saved?.PendingConsumers);
        // Native callbacks, addresses, tasks, scene references, queries and
        // delivered frame numbers are never promoted from the old process.
    }
    internal IDisposable BindMainPlayerCell(IFalloutMainPlayerCellConsumers consumers, string delivery)
    {
        RequireNotBusy(); ArgumentNullException.ThrowIfNull(consumers); ArgumentException.ThrowIfNullOrWhiteSpace(delivery);
        if (_mainPlayerCellSource is null || _mainPlayerCellRetired || _mainPlayerCellLease != Guid.Empty ||
            _mainPlayerCellActiveMain is not null || consumers.Source != _mainPlayerCellSource || string.IsNullOrWhiteSpace(consumers.Owner))
            throw new InvalidOperationException("Player/CELL native children must bind the same actual selected source lifetime once.");
        var lease = _mainPlayerCellLease = Guid.NewGuid(); _mainPlayerCellConsumers = consumers;
        _mainPlayerCellThread = Environment.CurrentManagedThreadId; _mainPlayerCellDelivery = delivery;
        return new MainPlayerCellLease(this, lease);
    }
    private sealed class MainPlayerCellLease(FalloutActorProcessRuntimeState owner, Guid lease) : IDisposable
    {
        public void Dispose()
        {
            if (owner._mainPlayerCellLease != lease) return;
            if (owner._mainPlayerCellActiveMain is not null)
            {
                owner._mainPlayerCellReentry = checked(owner._mainPlayerCellReentry + 1);
                throw new InvalidOperationException("Player native lifetime cannot retire an entered Main child.");
            }
            owner._mainPlayerCellLease = Guid.Empty; owner._mainPlayerCellConsumers = null;
            owner._mainPlayerCellThread = null; owner._mainPlayerCellDelivery = null;
        }
    }
    internal async Task ExecuteMainPlayerCell(FalloutMainScriptInvocation main)
    {
        RequireNotBusy(); main.Require(FalloutMainScriptCallerStep.Player);
        if (_mainPlayerCellActiveMain is not null)
        {
            _mainPlayerCellReentry = checked(_mainPlayerCellReentry + 1);
            throw new InvalidOperationException("Original Player child cannot be entered twice in one retained Main invocation.");
        }
        if (_mainPlayerCellSource is null || _mainPlayerPending is null || _mainPlayerCellRetired ||
            _mainPlayerCellLease == Guid.Empty || _mainPlayerCellConsumers is not { } consumers ||
            consumers.Source != _mainPlayerCellSource || _mainPlayerCellThread != Environment.CurrentManagedThreadId)
            throw new NotSupportedException("source-Main-Player-CELL-actual-native-child-lifetime-unbound");
        if (_mainPlayerCellLast is { Disposition: FalloutMainPlayerCellDisposition.Failed } failed)
            throw new InvalidOperationException("Player/CELL refuses replay of its retained entered prefix: " + failed.Error);
        var entered = Next(); _mainPlayerCellCalls = checked(_mainPlayerCellCalls + 1);
        _mainPlayerCellActiveMain = main;
        _mainPlayerCellLast = new(main.Identity, main.Ordinal, _process, entered, entered,
            FalloutMainPlayerCellDisposition.Entered, null, null, null, null, null, [], null, null);
        var faults = _mainPlayerCellReentry;
        FalloutPlayerPendingRequest? request = null;
        IDisposable? pendingLease = null;
        try
        {
            Enter(FalloutMainPlayerCellStep.PendingRead, child =>
            {
                child.Require(FalloutMainPlayerCellStep.PendingRead);
                if (_mainPlayerPending.Error is { } error) throw new InvalidOperationException(error);
                request = _mainPlayerPending.Next;
                _mainPlayerCellLast = _mainPlayerCellLast! with { Pending = request?.Identity, PendingKind = request?.Kind };
            });
            if (request is not null)
            {
                pendingLease = _mainPlayerPending.Enter(request);
                Enter(FalloutMainPlayerCellStep.PendingWorldPrelude, consumers.PendingWorldPrelude);
                Enter(FalloutMainPlayerCellStep.PendingOwnedChildRelease, consumers.ReleasePendingOwnedChild);
                var destinationReturned = false;
                await EnterAsync(FalloutMainPlayerCellStep.PendingDestination, async child =>
                {
                    if (request.Kind == FalloutPlayerPendingKind.Opaque)
                        throw new NotSupportedException("source-Player-pending-opaque-payload-target-arm-unowned");
                    destinationReturned = await consumers.TransferPending(child, request);
                    if (destinationReturned == (request.Kind == FalloutPlayerPendingKind.Empty))
                        throw new InvalidDataException("Player pending target arm changed the original empty-target/returned result.");
                });
                SetLastBoolean(destinationReturned);
                if (destinationReturned && request.Kind != FalloutPlayerPendingKind.ReferenceTravel)
                {
                    Enter(FalloutMainPlayerCellStep.PendingSceneScalar, child => consumers.StorePendingSceneScalar(child, request));
                    Enter(FalloutMainPlayerCellStep.PendingCallback, child => consumers.InvokePendingCallback(child, request));
                }
                // Empty target assertion also reaches the original furniture
                // tail; only the distinct reference-travel arm skips it.
                if (request.Kind != FalloutPlayerPendingKind.ReferenceTravel)
                    Enter(FalloutMainPlayerCellStep.PendingFurniture, child => consumers.PendingFurniture(child, request));
                Enter(FalloutMainPlayerCellStep.PendingDeferredDestruction, consumers.RetirePendingDeferredChildren);
                Enter(FalloutMainPlayerCellStep.PendingNullStore, child =>
                {
                    child.Require(FalloutMainPlayerCellStep.PendingNullStore);
                    _mainPlayerPending.Complete(request, "actual-original-Player-pending-final-consumers-returned");
                });
                if (destinationReturned && Read(FalloutMainPlayerCellStep.PendingFlagQueries, consumers.PendingFlagQueries))
                    Enter(FalloutMainPlayerCellStep.PendingFlagChild, consumers.PendingFlagChild);
                pendingLease.Dispose(); pendingLease = null;
                if (destinationReturned) { Finish(FalloutMainPlayerCellDisposition.PendingReturned); return; }
            }
            // This is the real cached menu field sampled earlier in the SAME
            // Main invocation. The independent interface kind2 query is only
            // consumed on that original short-circuit arm.
            var cachedMenu = main.ReadCachedInterfaceFields().MenuGate;
            _mainPlayerCellLast = _mainPlayerCellLast! with { CachedMenu = cachedMenu };
            if (cachedMenu && !Read(FalloutMainPlayerCellStep.HeldInterfaceQuery, consumers.HeldInterface))
            {
                Enter(FalloutMainPlayerCellStep.HeldChild, consumers.HeldChild);
                Finish(FalloutMainPlayerCellDisposition.HeldReturned); return;
            }
            var alternate = Read(FalloutMainPlayerCellStep.SceneMode, consumers.SceneMode);
            var present = Read(FalloutMainPlayerCellStep.SceneRead, consumers.ScenePresent);
            if (present)
            {
                if (!alternate) Enter(FalloutMainPlayerCellStep.SceneClock, consumers.SceneClock);
                Enter(FalloutMainPlayerCellStep.SceneChild, child => consumers.SceneChild(child, alternate));
            }
            if (alternate) { Finish(FalloutMainPlayerCellDisposition.SceneModeReturned); return; }
            FalloutMainPlayerSourceCell? before = null;
            Enter(FalloutMainPlayerCellStep.ParentCellRead, child =>
            {
                before = consumers.ParentCell(child);
                _mainPlayerCellLast = _mainPlayerCellLast! with { BeforeCell = before?.Source };
            });
            SetLastBoolean(before is not null);
            FalloutMainPlayerSourcePosition? position = null;
            Enter(FalloutMainPlayerCellStep.PositionRead, child => position = consumers.Position(child));
            if (before is null) { Finish(FalloutMainPlayerCellDisposition.CellUnchanged); return; }
            if (Read(FalloutMainPlayerCellStep.InteriorQuery, child =>
            {
                child.Require(FalloutMainPlayerCellStep.InteriorQuery); return (before.CellFlags & 1) != 0;
            }) || Read(FalloutMainPlayerCellStep.ContainmentQuery, child =>
            {
                child.Require(FalloutMainPlayerCellStep.ContainmentQuery);
                return MainPlayerPositionContained(before, position!);
            })) { Finish(FalloutMainPlayerCellDisposition.CellUnchanged); return; }
            FalloutMainPlayerCellTarget? target = null;
            Enter(FalloutMainPlayerCellStep.TargetCellRead, child =>
            {
                target = consumers.TargetCell(child, before, position!);
                _mainPlayerCellLast = _mainPlayerCellLast! with { AfterCell = target?.Source };
            });
            SetLastBoolean(target is not null);
            if (target is null) { Finish(FalloutMainPlayerCellDisposition.CellReturned); return; }
            if (!Read(FalloutMainPlayerCellStep.TargetPhaseQuery, child =>
            {
                child.Require(FalloutMainPlayerCellStep.TargetPhaseQuery); return target.SourcePhase is 3 or 6;
            })) await EnterAsync(FalloutMainPlayerCellStep.WorldLoad, child => consumers.LoadTarget(child, target, position!));
            Enter(FalloutMainPlayerCellStep.WorldBracketSet, child => consumers.SetWorldBracket(child, true));
            Enter(FalloutMainPlayerCellStep.PlayerBracketSet, child => consumers.SetPlayerBracket(child, true));
            Enter(FalloutMainPlayerCellStep.CellAttach, child => consumers.AttachCell(child, target));
            Enter(FalloutMainPlayerCellStep.RootStore, child => consumers.StoreRoot(child, target));
            Enter(FalloutMainPlayerCellStep.PlayerBracketClear, child => consumers.SetPlayerBracket(child, false));
            Enter(FalloutMainPlayerCellStep.WorldBracketClear, child => consumers.SetWorldBracket(child, false));
            Enter(FalloutMainPlayerCellStep.OptionalTreeChild, consumers.OptionalTreeChild);
            Finish(FalloutMainPlayerCellDisposition.CellReturned);
        }
        catch (Exception failure)
        {
            var children = _mainPlayerCellLast!.Children.ToArray();
            if (children.Length != 0 && children[^1].Returned is null)
                children[^1] = children[^1] with { FailureType = failure.GetType().FullName ?? failure.GetType().Name, Error = Message(failure) };
            _mainPlayerCellLast = _mainPlayerCellLast with
            {
                Changed = Next(),
                Disposition = FalloutMainPlayerCellDisposition.Failed,
                Children = children,
                FailureType = failure.GetType().FullName ?? failure.GetType().Name,
                Error = Message(failure)
            };
            if (request is not null && _mainPlayerPending.Pending && _mainPlayerPending.Error is null) _mainPlayerPending.Fail(request, failure);
            throw;
        }
        finally { pendingLease?.Dispose(); _mainPlayerCellChild = null; _mainPlayerCellActiveMain = null; }

        void Check()
        {
            main.Require(FalloutMainScriptCallerStep.Player);
            if (!ReferenceEquals(_mainPlayerCellActiveMain, main))
                throw new InvalidOperationException("Player continuation lost its retained Main invocation.");
            if (_mainPlayerCellThread != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("Player async continuation left its actual Main/native owner thread.");
            if (_mainPlayerCellReentry != faults)
                throw new InvalidOperationException("Player child swallowed a forbidden capture, retirement or caller reentry.");
        }
        FalloutMainPlayerCellInvocation Begin(FalloutMainPlayerCellStep step)
        {
            Check(); var next = Next();
            _mainPlayerCellLast = _mainPlayerCellLast! with
            {
                Changed = next,
                Children = [.. _mainPlayerCellLast.Children, new(step, next, null, consumers.Owner, null, null, null)]
            };
            return _mainPlayerCellChild = new(main, step);
        }
        void Returned(FalloutMainPlayerCellInvocation child, bool? value = null)
        {
            Check(); child.Require(child.Step); var children = _mainPlayerCellLast!.Children.ToArray();
            children[^1] = children[^1] with { Returned = Next(), Boolean = value };
            _mainPlayerCellLast = _mainPlayerCellLast with { Changed = _sequence, Children = children };
            _mainPlayerCellChild = null;
        }
        void Enter(FalloutMainPlayerCellStep step, Action<FalloutMainPlayerCellInvocation> action)
        { var child = Begin(step); action(child); Returned(child); }
        async Task EnterAsync(FalloutMainPlayerCellStep step, Func<FalloutMainPlayerCellInvocation, Task> action)
        { var child = Begin(step); await action(child); Returned(child); }
        bool Read(FalloutMainPlayerCellStep step, Func<FalloutMainPlayerCellInvocation, bool> read)
        { var child = Begin(step); var value = read(child); Returned(child, value); return value; }
        void Finish(FalloutMainPlayerCellDisposition disposition)
        { Check(); _mainPlayerCellLast = _mainPlayerCellLast! with { Changed = Next(), Disposition = disposition }; }
        void SetLastBoolean(bool value)
        {
            var children = _mainPlayerCellLast!.Children.ToArray();
            children[^1] = children[^1] with { Boolean = value };
            _mainPlayerCellLast = _mainPlayerCellLast with { Children = children };
        }
    }
    internal void RequireMainPlayerCellChild(FalloutMainPlayerCellInvocation child, FalloutMainPlayerCellStep step)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); child.Main.Require(FalloutMainScriptCallerStep.Player);
        var call = _mainPlayerCellLast;
        if (!ReferenceEquals(child.Owner, this) || !ReferenceEquals(_mainPlayerCellChild, child) || child.Step != step ||
            _mainPlayerCellThread != Environment.CurrentManagedThreadId || call is null || call.MainInvocation != child.Main.Identity ||
            call.Children.LastOrDefault() is not { Returned: null } entered || entered.Step != step)
        {
            _mainPlayerCellReentry = checked(_mainPlayerCellReentry + 1);
            throw new InvalidOperationException("Player/CELL operation has no actual current source child/thread lifetime.");
        }
    }
    internal void RetireMainPlayerCell()
    {
        if (_mainPlayerCellActiveMain is not null || _mainPlayerCellLease != Guid.Empty)
        {
            _mainPlayerCellReentry = checked(_mainPlayerCellReentry + 1);
            throw new InvalidOperationException("Actual Player native/entered children must retire before its source owner.");
        }
        var failures = new List<Exception>();
        try { _mainPlayerPending?.Retire(); } catch (Exception failure) { failures.Add(failure); }
        try { _mainPlayerPendingConsumers?.Retire(); } catch (Exception failure) { failures.Add(failure); }
        if (failures.Count != 0) throw new AggregateException("Player source retirement retains independent payload/child/task owners.", failures);
        _mainPlayerCellRetired = true;
    }
    internal static bool MainPlayerPositionContained(FalloutMainPlayerSourceCell cell, FalloutMainPlayerSourcePosition position)
    {
        if ((cell.CellFlags & 1) != 0) return false;
        if (cell.X is null || cell.Y is null) throw new InvalidDataException("Winning exterior CELL has no XCLC coordinates.");
        var coordinates = position.Rounding.Require();
        var x = FalloutQueuedReferencePriority.ConvertSourcePosition(position.XBits, coordinates) >> 12;
        var y = FalloutQueuedReferencePriority.ConvertSourcePosition(position.YBits, coordinates) >> 12;
        // CELL source coordinates live in the winning record, not in world
        // ancestry, a cached target prediction or the root's terrain count.
        return cell.X == x && cell.Y == y;
    }
}
