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
    private FalloutIdleCollectionPlayback? _packageIdles;
    private FalloutIdleConditions? _idleConditions;
    private IReadOnlyDictionary<FalloutFormKey, sbyte> _factions = new Dictionary<FalloutFormKey, sbyte>();
    private string? _packageIdleError;
    private readonly FalloutSoundRandomState _aiRandom = new(BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(sizeof(ulong))));
    private FalloutPluginRecord? _pendingPackage;
    private Transform3D _furnitureExitAnchor;
    private Basis _furnitureOccupiedBasis;
    private Vector3 _furnitureExitStart;
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
    private int CurrentAiProcedure => _aiPackage is null ? 0 : _sitting == 4 ? 21 : _travelActive ? 0 : 17;
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
        pendingPackage = _pendingPackage?.FormKey.ToString(),
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
        currentProcedure = CurrentAiProcedure,
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
        unbound = new[] { "retail-navigation-timing", "furniture-idle-variations", "idle-internal-loop-counts", "head-eye-aiming", "actor-save-restoration", "combat-event-dispatch" },
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
        AdvanceAi();
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
            _aiError is not null || _sitting == 4 || _travelActive) return delta;
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

    private void AdvanceAi()
    {
        if (_aiStack is null || _questState is null || _aiError is not null ||
            _aiQuestRevision == _questState.Revision && _aiActivityRevision == Activity.Revision) return;
        _aiQuestRevision = _questState.Revision;
        _aiActivityRevision = Activity.Revision;
        try
        {
            var selected = FalloutAiPackages.Select(_aiStack, Appearance.Npc, EvaluateAiCondition);
            if (_aiPackage?.FormKey == selected?.FormKey) return;
            if (_sitting == 4) { _pendingPackage = selected; return; }
            if (_aiPackage is not null)
            {
                if (_seat is not null)
                {
                    _pendingPackage = selected;
                    CancelIdle();
                    _furnitureOccupiedBasis = Basis;
                    _furnitureExitAnchor = new Transform3D(Basis * new Basis(Vector3.Up, _seat.HeadingDelta), Position);
                    _sitting = 4;
                    StartFurnitureAnimation();
                    return;
                }
                CancelIdle();
                _aiPackage = null;
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
                return;
            }
            var path = _aiCell.BaseObjects[reference.Base].ModelPath ?? throw new InvalidDataException("Furniture has no model.");
            var content = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned files are absent.");
            if (!content.TryRead(path, null, out var bytes, out _)) throw new FileNotFoundException("Furniture model is absent.", path);
            var seat = FalloutFurnitureSource.Read(_aiStack, furniture, FalloutNifFile.Read(bytes));
            _furnitureIdles ??= new(_aiStack, Appearance.SkeletonPath);
            _seat = seat;
            _sitting = 1;
            StartFurnitureAnimation();
            var units = Skeleton.UnitsToMetres;
            var offset = seat.Marker.Offset;
            var placement = GamebryoPackagePlacement.FromFurnitureMarker(target.ToString(), _referenceTransform!(reference),
                GamebryoCoordinate.ConvertVector(new(offset.X, offset.Y, offset.Z)) * units,
                new Quaternion(Vector3.Up, -seat.Marker.Orientation / 1000.0f),
                GamebryoCoordinate.ConvertVector(new(seat.PlacementOffset[0], seat.PlacementOffset[1], seat.PlacementOffset[2])) * units,
                new Quaternion(Vector3.Up, -seat.HeadingDelta), Scale);
            Transform = placement.SourceTransform;
            _furnitureReference = target;
            _aiPackage = selected;
            GD.Print($"OPENNV_NATIVE_FURNITURE_OCCUPIED reference={Appearance.Reference} package={selected.FormKey} " +
                $"target={target} marker={seat.MarkerId} sourceIndex={seat.Index} animation={_baseAnimation!.Sequence.Name} parity=unmeasured");
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or FileNotFoundException or InvalidOperationException)
        {
            _aiError = error.Message;
            GD.PushError($"OPENNV_NATIVE_AI_DIVERGENCE reference={Appearance.Reference}: {error.Message}");
        }
    }

    private void StartFurnitureAnimation()
    {
        var source = FalloutActorIdleSource.Resolve(_aiStack!, _furnitureIdles!.Select(EvaluateAiCondition));
        if (source.Objects.Count != 0) throw new NotSupportedException("Furniture base ANIO requires object ownership.");
        var content = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned files are absent.");
        if (!content.TryRead(source.AnimationPath, null, out var bytes, out _)) throw new FileNotFoundException("Furniture animation is absent.", source.AnimationPath);
        var nif = FalloutNifFile.Read(bytes);
        var sequences = nif.Roots.Select(nif.ReadObject).OfType<FalloutNifControllerSequence>().ToArray();
        if (sequences.Length != 1) throw new NotSupportedException("Furniture KF requires one sequence.");
        Action<FalloutNifAnimationSample>? rootOwner = null;
        if (_sitting == 4)
        {
            var root = sequences[0].ControlledBlocks.SingleOrDefault(link => link.NodeName == sequences[0].TargetName && link.ControllerType == "NiTransformController")
                ?? throw new NotSupportedException("Furniture exit has no authored accumulation channel.");
            var start = new FalloutNifAnimationSampler(nif, root.Interpolator).Sample(sequences[0].StartTime);
            _furnitureExitStart = SourceTranslation(start);
            rootOwner = sample =>
            {
                RequireTranslationOnlyRoot(sample);
                Transform = new(_furnitureExitAnchor.Basis, _furnitureExitAnchor * (SourceTranslation(sample) - _furnitureExitStart));
            };
        }
        var animation = new RuntimeNativeNifAnimation(nif, sequences[0], Skeleton, accumulationRoot: rootOwner);
        // The actor/package owns the accumulation frame. A furniture sequence
        // poses NonAccum beneath it; the skeleton file's preview placement is
        // not an additional actor displacement.
        var accumulation = Skeleton.BoneIndex(animation.Sequence.TargetName);
        if (_sitting != 4 && animation.Sequence.ControlledBlocks.Any(link => link.NodeName == animation.Sequence.TargetName))
            throw new NotSupportedException("Furniture accumulation channel requires root-motion extraction.");
        Skeleton.Node.SetBonePose(accumulation, Transform3D.Identity);
        animation.ApplySourceTime(animation.Sequence.StartTime);
        _baseAnimation = animation;
        _baseAnimationSeconds = animation.Sequence.StartTime;
        _baseElapsedSeconds = 0;
    }

    private void CompleteFurnitureExit()
    {
        if (_sitting != 4) return;
        // The source furniture heading is applied while occupying its marker;
        // the exit's root curve runs in the marker's approach frame.
        Basis = _furnitureOccupiedBasis;
        _seat = null;
        _furnitureReference = null;
        _sitting = 0;
        _aiPackage = null;
        _baseAnimation = null;
        _aiQuestRevision = -1;
        _pendingPackage = null;
        AdvanceAi();
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
        392 => 0, // This owner is an NPC; the player's view cannot be its first-person view.
        247 => 0, // No acquired item is bound to this furniture procedure.
        _ => throw new NotSupportedException($"AI condition {condition.Owner.FormKey}/{condition.Function} has no authoritative owner."),
    };
}
