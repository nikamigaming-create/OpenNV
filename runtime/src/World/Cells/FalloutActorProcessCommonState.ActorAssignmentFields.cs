using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessCommonState
{
    private FalloutPackageCommandFieldInvocation? _actorAssignmentInvocation;
    private long _actorAssignmentFault;
    private readonly Dictionary<(FalloutFormKey Actor, long Epoch), HashSet<Guid>> _actorAssignmentCommands = [];
    private FalloutActorPackageAssignmentCall? _lastActorAssignment;
    private string? _actorAssignmentFailure;
    internal FalloutActorPackageAssignmentCall? LastActorPackageAssignment => _lastActorAssignment;
    internal string? ActorAssignmentSaveBlocker => _actorAssignmentInvocation is not null
        ? "source-Actor-package-assignment-entered" : _actorAssignmentFailure;

    internal void BindActorPackageAssignment(FalloutActorPackageAssignmentSource source, FalloutFormKey actor,
        long epoch, Action<FalloutProcessSceneQuery> requireActualConstruction)
    {
        RequireNotBusy(); source.Validate(); ArgumentNullException.ThrowIfNull(requireActualConstruction);
        var process = ReadSceneQueries(actor, epoch); var current = Require(actor);
        if (source.Command.Fields.Controls.Main.EngineSha256 != _source.ExecutableSha256 || process.Level != FalloutDetectionProcessLevel.High)
            throw new NotSupportedException("source-Actor-package-assignment-selected-process-construction-unqualified");
        Callback(() => { requireActualConstruction(process); return true; });
        if (current.ActorAssignment is { } retained)
        {
            if (retained.Source != source || retained.Publication.Process != _process)
                throw new InvalidDataException("Actor assignment source bind changed its actual retained Common field owner.");
        }
        else
        {
            var packages = current.Packages ?? throw new NotSupportedException("source-Actor-package-assignment-package-field-constructors-unavailable");
            if (!current.Source.EnginePlayer || epoch != 1 || packages.ConstructionKind != FalloutProcessPackageConstructionKind.InitialPlayerConstructor ||
                packages.Primary is not { } primary || packages.RetiredInitialLow is null || packages.Current.Mutations?.Count > 0)
                throw new NotSupportedException("source-Actor-package-assignment-initial-High-constructor-prefix-unqualified");
            var factory = Guid.NewGuid(); var changed = Next(); var values = FalloutActorAssignmentValues.Constructed;
            var construction = new FalloutActorAssignmentConstruction(source.Identity, current.Source, _process, factory, epoch,
                changed, packages.Current.Constructor.Owner, primary.Constructor.Owner, true, values);
            var publication = new FalloutActorAssignmentPublication(_process, factory, changed);
            _current[actor] = current with
            {
                ActorAssignment = new(source, construction, publication, values, [publication], [], [],
                    new Dictionary<FalloutActorAssignmentLane, string>(), [], [], []),
                Changed = changed
            };
        }
        var fields = Require(actor).ActorAssignment!;
        if (!_actorAssignmentCommands.ContainsKey((actor, epoch)))
            _actorAssignmentCommands.Add((actor, epoch), fields.Calls.Select(call => call.Invocation.SourceCommand).ToHashSet());
        _lastActorAssignment = fields.Calls.LastOrDefault();
        _actorAssignmentFailure ??= fields.Calls.FirstOrDefault(call => !call.Returned)?.Failure;
    }

    internal FalloutActorAssignmentRead<uint> ReadActorAssignmentAdjustment(FalloutPackageCommandFieldInvocation invocation) =>
        ReadActorAssignment(invocation, FalloutActorAssignmentLane.Adjustment, fields => fields.Values.Adjustment);
    internal FalloutActorAssignmentRead<int> ReadActorAssignmentTier(FalloutPackageCommandFieldInvocation invocation) =>
        ReadActorAssignment(invocation, FalloutActorAssignmentLane.Tier, fields => fields.Values.Tier);

    private FalloutActorAssignmentRead<T> ReadActorAssignment<T>(FalloutPackageCommandFieldInvocation invocation,
        FalloutActorAssignmentLane lane, Func<FalloutProcessActorAssignmentFields, T> read)
    {
        var process = ReadSceneQueriesForPackageCommand(invocation); var fields = RequireActorAssignmentFields(invocation);
        RequireActorAssignmentLane(fields, lane);
        return new(invocation, process, fields.Source.Identity, fields.Publication.Factory, Require(invocation.Actor.Reference).Changed, read(fields));
    }

    internal FalloutActorAssignmentStore StoreActorAssignmentAdjustment(FalloutPackageCommandFieldInvocation invocation,
        uint suppliedBits, Action<FalloutPackageCommandFieldInvocation> requireActualSetter)
    {
        var before = ReadActorAssignmentAdjustment(invocation);
        var value = BitConverter.UInt32BitsToSingle(before.Value) + BitConverter.UInt32BitsToSingle(suppliedBits);
        return StoreActorAssignmentCell(invocation, FalloutActorAssignmentCell.Adjustment,
            BitConverter.SingleToUInt32Bits(value), requireActualSetter, suppliedBits);
    }

    internal FalloutActorAssignmentWriter ObserveActorAssignmentWriter(FalloutPackageCommandFieldInvocation invocation,
        FalloutActorAssignmentLane lane, string boundary, Action<FalloutPackageCommandFieldInvocation> requireActualWriter)
    {
        if (!Enum.IsDefined(lane)) throw new InvalidDataException("Actor assignment writer has no actual field lane.");
        ArgumentException.ThrowIfNullOrWhiteSpace(boundary);
        return PackageCommandStore(invocation, requireActualWriter, process =>
        {
            var current = Require(invocation.Actor.Reference); var fields = RequireActorAssignmentFields(invocation);
            var writer = new FalloutActorAssignmentWriter(invocation, process, lane, Next(), boundary);
            var unknown = new Dictionary<FalloutActorAssignmentLane, string>(fields.UnownedWriters) { [lane] = boundary };
            _current[current.Source.Reference] = current with
            {
                ActorAssignment = fields with { Writers = AppendPackageCommandStore(fields.Writers, writer), UnownedWriters = unknown },
                Changed = writer.Changed
            };
            _actorAssignmentFailure ??= boundary; return writer;
        });
    }

    private FalloutProcessActorAssignmentFields RequireActorAssignmentFields(FalloutPackageCommandFieldInvocation invocation)
    {
        if (_actorAssignmentInvocation is { } entered && entered != invocation)
        { ++_actorAssignmentFault; throw new InvalidOperationException("Actor assignment reentered another original command."); }
        var current = Require(invocation.Actor.Reference);
        var fields = current.ActorAssignment ?? throw new NotSupportedException("source-Actor-package-assignment-field-constructors-unbound");
        if (current.Source != invocation.Actor || current.Epoch != invocation.Epoch || invocation.Process != _process ||
            fields.Publication.Process != _process || current.Level != FalloutDetectionProcessLevel.High)
            throw new InvalidOperationException("Actor assignment selected another actual Common actor/class/epoch.");
        return fields;
    }
    private static void RequireActorAssignmentLane(FalloutProcessActorAssignmentFields fields, FalloutActorAssignmentLane lane)
    {
        if (fields.UnownedWriters.TryGetValue(lane, out var boundary)) throw new NotSupportedException(boundary);
    }
    private void RequireActorAssignmentCommonFlags(FalloutPackageCommandFieldInvocation invocation)
    {
        if (Require(invocation.Actor.Reference).ActorAssignment is { } fields)
            RequireActorAssignmentLane(fields, FalloutActorAssignmentLane.CommonFlags);
    }
    private void RequireActorAssignmentCapture()
    {
        if (_actorAssignmentInvocation is not null)
        { ++_actorAssignmentFault; throw new InvalidOperationException("Entered Actor assignment cannot be captured or retired."); }
    }

    private FalloutActorAssignmentStore StoreActorAssignmentCell(FalloutPackageCommandFieldInvocation invocation,
        FalloutActorAssignmentCell cell, uint bits, Action<FalloutPackageCommandFieldInvocation> requireActual, uint? operand = null)
    {
        return PackageCommandStore(invocation, requireActual, process =>
        {
            var current = Require(invocation.Actor.Reference); var fields = RequireActorAssignmentFields(invocation);
            var lane = ActorAssignmentLane(cell); var known = !fields.UnownedWriters.ContainsKey(lane);
            var before = ActorAssignmentBits(fields.Values, current.Scalars, cell);
            uint after = bits;
            if (cell is FalloutActorAssignmentCell.CommonFlagTwo or FalloutActorAssignmentCell.CommonFlagOne or FalloutActorAssignmentCell.CommonFlagForty)
            {
                RequireActorAssignmentLane(fields, lane);
                after = (byte)(current.Scalars.Flags & ~ActorAssignmentFlagMask(cell));
            }
            var store = new FalloutActorAssignmentStore(invocation, process, fields.Publication.Factory, cell, Next(),
                known ? before : null, after, BeforeKnown: known, OperandBits: operand);
            var unknown = new Dictionary<FalloutActorAssignmentLane, string>(fields.UnownedWriters);
            if (cell is not (FalloutActorAssignmentCell.TailByte or FalloutActorAssignmentCell.TailFirstWord or
                FalloutActorAssignmentCell.TailIndexWord or FalloutActorAssignmentCell.TailFinalWord or
                FalloutActorAssignmentCell.TailFirstPointer or FalloutActorAssignmentCell.TailClock or FalloutActorAssignmentCell.TailSecondPointer or
                FalloutActorAssignmentCell.TailSecondByte or FalloutActorAssignmentCell.TailThirdPointer or FalloutActorAssignmentCell.TailThirdByte or FalloutActorAssignmentCell.TailFourthPointer))
                unknown.Remove(lane);
            var scalars = cell switch
            {
                FalloutActorAssignmentCell.CommonWord => current.Scalars with { FirstWord = after },
                FalloutActorAssignmentCell.CommonFlagTwo or FalloutActorAssignmentCell.CommonFlagOne or FalloutActorAssignmentCell.CommonFlagForty =>
                    current.Scalars with { Flags = checked((byte)after) },
                _ => current.Scalars
            };
            _current[current.Source.Reference] = current with
            {
                ActorAssignment = fields with
                {
                    Values = ApplyActorAssignment(fields.Values, store),
                    Stores = AppendPackageCommandStore(fields.Stores, store),
                    UnownedWriters = unknown
                },
                Scalars = scalars,
                Changed = store.Changed
            };
            return store;
        });
    }

    private static byte ActorAssignmentFlagMask(FalloutActorAssignmentCell cell) => cell switch
    {
        FalloutActorAssignmentCell.CommonFlagTwo => FalloutActorPackageAssignmentSource.ResetFlagTwo,
        FalloutActorAssignmentCell.CommonFlagOne => FalloutActorPackageAssignmentSource.ResetFlagOne,
        FalloutActorAssignmentCell.CommonFlagForty => FalloutActorPackageAssignmentSource.ResetFlagForty,
        _ => throw new InvalidDataException("Actor assignment selected an unrelated Common flag.")
    };
    private static FalloutActorAssignmentLane ActorAssignmentLane(FalloutActorAssignmentCell cell) => cell switch
    {
        FalloutActorAssignmentCell.Adjustment => FalloutActorAssignmentLane.Adjustment,
        FalloutActorAssignmentCell.LinkedReference => FalloutActorAssignmentLane.LinkedReference,
        FalloutActorAssignmentCell.InlineValue or FalloutActorAssignmentCell.InlineLink => FalloutActorAssignmentLane.InlineList,
        FalloutActorAssignmentCell.LinkedEffectIndex => FalloutActorAssignmentLane.LinkedEffectIndex,
        FalloutActorAssignmentCell.OtherEffectActor => FalloutActorAssignmentLane.OtherEffectActor,
        FalloutActorAssignmentCell.Tier => FalloutActorAssignmentLane.Tier,
        FalloutActorAssignmentCell.ResetMode => FalloutActorAssignmentLane.ResetMode,
        FalloutActorAssignmentCell.IndependentResetByte => FalloutActorAssignmentLane.IndependentResetByte,
        FalloutActorAssignmentCell.CommonWord => FalloutActorAssignmentLane.CommonWord,
        FalloutActorAssignmentCell.CommonFlagTwo or FalloutActorAssignmentCell.CommonFlagOne or FalloutActorAssignmentCell.CommonFlagForty => FalloutActorAssignmentLane.CommonFlags,
        _ => FalloutActorAssignmentLane.ResetFields
    };
    private static uint? ActorAssignmentBits(FalloutActorAssignmentValues value, FalloutProcessCommonScalars scalars, FalloutActorAssignmentCell cell) => cell switch
    {
        FalloutActorAssignmentCell.Adjustment => value.Adjustment,
        FalloutActorAssignmentCell.LinkedEffectIndex => unchecked((uint)value.LinkedEffectIndex),
        FalloutActorAssignmentCell.Tier => unchecked((uint)value.Tier),
        FalloutActorAssignmentCell.ResetMode => value.ResetMode,
        FalloutActorAssignmentCell.IndependentResetByte => value.IndependentResetByte,
        FalloutActorAssignmentCell.TailByte => value.TailByte,
        FalloutActorAssignmentCell.TailFirstWord => value.TailFirstWord,
        FalloutActorAssignmentCell.TailIndexWord => value.TailIndexWord,
        FalloutActorAssignmentCell.TailFinalWord => value.TailFinalWord,
        FalloutActorAssignmentCell.TailClock => value.TailClock,
        FalloutActorAssignmentCell.TailSecondByte => value.TailSecondByte,
        FalloutActorAssignmentCell.TailThirdByte => value.TailThirdByte,
        FalloutActorAssignmentCell.TailFinalByte => value.TailFinalByte,
        FalloutActorAssignmentCell.CommonWord => scalars.FirstWord,
        FalloutActorAssignmentCell.CommonFlagTwo or FalloutActorAssignmentCell.CommonFlagOne or FalloutActorAssignmentCell.CommonFlagForty => scalars.Flags,
        _ => null
    };
    private static FalloutActorAssignmentValues ApplyActorAssignment(FalloutActorAssignmentValues value, FalloutActorAssignmentStore store)
    {
        uint Bits() => store.AfterBits ?? throw new InvalidDataException("Actor scalar writer omitted its actual word.");
        return store.Cell switch
        {
            FalloutActorAssignmentCell.Adjustment => value with { Adjustment = Bits() },
            FalloutActorAssignmentCell.LinkedReference => value with { LinkedReference = store.AfterReference },
            FalloutActorAssignmentCell.ResetMode => value with { ResetMode = checked((ushort)Bits()) },
            FalloutActorAssignmentCell.IndependentResetByte => value with { IndependentResetByte = checked((byte)Bits()) },
            FalloutActorAssignmentCell.TailByte => value with { TailByte = checked((byte)Bits()) },
            FalloutActorAssignmentCell.TailFirstWord => value with { TailFirstWord = checked((ushort)Bits()) },
            FalloutActorAssignmentCell.TailIndexWord => value with { TailIndexWord = checked((ushort)Bits()) },
            FalloutActorAssignmentCell.TailFinalWord => value with { TailFinalWord = checked((ushort)Bits()) },
            FalloutActorAssignmentCell.TailFirstPointer => value with { TailFirstPointer = store.AfterObject },
            FalloutActorAssignmentCell.TailClock => value with { TailClock = Bits() },
            FalloutActorAssignmentCell.TailSecondPointer => value with { TailSecondPointer = store.AfterObject },
            FalloutActorAssignmentCell.TailSecondByte => value with { TailSecondByte = checked((byte)Bits()) },
            FalloutActorAssignmentCell.TailThirdPointer => value with { TailThirdPointer = store.AfterObject },
            FalloutActorAssignmentCell.TailThirdByte => value with { TailThirdByte = checked((byte)Bits()) },
            FalloutActorAssignmentCell.TailFourthPointer => value with { TailFourthPointer = store.AfterObject },
            FalloutActorAssignmentCell.TailFinalByte => value with { TailFinalByte = checked((byte)Bits()) },
            FalloutActorAssignmentCell.CommonWord or FalloutActorAssignmentCell.CommonFlagTwo or FalloutActorAssignmentCell.CommonFlagOne or FalloutActorAssignmentCell.CommonFlagForty => value,
            _ => throw new InvalidDataException("Actor assignment reached an unowned cell writer.")
        };
    }
}
