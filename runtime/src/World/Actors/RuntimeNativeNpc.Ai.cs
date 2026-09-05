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
    internal bool WeaponDrawn { get; private set; }

    internal object AiState => new
    {
        package = _aiPackage?.FormKey.ToString(),
        furniture = _furnitureReference?.ToString(),
        marker = _seat?.MarkerId,
        sitting = _sitting,
        idleCollection = _packageIdleSource is null ? null : new
        {
            source = _packageIdleSource.Form.ToString(),
            flags = _packageIdleSource.IdleFlags,
            timer = _packageIdleSource.IdleTimer,
            idles = _packageIdleSource.Idles.Select(value => value.ToString()).ToArray(),
            owner = _packageIdleSource.Idles.Count == 0 ? "empty-source-collection" : "unbound-idle-playback",
        },
        error = _aiError,
        unbound = new[] { "travel-navigation", "furniture-idle-variations", "package-idle-rng", "head-eye-aiming" },
    };

    internal void ConfigureAi(FalloutPluginStack stack, FalloutQuestState quests, FalloutCellScene cell,
        Func<FalloutPlacedReference, Transform3D> referenceTransform)
    {
        _aiStack = stack;
        _questState = quests;
        _aiCell = cell;
        _referenceTransform = referenceTransform;
        AdvanceAi();
    }

    private void AdvanceAi()
    {
        if (_aiStack is null || _questState is null || _aiError is not null || _aiQuestRevision == _questState.Revision) return;
        _aiQuestRevision = _questState.Revision;
        try
        {
            var selected = FalloutAiPackages.Select(_aiStack, Appearance.Npc, EvaluateAiCondition);
            if (_aiPackage?.FormKey == selected?.FormKey) return;
            if (_aiPackage is not null)
            {
                if (_seat is not null)
                {
                    _sitting = 4;
                    StartFurnitureAnimation();
                    _aiError = $"Selected next PACK {selected?.FormKey}: exit root transfer and navigation remain unbound.";
                    GD.PushError($"OPENNV_NATIVE_AI_DIVERGENCE reference={Appearance.Reference}: {_aiError}");
                    return;
                }
                throw new NotSupportedException("Package transition requires source travel ownership.");
            }
            if (selected is null) return;
            _packageIdleSource = FalloutScriptPackage.Read(selected);
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
            if (furniture.Signature != "FURN") throw new NotSupportedException($"PACK {selected.FormKey} needs reference-marker travel ownership.");
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
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or FileNotFoundException)
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
        var animation = new RuntimeNativeNifAnimation(nif, sequences[0], Skeleton);
        // The actor/package owns the accumulation frame. A furniture sequence
        // poses NonAccum beneath it; the skeleton file's preview placement is
        // not an additional actor displacement.
        var accumulation = Skeleton.BoneIndex(animation.Sequence.TargetName);
        if (animation.Sequence.ControlledBlocks.Any(link => link.NodeName == animation.Sequence.TargetName))
            throw new NotSupportedException("Furniture accumulation channel requires root-motion extraction.");
        Skeleton.Node.SetBonePose(accumulation, Transform3D.Identity);
        animation.ApplySourceTime(animation.Sequence.StartTime);
        _baseAnimation = animation;
        _baseAnimationSeconds = animation.Sequence.StartTime;
    }

    private float EvaluateAiCondition(FalloutCondition condition) => condition.Function switch
    {
        58 or 59 or 79 or 546 => _questState!.Evaluate(condition),
        70 => (Appearance.Female ? 1u : 0u) == condition.Argument1 ? 1 : 0,
        72 => Appearance.Npc == condition.FormArgument1 ? 1 : 0,
        101 => WeaponDrawn ? 1 : 0,
        159 => _sitting,
        160 => _seat?.MarkerId ?? 0,
        162 => _furnitureReference == condition.FormArgument1 ? 1 : 0,
        163 => _seat?.Furniture == condition.FormArgument1 ? 1 : 0,
        182 => Appearance.EquippedArmor.Contains(condition.FormArgument1) ? 1 : 0,
        392 => 0, // This owner is an NPC; the player's view cannot be its first-person view.
        247 => 0, // No acquired item is bound to this furniture procedure.
        _ => throw new NotSupportedException($"AI condition {condition.Owner.FormKey}/{condition.Function} has no authoritative owner."),
    };
}
