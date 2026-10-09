using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

// The graph authenticates source identities. Registration, file traversal,
// the winning-file filter and head insertion provide the list's actual order.
// Scene order and runtime FormID order never enter this owner.
internal sealed class FalloutSourceCellReferenceIngestion : IFalloutSourceOwnedCellReferenceIngestion, IDisposable
{
    private sealed record Input(FalloutPluginContext Context, FalloutPluginRecord Record, int Group);
    private sealed record Plan(FalloutSourceCellIngestionEvidence Evidence,
        IReadOnlyList<FalloutSourceCellIngestionInput> Steps);

    private readonly FalloutSourceCellLoaderDeclaration _source;
    private readonly FalloutPluginStack _records;
    private readonly string _stack;
    private readonly Dictionary<FalloutFormKey, List<Input>> _children = new(FalloutFormKeyComparer.Instance);
    private readonly Dictionary<FalloutFormKey, HashSet<int>> _cellFiles = new(FalloutFormKeyComparer.Instance);
    private readonly Dictionary<FalloutFormKey, HashSet<FalloutFormKey>> _rawParents = new(FalloutFormKeyComparer.Instance);
    private readonly Dictionary<FalloutFormKey, Plan> _plans = new(FalloutFormKeyComparer.Instance);
    private readonly Dictionary<FalloutFormKey, long> _entered = new(FalloutFormKeyComparer.Instance);
    private int _thread = Environment.CurrentManagedThreadId;
    private object? _nativeOwner;
    private bool _indexed, _disposed, _busy;
    private long _calls;
    private Exception? _failure;

    internal FalloutSourceCellReferenceIngestion(FalloutSourceCellLoaderDeclaration source,
        FalloutPluginStack records, string stack)
    {
        source.Validate(); ArgumentNullException.ThrowIfNull(records);
        ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        if (records.OwnedSource is { } owned && owned.StackId != stack)
            throw new InvalidDataException("CELL ingestion changed the actual retained plugin selection.");
        _source = source; _records = records; _stack = stack;
    }

    public string EngineSha256 => _source.EngineSha256;
    public string Owner => "selected-source-CELL-eager-and-lazy-loader/" + _source.ContractSha256;
    internal object State => new
    {
        source = _source,
        stack = _stack,
        calls = _calls,
        entered = _entered.ToArray(),
        indexed = _indexed,
        failure = _failure?.ToString()
    };
    internal void PublishNativeOwner(object actualOwner)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(actualOwner);
        if (_busy) throw new InvalidOperationException("Source CELL reader cannot publish while its insertion stream is entered.");
        if (_nativeOwner is not null)
        {
            if (!ReferenceEquals(_nativeOwner, actualOwner) || _thread != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("Source CELL reader changed its actual native caller/root.");
            return;
        }
        if (_entered.Count != 0) throw new InvalidOperationException("Source CELL insertion started before actual native publication.");
        _thread = Environment.CurrentManagedThreadId; _nativeOwner = actualOwner;
    }

    public FalloutSourceCellIngestionEvidence ReadEvidence(FalloutCellProcessData cell)
    {
        Require(); return Copy(ReadPlan(cell).Evidence);
    }

    public void RequireEvidence(FalloutSourceCellIngestionEvidence evidence, FalloutCellProcessData cell)
    {
        Require(); Validate(evidence);
        var current = ReadPlan(cell).Evidence;
        if (evidence.Source != current.Source || evidence.Stack != current.Stack || evidence.Cell != current.Cell ||
            evidence.GraphSha256 != current.GraphSha256 || evidence.InputsSha256 != current.InputsSha256 ||
            !evidence.Inputs.SequenceEqual(current.Inputs))
            throw new InvalidDataException("Cold CELL loader provenance differs from the actual unchanged source stream.");
    }

    public IEnumerable<FalloutFormKey> ReadEnteredInsertions(FalloutCellProcessData cell)
    {
        Require();
        if (_nativeOwner is null) throw new InvalidOperationException("Live CELL insertion requires its genuine consuming-thread native publication.");
        if (_entered.ContainsKey(cell.Source.Cell))
            throw new InvalidOperationException("An entered source CELL loader cannot retry or replay its insertions.");
        var plan = ReadPlan(cell);
        _busy = true; _entered.Add(cell.Source.Cell, _calls = checked(_calls + 1));
        try
        {
            foreach (var step in plan.Steps)
            {
                if (step.Refusal is { } refusal)
                {
                    _failure = new NotSupportedException(refusal);
                    throw _failure;
                }
                if (step.Disposition is FalloutSourceCellIngestionDisposition.InsertPersistent or
                    FalloutSourceCellIngestionDisposition.InsertWinningTemporary)
                    yield return step.Reference;
            }
        }
        finally { _busy = false; }
    }

    private void IndexSourceStream()
    {
        if (_indexed) return;
        var expected = 0;
        foreach (var context in _records.Plugins)
        {
            if (context.LoadOrderIndex != expected++)
                throw new InvalidDataException("Source CELL files lost their actual configured registration sequence.");
            long prior = -1;
            foreach (var record in context.Plugin.Records)
            {
                if (record.HeaderOffset <= prior)
                    throw new InvalidDataException("Source CELL reader reordered physical records within a registered file.");
                prior = record.HeaderOffset;
                if (record.Signature == "CELL")
                {
                    if (!_cellFiles.TryGetValue(record.FormKey, out var files))
                        _cellFiles.Add(record.FormKey, files = []);
                    files.Add(context.LoadOrderIndex);
                    continue;
                }
                if (record.Signature is not ("REFR" or "ACHR" or "ACRE" or "PGRE" or "PMIS" or "PBEA" or "PFLA" or "PCON"))
                    continue;
                var parent = FalloutCellSceneReader.ParentCell(record) ??
                    throw new InvalidDataException("Source placed reference has no actual CELL-child group.");
                var groups = record.Groups.Where(group => group.Type is 8 or 9 or 10).ToArray();
                if (groups.Length != 1 || record.Plugin.AdjustFormId(groups[0].LabelAsUInt32) != parent)
                    throw new InvalidDataException("Source reference lost its real persistent/temporary group identity.");
                if (!_children.TryGetValue(parent, out var inputs)) _children.Add(parent, inputs = []);
                inputs.Add(new(context, record, groups[0].Type));
                if (!_rawParents.TryGetValue(record.FormKey, out var parents))
                    _rawParents.Add(record.FormKey, parents = new(FalloutFormKeyComparer.Instance));
                parents.Add(parent);
            }
        }
        _indexed = true;
    }

    private Plan ReadPlan(FalloutCellProcessData cell)
    {
        FalloutCellExtraProcessState.RequireCell(cell.Source, cell.Source.Cell);
        if (_plans.TryGetValue(cell.Source.Cell, out var cached))
        {
            if (cached.Evidence.Cell != cell.Source || cached.Evidence.GraphSha256 != cell.GraphSha256)
                throw new InvalidDataException("CELL loader changed its immutable source graph during the same lifetime.");
            return cached;
        }
        IndexSourceStream();
        if (cell.Source.Worldspace is not null || (cell.Source.Flags & 0x400) != 0)
            throw new NotSupportedException("Exterior/persistent CELL initial registration has independent world-group and persistent-parent consumers.");
        var inputs = _children.GetValueOrDefault(cell.Source.Cell) ?? [];
        var selected = cell.References.ToDictionary(reference => reference.Reference, FalloutFormKeyComparer.Instance);
        var steps = new List<FalloutSourceCellIngestionInput>();
        var constructed = new HashSet<FalloutFormKey>(FalloutFormKeyComparer.Instance);
        var seenInFile = new HashSet<(int File, FalloutFormKey Key)>();
        foreach (var group in new[] { 8, 9 })
            foreach (var input in inputs.Where(input => input.Group == group))
            {
                var record = input.Record;
                var sourceBody = record.ReadData();
                var digest = Hash(sourceBody);
                var kind = FalloutSourceCellIngestionDisposition.Refused;
                string? refusal = null;
                if (!_cellFiles.TryGetValue(cell.Source.Cell, out var files) || !files.Contains(input.Context.LoadOrderIndex))
                    refusal = "Source CELL child file has no registered CELL declaration; its loader file-list producer is unowned.";
                else if (!seenInFile.Add((input.Context.LoadOrderIndex, record.FormKey)))
                    refusal = "Repeated placed-reference bodies in one original file require the actual object/winner table consumer.";
                else if (_rawParents[record.FormKey].Count != 1)
                    refusal = "Source override moves ParentCELL before initial load; the cross-CELL loader transaction is unowned.";
                else if (record.IsDeleted)
                    refusal = "Deleted source reference requires its actual eager/lazy construction and linked-list retirement producer.";
                else if ((record.Flags & 0x4000) != 0)
                    refusal = "Partial source reference requires the original partial-form load and ParentCELL consumer.";
                else if (!_records.TryGetWinner(record.FormKey, out var winner) || winner.IsDeleted ||
                    FalloutCellSceneReader.ParentCell(winner) != cell.Source.Cell || !selected.TryGetValue(record.FormKey, out var actual))
                    refusal = "Source initial reference has no admitted final winning CELL membership.";
                else if (group == 9 && !ReferenceEquals(winner.Plugin, record.Plugin))
                    kind = FalloutSourceCellIngestionDisposition.SkipEarlierTemporaryProvider;
                else
                {
                    if (group == 9 && winner.HeaderOffset != record.HeaderOffset)
                        refusal = "Original temporary winning-file selection has duplicate source positions.";
                    else
                    {
                        var positions = record.ReadSubrecords().Where(field => field.Signature == "DATA").ToArray();
                        var basis = FalloutDialogueTopic.RequiredForm(record, "NAME");
                        if (positions.Length == 1 && positions[0].Data.Length == 24 &&
                            _records.TryGetEffective(basis, out var sourceBase) && !sourceBase.IsDeleted)
                        {
                            if (ReferenceEquals(winner, record) && (actual.Flags != record.Flags || actual.Sha256 != digest || actual.Base != basis))
                                throw new InvalidDataException("CELL stream changed its actual winning reference bytes/base.");
                            kind = !constructed.Add(record.FormKey) ? group == 8 ?
                                FalloutSourceCellIngestionDisposition.RetainPersistentMembership :
                                FalloutSourceCellIngestionDisposition.SkipAlreadyConstructed : group == 8 ?
                                FalloutSourceCellIngestionDisposition.InsertPersistent :
                                FalloutSourceCellIngestionDisposition.InsertWinningTemporary;
                        }
                        else refusal = "Source initial reference has no admitted NAME/DATA and live base load result.";
                    }
                }
                steps.Add(new(input.Context.Plugin.Name, input.Context.Sha256, input.Context.LoadOrderIndex,
                    record.HeaderOffset, record.FormKey, cell.Source.Cell, record.Signature, record.Flags, group,
                    digest, kind, refusal));
            }
        foreach (var input in inputs.Where(input => input.Group == 10))
            steps.Add(new(input.Context.Plugin.Name, input.Context.Sha256, input.Context.LoadOrderIndex,
                input.Record.HeaderOffset, input.Record.FormKey, cell.Source.Cell, input.Record.Signature,
                input.Record.Flags, input.Group, Hash(input.Record.ReadData()), FalloutSourceCellIngestionDisposition.Refused,
                "Visible-distant CELL references have an independent source registration consumer."));
        var evidence = new FalloutSourceCellIngestionEvidence(_source, _stack, cell.Source, cell.GraphSha256,
            Hash(JsonSerializer.SerializeToUtf8Bytes(steps)), steps.ToArray());
        Validate(evidence);
        var plan = new Plan(evidence, evidence.Inputs); _plans.Add(cell.Source.Cell, plan); return plan;
    }

    internal static void Validate(FalloutSourceCellIngestionEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (evidence.Source is null || evidence.Cell is null || string.IsNullOrWhiteSpace(evidence.Stack) ||
            !FalloutAdvancementRuntimeReceipt.Digest(evidence.GraphSha256) || evidence.Inputs is null ||
            evidence.Inputs.Any(input => input is null || string.IsNullOrWhiteSpace(input.Plugin) ||
                !FalloutAdvancementRuntimeReceipt.Digest(input.PluginSha256) || input.LoadOrder < 0 || input.HeaderOffset < 0 ||
                input.Cell != evidence.Cell.Cell || !FalloutAdvancementRuntimeReceipt.Digest(input.BodySha256) ||
                !Enum.IsDefined(input.Disposition) || (input.Disposition == FalloutSourceCellIngestionDisposition.Refused) != (input.Refusal is not null) ||
                input.Refusal is not null && string.IsNullOrWhiteSpace(input.Refusal)) ||
            evidence.InputsSha256 != Hash(JsonSerializer.SerializeToUtf8Bytes(evidence.Inputs)))
            throw new InvalidDataException("Source CELL ingestion omitted exact ordered source bodies or refusals.");
        evidence.Source.Validate();
        FalloutCellExtraProcessState.RequireCell(evidence.Cell, evidence.Cell.Cell);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static FalloutSourceCellIngestionEvidence Copy(FalloutSourceCellIngestionEvidence value) => value with { Inputs = value.Inputs.ToArray() };
    private void Require()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_thread != Environment.CurrentManagedThreadId || _busy)
            throw new InvalidOperationException("Source CELL loader changed its original caller thread or reentered its stream.");
        if (_failure is not null) throw new InvalidOperationException("Source CELL loader retains its failed source prefix.", _failure);
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_busy || _thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Source CELL loader cannot retire an entered or foreign-thread stream.");
        _disposed = true; _plans.Clear(); _children.Clear(); _cellFiles.Clear(); _rawParents.Clear();
    }
}
