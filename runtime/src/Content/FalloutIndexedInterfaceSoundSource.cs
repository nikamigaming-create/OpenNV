using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutIndexedInterfaceCue(FalloutInterfaceSoundEntry Entry,
    FalloutFormKey? Sound, string? Winner, string? RecordSha256, FalloutSoundRecord? Descriptor,
    FalloutSoundFlags? OriginalFlags = null, long? SelectionOrdinal = null);

internal sealed class FalloutIndexedInterfaceSoundSource
{
    private readonly FalloutPluginStack _records;
    private readonly FalloutAdvancementRuntimeSource _runtime;
    internal FalloutInterfaceSoundCatalogue Catalogue { get; }
    internal FalloutMenuCuePlaybackSource Playback { get; }
    internal FalloutMenuSoundSelection Selection { get; }
    internal string Identity { get; }
    internal FalloutPluginStack Records => _records;

    internal FalloutIndexedInterfaceSoundSource(FalloutPluginStack records, FalloutAdvancementRuntimeSource runtime,
        FalloutMenuSoundSelectionSnapshot? restoreSelection = null)
    {
        ArgumentNullException.ThrowIfNull(records); ArgumentNullException.ThrowIfNull(runtime);
        var source = records.OwnedSource ?? throw new NotSupportedException("Indexed interface sounds have no selected owned source.");
        if (!ReferenceEquals(source, runtime.OwnedSource))
            throw new InvalidDataException("Interface sound source differs from its actual selected runtime owner.");
        runtime.Receipt.Validate(); _records = records; _runtime = runtime;
        Catalogue = FalloutExecutableStringTable.ReadInterfaceSounds(source.FalloutExecutablePath, runtime.Receipt.EngineSha256);
        Playback = new(runtime.Receipt.EngineSha256, runtime.Receipt.SourceSha256, FalloutMenuCuePlaybackSource.CurrentContractSha256);
        Playback.Validate();
        Selection = source.OpenMenuSoundSelection(records, runtime, restoreSelection);
        Identity = FalloutAdvancementRuntimeReceipt.Hash(Catalogue.Identity + "\0" + Playback.Identity + "\0" + Selection.Source.Identity + "\0" + runtime.Receipt.SourceSha256);
    }

    internal FalloutIndexedInterfaceCue Resolve(FalloutInterfaceSoundCall call, long voiceOrdinal)
    {
        _runtime.Receipt.Validate();
        call.Validate(); if (voiceOrdinal <= 0) throw new InvalidDataException("Indexed selection has no real entered voice ordinal.");
        var entry = Catalogue.Resolve(call.Index);
        if (entry.Disposition == FalloutInterfaceSoundDisposition.SourceSilent) return new(entry, null, null, null, null);
        // This is a manager request flag domain, distinct from SOUN/SNDD bits.
        // All admitted original indexed branches declare this 2D/menu request.
        if (entry.RequestFlags != 0x121)
            throw new NotSupportedException("Indexed cue request flags require their actual source manager consumer.");
        var record = FalloutSoundRecordReader.Find(_records, entry.EditorId!);
        var descriptor = FalloutSoundRecordReader.Read(_records, record.FormKey);
        if (!descriptor.EditorId.Equals(entry.EditorId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Indexed cue resolved another original sound editor ID.");
        var originalFlags = descriptor.Flags;
        // The reviewed named-file producer for this source translates the
        // SOUN 2D/random/loop/radius bits and does not translate LFE360. This is
        // an indexed position-independent request, not a spatial LFE owner.
        // Another executable's untranslated-bit behavior remains unadmitted.
        if ((descriptor.Flags & FalloutSoundFlags.Lfe360) != 0 &&
            Catalogue.EngineSha256 == "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57")
            descriptor = descriptor with { Flags = descriptor.Flags & ~FalloutSoundFlags.Lfe360 };
        var prepared = Selection.Prepare(descriptor, new(call.Owner, call.Occurrence, call.BranchOrdinal,
            record.FormKey, voiceOrdinal, call.Index));
        descriptor = Playback.PreparedFile(_records, prepared, Selection);
        return new(entry, record.FormKey, record.Plugin.Name, FalloutRestLocation.SourceIdentity(record), descriptor, originalFlags, prepared.SelectionOrdinal);
    }

    internal void RequirePreparedDeclaration(FalloutInterfaceSoundEntry entry, FalloutFormKey? sound,
        string? winner, string? recordSha256, string? logicalPath, FalloutSoundFlags? flags, long? selectionOrdinal, long voiceOrdinal)
    {
        if (entry.Disposition == FalloutInterfaceSoundDisposition.SourceSilent)
        {
            if (sound is not null || winner is not null || recordSha256 is not null || logicalPath is not null || flags is not null || selectionOrdinal is not null)
                throw new InvalidDataException("Source-silent indexed branch fabricated a sound declaration.");
            return;
        }
        var record = FalloutSoundRecordReader.Find(_records, entry.EditorId!);
        var descriptor = FalloutSoundRecordReader.Read(_records, record.FormKey);
        if (record.FormKey != sound || record.Plugin.Name != winner || FalloutRestLocation.SourceIdentity(record) != recordSha256 ||
            descriptor.Flags != flags || logicalPath is null || FalloutBsaArchive.CanonicalPath(logicalPath) != logicalPath ||
            !logicalPath.StartsWith("sound\\", StringComparison.Ordinal) ||
            Path.GetExtension(logicalPath).ToLowerInvariant() is not (".wav" or ".ogg") || selectionOrdinal is not { } selected)
            throw new InvalidDataException("Indexed sound receipt changed its original winning declaration/prepared file.");
        Selection.RequirePrepared(selected, record.FormKey, logicalPath, record.Plugin.Name, recordSha256!, voiceOrdinal);
        // A real SetSoundPath later in this session must not rebind a prepared
        // or finished voice. Current mutable paths are checked by Resolve on
        // the next source call; this receipt keeps the actual earlier path.
    }

    internal string ReadPreparedMediaSha256(string logicalPath)
    {
        var source = _records.OwnedSource ?? throw new NotSupportedException("Indexed sound media has no actual source graph.");
        if (!source.TryRead(logicalPath, null, out var bytes, out _))
            throw new FileNotFoundException("Indexed sound continuation lost its actual winning media.", logicalPath);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
