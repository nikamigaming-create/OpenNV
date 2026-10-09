using System.Text.Json;
using OpenNV.Runtime.Compatibility.NativePlugins;
using OpenNV.Runtime.Content;

internal static partial class NativeLoadedFileContracts
{
    // Reuses the existing independent ESM/override/construction fixture bytes.
    // This proves source-component ownership only; no original DLL executes.
    private static void CheckSourceContinuations(FalloutPluginStack records, FalloutPluginContext context,
        FalloutNativeBinaryFileConstruction construction)
    {
        var original = File.ReadAllBytes(context.Plugin.Path); var originalHash = Hash(context.Plugin.Path);
        using (var source = new FalloutNativePluginBinaryFile(records, context, construction, 8))
        {
            Require(Read(source, 3).AsSpan().SequenceEqual(original.AsSpan(0, 3)), "continuation actual buffered prefix");
            source.Seek(3, construction.SeekEnd); _ = Read(source, 2);
            var saved = source.CaptureSourceState();
            Require(saved.WrittenExtent == 8 && saved.BufferBytes == 3 && saved.BufferSources.Count == 2,
                "source-coordinate continuation retains genuine short-refill tail origins");
            var document = JsonSerializer.Serialize(saved);
            Require(!document.Contains("WrittenBuffer", StringComparison.Ordinal), "no retail buffer is a persistent continuation input");
            using var recreated = new FalloutNativePluginBinaryFile(records, context, construction, 8);
            recreated.RestoreSourceState(saved);
            Require(Read(recreated, 5).AsSpan().SequenceEqual(original.AsSpan(original.Length - 1)),
                "source coordinates recreate actual independent backend/logical/buffer progress");
            var before = JsonSerializer.Serialize(source.CaptureSourceState());
            Refuse(() => source.RestoreSourceState(saved with { WrittenSha256 = new string('0', 64) }), "source buffer digest divergence");
            Refuse(() => source.RestoreSourceState(saved with { BufferSources = saved.BufferSources.Skip(1).ToArray() }), "missing source tail range");
            Refuse(() => source.RestoreSourceState(saved with { BufferSources = saved.BufferSources.Select((row, ordinal) =>
                ordinal == 0 ? row with { Offset = 1 } : row).ToArray() }), "overlapping source range origin");
            Refuse(() => source.RestoreSourceState(saved with { RuntimeSha256 = new string('0', 64) }), "unrelated executable construction");
            Require(JsonSerializer.Serialize(source.CaptureSourceState()) == before, "failed source coordinate preflight changes no reader");
        }

        var record = context.Plugin.Records.Single(row => row.RawFormId == 0x800);
        using (var loaded = new FalloutNativePluginLoadedFile(records, context, construction, 8))
        {
            loaded.SelectRecord(record); _ = loaded.NextChunk(); _ = loaded.ReadChunk(3);
            var saved = loaded.CaptureSourceState();
            Require(saved.Parser.RecordHeaderOffset == record.HeaderOffset && saved.Parser.RecordHeaderSha256 is { Length: 64 } &&
                !JsonSerializer.Serialize(saved).Contains("RecordHeader\"", StringComparison.Ordinal),
                "parser continuation keeps original header identity rather than saved header bytes");
            using var cold = new FalloutNativePluginLoadedFile(records, context, construction, 8);
            cold.RestoreSourceState(saved);
            Require(cold.ReadChunk(0).Bytes.Span.SequenceEqual("buffer-fixture\0"u8), "actual parser/cursor reconstructs source chunk");
            var before = FalloutNativePluginLoadedFile.SourceStateSha256(loaded.CaptureSourceState());
            Refuse(() => loaded.RestoreSourceState(saved with { Parser = saved.Parser with { RecordHeaderSha256 = new string('0', 64) } }),
                "changed original header digest");
            Refuse(() => loaded.RestoreSourceState(saved with { Parser = saved.Parser with { RecordHeaderOffset = 0 } }),
                "unrelated real original header is not a substitute current record");
            Refuse(() => loaded.RestoreSourceState(saved with { Parser = saved.Parser with { BodySha256 = new string('0', 64) } }),
                "changed original decoded body identity");
            Require(FalloutNativePluginLoadedFile.SourceStateSha256(loaded.CaptureSourceState()) == before,
                "failed joint source reconstruction preserves independent actual state");
        }

        var later = records.Plugins.Single(row => row.Plugin.Name == "Later.esp");
        using (var files = new FalloutNativePluginLoadedFiles(records, construction))
        {
            using var first = files.Retain(context); using var second = files.Retain(context); using var other = files.Retain(later);
            first.SelectRecord(record); _ = first.CallSourceFile(NativeNvseFileMethod.NextChunk, 0);
            var saved = files.CaptureSourceState();
            _ = second.CallSourceFile(NativeNvseFileMethod.ReadChunk, 3);
            var before = JsonSerializer.Serialize(files.CaptureSourceState());
            var changedLater = saved with { Contributors = saved.Contributors.Select((row, ordinal) => ordinal == 0 ? row : row with
                { State = row.State with { Metadata = row.State.Metadata with { Written = row.State.Metadata.Written ^ 1 } } }).ToArray() };
            Refuse(() => files.RestoreSourceState(changedLater), "later actual metadata divergence after earlier source restoration");
            Require(JsonSerializer.Serialize(files.CaptureSourceState()) == before, "joint source restore rolls back earlier valid contributor");
            Refuse(() => files.RestoreSourceState(saved with { Selection = saved.Selection.Reverse().ToArray() }), "changed complete selected contributor order");
            Refuse(() => files.RestoreSourceState(saved with { Selection = saved.Selection.Select((row, ordinal) => ordinal == 0 ? row :
                row with { Masters = [] }).ToArray() }), "changed actual master scope");
            files.RestoreSourceState(saved);
            Require(JsonSerializer.Serialize(files.CaptureSourceState()) == JsonSerializer.Serialize(saved) &&
                first.SourceCurrentStamp() == second.SourceCurrentStamp(), "one real source continuation is shared across module leases");
        }

        var failed = new FalloutNativePluginLoadedFiles(records, construction);
        var broken = failed.Retain(context); var shared = failed.Retain(context); var independent = failed.Retain(later);
        try
        {
            broken.SelectRecord(record); _ = broken.CallSourceFile(NativeNvseFileMethod.NextChunk, 0);
            _ = broken.CallSourceFile(NativeNvseFileMethod.ReadChunk, 3);
            broken.RetainSourceOutputFailure(new IOException("authored failed native delivery after committed source prefix"));
            Refuse(() => shared.CallSourceFile(NativeNvseFileMethod.ReadChunk, 0), "another module cannot replay a failed shared prefix");
            broken.RetainSourceOutputFailure(new IOException("authored later failure on the already failed actual lease"));
            Require(independent.SourceCurrentStamp().SourceSha256 == later.Sha256,
                "a failed source does not mark unrelated actual contributor data failed");
        }
        finally
        {
            broken.Dispose(); independent.Dispose();
            Refuse(shared.Dispose, "last failed contributor lease retains actual failure after closing handles");
            Refuse(failed.Dispose, "collection retirement preserves the actual source failure");
        }
        Require(Hash(context.Plugin.Path) == originalHash, "source continuation leaves original input bytes unchanged");
        Console.WriteLine("OPENNV_NATIVE_SOURCE_CONTINUATION_CONTRACT_PASS component=original-coordinates parser-buffer-metadata nativeReadback=UNEXECUTED originalDllCold=UNOWNED");
    }
}
