using System.Buffers.Binary;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutPackageTargetWords(byte Kind, uint Payload, uint Count, uint DistanceBits)
{
    internal static FalloutPackageTargetWords Constructed => new(2, 0, 0, 0);
}
internal sealed record FalloutPackageTargetLoad(int SubrecordOrdinal, FalloutPackageTargetWords Input,
    FalloutPackageTargetWords Before, FalloutPackageTargetWords After, int SourceExtent = 16);
internal sealed record FalloutPackageTargetPublication(Guid Factory, Guid? PreviousFactory = null, Guid? Object = null);
internal sealed record FalloutPackageRuntimeTargetSnapshot(string Source, FalloutFormKey Package, string RecordSha256,
    Guid Constructor, Guid Factory, Guid? Object, FalloutPackageTargetWords? Values,
    IReadOnlyList<FalloutPackageTargetLoad> Loads, IReadOnlyList<FalloutPackageTargetPublication> Publications,
    bool Available);
internal sealed record FalloutPackageRuntimeTargetRead(FalloutPackageCommandFieldInvocation Invocation,
    Guid Registry, string Source, FalloutFormKey Package, string RecordSha256, Guid Constructor, Guid Factory,
    Guid? Object, FalloutPackageTargetWords? Values, FalloutFormKey? PayloadForm, long Sequence);

internal sealed class FalloutPackageRuntimeTargetFields
{
    internal const int SourceBufferBytes = 16;
    private readonly FalloutFormKey _package;
    private readonly string _recordSha256;
    private readonly List<FalloutPackageTargetLoad> _loads = [];
    private readonly List<FalloutPackageTargetPublication> _publications = [];
    internal Guid Constructor { get; private set; } = Guid.NewGuid();
    internal Guid Factory { get; } = Guid.NewGuid();
    internal Guid? Object { get; private set; }
    internal FalloutPackageTargetWords? Values { get; private set; }
    internal bool Available { get; set; } = true;
    internal static string Source { get; } = FalloutAdvancementRuntimeReceipt.Hash("source-FO3-PACK-current-target/v2;" +
        "PACK-target-pointer-null-constructor;PTDT-sixteen-byte-zero-read-buffer;" +
        "actual-zero-through-sixteen-byte-source-extent-copy-omitted-bytes-stay-zero-oversized-diagnostic-unowned;" +
        "first-type-byte-payload-word-count-word-distance-Float32-word;" +
        "target-object-two-zero-zero-zero-constructor-first-present-allocation;" +
        "ordered-copy-kind-change-zero-for012-payload-copy-only012-other-kinds-preserve-payload;" +
        "same-object-repeat-loads-known-current-field-unknown-target-writer-refuses;" +
        "form-reference-lifetime-resolution-independent-kind3-Actor-Extra81-unowned;" +
        "cold-original-construction-load-history-fresh-actual-managed-target-object");

    private FalloutPackageRuntimeTargetFields(FalloutFormKey package, string recordSha256)
    { _package = package; _recordSha256 = recordSha256; _publications.Add(new(Factory)); }
    internal static FalloutPackageRuntimeTargetFields Load(FalloutPluginRecord record, string sha)
    {
        var fields = new FalloutPackageRuntimeTargetFields(record.FormKey, sha); var ordinal = 0;
        foreach (var row in record.ReadSubrecords())
        {
            if (row.Signature == "PTDT")
            {
                var input = ReadInput(row.Data.Span);
                if (fields.Values is null) { fields.Object = Guid.NewGuid(); fields.Values = FalloutPackageTargetWords.Constructed; }
                var before = fields.Values ?? throw new InvalidOperationException("Target allocation did not publish its real fields."); var after = Copy(before, input);
                fields._loads.Add(new(ordinal, input, before, after, row.Data.Length)); fields.Values = after;
            }
            ordinal++;
        }
        fields._publications[0] = new(fields.Factory, Object: fields.Object); return fields;
    }
    private static FalloutPackageTargetWords Copy(FalloutPackageTargetWords before, FalloutPackageTargetWords input) =>
        new(input.Kind, input.Kind is 0 or 1 or 2 ? input.Payload : before.Payload, input.Count, input.DistanceBits);
    private static FalloutPackageTargetWords ReadInput(ReadOnlySpan<byte> source)
    {
        if (source.Length > SourceBufferBytes)
            throw new NotSupportedException("source-PACK-target-oversized-read-diagnostic-unbound");
        Span<byte> buffer = stackalloc byte[SourceBufferBytes]; buffer.Clear(); source.CopyTo(buffer);
        return new(buffer[0], BinaryPrimitives.ReadUInt32LittleEndian(buffer[4..]),
            BinaryPrimitives.ReadUInt32LittleEndian(buffer[8..]), BinaryPrimitives.ReadUInt32LittleEndian(buffer[12..]));
    }
    private static void ValidateInput(FalloutPackageTargetLoad load)
    {
        if (load.SourceExtent is < 0 or > SourceBufferBytes)
            throw new InvalidDataException("PACK target replaced its actual bounded source read extent.");
        Span<byte> buffer = stackalloc byte[SourceBufferBytes]; buffer.Clear(); buffer[0] = load.Input.Kind;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[4..], load.Input.Payload);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[8..], load.Input.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[12..], load.Input.DistanceBits);
        if (buffer[load.SourceExtent..].IndexOfAnyExcept((byte)0) >= 0)
            throw new InvalidDataException("PACK target fabricated bytes outside its actual source read.");
    }
    internal FalloutPackageRuntimeTargetSnapshot Capture() => new(Source, _package, _recordSha256, Constructor, Factory, Object,
        Values, Array.AsReadOnly(_loads.ToArray()), Array.AsReadOnly(_publications.ToArray()), Available);
    internal void Restore(FalloutPackageRuntimeTargetSnapshot saved)
    {
        Validate(saved);
        var current = Capture();
        if (saved.Package != _package || saved.RecordSha256 != _recordSha256 || saved.Factory == Factory ||
            !saved.Loads.SequenceEqual(current.Loads) || saved.Values != current.Values || (saved.Object is null) != (Object is null))
            throw new InvalidDataException("Cold mutable PACK target changed its original load graph or reused a field lifetime.");
        Constructor = saved.Constructor; Available = saved.Available;
        _publications.Clear(); _publications.AddRange(saved.Publications); _publications.Add(new(Factory, saved.Factory, Object));
    }
    internal static void Validate(FalloutPackageRuntimeTargetSnapshot saved)
    {
        if (saved.Source != Source || saved.Package.ObjectId == 0 || string.IsNullOrWhiteSpace(saved.Package.OwnerPlugin) ||
            saved.RecordSha256 is not { Length: 64 } || saved.Constructor == Guid.Empty || saved.Factory == Guid.Empty ||
            saved.Object == Guid.Empty || saved.Loads is null || saved.Publications is not { Count: > 0 } ||
            saved.Publications[^1].Factory != saved.Factory || (saved.Values is not null) != (saved.Object is not null))
            throw new InvalidDataException("PACK target omitted its actual constructor, source load or current object.");
        FalloutPackageTargetWords? values = null; var ordinal = -1;
        foreach (var load in saved.Loads)
        {
            ValidateInput(load);
            var before = values ?? FalloutPackageTargetWords.Constructed;
            if (load.SubrecordOrdinal <= ordinal || load.Before != before || load.After != Copy(before, load.Input))
                throw new InvalidDataException("PACK target omitted or reordered its actual constructor/load stores.");
            values = load.After; ordinal = load.SubrecordOrdinal;
        }
        if (values != saved.Values) throw new InvalidDataException("PACK target current fields differ from their actual source load.");
        var ids = new HashSet<Guid>(); var objects = new HashSet<Guid>();
        for (var index = 0; index < saved.Publications.Count; ++index)
            if (saved.Publications[index].Factory == Guid.Empty || !ids.Add(saved.Publications[index].Factory) ||
                saved.Publications[index].PreviousFactory != (index == 0 ? null : saved.Publications[index - 1].Factory) ||
                (saved.Publications[index].Object is null) != (saved.Values is null) || saved.Publications[index].Object == Guid.Empty ||
                saved.Publications[index].Object is { } identity && !objects.Add(identity))
                throw new InvalidDataException("PACK target reused or replaced a current cold field owner.");
        if (saved.Publications[^1].Object != saved.Object) throw new InvalidDataException("PACK target omitted its actual fresh current object.");
    }
}

internal sealed partial class FalloutPackageRuntimeRegistry
{
    internal FalloutPackageRuntimeTargetRead ReadCurrentTarget(FalloutPackageCommandFieldInvocation invocation,
        FalloutPackageRuntimeObject package)
    {
        RequireInvocation(invocation); RequireObject(package);
        if (!package.Target.Available) throw new NotSupportedException("source-mutable-PACK-target-current-writer-unbound");
        var fields = package.Target; var values = fields.Values;
        var form = values is { Kind: 0 or 1, Payload: not 0 } ? package.Record.Plugin.AdjustFormId(values.Payload) : (FalloutFormKey?)null;
        return new(invocation, Factory, FalloutPackageRuntimeTargetFields.Source, package.Form, package.RecordSha256, fields.Constructor, fields.Factory,
            fields.Object, values, form, _sequence);
    }
    internal void ObserveUnownedTarget(FalloutPackageCommandFieldInvocation invocation, FalloutPackageRuntimeObject package, string producer) =>
        Mutate(invocation, package, FalloutPackageRuntimeMutationKind.UnownedTarget, producer, () =>
        { package.Target.Available = false; RetainUnavailable(package); return package.LifetimeCount; });

    internal static void ValidateTargetRead(FalloutPackageRuntimeTargetRead read, FalloutPackageRegistrySnapshot saved)
        => new TargetReadValidationIndex(saved).Validate(read);

    internal static void ValidateTargetReads(IEnumerable<FalloutPackageRuntimeTargetRead> reads, FalloutPackageRegistrySnapshot saved)
    {
        ArgumentNullException.ThrowIfNull(reads);
        var index = new TargetReadValidationIndex(saved);
        foreach (var read in reads) index.Validate(read);
    }

    private sealed class TargetReadValidationIndex
    {
        private readonly long _sequence;
        private readonly Dictionary<Guid, int> _registries = [];
        private readonly Dictionary<FalloutFormKey, TargetBinding> _targets = new(FalloutFormKeyComparer.Instance);
        internal TargetReadValidationIndex(FalloutPackageRegistrySnapshot saved)
        {
            _sequence = saved.Sequence;
            for (var ordinal = 0; ordinal < saved.Publications.Count; ++ordinal)
                if (!_registries.TryAdd(saved.Publications[ordinal].Factory, ordinal))
                    throw new InvalidDataException("Target read has a duplicate registry lifetime.");
            foreach (var package in saved.Packages)
            {
                var target = package.Target ?? throw new InvalidDataException("Target read omitted its actual field owner.");
                if (target.Publications.Count != saved.Publications.Count)
                    throw new InvalidDataException("Target fields omitted their exact registry cold publication association.");
                long? firstUnowned = null;
                foreach (var writer in package.History)
                    if (writer.Kind == FalloutPackageRuntimeMutationKind.UnownedTarget)
                        firstUnowned = firstUnowned is { } prior ? Math.Min(prior, writer.Sequence) : writer.Sequence;
                if (!_targets.TryAdd(package.Form, new(package.RecordSha256, target, firstUnowned)))
                    throw new InvalidDataException("Target read has a duplicate winning PACK.");
            }
        }
        internal void Validate(FalloutPackageRuntimeTargetRead read)
        {
            RequireInvocation(read.Invocation);
            if (!_registries.TryGetValue(read.Registry, out var ordinal) || read.Sequence < 0 || read.Sequence > _sequence ||
                read.Source != FalloutPackageRuntimeTargetFields.Source)
                throw new InvalidDataException("Target read borrowed another actual registry lifetime.");
            if (!_targets.TryGetValue(read.Package, out var binding))
                throw new InvalidDataException("Target read selected another original winning PACK.");
            var target = binding.Target; var publication = target.Publications[ordinal];
            if (read.RecordSha256 != binding.RecordSha256 || read.Constructor != target.Constructor || read.Values != target.Values ||
                publication.Factory != read.Factory || publication.Object != read.Object ||
                binding.FirstUnowned is { } firstUnowned && firstUnowned <= read.Sequence)
                throw new InvalidDataException("Target read crossed an unavailable writer or replaced a constructor/current target object.");
        }
        private sealed record TargetBinding(string RecordSha256, FalloutPackageRuntimeTargetSnapshot Target, long? FirstUnowned);
    }
}
