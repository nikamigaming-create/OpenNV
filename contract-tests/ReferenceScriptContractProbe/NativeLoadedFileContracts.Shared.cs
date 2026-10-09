using System.Text.Json;
using OpenNV.Runtime.Compatibility.NativePlugins;
using OpenNV.Runtime.Content;

internal static partial class NativeLoadedFileContracts
{
    private static void CheckSharedContributors(string directory, FalloutPluginStack records,
        FalloutPluginContext context, FalloutNativeBinaryFileConstruction construction)
    {
        using var files = new FalloutNativePluginLoadedFiles(records, construction);
        using var first = files.Retain(context); using var second = files.Retain(context);
        Require(files.ReaderCount == 1 && first.Generation == second.Generation,
            "independent original-module projections share one actual contributor lifetime");
        var generation = first.Generation;
        var record = context.Plugin.Records.Single(row => row.RawFormId == 0x800);
        first.SelectRecord(record);
        Require(first.CallSourceFile(NativeNvseFileMethod.NextChunk, 0).Result == Tag("EDID") &&
            first.CallSourceFile(NativeNvseFileMethod.ReadChunk, 3).Bytes.Span.SequenceEqual("bu\0"u8),
            "first actual callback retains its source truncation/cursor");
        Require(second.CallSourceFile(NativeNvseFileMethod.ReadChunk, 0).Bytes.Span.SequenceEqual("buffer-fixture\0"u8) &&
            first.Capture().Parser.BytesRead == second.Capture().Parser.BytesRead,
            "second actual module sees the shared advancing/repeated-read owner");
        first.Dispose();
        Refuse(() => first.CallSourceFile(NativeNvseFileMethod.ReadChunk, 0), "retired module file lease");
        Require(files.ReaderCount == 1 && second.CallSourceFile(NativeNvseFileMethod.ReadChunk, 0).Bytes.Length != 0,
            "retiring one projection preserves an independent source retainer");

        var overlay = records.Plugins.Single(row => row.Plugin.Name == "Later.esp");
        using var other = files.Retain(overlay);
        var settled = files.Capture();
        Require(settled.Readers.Select(row => row.Plugin).SequenceEqual(records.Plugins.Select(row => row.Plugin.Name)),
            "collection snapshot preserves actual selected contributor order");
        Require(second.CallSourceFile(NativeNvseFileMethod.AdvanceChunk, 0).Result == 1 &&
            second.CallSourceFile(NativeNvseFileMethod.Read32, 4).Bytes.Span.SequenceEqual(new byte[] { 17, 34, 51, 68 }),
            "shared source method genuinely changes the current chunk before cold refusal");
        var before = JsonSerializer.Serialize(files.Capture());
        var malformed = settled with
        {
            Readers = settled.Readers.Select((row, ordinal) => ordinal == 0 ? row : row with
            { File = row.File with { Metadata = row.File.Metadata with { Written = row.File.Metadata.Written ^ 1 } } }).ToArray()
        };
        Refuse(() => files.Restore(malformed), "later contributor metadata mismatch after genuine earlier restoration");
        Require(JsonSerializer.Serialize(files.Capture()) == before,
            "collection refusal independently rolls back the earlier successfully restored sibling");
        files.Restore(settled);
        Require(JsonSerializer.Serialize(files.Capture()) == JsonSerializer.Serialize(settled),
            "all exact original contributor/parser/buffer states restore without new native addresses");

        Refuse(() => second.CallSourceFile((NativeNvseFileMethod)99, 0), "unknown native method");
        Refuse(() => second.CallSourceFile(NativeNvseFileMethod.Read32, 3), "foreign native argument extent");
        Require(JsonSerializer.Serialize(files.Capture()) == JsonSerializer.Serialize(settled),
            "unadmitted ABI request changes no actual source prefix");
        using (var foreign = FalloutPluginStack.Load(directory, ["Current.esm", "Later.esp"]))
            Refuse(() => files.Retain(foreign.Plugins.Single(row => row.Plugin.Name == context.Plugin.Name)),
                "same named/hash source from a different campaign reader lifetime");
        other.Dispose(); second.Dispose();
        Require(files.ReaderCount == 0, "last source lease retires real contributor handles");
        using var next = files.Retain(context);
        Require(next.Generation > generation && files.ReaderCount == 1,
            "recreated source reader owns a new process-local lifetime rather than reviving a retired lease");
    }
}
