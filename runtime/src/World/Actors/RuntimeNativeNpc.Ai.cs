using System.Buffers.Binary;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private FalloutPluginStack? _aiStack;
    private FalloutQuestState? _questState;
    private FalloutCellScene? _aiCell;
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
    private FalloutIdleCollectionPlayback? _packageIdles;
    private FalloutIdleConditions? _idleConditions;
    private IReadOnlyDictionary<FalloutFormKey, sbyte> _factions = new Dictionary<FalloutFormKey, sbyte>();
    private string? _packageIdleError;
    private readonly FalloutSoundRandomState _aiRandom = new(BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(sizeof(ulong))));
    private FalloutPluginRecord? _pendingPackage;
    internal FalloutActorActivityState Activity { get; } = new();
    internal bool WeaponDrawn => Activity.WeaponDrawn;
    private long _aiActivityRevision = -1;
    internal string? PackageIdleError => _packageIdleError;
    internal string? AiError => _aiError;
    internal int SittingState => _sitting;
    internal bool Traveling => _travelActive;
    internal FalloutFormKey? CurrentPackage => _aiPackage?.FormKey;

    // These are script-visible engine procedure codes. The currently owned
    // travel procedure ends on arrival; furniture exit has its own transition.
    private int CurrentAiProcedure => _sitting == 2
        ? throw new NotSupportedException("Furniture entry needs its native script-visible procedure code.")
        : _aiPackage is null ? 0 : _sitting == 4 ? 21 : _travelActive ? 0 : 17;
    private int CurrentAiPackage => _packageIdleSource is null ? 0 : _packageIdleSource.Procedure switch
    {
        6 => 14, // Source PACK travel type -> script-visible Travel package.
        _ => throw new NotSupportedException("Current package condition needs its active procedure owner."),
    };

    internal object AiState => new
    {
        package = _aiPackage?.FormKey.ToString(),
        furniture = _furnitureReference?.ToString(),
        marker = _seat?.MarkerId,
        sitting = _sitting,
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
        currentProcedure = _sitting == 2 ? (int?)null : CurrentAiProcedure,
        navigation = TravelState,
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
        unbound = new[] { "retail-navigation-timing", "furniture-entry-script-procedure-code", "furniture-idle-variations", "idle-internal-loop-counts", "head-eye-aiming", "actor-save-restoration", "combat-event-dispatch" },
    };

    internal void ConfigureAi(FalloutPluginStack stack, FalloutQuestState quests, FalloutCellScene cell,
        Func<FalloutPlacedReference, Transform3D> referenceTransform)
    {
        _aiStack = stack;
        _idleConditions = new(stack);
        _factions = FalloutAiPackages.ReadFactions(stack, Appearance.Npc);
        _questState = quests;
        _aiCell = cell;
        _referenceTransform = referenceTransform;
        _packageEvents = new(DispatchPackageEvent);
        AdvanceAi(initializing: true);
    }

    private void DispatchPackageEvent(FalloutScriptPackage package, string kind)
    {
        // Result scripts precede the event's topic and idle. An unsupported
        // reached effect keeps this event failed, rather than replaying it.
        if (package.EventPrograms.GetValueOrDefault(kind) is { } program)
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
        if (package.Events.GetValueOrDefault(kind) is { } idle)
        {
            if (kind == "POCA")
                throw new NotSupportedException("Package-change idle needs deferred replacement ownership.");
            PlayIdle(_aiStack!, idle, "package-event");
        }
        GD.Print($"OPENNV_NATIVE_PACKAGE_EVENT reference={Appearance.Reference} package={package.Form} event={kind} owner=actor-procedure");
    }

    internal void EvaluatePackages(bool reset)
    {
        if (_aiStack is null || _questState is null)
            throw new NotSupportedException("Actor package commands require the live AI owner.");
        // Reset requests a fresh source selection; it never grants arrival or
        // changes the actor's transform. Procedure transitions own those steps.
        if (reset) { _aiError = null; _packageIdleError = null; }
        _aiQuestRevision = -1;
        AdvanceAi();
        if (_aiError is not null) throw new NotSupportedException(_aiError);
    }

    private double PreparePackageIdle(double delta)
    {
        if (_animation is not null || _responseIdleActive || _packageIdles is null || _packageIdleError is not null ||
            _aiError is not null || _sitting is 2 or 4 || _travelActive) return delta;
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

    private void AdvanceAi(bool initializing = false)
    {
        if (_aiStack is null || _questState is null || _aiError is not null ||
            _aiQuestRevision == _questState.Revision && _aiActivityRevision == Activity.Revision) return;
        _aiQuestRevision = _questState.Revision;
        _aiActivityRevision = Activity.Revision;
        try
        {
            var selected = FalloutAiPackages.Select(_aiStack, Appearance.Npc, EvaluateAiCondition);
            if (_aiPackage?.FormKey == selected?.FormKey) return;
            if (_sitting is 2 or 4) { _pendingPackage = selected; return; }
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
                _travelActive = false;
                ClearFurniture();
            }
            if (selected is null) return;
            _packageIdleSource = FalloutScriptPackage.Read(selected);
            _packageIdles = new(_packageIdleSource, _idleReplays,
                idle => _idleConditions!.AllPass(idle, EvaluateAiCondition));
            var fields = selected.ReadSubrecords().ToArray();
            var data = fields.Single(field => field.Signature == "PKDT").Data;
            var location = fields.Single(field => field.Signature == "PLDT").Data;
            if (data.Length != 12 || location.Length != 12) throw new InvalidDataException("AI package has an invalid field extent.");
            if (data.Span[4] != 6 || BinaryPrimitives.ReadInt32LittleEndian(location.Span) != 0 ||
                BinaryPrimitives.ReadInt32LittleEndian(location.Span[8..]) != 0)
                throw new NotSupportedException($"PACK {selected.FormKey} requires its travel/procedure owner.");
            var target = selected.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(location.Span[4..]));
            var reference = _aiCell!.References.SingleOrDefault(value => value.FormKey == target) ??
                throw new NotSupportedException($"PACK {selected.FormKey} target {target} is outside the active cell.");
            var furniture = _aiStack.GetEffective(reference.Base);
            if (furniture.Signature != "FURN")
            {
                StartTravel(selected, reference);
                _aiPackage = selected;
                _packageEvents!.Change(_packageIdleSource);
                return;
            }
            BeginFurniturePackage(selected, reference, furniture, initializing);
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or FileNotFoundException or InvalidOperationException)
        {
            _aiError = error.Message;
            GD.PushError($"OPENNV_NATIVE_AI_DIVERGENCE reference={Appearance.Reference}: {error.Message}");
        }
    }

    private float EvaluateAiCondition(FalloutCondition condition) => condition.Function switch
    {
        58 or 59 or 79 or 546 => _questState!.Evaluate(condition),
        63 => Activity.Attacked ? 1 : 0,
        69 => Appearance.Race == condition.FormArgument1 ? 1 : 0,
        70 => (Appearance.Female ? 1u : 0u) == condition.Argument1 ? 1 : 0,
        71 => _factions.TryGetValue(condition.FormArgument1, out var rank) && rank >= 0 ? 1 : 0,
        72 => Appearance.Npc == condition.FormArgument1 ? 1 : 0,
        77 => _aiRandom.NextBounded(100),
        91 => Activity.Alerted ? 1 : 0,
        101 => WeaponDrawn ? 1 : 0,
        110 => CurrentAiPackage,
        143 => CurrentAiProcedure,
        159 => _sitting,
        160 => _seat?.MarkerId ?? 0,
        162 => _furnitureReference == condition.FormArgument1 ? 1 : 0,
        163 => _seat?.Furniture == condition.FormArgument1 ? 1 : 0,
        182 => Appearance.EquippedArmor.Contains(condition.FormArgument1) ? 1 : 0,
        286 => Activity.Sneaking ? 1 : 0,
        287 => Activity.Running ? 1 : 0,
        289 => Activity.InCombat ? 1 : 0,
        365 => FalloutRaceProperties.IsChild(_aiStack!.GetEffective(Appearance.Race)) ? 1 : 0,
        392 => 0, // This owner is an NPC; the player's view cannot be its first-person view.
        247 => 0, // No acquired item is bound to this furniture procedure.
        _ => throw new NotSupportedException($"AI condition {condition.Owner.FormKey}/{condition.Function} has no authoritative owner."),
    };
}
