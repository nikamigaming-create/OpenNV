using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

internal static partial class CellGraphAudit
{
    internal sealed class AnimationSoundInspection
    {
        public List<AnimationTextKeyRow> Keys { get; } = [];
        public List<ResourceDependency> DependencyEdges { get; } = [];
        public List<object> Failures { get; } = [];
        public string Scope { get; } = "complete decoded text-key declarations; current selected source path; every source variant; no timeline or playback";
    }

    internal sealed class AnimationTextKeyRow
    {
        public string DeclarationKind { get; } = "nif-text-key";
        public int Block { get; init; }
        public string Type { get; init; } = "";
        public int Offset { get; init; }
        public int Bytes { get; init; }
        public string Name { get; init; } = "";
        public int SourceOrdinal { get; init; }
        public float SourceSeconds { get; init; }
        public uint SourceTimeBits { get; init; }
        public string RawText { get; init; } = "";
        public List<AnimationSoundSegmentRow> Segments { get; } = [];
        public string SequenceEligibility { get; } = "uninspected; orphan, unused and outside-interval keys are retained";
        public string NativeAdmission { get; } = "unverified";
    }

    internal sealed class AnimationSoundSegmentRow
    {
        public required FalloutNifTextKeyDeclaration Declaration { get; init; }
        public string SourceDisposition { get; set; } = "no-sound-resource-request; event semantics uninspected";
        public string ReaderOperation { get; set; } = "not-requested";
        public string? Sound { get; set; }
        public string? DeclaringMaster { get; set; }
        public string? WinningPlugin { get; set; }
        public int? WinningLoadOrder { get; set; }
        public string[] DeclaredMasters { get; set; } = [];
        public uint? RawFormId { get; set; }
        public uint? RecordFlags { get; set; }
        public ushort? FormVersion { get; set; }
        public long? HeaderOffset { get; set; }
        public long? DataOffset { get; set; }
        public int? StoredBytes { get; set; }
        public int? DecodedBytes { get; set; }
        public string? RecordSha256 { get; set; }
        public string? WinningEditorId { get; set; }
        public string? DeclaredFile { get; set; }
        public string? CurrentFile { get; set; }
        public long? PathRevision { get; set; }
        public string? LogicalPath { get; set; }
        public bool? HasExactFile { get; set; }
        public FalloutSoundRecord? Descriptor { get; set; }
        public List<AnimationSoundVariantRow> Variants { get; } = [];
        public string EmitterAdmission { get; } = "unverified; encoded empty emitter is not a fabricated node";
        public string NativeAdmission { get; } = "unverified";
        public string StateAndDynamicPaths { get; } = "uninspected; no event, chance, loop, scheduling, save or SetSoundPath execution";
        public string? ErrorType { get; set; }
        public string? Error { get; set; }
    }

    internal sealed record AnimationSoundVariantRow(int SourceOrdinal, string Path)
    {
        public string Kind { get; } = "audio";
        public string Selection { get; } = "unselected; every source variant retained";
        public string ResourceEvidence { get; } = "join existing winning ResourceRow by Path and Kind; declaration is not a byte/decode receipt";
        public string NativeAdmission { get; } = "unverified";
    }

    /// <summary>
    /// Uses the selected graph's existing sound and variant owners. Source
    /// bytes and decoding remain the responsibility of CellAuditResources.
    /// </summary>
    internal sealed class SourceAnimationSoundDependencies(
        RuntimeLiveContentSource source, FalloutPluginStack? records)
    {
        internal AnimationSoundInspection Read(FalloutNifTextKeyExtraData textKeys)
        {
            var result = new AnimationSoundInspection();
            var edges = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < textKeys.Keys.Length; index++)
            {
                var key = textKeys.Keys[index];
                var row = new AnimationTextKeyRow
                {
                    Block = textKeys.Block.Index, Type = textKeys.Block.TypeName,
                    Offset = textKeys.Block.Offset, Bytes = textKeys.Block.Size,
                    Name = textKeys.Name, SourceOrdinal = index, SourceSeconds = key.Time,
                    SourceTimeBits = BitConverter.SingleToUInt32Bits(key.Time), RawText = key.Value
                };
                result.Keys.Add(row);
                foreach (var declaration in FalloutNifTextKeyDeclarations.Read(key.Value))
                {
                    var segment = new AnimationSoundSegmentRow { Declaration = declaration };
                    row.Segments.Add(segment);
                    if (declaration.Kind != FalloutNifTextKeyDeclarationKind.Sound) continue;
                    try
                    {
                        segment.ReaderOperation = "exact-owned-source";
                        var owner = records;
                        if (owner is null || !ReferenceEquals(owner.OwnedSource, source))
                            throw new InvalidDataException("NIF sound dependencies have no exact selected source/record owner.");
                        segment.ReaderOperation = "FalloutSoundRecordReader.Find";
                        var record = FalloutSoundRecordReader.Find(owner, declaration.EditorId!);
                        segment.Sound = record.FormKey.ToString();
                        segment.DeclaringMaster = record.FormKey.OwnerPlugin;
                        segment.WinningPlugin = record.Plugin.Name;
                        segment.WinningLoadOrder = owner.Plugins.Single(context => ReferenceEquals(context.Plugin, record.Plugin)).LoadOrderIndex;
                        segment.DeclaredMasters = record.Plugin.Masters.ToArray();
                        segment.RawFormId = record.RawFormId; segment.RecordFlags = record.Flags;
                        segment.FormVersion = record.FormVersion;
                        segment.HeaderOffset = record.HeaderOffset; segment.DataOffset = record.DataOffset;
                        segment.StoredBytes = record.StoredSize;
                        segment.ReaderOperation = "FalloutPluginRecord.ReadData";
                        var data = record.ReadData();
                        segment.DecodedBytes = data.Length;
                        segment.RecordSha256 = Convert.ToHexString(SHA256.HashData(data));
                        segment.ReaderOperation = "FalloutSoundPaths.Read";
                        var path = owner.SoundPaths.Read(record.FormKey);
                        segment.CurrentFile = path.File; segment.PathRevision = path.Revision;
                        segment.ReaderOperation = "FalloutSoundRecordReader.Read";
                        var descriptor = FalloutSoundRecordReader.Read(owner, record.FormKey);
                        segment.Descriptor = descriptor;
                        segment.WinningEditorId = descriptor.EditorId;
                        segment.LogicalPath = descriptor.LogicalPath;
                        segment.HasExactFile = descriptor.HasExactFile;
                        segment.DeclaredFile = FalloutPlugin.DecodeZeroTerminated(
                            record.ReadSubrecords().Single(field => field.Signature == "FNAM").Data.Span, "SOUN FNAM");
                        // This is the generic sound-name owner reached by the
                        // runtime after emitter resolution. It does not prove
                        // that resolution can happen for this declaration.
                        segment.ReaderOperation = "FalloutAnimationSound.EditorId";
                        _ = FalloutAnimationSound.EditorId(FalloutNifTextKeyDeclarations.SoundPrefix + declaration.EditorId);
                        segment.ReaderOperation = "FalloutAnimationSound.Variants";
                        var variants = FalloutAnimationSound.Variants(descriptor,
                            descriptor.HasExactFile ? [] : source.ResourcePathsUnder(descriptor.LogicalPath));
                        for (var variant = 0; variant < variants.Count; variant++)
                        {
                            var logical = Canonical(variants[variant]);
                            segment.Variants.Add(new(variant, logical));
                            if (edges.Add(logical)) result.DependencyEdges.Add(new(logical, "audio"));
                        }
                        segment.SourceDisposition = "winning-sound-and-all-variant-declarations-resolved; bytes and semantics separate";
                    }
                    catch (Exception error)
                    {
                        segment.SourceDisposition = "sound-source-declaration-refused";
                        segment.ErrorType = error.GetType().FullName; segment.Error = error.Message;
                        result.Failures.Add(new { lane = "nif-animation-sound-declaration", block = textKeys.Block.Index,
                            type = textKeys.Block.TypeName, sourceOrdinal = index, sourceSeconds = key.Time,
                            sourceTimeBits = row.SourceTimeBits, rawText = key.Value,
                            rawSegmentOrdinal = declaration.RawSegmentOrdinal, dispatchOrdinal = declaration.DispatchOrdinal,
                            declaration.Text, declaration.EditorId, declaration.Emitter,
                            operation = segment.ReaderOperation, errorType = segment.ErrorType, error = error.Message });
                    }
                }
            }
            return result;
        }
    }
}
