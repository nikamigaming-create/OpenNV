using System.Buffers.Binary;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private FalloutPluginStack? _aiStack;
    private FalloutActorTemplateSelection? _templates;
    private FalloutQuestState? _questState;
    private FalloutGameTime? _aiClock;
    private FalloutGlobalState? _aiGlobals;
    private FalloutScheduleTime? _aiScheduleTime;
    private double _aiPollRemaining;
    private FalloutFormKey? _failedPackage;
    private FalloutCellScene? _aiCell;
    private FalloutReferenceWorld? _aiWorld;
    private FalloutReferenceInstance? _aiReferenceState;
    private Func<FalloutFormKey?>? _currentPackageQuery;
    private Func<FalloutActorPackageAssignment?>? _packageAssignmentCapture;
    internal void UpdateResidentScene(FalloutCellScene cell) => _aiCell = cell;
    private Func<FalloutPlacedReference, Transform3D>? _referenceTransform;
    private FalloutPluginRecord? _aiPackage;
    private FalloutFurnitureIdleTree? _furnitureIdles;
    private FalloutFurnitureSeat? _seat;
    private FalloutFormKey? _furnitureReference;
    private long _aiQuestRevision = -1;
    private string? _aiError;
    private int _sitting;
    private FalloutScriptPackage? _packageIdleSource;
    private FalloutPackageEvents? _packageEvents;
    internal Action<FalloutPackageEvent, FalloutFormKey>? ExecutePackageEvent { get; set; }
    private FalloutIdleCollectionPlayback? _packageIdles;
    private FalloutIdleConditions? _idleConditions;
    private IReadOnlyDictionary<FalloutFormKey, sbyte> _factions = new Dictionary<FalloutFormKey, sbyte>();
    private Func<IReadOnlyDictionary<FalloutFormKey, sbyte>>? _liveFactions;
    private string? _packageIdleError;
    private readonly FalloutSoundRandomState _aiRandom = new(BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(sizeof(ulong))));
    private FalloutPluginRecord? _pendingPackage;
    internal FalloutActorActivityState Activity { get; } = new();
    internal bool WeaponDrawn => Activity.WeaponDrawn;
    private long _aiActivityRevision = -1;
    internal string? PackageIdleError => _packageIdleError;
    internal string? PackageEventError => _packageEvents?.Error;
    internal string? AiError => _aiError;
    internal int SittingState => _sitting == 1 ? 3 : _sitting;
    internal FalloutFormKey? CurrentFurniture => _sitting is 1 or 2 or 4 ? _furnitureReference : null;
    internal bool Traveling => _travelActive || (_escortPackage is not null || _editorTravel is not null || _dialogueNativeMovement) && Combat?.PackageMoving == true;
    internal FalloutFormKey? CurrentPackage => _sourceSelectionKnown ? _selectedSourcePackage : _aiPackage?.FormKey;

    // These are script-visible engine procedure codes. The currently owned
    // travel procedure ends on arrival; furniture exit has its own transition.
    private int CurrentAiProcedure => _requestedSelection is not null || _aiError is not null
        ? throw new NotSupportedException("Current AI procedure has no admitted native continuation.")
        : _sitting == 2
        ? throw new NotSupportedException("Furniture entry needs its native script-visible procedure code.")
        : _escortPackage is not null ? _escortProgress?.Complete == true ? 17 : _escortStatus switch
        {
            "ApproachTarget" => 19,
            "WaitForTarget" or "waiting-for-target-residency" => 3,
            _ => 12,
        } : _aiPackage is null ? 0 : _sitting == 4 ? 21 : _travelActive || _editorTravelProgress is { Complete: false } ? 0 : 17;
    private int CurrentAiPackage => _requestedSelection is not null || _aiError is not null
        ? throw new NotSupportedException("Current AI package code has no admitted native continuation.")
        : _packageIdleSource is null ? 0 : _packageIdleSource.Procedure switch
        {
            2 => 2,
            6 => 14, // Source PACK travel type -> script-visible Travel package.
            13 => 37, // Fallout script-visible Patrol code (not the PACK type).
            _ => throw new NotSupportedException("Current package condition needs its active procedure owner."),
        };

    internal object AiState => new
    {
        package = _aiPackage?.FormKey.ToString(),
        selectedPackage = CurrentPackage?.ToString(),
        evaluationPending = _requestedSelection is not null,
        furniture = _furnitureReference?.ToString(),
        marker = _seat?.MarkerId,
        sitting = SittingState,
        furniturePhase = _furnitureApproaching ? "approaching" : _sitting switch
        {
            1 => "occupied",
            2 => "entering",
            4 => "exiting",
            _ => "none",
        },
        furnitureInitialPlacement = _furnitureInitialPlacement,
        pendingPackage = _pendingPackage?.FormKey.ToString(),
        packageEvents = _packageEvents is null ? null : new
        {
            package = _packageEvents.Active?.Form.ToString(),
            _packageEvents.Done,
            _packageEvents.Revision,
            _packageEvents.LastEvent,
            lastPackage = _packageEvents.LastPackage?.ToString(),
            _packageEvents.Error,
        },
        randomState = _aiRandom.State.ToString("x16", System.Globalization.CultureInfo.InvariantCulture),
        randomOwner = "opennv-authoritative-retail-stream-unmatched",
        activity = new
        {
            Activity.Alerted,
            Activity.Attacked,
            Activity.WeaponDrawn,
            Activity.Running,
            Activity.Sneaking,
            Activity.InCombat,
            Activity.Revision,
        },
        factions = _factions.Select(value => new { faction = value.Key.ToString(), rank = value.Value }).ToArray(),
        currentProcedure = _sitting == 2 || _requestedSelection is not null || _aiError is not null ? (int?)null : CurrentAiProcedure,
        navigation = TravelState,
        escort = EscortState,
        editorTravel = EditorTravelState,
        patrol = _patrol is null ? null : new { source = _patrol.SourceSha256, points = _patrol.Points.Count, progress = _patrolProgress, status = _patrolStatus },
        dialoguePackage = _dialoguePackage is null ? null : new
        {
            source = _dialoguePackage,
            requested = _dialoguePackageRequested,
            waitReached = _dialogueWaitReached,
            nativeTargetMovement = _dialogueNativeMovement,
            unbound = new[] { "package-camera-zoom", "package-head-target-priority", "pre-conversation-target-movement" }
        },
        idleCollection = _packageIdleSource is null ? null : new
        {
            source = _packageIdleSource.Form.ToString(),
            flags = _packageIdleSource.IdleFlags,
            timer = _packageIdleSource.IdleTimer,
            idles = _packageIdleSource.Idles.Select(value => value.ToString()).ToArray(),
            owner = _packageIdleSource.Idles.Count == 0 ? "empty-source-collection" : "source-collection-clock",
            conditions = "candidate-then-source-parents",
            lastConditionDecision = _idleConditions?.LastDecision is not { } decision ? null : new
            {
                candidate = decision.Candidate.ToString(),
                decision.Eligible,
                stoppedAt = decision.StoppedAt?.ToString(),
                decision.ConditionsEvaluated,
            },
            waitSeconds = _packageIdles?.WaitSeconds,
            complete = _packageIdles?.Complete,
            cursor = _packageIdles?.Cursor,
            error = _packageIdleError,
        },
        error = _aiError,
        scheduleTime = _aiScheduleTime,
        evaluationPolicy = "quest-activity-hour-changes-and-ten-second-poll;retail-cadence-unmatched",
        referencePackageEventOwner = _aiWorld is null ? "unbound-no-reference-world" : "shared-reference-world",
        unbound = new[] { "retail-navigation-timing", "furniture-entry-script-procedure-code", "furniture-idle-variations", "idle-internal-loop-counts", "head-eye-aiming", "actor-save-restoration", "combat-event-dispatch" },
    };

    internal void ConfigureAi(FalloutPluginStack stack, FalloutQuestState quests, FalloutCellScene cell,
        Func<FalloutPlacedReference, Transform3D> referenceTransform,
        Func<IReadOnlyDictionary<FalloutFormKey, sbyte>>? factions = null,
        FalloutGameTime? clock = null, FalloutGlobalState? globals = null, FalloutReferenceWorld? world = null)
    {
        _aiStack = stack;
        _idleConditions = new(stack);
        _factions = FalloutAiPackages.ReadFactions(stack, Appearance.Npc, _templates);
        _liveFactions = factions;
        _questState = quests;
        _aiClock = clock; _aiGlobals = globals;
        _aiCell = cell;
        _aiWorld = world;
        _bindingInitialBase = true;
        if (world is not null)
        {
            _aiReferenceState = world.Get(Appearance.Reference!.Value);
            _baseClock = _aiReferenceState.Animation;
            _aiReferenceState.QueryCurrentPackage = _currentPackageQuery = () => CurrentPackage;
        }
        _referenceTransform = referenceTransform;
        _packageEvents = new(DispatchPackageEvent);
        if (world?.UnloadedPackages is { } unloaded)
            unloaded.BindNative(Appearance.Reference!.Value, _packageEvents);
        else if (_aiReferenceState?.PackageAssignment is { } retained && _aiReferenceState.PackageMotion?.Package != retained.Package)
            retained.Bind(stack, _packageEvents);
        if (_aiReferenceState is { } packageState)
            packageState.CapturePackageAssignment = _packageAssignmentCapture = () => FalloutActorPackageAssignment.Capture(stack, _packageEvents);
        // A stationary, unarmed actor owns its source movement-group idle
        // independently of package selection. An unsupported package must not
        // erase that motion owner and leave the skeleton in its bind pose.
        PlayLocomotion(moving: false);
        AdvanceAi(initializing: true);
        _bindingInitialBase = false;
        _baseClock.Bind(_baseResource, _baseHash);
        ResumeBaseClock();
    }

    private void DispatchPackageEvent(FalloutScriptPackage package, string kind)
    {
        // The source process marks the actual actor before executing its PACK
        // result. Its attached script consumes these marks in declaration order
        // on the normal source frame, including events produced before 3D binds.
        if (_aiWorld is { } world)
        {
            if (kind == "POBA") world.MarkPackageStart(Appearance.Reference!.Value, _aiStack!.GetEffective(package.Form), _aiClock);
            world.PackageEvents.Mark(Appearance.Reference!.Value, package.Form, kind switch
            {
                "POBA" => FalloutReferencePackageEventKind.Start,
                "POEA" => FalloutReferencePackageEventKind.Done,
                "POCA" => FalloutReferencePackageEventKind.Change,
                _ => throw new InvalidDataException("Package lifecycle event kind is unknown."),
            });
        }
        try { DispatchPackageActions(package, kind); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or FileNotFoundException)
        {
            // An unsupported embedded result cannot release the attached actor
            // script's suffix. Keep the same source fault with the world owner,
            // even if this actor's native presentation is later evicted.
            if (_aiWorld is { } failedWorld)
                failedWorld.Get(Appearance.Reference!.Value).ScriptError ??= $"Package {kind} {package.Form}: {error.Message}";
            throw;
        }
    }

    private void DispatchPackageActions(FalloutScriptPackage package, string kind)
    {
        // Result scripts precede the event's topic and idle. An unsupported
        // reached effect keeps this event failed, rather than replaying it.
        if (package.EventPrograms.GetValueOrDefault(kind) is { } program)
        {
            if (ExecutePackageEvent is { } execute) execute(program, Appearance.Reference!.Value);
            else
            {
                var commands = FalloutHeadTrackingPrograms.Bind(program.Source,
                    new(_aiStack!, program.Package, program.Package, program.Fields), Appearance.Reference).ToDictionary(command => command.Line);
                var index = 0;
                program.ExecuteScript(line =>
                {
                    if (!commands.TryGetValue(index++, out var command))
                        throw new NotSupportedException($"Package event command is unbound: {line}");
                    ApplyBoundHeadTrackingCommand(command);
                });
            }
        }
        if (package.Events.GetValueOrDefault(kind) is { } idle)
        {
            // A change event selects its source pose without an invented completion
            // barrier. Its IDLE may loop forever; a later event can replace it.
            PlayIdle(_aiStack!, idle, "package-event");
        }
        GD.Print($"OPENNV_NATIVE_PACKAGE_EVENT reference={Appearance.Reference} package={package.Form} event={kind} owner=actor-procedure");
    }

    private double PreparePackageIdle(double delta)
    {
        if (_animation is not null || _responseIdleActive || _packageIdles is null || _packageIdleError is not null ||
            _aiError is not null || _sitting is 2 or 4 || _travelActive || _escortPackage is not null && _escortProgress?.Complete != true ||
            _patrol is not null && _patrolProgress?.Arrived != true ||
            _editorTravel is not null && _editorTravelProgress?.Complete != true) return delta;
        var remaining = _packageIdles.AdvanceWait(delta);
        try
        {
            if (_packageIdles.Select() is not { } idle) return remaining;
            PlayIdle(_aiStack!, idle, "package-idle");
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or FileNotFoundException)
        {
            _packageIdleError = error.Message;
            GD.PushError($"OPENNV_NATIVE_PACKAGE_IDLE_DIVERGENCE reference={Appearance.Reference}: {error.Message}");
        }
        return remaining;
    }

    private void AdvanceAi(bool initializing = false, double delta = 0)
    {
        if (_aiStack is null || _questState is null) return;
        if (NpcDialogueActive?.Invoke() == true) return;
        _aiPollRemaining -= delta;
        var scheduleTime = _aiClock?.ScheduleTime();
        if (scheduleTime is { } moment) scheduleTime = moment with { Hour = MathF.Floor(moment.Hour) };
        if (_requestedSelection is null && _aiQuestRevision == _questState.Revision && _aiActivityRevision == Activity.Revision &&
            _aiScheduleTime == scheduleTime && _aiPollRemaining > 0) return;
        _aiQuestRevision = _questState.Revision;
        _aiActivityRevision = Activity.Revision;
        _aiScheduleTime = scheduleTime;
        _aiPollRemaining = 10;
        FalloutPluginRecord? selected = null;
        var forced = _requestedSelection is not null;
        try
        {
            var selection = _requestedSelection ?? SelectSourcePackage();
            _requestedSelection = null;
            selected = selection.Record;
            _selectedSourcePackage = selected?.FormKey; _sourceSelectionKnown = true;
            if (!forced && _aiError is not null && selected is not null && _failedPackage == selected.FormKey) return;
            // A failed procedure cannot freeze a later eligible package. Event
            // errors retain their separate exactly-once failure latch.
            _aiError = null; _failedPackage = null;
            if (_aiPackage?.FormKey == selected?.FormKey) return;
            if (!forced && _editorTravel is { MustComplete: true } && _editorTravelProgress?.Complete != true)
                throw new NotSupportedException("Incomplete editor travel needs its must-complete package reevaluation owner.");
            if (_sitting is 2 or 4) { _pendingPackage = selected; return; }
            if (_sitting == 1 && selected is not null && RetainFurniturePackage(selected)) return;
            if (_aiPackage is not null)
            {
                if (_seat is not null && !_furnitureApproaching)
                {
                    _pendingPackage = selected;
                    CancelIdle();
                    _furnitureOccupied = Transform;
                    _sitting = 4;
                    StartFurnitureAnimation();
                    return;
                }
                CancelIdle();
                _packageEvents!.Change(null);
                _aiPackage = null;
                _packageIdleSource = null;
                _packageIdles = null;
                _travelProgress?.Cancel();
                ClearFurniture();
                ClearDialoguePackage();
                _patrol = null; _patrolProgress = null;
                if ((_escortPackage is not null || _editorTravel is not null) && _aiWorld is { } escortWorld)
                    escortWorld.Get(Appearance.Reference!.Value).ProcedureCaptureBlocker = null;
                _escortPackage = null; _escortProgress = null; _escortDestination = null; _escortStatus = null;
                _editorTravel = null; _editorTravelProgress = null; _editorTravelDestination = null; _editorTravelStatus = null;
            }
            if (selected is null) return;
            _packageIdleSource = selection.Declaration!;
            _packageIdles = new(_packageIdleSource, _idleReplays,
                idle => _idleConditions!.AllPass(idle, EvaluateAiCondition));
            if (_packageIdleSource.Procedure == 2) { BeginEscort(selected, initializing); return; }
            if (_packageIdleSource.Procedure == 13) { BeginPatrol(selected); return; }
            if (_packageIdleSource is { Procedure: 6, LocationType: 3 }) { BeginEditorTravel(selected, initializing); return; }
            if (_packageIdleSource.Procedure == 15)
            {
                FalloutPlacedReference? wait = null;
                if (_packageIdleSource.LocationType is not null)
                {
                    if (_packageIdleSource.LocationType is not (0 or 2) || _packageIdleSource.LocationRadius != 0)
                        throw new NotSupportedException($"PACK {selected.FormKey} requires its dialogue location owner.");
                    if (_packageIdleSource.LocationType == 0)
                        wait = _aiCell!.References.SingleOrDefault(value => value.FormKey == _packageIdleSource.LocationReference) ??
                            throw new NotSupportedException($"PACK {selected.FormKey} dialogue wait location is outside the active cell.");
                }
                BeginDialoguePackage(selected, wait, initializing);
                return;
            }
            var fields = selected.ReadSubrecords().ToArray();
            var data = fields.Single(field => field.Signature == "PKDT").Data;
            var location = fields.Single(field => field.Signature == "PLDT").Data;
            if (data.Length != 12 || location.Length != 12) throw new InvalidDataException("AI package has an invalid field extent.");
            if (data.Span[4] != 6 || BinaryPrimitives.ReadInt32LittleEndian(location.Span) != 0)
                throw new NotSupportedException($"PACK {selected.FormKey} requires its travel/procedure owner.");
            var target = selected.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(location.Span[4..]));
            var reference = _aiCell!.References.SingleOrDefault(value => value.FormKey == target) ??
                throw new NotSupportedException($"PACK {selected.FormKey} target {target} is outside the active cell.");
            var furniture = _aiStack.GetEffective(reference.Base);
            if (furniture.Signature != "FURN")
            {
                StartTravel(selected, reference, destinationRadiusGameUnits: _packageIdleSource.LocationRadius);
                _aiPackage = selected;
                _packageEvents!.Change(_packageIdleSource);
                CompletePendingTravel();
                return;
            }
            if (_packageIdleSource.LocationRadius != 0)
                throw new NotSupportedException($"PACK {selected.FormKey} requires its furniture location-radius owner.");
            BeginFurniturePackage(selected, reference, furniture, initializing);
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or FileNotFoundException or InvalidOperationException)
        {
            var changed = _aiError != error.Message;
            _aiError = error.Message;
            _failedPackage = selected?.FormKey;
            BlockSelectionCapture($"Actor package procedure remains unbound: {error.Message}");
            if (changed) GD.PushError($"OPENNV_NATIVE_AI_DIVERGENCE reference={Appearance.Reference}: {error.Message}");
        }
        finally
        {
            if (_requestedSelection is null && _aiError is null && _pendingPackage is null &&
                _aiPackage?.FormKey == _selectedSourcePackage) ClearSelectionCapture();
        }
    }

    internal float EvaluateAiCondition(FalloutCondition condition)
    {
        if (condition.Function == 50)
            return FalloutAiPackages.HasTalkedToPlayer(condition, Appearance.Reference!.Value,
                reference => (_aiWorld ?? throw new NotSupportedException("AI talked-to-player query has no shared reference owner."))
                    .Get(reference).TalkedToPlayer) ? 1 : 0;
        if (condition.Function == 289)
            return (Combat ?? throw new NotSupportedException("AI combat query has no engagement owner."))
                .IsInCombat(FalloutAiPackages.ConditionSubject(condition, Appearance.Reference!.Value)) ? 1 : 0;
        if (condition.Function == 161)
            return FalloutAiPackages.IsCurrentPackage(condition, Appearance.Reference!.Value, CurrentPackage,
                reference => (_aiWorld ?? throw new NotSupportedException("AI current-package query has no reference world."))
                    .CurrentPackage(reference)) ? 1 : 0;
        if (condition.RunOn == 0) return EvaluateOwnAiCondition(condition);
        throw new NotSupportedException($"AI condition {condition.Owner.FormKey}/{condition.Function}/{condition.RunOn} has no subject owner.");
    }

    private float EvaluateOwnAiCondition(FalloutCondition condition) => condition.Function switch
    {
        18 => (_aiClock ?? throw new NotSupportedException("AI time query has no simulation clock.")).Hour,
        74 => (_aiGlobals ?? throw new NotSupportedException("AI global query has no state owner.")).Get(condition.FormArgument1),
        25 => _travelActive || Combat?.PackageMoving == true ? 1 : 0,
        53 => (float)(_aiWorld ?? throw new NotSupportedException("AI script-variable query has no shared reference owner."))
            .ReadVariable(_questState!, condition.FormArgument1, condition.Argument2),
        58 or 59 or 79 or 546 => _questState!.Evaluate(condition),
        63 => Activity.Attacked ? 1 : 0,
        69 => Appearance.Race == condition.FormArgument1 ? 1 : 0,
        70 => (Appearance.Female ? 1u : 0u) == condition.Argument1 ? 1 : 0,
        71 => (_liveFactions?.Invoke() ?? _factions).GetValueOrDefault(condition.FormArgument1, (sbyte)-1) >= 0 ? 1 : 0,
        73 => (_liveFactions?.Invoke() ?? _factions).GetValueOrDefault(condition.FormArgument1, (sbyte)-1),
        72 => Appearance.Npc == condition.FormArgument1 ? 1 : 0,
        77 => _aiRandom.NextBounded(100),
        91 => Activity.Alerted ? 1 : 0,
        101 => WeaponDrawn ? 1 : 0,
        107 => Combat?.KnockedDown == true ? 2 : 0,
        108 => Combat?.WeaponAnimationType ?? 0,
        110 => CurrentAiPackage,
        143 => CurrentAiProcedure,
        159 => SittingState,
        160 => _seat?.MarkerId ?? 0,
        162 => _furnitureReference == condition.FormArgument1 ? 1 : 0,
        163 => _seat?.Furniture == condition.FormArgument1 ? 1 : 0,
        182 => Appearance.EquippedArmor.Contains(condition.FormArgument1) ? 1 : 0,
        286 => Activity.Sneaking ? 1 : 0,
        287 => Activity.Running ? 1 : 0,
        300 when condition.RunOn == 0 => (_aiWorld ?? throw new NotSupportedException("AI interior query has no world owner."))
            .IsInInterior(Appearance.Reference ?? throw new NotSupportedException("AI interior query has no placed reference.")) ? 1 : 0,
        365 => FalloutRaceProperties.IsChild(_aiStack!.GetEffective(Appearance.Race)) ? 1 : 0,
        392 => 0, // This owner is an NPC; the player's view cannot be its first-person view.
        247 => 0, // No acquired item is bound to this furniture procedure.
        _ => throw new NotSupportedException($"AI condition {condition.Owner.FormKey}/{condition.Function} has no authoritative owner."),
    };
}
