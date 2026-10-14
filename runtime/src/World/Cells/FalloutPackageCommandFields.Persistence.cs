using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorPackageCommandFields
{
    private static void ValidateConstruction(FalloutPackageCommandFieldConstruction value)
    {
        if (string.IsNullOrWhiteSpace(value.Producer) || value.PrecedingWritersClosed && value.Boundary is not null || value.Boundary is { Length: 0 })
            throw new InvalidDataException("Package command fields lost their constructor/current-writer admission.");
        if (value.ClosedLanes is { } lanes && (lanes.Any(lane => !Enum.IsDefined(lane)) || lanes.Distinct().Count() != lanes.Count))
            throw new InvalidDataException("Package constructor admission repeated or invented an independent source lane.");
    }
    private static bool EquivalentConstruction(FalloutPackageCommandFieldConstruction first, FalloutPackageCommandFieldConstruction second) =>
        first.Producer == second.Producer && first.PrecedingWritersClosed == second.PrecedingWritersClosed && first.Boundary == second.Boundary &&
        (first.ClosedLanes ?? []).SequenceEqual(second.ClosedLanes ?? []);
    internal static void Validate(FalloutPackageCommandFieldSnapshot saved, FalloutPackageCommandFieldsSource source,
        string stack, FalloutCombatActorIdentity actor, IReadOnlyCollection<Guid>? actualProcessLineage = null)
    {
        ArgumentNullException.ThrowIfNull(saved); source.Validate(); saved.Source.Validate(); actor.Validate();
        if (saved.Schema != Schema || saved.Source.Identity != source.Identity || saved.Stack != stack || saved.Actor != actor ||
            saved.Process == Guid.Empty || saved.Factory == Guid.Empty || saved.Constructor == Guid.Empty || saved.Epoch < 1 ||
            saved.Sequence < 0 || saved.Values is null || saved.Operations is null || saved.Publications is not { Count: > 0 } ||
            saved.Availability is null || saved.Availability.Count != Enum.GetValues<FalloutPackageCommandFieldLane>().Length)
            throw new InvalidDataException("Package command cold fields changed their exact source, Actor or schema.");
        ValidateConstruction(saved.Construction);
        var processes = new HashSet<Guid>(); var factories = new HashSet<Guid>(); var constructors = new HashSet<Guid>();
        var publicationOrder = new Dictionary<Guid, int>();
        FalloutPackageCommandFieldPublication? previous = null;
        foreach (var publication in saved.Publications)
        {
            if (publication is null || publication.Process == Guid.Empty || publication.Factory == Guid.Empty || publication.Constructor == Guid.Empty ||
                publication.Epoch != saved.Epoch || !processes.Add(publication.Process) || !factories.Add(publication.Factory) ||
                !constructors.Add(publication.Constructor) || actualProcessLineage is not null && !actualProcessLineage.Contains(publication.Process) ||
                (previous is null ? publication.PreviousProcess is not null || publication.PreviousFactory is not null :
                    publication.PreviousProcess != previous.Process || publication.PreviousFactory != previous.Factory))
                throw new InvalidDataException("Package command fields lost complete actual process/factory/constructor lineage.");
            previous = publication;
            publicationOrder.Add(publication.Process, publicationOrder.Count);
        }
        if (previous!.Process != saved.Process || previous.Factory != saved.Factory || previous.Constructor != saved.Constructor)
            throw new InvalidDataException("Package command snapshot did not retain its actual current field factory.");
        var available = Enum.GetValues<FalloutPackageCommandFieldLane>().ToDictionary(lane => lane, lane =>
        {
            var admitted = saved.Construction.PrecedingWritersClosed || saved.Construction.ClosedLanes?.Contains(lane) == true;
            return new FalloutPackageCommandFieldAvailability(lane, admitted, admitted ? null : saved.Construction.Boundary ?? PrecedingWriterBoundary);
        });
        var values = FalloutPackageCommandFieldValues.Constructed; long sequence = 0;
        var sourceThreads = new Dictionary<Guid, int>(); var lastPublication = -1;
        var scriptCalls = ValidateScriptInitializationHistory(saved, source, stack, actor);
        var scriptOperations = saved.ScriptInitializationOperations ?? [];
        var history = saved.Operations.Select(operation => (operation.Entered, Command: (FalloutPackageCommandFieldOperation?)operation,
            Script: (FalloutScriptExtraInitializationFieldOperation?)null)).Concat(scriptOperations.Select(operation =>
            (operation.Entered, Command: (FalloutPackageCommandFieldOperation?)null, Script: (FalloutScriptExtraInitializationFieldOperation?)operation)))
            .OrderBy(value => value.Entered);
        foreach (var entry in history)
        {
            if (entry.Script is { } script)
            {
                ValidateScriptInitializationOperation(script, scriptCalls, ref values, available, ref sequence,
                    sourceThreads, publicationOrder, ref lastPublication);
                continue;
            }
            var operation = entry.Command!;
            if (operation is null || operation.Invocation is not { } invocation || invocation.SourceCommand == Guid.Empty ||
                invocation.Actor != actor || !processes.Contains(invocation.Process) || invocation.Epoch != saved.Epoch || invocation.ThreadId < 1 ||
                string.IsNullOrWhiteSpace(operation.Kind) || operation.Entered != ++sequence || operation.Stores is null ||
                operation.Returned != (operation.Failure is null) || operation.Failure is { Length: 0 })
                throw new InvalidDataException("Package command history lost an actual entered caller, failure or process publication.");
            var currentPublication = publicationOrder[invocation.Process];
            if (currentPublication < lastPublication || sourceThreads.TryGetValue(invocation.Process, out var thread) && thread != invocation.ThreadId)
                throw new InvalidDataException("Package command history returned to a retired process or changed its source thread.");
            lastPublication = currentPublication; sourceThreads.TryAdd(invocation.Process, invocation.ThreadId);
            var before = values;
            ValidateOperation(operation, before, available);
            foreach (var store in operation.Stores)
            {
                if (store is null || store.Changed != ++sequence)
                    throw new InvalidDataException("Package command store history omitted or reordered committed source fields.");
                values = ApplyStore(values, store, operation.PublishedRows, operation.EventFactory);
            }
            if (operation.Changed != ++sequence)
                throw new InvalidDataException("Package command history lost its exact completed/failed prefix extent.");
            if (operation.InvalidatedLane is { } invalidated)
            {
                if (!Enum.IsDefined(invalidated) || string.IsNullOrWhiteSpace(operation.InvalidationBoundary) ||
                    operation.Kind != "observed-unowned-writer" || operation.Stores.Count != 0)
                    throw new InvalidDataException("Unowned command field writer hid its unavailable lane.");
                available[invalidated] = new(invalidated, false, operation.InvalidationBoundary);
            }
            else if (operation.InvalidationBoundary is not null || operation.Kind == "observed-unowned-writer")
                throw new InvalidDataException("Command field observation lost its exact unavailable lane.");
            if (operation.Kind == "current-position-store" && operation.Stores.Count == 3)
                available[FalloutPackageCommandFieldLane.Position] = new(FalloutPackageCommandFieldLane.Position, true, null);
            if (operation.Kind == "current-ParentCELL-store" && operation.Stores.Count == 1)
                available[FalloutPackageCommandFieldLane.ParentCell] = new(FalloutPackageCommandFieldLane.ParentCell, true, null);
            if (operation.Kind == "raw-rotation-Z-store" && operation.Stores.Count == 1)
                available[FalloutPackageCommandFieldLane.RotationZ] = new(FalloutPackageCommandFieldLane.RotationZ, true, null);
            ValidateEvidence(operation, before, values, source, stack);
            if (operation.AnimationInitialization is { } animationInitialization)
            {
                var publication = saved.Publications.Single(value => value.Process == invocation.Process);
                if (animationInitialization.FieldsFactory != publication.Factory || animationInitialization.Constructor != publication.Constructor ||
                    saved.Operations.Count(value => value.AnimationInitialization is not null) != 1 ||
                    operation.Returned && saved.Operations.Any(value => value.Entered < operation.Entered &&
                        (value.InvalidatedLane == FalloutPackageCommandFieldLane.Animation || value.Stores.Any(store =>
                            store.Field is FalloutPackageCommandField.View or FalloutPackageCommandField.AnimationOverride or FalloutPackageCommandField.FirstPersonAnimation))))
                    throw new InvalidDataException("Animation constructor admission lost its original field publication or replayed after another writer.");
                if (operation.Returned)
                    available[FalloutPackageCommandFieldLane.Animation] = new(FalloutPackageCommandFieldLane.Animation, true, null);
            }
            if (operation.SavedPositionInitialization is { } savedInitialization)
            {
                var publication = saved.Publications.Single(value => value.Process == invocation.Process);
                if (savedInitialization.FieldsFactory != publication.Factory || savedInitialization.Constructor != publication.Constructor ||
                    saved.Operations.Count(value => value.SavedPositionInitialization is not null) != 1 ||
                    operation.Returned && saved.Operations.Any(value => value.Entered < operation.Entered &&
                        (value.InvalidatedLane == FalloutPackageCommandFieldLane.SavedPosition || value.Stores.Any(store => IsSavedPositionField(store.Field)))))
                    throw new InvalidDataException("SavedPosition constructor admission replayed or crossed an actual Extra writer.");
                if (operation.Returned)
                    available[FalloutPackageCommandFieldLane.SavedPosition] = new(FalloutPackageCommandFieldLane.SavedPosition, true, null);
            }
        }
        if (sequence != saved.Sequence || !EquivalentValues(values, saved.Values) ||
            saved.Availability.Select(value => value.Lane).Distinct().Count() != available.Count ||
            saved.Availability.Any(value => !available.TryGetValue(value.Lane, out var expected) || value != expected))
            throw new InvalidDataException("Package command current fields/availability disagree with their original ordered writers.");
    }

    private static void ValidateOperation(FalloutPackageCommandFieldOperation operation, FalloutPackageCommandFieldValues before,
        IReadOnlyDictionary<FalloutPackageCommandFieldLane, FalloutPackageCommandFieldAvailability> available)
    {
        FalloutPackageCommandField[] expected = operation.Kind switch
        {
            "current-position-getter" or "selected-animation-context-getter" or "observed-unowned-writer" or AnimationInitializationKind or SavedInitializationKind => [],
            "current-position-store" => [FalloutPackageCommandField.PositionX, FalloutPackageCommandField.PositionY, FalloutPackageCommandField.PositionZ],
            "current-ParentCELL-store" => [FalloutPackageCommandField.ParentCell],
            "raw-rotation-Z-store" => [FalloutPackageCommandField.RotationZ],
            "script-Extra-setter" => before.ScriptExtra is null ? [FalloutPackageCommandField.ScriptConstruction, FalloutPackageCommandField.ScriptForm,
                FalloutPackageCommandField.ScriptConstructorEventList, FalloutPackageCommandField.ScriptPresence] :
                [FalloutPackageCommandField.ScriptForm],
            "script-event-list-setter" => before.ScriptExtra is null ? [] : [FalloutPackageCommandField.ScriptEventList],
            "package-event-marker" => operation.Stores.Select(_ => FalloutPackageCommandField.EventMask).ToArray(),
            "saved-position-Extra-and-reference-change" => before.SavedPosition is null ?
                [FalloutPackageCommandField.SavedConstruction, FalloutPackageCommandField.SavedSpace, FalloutPackageCommandField.SavedX,
                    FalloutPackageCommandField.SavedY, FalloutPackageCommandField.SavedZ, FalloutPackageCommandField.SavedRotationZ,
                    FalloutPackageCommandField.SavedPresence] :
                [FalloutPackageCommandField.SavedSpace, FalloutPackageCommandField.SavedX, FalloutPackageCommandField.SavedY, FalloutPackageCommandField.SavedZ],
            _ => throw new InvalidDataException("Saved command fields contain an unowned operation.")
        };
        if (operation.Stores.Count > expected.Length || !operation.Stores.Select(value => value.Field).SequenceEqual(expected.Take(operation.Stores.Count)) ||
            operation.Returned && operation.Stores.Count != expected.Length || operation.Kind == "package-event-marker" && operation.Stores.Count > 2 ||
            operation.PublishedRows is not null && operation.Kind != "script-event-list-setter" ||
            operation.Kind == "script-event-list-setter" && operation.Stores.Count != 0 && operation.PublishedRows is null ||
            operation.PublishedRows is null && operation.EventFactory is not null)
            throw new InvalidDataException("Command field operation borrowed another child or changed its source store order.");
        if (operation.Kind is "script-Extra-setter" or "script-event-list-setter" or "package-event-marker" &&
            !available[FalloutPackageCommandFieldLane.ScriptExtra].Available && (operation.Stores.Count != 0 || operation.Returned))
            throw new InvalidDataException("Package event operation admitted an unclosed preceding Script Extra writer.");
        if (operation.Kind == "saved-position-Extra-and-reference-change" && operation.Stores.Count != 0 &&
            (operation.SavedPositionCall?.PositionRead is null && (!available[FalloutPackageCommandFieldLane.PositionMode].Available || before.PositionMode != 0 ||
             !available[FalloutPackageCommandFieldLane.Position].Available) ||
             !available[FalloutPackageCommandFieldLane.RotationZ].Available && operation.SavedPositionCall?.RotationRead is null ||
             !available[FalloutPackageCommandFieldLane.ParentCell].Available && operation.SavedPositionCall?.ParentRead is null ||
             !available[FalloutPackageCommandFieldLane.SavedPosition].Available))
            throw new InvalidDataException("Saved-position stores bypassed unavailable current reference or Extra fields.");
        if (operation.Kind == "current-position-getter" && operation.Returned &&
            (!available[FalloutPackageCommandFieldLane.PositionMode].Available || before.PositionMode != 0 ||
             !available[FalloutPackageCommandFieldLane.Position].Available))
            throw new InvalidDataException("Current-position getter fabricated an adjusted/pending/native pose.");
        if (operation.Kind == "selected-animation-context-getter" && operation.Returned &&
            !available[FalloutPackageCommandFieldLane.Animation].Available)
            throw new InvalidDataException("Animation selection admitted an unclosed preceding publisher.");
    }

    private static void ValidateEvidence(FalloutPackageCommandFieldOperation operation,
        FalloutPackageCommandFieldValues before, FalloutPackageCommandFieldValues after, FalloutPackageCommandFieldsSource source, string stack)
    {
        if ((operation.Kind == "package-event-marker") != (operation.EventMark is not null) ||
            (operation.Kind == "selected-animation-context-getter") != (operation.AnimationRead is not null) ||
            (operation.Kind == "saved-position-Extra-and-reference-change") != (operation.SavedPositionCall is not null) ||
            (operation.Kind == AnimationInitializationKind) != (operation.AnimationInitialization is not null) ||
            (operation.Kind == SavedInitializationKind) != (operation.SavedPositionInitialization is not null))
            throw new InvalidDataException("Command fields omitted or borrowed a sibling child receipt.");
        if (operation.SavedPositionInitialization is { } savedInitialization)
        {
            RequireReceipt(savedInitialization.Invocation, savedInitialization.Entered, savedInitialization.Changed,
                savedInitialization.Returned, savedInitialization.Failure, operation);
            if (savedInitialization.Source != source.Identity || savedInitialization.FieldsFactory == Guid.Empty || savedInitialization.Constructor == Guid.Empty)
                throw new InvalidDataException("SavedPosition constructor receipt changed its actual field authority.");
            if (savedInitialization.Returned) ValidateSavedPrefix(savedInitialization.Prefix, operation.Invocation, source);
        }
        if (operation.AnimationInitialization is { } initialization)
        {
            RequireReceipt(initialization.Invocation, initialization.Entered, initialization.Changed,
                initialization.Returned, initialization.Failure, operation);
            if (initialization.Source != source.Identity || initialization.FieldsFactory == Guid.Empty || initialization.Constructor == Guid.Empty ||
                initialization.OverrideContext != before.AnimationOverride || initialization.FirstPersonContext != before.FirstPersonAnimation ||
                initialization.ConfiguredCell is null && initialization.ConfiguredGridX is not null ||
                initialization.ConfiguredCell is { Length: > 0 } && initialization.ConfiguredGridX is not null ||
                initialization.Returned && (!operation.Invocation.Actor.EnginePlayer || initialization.ConfiguredCell != "" ||
                    initialization.ConfiguredGridX != "" || initialization.OverrideContext is not null || initialization.FirstPersonContext is not null))
                throw new InvalidDataException("Animation constructor prefix changed its actual selected settings, fields or null return.");
        }
        if (operation.EventMark is { } mark)
        {
            RequireReceipt(mark.Invocation, mark.Entered, mark.Changed, mark.Returned, mark.Failure, operation);
            ValidateForm(mark.Package);
            if (!mark.Stores.SequenceEqual(operation.Stores)) throw new InvalidDataException("Event mark lost its actual committed store prefix.");
            var targets = EventTargets(before.ScriptExtra, mark.Package);
            if (operation.Stores.Count > targets.Count || mark.Returned && mark.Result != (targets.Count != 0) ||
                mark.Result is { } result && result != (targets.Count != 0))
                throw new InvalidDataException("Package marker fabricated an allocated/matched event row or Boolean return.");
            var masks = before.ScriptExtra?.Rows.Where(row => row is not null).ToDictionary(row => row!.Identity, row => row!.Mask) ?? [];
            for (var index = 0; index < operation.Stores.Count; index++)
            {
                var store = operation.Stores[index]; var row = targets[index]; var expected = masks[row] | mark.Mask;
                if (store.Row != row || store.Bits != expected) throw new InvalidDataException("Package marker changed mask OR order or wildcard selection.");
                masks[row] = expected;
            }
        }
        if (operation.AnimationRead is { } animation)
        {
            RequireReceipt(animation.Invocation, animation.Entered, animation.Changed, animation.Returned, animation.Failure, operation);
            if (animation.Returned && (animation.Context is not null || animation.Selection is null) ||
                animation.Selection is { } selection && selection != (operation.Invocation.Actor.EnginePlayer ?
                    SelectAnimation(animation.ViewRead?.View ?? 0, before.AnimationOverride, before.FirstPersonAnimation) : FalloutPackageAnimationSelection.Process))
                throw new InvalidDataException("Package animation selection substituted a model root or a nonnull consumer return.");
            if (animation.ViewRead is { } view && (!operation.Invocation.Actor.EnginePlayer || before.AnimationOverride is not null ||
                view.Process == Guid.Empty || view.Constructor == Guid.Empty || view.Stack != stack || view.ExecutableSha256 != source.Controls.Main.EngineSha256) ||
                animation.Selection is not null && operation.Invocation.Actor.EnginePlayer && before.AnimationOverride is null && animation.ViewRead is null)
                throw new InvalidDataException("Package animation borrowed a view without its actual independent scene owner or short circuit.");
            if (animation.Common is { } common && (animation.Selection != FalloutPackageAnimationSelection.Process ||
                common.Actor != operation.Invocation.Actor || common.Process != operation.Invocation.Process || common.Epoch != operation.Invocation.Epoch ||
                common.Stack != stack || common.ExecutableSha256 != source.Controls.Main.EngineSha256 ||
                common.GameplayOwnership == Guid.Empty || common.AnimationContext != animation.Context) ||
                animation.Returned && animation.Selection == FalloutPackageAnimationSelection.Process && animation.Common is null)
                throw new InvalidDataException("Package animation getter lost its actual Common source lease.");
        }
        if (operation.SavedPositionCall is { } saved)
        {
            RequireReceipt(saved.Invocation, saved.Entered, saved.Changed, saved.Returned, saved.Failure, operation);
            if (!saved.Stores.SequenceEqual(operation.Stores) || saved.Before != before.SavedPosition || saved.After != after.SavedPosition ||
                saved.Position is { } position && position != (saved.PositionRead?.Position ?? before.Position) ||
                saved.RotationZ is { } rotation && rotation != (saved.RotationRead?.ZBits ?? before.RotationZ) ||
                saved.ParentCellReturned && (saved.Position is null || saved.ParentCell != (saved.ParentRead is { } parent ? parent.Cell : before.ParentCell)) ||
                saved.WorldspaceReturned && (!saved.ParentCellReturned || saved.Consumer is null) ||
                saved.Consumer == Guid.Empty || !saved.WorldspaceReturned && saved.Worldspace is not null ||
                saved.ChangeEntered && !saved.WorldspaceReturned || saved.Returned && !saved.ChangeEntered ||
                saved.Stores.Count != 0 && !saved.WorldspaceReturned)
                throw new InvalidDataException("Saved-position receipt changed command-time position/space, failed stores or callback order.");
            if (saved.PositionRead is { } positionRead)
            { ValidatePositionBorrow(positionRead, operation.Invocation, source, stack); if (saved.Position != positionRead.Position) throw new InvalidDataException("Saved XYZ differs from its actual borrowed read."); }
            if (saved.ParentRead is { } parentRead)
            { ValidateParentBorrow(parentRead, operation.Invocation, source, stack); if (!saved.ParentCellReturned || saved.ParentCell != parentRead.Cell) throw new InvalidDataException("Saved CELL differs from its actual borrowed read."); }
            if (saved.WorldspaceRead is { } worldspaceRead)
            {
                ValidateWorldspaceBorrow(worldspaceRead, operation.Invocation, source, stack);
                if (!saved.WorldspaceReturned || saved.Worldspace != worldspaceRead.Worldspace || saved.ParentRead != worldspaceRead.Parent ||
                    saved.PositionRead is { } xyz && (xyz.Process != worldspaceRead.Process || xyz.Factory != worldspaceRead.Factory || xyz.Constructor != worldspaceRead.Constructor))
                    throw new InvalidDataException("Saved Worldspace/XYZ/CELL borrows changed their actual joined reference owners.");
            }
            if (saved.RotationRead is { } read && (read.Source != source.Controls.Identity || read.Stack != stack ||
                read.Process == Guid.Empty || read.Factory == Guid.Empty || read.Constructor == Guid.Empty ||
                read.Reference != operation.Invocation.Actor.Reference || read.Base != operation.Invocation.Actor.Base ||
                read.Sequence < 0 || saved.RotationZ != read.ZBits))
                throw new InvalidDataException("Saved-position rotation omitted its actual independent reference source/lifetime.");
            var stored = after.PendingSavedPosition ?? after.SavedPosition;
            if (saved.Stores.Any(value => value.Field == FalloutPackageCommandField.SavedSpace) &&
                (stored!.Space != (saved.Worldspace ?? saved.ParentCell) || saved.ChangeEntered && stored.Position != saved.Position ||
                before.SavedPosition is { } previous && stored.RotationZ != previous.RotationZ))
                throw new InvalidDataException("Saved-position update changed its source space/XYZ or rewrote retained rotation.");
        }
    }
    private static void RequireReceipt(FalloutPackageCommandFieldInvocation invocation, long entered, long changed,
        bool returned, string? failure, FalloutPackageCommandFieldOperation operation)
    {
        if (invocation != operation.Invocation || entered != operation.Entered || changed != operation.Changed || returned != operation.Returned || failure != operation.Failure)
            throw new InvalidDataException("Package command child receipt belongs to another entered source operation.");
    }
    private static IReadOnlyList<Guid> EventTargets(FalloutPackageScriptExtra? extra, FalloutFormKey package)
    {
        if (extra?.EventList is null) return [];
        Guid? selected = null, empty = null; var targets = new List<Guid>(2);
        foreach (var row in extra.Rows)
        {
            if (row is null) break;
            if (row.Form == package) { selected = row.Identity; break; }
            if (row.Form is null) empty = row.Identity;
        }
        if ((selected ?? empty) is { } found) targets.Add(found);
        foreach (var row in extra.Rows)
        { if (row is null) break; if (row.Form is null) { targets.Add(row.Identity); break; } }
        return targets;
    }
    private static bool EquivalentValues(FalloutPackageCommandFieldValues first, FalloutPackageCommandFieldValues second)
    {
        if ((first with { ScriptExtra = null, PendingScriptExtra = null }) != (second with { ScriptExtra = null, PendingScriptExtra = null })) return false;
        if (!EquivalentExtra(first.PendingScriptExtra, second.PendingScriptExtra)) return false;
        return EquivalentExtra(first.ScriptExtra, second.ScriptExtra);
    }
    private static bool EquivalentExtra(FalloutPackageScriptExtra? first, FalloutPackageScriptExtra? second)
    {
        if (first is not { } a) return second is null;
        return second is { } b && a.Identity == b.Identity && a.Script == b.Script && a.EventList == b.EventList &&
            a.EventFactory == b.EventFactory && a.Rows.SequenceEqual(b.Rows);
    }
}
