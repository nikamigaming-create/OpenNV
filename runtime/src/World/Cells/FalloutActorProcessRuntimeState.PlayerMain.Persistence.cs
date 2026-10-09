using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessRuntimeState
{
    internal FalloutMainPlayerCellSnapshot CaptureMainPlayerCell()
    {
        RequireNotBusy();
        if (_mainPlayerCellSource is null || _mainPlayerPending is null)
            throw new NotSupportedException("source-Main-Player-CELL-constructor-unbound");
        if (_mainPlayerCellActiveMain is not null)
        {
            _mainPlayerCellReentry = checked(_mainPlayerCellReentry + 1);
            throw new NotSupportedException("Main Player child still owns its original pending/native source prefix.");
        }
        if (MainPlayerColdRootToRebind is not null)
            throw new NotSupportedException("Cold Player source root is awaiting actual new-process native publication.");
        var saved = new FalloutMainPlayerCellSnapshot(MainPlayerCellSchema, _mainPlayerCellSource, _stack,
            _process, _sequence, _mainPlayerCellCalls, _mainPlayerCellLast, _mainPlayerPending.Capture(), _mainPlayerCellCold,
            _mainPlayerWorldBracket, _mainPlayerMovementBracket, _mainPlayerRootBinding, MainPlayerPendingConsumers.Capture());
        ValidateMainPlayerCell(saved); return saved;
    }
    internal static void ValidateMainPlayerCell(FalloutMainPlayerCellSnapshot saved)
    {
        if (saved is null || saved.Schema != MainPlayerCellSchema || saved.Source is null ||
            string.IsNullOrWhiteSpace(saved.Stack) || saved.CapturedProcess == Guid.Empty || saved.Changed < 0 ||
            saved.Calls < 0 || (saved.Calls == 0) != (saved.LastCall is null))
            throw new InvalidDataException("Player/CELL continuation omitted its actual source/process/call owner.");
        saved.Source.Validate(); FalloutPlayerPendingSlot.Validate(saved.Pending); RequirePendingConsumersContinuation(saved);
        if (!saved.Source.HasSourceRootStore && (saved.RootBinding is not null || saved.PlayerBracket is not null))
            throw new InvalidDataException("FO3 Player continuation invented an absent movement bracket/root-store source arm.");
        if (saved.RootBinding is { } binding && (binding.Process != saved.CapturedProcess || binding.NativeRoot == 0 ||
            binding.Changed < 1 || binding.Changed > saved.Changed || binding.Cell.ObjectId == 0 || string.IsNullOrWhiteSpace(binding.Cell.OwnerPlugin)))
            throw new InvalidDataException("Player source root store lost its actual process/native lifetime.");
        if (saved.LastCall is { Disposition: not FalloutMainPlayerCellDisposition.Failed } &&
            (saved.WorldBracket == true || saved.PlayerBracket == true))
            throw new InvalidDataException("Completed Player child left the original movement bracket set.");
        if (saved.ColdHandoff is { } cold && (cold.PreviousProcess == Guid.Empty || cold.CurrentProcess != saved.CapturedProcess ||
            cold.PreviousProcess == cold.CurrentProcess || cold.Sequence < 1 || cold.Sequence > saved.Changed))
            throw new InvalidDataException("Player/CELL continuation lost its genuine new-process epoch handoff.");
        if (saved.LastCall is not { } call) return;
        if (call.MainInvocation == Guid.Empty || call.MainOrdinal < saved.Calls || call.Process == Guid.Empty || call.Entered < 1 ||
            call.Changed < call.Entered || call.Changed > saved.Changed || !Enum.IsDefined(call.Disposition) ||
            call.Disposition == FalloutMainPlayerCellDisposition.Entered || call.Children is null || call.Children.Count == 0 ||
            (call.Disposition == FalloutMainPlayerCellDisposition.Failed) != (call.Error is not null) ||
            (call.Error is null) != (call.FailureType is null))
            throw new InvalidDataException("Player/CELL snapshot invented a completed or replayable entered source call.");
        var previous = call.Entered;
        if ((call.Pending is null) != (call.PendingKind is null) || call.PendingKind is { } kind && !Enum.IsDefined(kind))
            throw new InvalidDataException("Player pending branch lost its actual typed request identity.");
        for (var i = 0; i < call.Children.Count; i++)
        {
            var child = call.Children[i];
            if (!Enum.IsDefined(child.Step) || string.IsNullOrWhiteSpace(child.Owner) || child.Entered <= previous ||
                child.Entered > call.Changed || child.Returned is { } returned && (returned <= child.Entered || returned > call.Changed) ||
                (child.Error is null) != (child.FailureType is null) || child.Returned is not null && child.Error is not null ||
                child.Returned is null && (call.Disposition != FalloutMainPlayerCellDisposition.Failed || i != call.Children.Count - 1 ||
                    child.Error != call.Error || child.FailureType != call.FailureType))
                throw new InvalidDataException("Player/CELL child order or retained original failure changed.");
            previous = child.Returned ?? child.Entered;
        }
        if (call.BeforeCell is { } before) RequirePlayerCellIdentity(before);
        if (call.AfterCell is { } after) RequirePlayerCellIdentity(after);
        var index = 0;
        try
        {
            Need(FalloutMainPlayerCellStep.PendingRead);
            if (call.Pending is not null)
            {
                Need(FalloutMainPlayerCellStep.PendingWorldPrelude); Need(FalloutMainPlayerCellStep.PendingOwnedChildRelease);
                var returned = Read(FalloutMainPlayerCellStep.PendingDestination);
                if (call.PendingKind == FalloutPlayerPendingKind.Opaque || returned == (call.PendingKind == FalloutPlayerPendingKind.Empty))
                    throw new InvalidDataException("Player pending continuation changed original target/empty-branch result.");
                // Reference travel skips all three. An empty target still
                // reaches furniture, but does not store scalar/invoke callback.
                if (returned && call.PendingKind != FalloutPlayerPendingKind.ReferenceTravel)
                {
                    if (saved.Source.HasOuterPendingScalar) Need(FalloutMainPlayerCellStep.PendingSceneScalar);
                    Need(FalloutMainPlayerCellStep.PendingCallback);
                }
                if (call.PendingKind != FalloutPlayerPendingKind.ReferenceTravel) Need(FalloutMainPlayerCellStep.PendingFurniture);
                Need(FalloutMainPlayerCellStep.PendingDeferredDestruction); Need(FalloutMainPlayerCellStep.PendingNullStore);
                if (returned)
                {
                    if (Read(FalloutMainPlayerCellStep.PendingFlagQueries)) Need(FalloutMainPlayerCellStep.PendingFlagChild);
                    Finish(FalloutMainPlayerCellDisposition.PendingReturned); return;
                }
            }
            if (call.CachedMenu is null) throw new InvalidDataException("Player normal branch omitted the actual same-invocation Main menu cache.");
            if (call.CachedMenu.Value && !Read(FalloutMainPlayerCellStep.HeldInterfaceQuery))
            {
                Need(FalloutMainPlayerCellStep.HeldChild); Finish(FalloutMainPlayerCellDisposition.HeldReturned); return;
            }
            var alternate = Read(FalloutMainPlayerCellStep.SceneMode);
            if (Read(FalloutMainPlayerCellStep.SceneRead))
            {
                if (!alternate) Need(FalloutMainPlayerCellStep.SceneClock);
                Need(FalloutMainPlayerCellStep.SceneChild);
            }
            if (alternate) { Finish(FalloutMainPlayerCellDisposition.SceneModeReturned); return; }
            var parent = Read(FalloutMainPlayerCellStep.ParentCellRead); Need(FalloutMainPlayerCellStep.PositionRead);
            if (parent != (call.BeforeCell is not null)) throw new InvalidDataException("Player parent-CELL read lost its exact selected source identity.");
            if (!parent) { Finish(FalloutMainPlayerCellDisposition.CellUnchanged); return; }
            if (Read(FalloutMainPlayerCellStep.InteriorQuery) || Read(FalloutMainPlayerCellStep.ContainmentQuery))
            { Finish(FalloutMainPlayerCellDisposition.CellUnchanged); return; }
            var target = Read(FalloutMainPlayerCellStep.TargetCellRead);
            if (target != (call.AfterCell is not null)) throw new InvalidDataException("Player exterior target read lost its exact selected source identity.");
            if (!target) { Finish(FalloutMainPlayerCellDisposition.CellReturned); return; }
            if (!Read(FalloutMainPlayerCellStep.TargetPhaseQuery)) Need(FalloutMainPlayerCellStep.WorldLoad);
            Need(FalloutMainPlayerCellStep.WorldBracketSet);
            if (saved.Source.HasPlayerMovementBracket) Need(FalloutMainPlayerCellStep.PlayerBracketSet);
            Need(FalloutMainPlayerCellStep.CellAttach);
            if (saved.Source.HasSourceRootStore) Need(FalloutMainPlayerCellStep.RootStore);
            if (saved.Source.HasPlayerMovementBracket) Need(FalloutMainPlayerCellStep.PlayerBracketClear);
            Need(FalloutMainPlayerCellStep.WorldBracketClear);
            Need(FalloutMainPlayerCellStep.OptionalTreeChild); Finish(FalloutMainPlayerCellDisposition.CellReturned);
        }
        catch (PlayerCellFailedPrefix) { }
        void Need(FalloutMainPlayerCellStep step)
        {
            if (index >= call.Children.Count || call.Children[index].Step != step)
                throw new InvalidDataException("Player/CELL source continuation skipped or reordered " + step);
            var child = call.Children[index++];
            if (child.Returned is null) throw new PlayerCellFailedPrefix();
        }
        bool Read(FalloutMainPlayerCellStep step)
        {
            Need(step); return call.Children[index - 1].Boolean ??
                throw new InvalidDataException("Player/CELL branch lost its actual returned Boolean: " + step);
        }
        void Finish(FalloutMainPlayerCellDisposition disposition)
        {
            if (index != call.Children.Count || call.Disposition != disposition)
                throw new InvalidDataException("Player/CELL source arm has an invented completion or additional child.");
        }
    }
    private sealed class PlayerCellFailedPrefix : Exception { }
    internal static void RequireMainPlayerCellCaller(FalloutMainPlayerCellSnapshot player, FalloutMainScriptCallerSnapshot main)
    {
        ValidateMainPlayerCell(player); ValidateMainScriptCaller(main);
        if (player.Source.Main != main.Source || player.Stack != main.Stack || player.CapturedProcess != main.CapturedProcess)
            throw new InvalidDataException("Player child and parent Main capture belong to different source/process owners.");
        if (player.LastCall is not { } call) return;
        if (main.LastCall is not { } parent || call.MainOrdinal > parent.Ordinal)
            throw new InvalidDataException("Player source call is newer than its actual captured parent.");
        if (call.MainOrdinal != parent.Ordinal) return; // A later input-suppressed Main does not invoke Player.
        var child = parent.Children.SingleOrDefault(child => child.Step == FalloutMainScriptCallerStep.Player);
        if (child is null || call.MainInvocation != parent.Invocation || call.Process != parent.SourceProcess || call.Entered <= child.Entered || call.Changed > (child.Returned ?? parent.Changed) ||
            (call.Disposition == FalloutMainPlayerCellDisposition.Failed) != (child.Returned is null))
            throw new InvalidDataException("Player source prefix changed its exact parent child return/failure boundary.");
    }
    private static void RequirePlayerCellIdentity(FalloutCellProcessIdentity cell)
    {
        if (cell.Cell.ObjectId == 0 || string.IsNullOrWhiteSpace(cell.Cell.OwnerPlugin) || cell.Sha256 is not { Length: 64 } ||
            !cell.Sha256.All(Uri.IsHexDigit) || (cell.Worldspace is null) != (cell.WorldspaceSha256 is null) ||
            cell.WorldspaceSha256 is { } sha && (sha.Length != 64 || !sha.All(Uri.IsHexDigit)))
            throw new InvalidDataException("Player/CELL source identity lost winning bytes or world/master ownership.");
    }
}
