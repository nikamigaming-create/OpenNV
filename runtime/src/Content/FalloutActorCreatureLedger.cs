using System.Buffers.Binary;
using System.Text;

namespace OpenNV.Runtime.Content;

internal delegate bool FalloutActorResourceResolver(
    string logicalPath,
    string? preferredArchive,
    out string source);

internal sealed record FalloutActorCreatureLedgerRow(
    FalloutFormKey Reference,
    string ReferenceType,
    FalloutFormKey? ParentCell,
    FalloutFormKey Base,
    string? BaseType,
    bool InitiallyDisabled,
    string? ModelPath,
    string? ModelSource,
    IReadOnlyList<FalloutFormKey> TemplateClosure,
    string? Blocker);

internal sealed record FalloutActorCreatureLedger(
    string Game,
    int EffectiveCells,
    int CellsWithActors,
    int HumanoidReferences,
    int CreatureReferences,
    int UniqueHumanoidBases,
    int UniqueCreatureBases,
    int InitiallyDisabledReferences,
    int ModelResolvedReferences,
    int ModelMissingReferences,
    int ModelAbsentReferences,
    int TemplateLinkedBases,
    IReadOnlyList<FalloutActorCreatureLedgerRow> Rows)
{
    internal int TotalReferences => HumanoidReferences + CreatureReferences;
    internal int BlockedReferences => Rows.Count(row => row.Blocker is not null);
}

internal static class FalloutActorCreatureLedgerBuilder
{
    private const string HumanoidReference = "ACHR";
    private const string CreatureReference = "ACRE";
    private const string HumanoidBase = "NPC_";
    private const string CreatureBase = "CREA";
    private const string HumanoidLeveledTemplate = "LVLN";
    private const string CreatureLeveledTemplate = "LVLC";
    private const int DataPrefixLength = 5;
    private const uint InitiallyDisabledFlag = 0x0000_0800;

    internal static FalloutActorCreatureLedger Build(
        FalloutPluginStack stack,
        RuntimeOwnedContentSource source)
        => Build(stack, source.Game, source.TryResolve);

    internal static FalloutActorCreatureLedger Build(
        FalloutPluginStack stack,
        string game,
        FalloutActorResourceResolver resolve)
    {
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(resolve);
        if (game is not (RuntimeOwnedContentSource.Fallout3Game or
            RuntimeOwnedContentSource.FalloutNewVegasGame))
            throw new InvalidDataException(
                "The actor ledger requires a registered Fallout 3 or New Vegas source stack.");

        var cells = stack.EffectiveRecords("CELL")
            .ToDictionary(record => record.FormKey, FormKeyComparer.Instance);
        var assessments = new Dictionary<FalloutFormKey, BaseAssessment>(FormKeyComparer.Instance);
        var rows = new List<FalloutActorCreatureLedgerRow>();
        foreach (var reference in stack.EffectiveRecords(HumanoidReference)
                     .Concat(stack.EffectiveRecords(CreatureReference))
                     .OrderBy(record => stack.RuntimeFormId(record.FormKey)))
        {
            var expectedBaseType = reference.Signature == HumanoidReference
                ? HumanoidBase
                : CreatureBase;
            var parent = FalloutCellSceneReader.ParentCell(reference);
            var baseKey = ReadRequiredFormId(reference, "NAME");
            string? blocker = null;
            if (parent is null)
                blocker = "reference-has-no-parent-cell";
            else if (!cells.ContainsKey(parent.Value))
                blocker = $"parent-cell-is-not-effective:{parent.Value}";

            if (!assessments.TryGetValue(baseKey, out var assessment))
            {
                assessment = AssessBase(stack, resolve, baseKey, expectedBaseType);
                assessments.Add(baseKey, assessment);
            }
            else if (assessment.ExpectedType != expectedBaseType)
            {
                throw new InvalidDataException(
                    $"Actor base {baseKey} is referenced as both {assessment.ExpectedType} and {expectedBaseType}.");
            }
            blocker ??= assessment.Blocker;
            rows.Add(new FalloutActorCreatureLedgerRow(
                reference.FormKey,
                reference.Signature,
                parent,
                baseKey,
                assessment.ActualType,
                (reference.Flags & InitiallyDisabledFlag) != 0,
                assessment.ModelPath,
                assessment.ModelSource,
                assessment.TemplateClosure,
                blocker));
        }

        var humanoidRows = rows.Where(row => row.ReferenceType == HumanoidReference).ToArray();
        var creatureRows = rows.Where(row => row.ReferenceType == CreatureReference).ToArray();
        var modelResolved = rows.Count(row => row.ModelSource is not null);
        var modelMissing = rows.Count(row => row.ModelPath is not null && row.ModelSource is null);
        var modelAbsent = rows.Count(row => row.ModelPath is null);
        if (rows.Count != humanoidRows.Length + creatureRows.Length ||
            rows.Count != modelResolved + modelMissing + modelAbsent)
            throw new InvalidDataException("Fallout 3 actor ledger accounting is not closed.");

        return new FalloutActorCreatureLedger(
            game,
            cells.Count,
            rows.Where(row => row.ParentCell is not null).Select(row => row.ParentCell!.Value)
                .Distinct(FormKeyComparer.Instance).Count(),
            humanoidRows.Length,
            creatureRows.Length,
            humanoidRows.Select(row => row.Base).Distinct(FormKeyComparer.Instance).Count(),
            creatureRows.Select(row => row.Base).Distinct(FormKeyComparer.Instance).Count(),
            rows.Count(row => row.InitiallyDisabled),
            modelResolved,
            modelMissing,
            modelAbsent,
            assessments.Values.Count(value => value.TemplateClosure.Count > 0),
            rows);
    }

    private static BaseAssessment AssessBase(
        FalloutPluginStack stack,
        FalloutActorResourceResolver resolve,
        FalloutFormKey key,
        string expectedType)
    {
        if (!stack.TryGetEffective(key, out var record))
            return new BaseAssessment(expectedType, null, null, null, [], "base-is-not-effective");
        if (record.Signature != expectedType)
            return new BaseAssessment(
                expectedType,
                record.Signature,
                null,
                null,
                [],
                $"base-type-is-{record.Signature}-expected-{expectedType}");

        var closure = ReadTemplateClosure(stack, record, expectedType, out var templateBlocker);
        var modelRows = record.ReadSubrecords().Where(value => value.Signature == "MODL").ToArray();
        if (modelRows.Length > 1)
            return new BaseAssessment(
                expectedType,
                record.Signature,
                null,
                null,
                closure,
                "base-has-multiple-model-paths");
        string? modelPath = null;
        string? modelSource = null;
        string? blocker = templateBlocker;
        if (modelRows.Length == 1)
        {
            modelPath = NormalizeModelPath(modelRows[0].Data.Span, record);
            if (resolve(modelPath, null, out var resolvedSource))
                modelSource = resolvedSource;
            else
                blocker ??= "model-resource-is-missing";
        }
        return new BaseAssessment(
            expectedType,
            record.Signature,
            modelPath,
            modelSource,
            closure,
            blocker);
    }

    private static IReadOnlyList<FalloutFormKey> ReadTemplateClosure(
        FalloutPluginStack stack,
        FalloutPluginRecord source,
        string expectedType,
        out string? blocker)
    {
        blocker = null;
        var result = new List<FalloutFormKey>();
        var seen = new HashSet<FalloutFormKey>(FormKeyComparer.Instance) { source.FormKey };
        var current = source;
        while (true)
        {
            var rows = current.ReadSubrecords().Where(value => value.Signature == "TPLT").ToArray();
            if (rows.Length == 0)
                return result;
            if (rows.Length != 1 || rows[0].Data.Length != sizeof(uint))
            {
                blocker = "template-layout-is-unsupported";
                return result;
            }
            var raw = BinaryPrimitives.ReadUInt32LittleEndian(rows[0].Data.Span);
            if (raw == 0)
                return result;
            var key = current.Plugin.AdjustFormId(raw);
            if (!seen.Add(key))
            {
                blocker = "template-cycle";
                return result;
            }
            result.Add(key);
            if (!stack.TryGetEffective(key, out current))
            {
                blocker = "template-is-not-effective";
                return result;
            }
            if (current.Signature == LeveledTemplateType(expectedType))
            {
                // FO3 permits an actor template to be a leveled actor list. The list is a
                // terminal runtime selection source, not another actor template record.
                return result;
            }
            if (current.Signature != expectedType)
            {
                blocker = $"template-type-is-{current.Signature}-expected-{expectedType}";
                return result;
            }
        }
    }

    private static string LeveledTemplateType(string actorType) => actorType switch
    {
        HumanoidBase => HumanoidLeveledTemplate,
        CreatureBase => CreatureLeveledTemplate,
        _ => throw new ArgumentOutOfRangeException(nameof(actorType), actorType, "Unsupported actor type."),
    };

    private static FalloutFormKey ReadRequiredFormId(FalloutPluginRecord record, string signature)
    {
        var rows = record.ReadSubrecords().Where(value => value.Signature == signature).ToArray();
        if (rows.Length != 1 || rows[0].Data.Length != sizeof(uint))
            throw new InvalidDataException(
                $"{record.Plugin.Name} {record.Signature} {record.RawFormId:x8} must have one {signature} FormID.");
        var raw = BinaryPrimitives.ReadUInt32LittleEndian(rows[0].Data.Span);
        if (raw == 0)
            throw new InvalidDataException(
                $"{record.Plugin.Name} {record.Signature} {record.RawFormId:x8} has a null {signature} FormID.");
        return record.Plugin.AdjustFormId(raw);
    }

    private static string NormalizeModelPath(ReadOnlySpan<byte> bytes, FalloutPluginRecord record)
    {
        var terminator = bytes.IndexOf((byte)0);
        var payload = terminator >= 0 ? bytes[..terminator] : bytes;
        if (payload.Length == 0 || terminator >= 0 && bytes[(terminator + 1)..].IndexOfAnyExcept((byte)0) >= 0)
            throw new InvalidDataException(
                $"{record.Plugin.Name} {record.Signature} {record.RawFormId:x8} has an invalid MODL path.");
        var text = Encoding.UTF8.GetString(payload).Replace('/', '\\').TrimStart('\\');
        if (text.StartsWith("data\\meshes\\", StringComparison.OrdinalIgnoreCase))
            text = text[DataPrefixLength..];
        if (!text.StartsWith("meshes\\", StringComparison.OrdinalIgnoreCase))
            text = $"meshes\\{text}";
        if (!text.EndsWith(".nif", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"{record.Plugin.Name} {record.Signature} {record.RawFormId:x8} MODL is not a NIF path.");
        return FalloutBsaArchive.CanonicalPath(text);
    }

    private sealed record BaseAssessment(
        string ExpectedType,
        string? ActualType,
        string? ModelPath,
        string? ModelSource,
        IReadOnlyList<FalloutFormKey> TemplateClosure,
        string? Blocker);

    private sealed class FormKeyComparer : IEqualityComparer<FalloutFormKey>
    {
        internal static FormKeyComparer Instance { get; } = new();

        public bool Equals(FalloutFormKey left, FalloutFormKey right) =>
            left.ObjectId == right.ObjectId &&
            string.Equals(left.OwnerPlugin, right.OwnerPlugin, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(FalloutFormKey key) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(key.OwnerPlugin), key.ObjectId);
    }
}
