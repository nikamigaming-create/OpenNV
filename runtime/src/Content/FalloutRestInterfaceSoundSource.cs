using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutRestInterfaceCue(FalloutFormKey Sound, string EditorId, string RecordSha256,
    int CallerIndex, string CatalogueSha256)
{
    internal FalloutSoundRecord RequireCurrent(FalloutPluginStack records)
    {
        var original = FalloutSoundRecordReader.Find(records, EditorId);
        if (original.FormKey != Sound || original.Signature != "SOUN" ||
            FalloutRestLocation.SourceIdentity(original) != RecordSha256)
            throw new InvalidDataException("Rest interface sound changed its winning source lookup/identity.");
        return FalloutSoundRecordReader.Read(records, Sound);
    }
}

internal sealed record FalloutRestInterfaceSoundSource(string RestSourceSha256,
    FalloutRestInterfaceCue Start, FalloutRestInterfaceCue Cancel)
{
    internal string Identity => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this))));

    internal static FalloutRestInterfaceSoundSource Read(FalloutPluginStack records, FalloutSleepWaitSource rest,
        FalloutInterfaceSoundCatalogue? retainedCatalogue = null)
    {
        rest.Validate();
        // Both reviewed original button branches enter the shared interface
        // sound selector. These are engine cue declarations, never grants,
        // actor placement, quest outcomes or sound-path overrides.
        var source = records.OwnedSource ?? throw new NotSupportedException("Rest cues have no actual selected executable source.");
        var catalogue = retainedCatalogue ?? FalloutExecutableStringTable.ReadInterfaceSounds(source.FalloutExecutablePath, rest.EngineSha256);
        catalogue.Validate();
        if (catalogue.EngineSha256 != rest.EngineSha256)
            throw new InvalidDataException("Rest cue catalogue has another actual selected engine.");
        return new(rest.Identity, Cue(1), Cue(2));
        FalloutRestInterfaceCue Cue(int callerIndex)
        {
            var entry = catalogue.Resolve(callerIndex);
            if (entry.Disposition != FalloutInterfaceSoundDisposition.NamedSound || entry.EditorId is not { } editorId)
                throw new NotSupportedException("Original rest button no longer has an admitted named indexed cue.");
            var record = FalloutSoundRecordReader.Find(records, editorId);
            _ = FalloutSoundRecordReader.Read(records, record.FormKey);
            return new(record.FormKey, editorId, FalloutRestLocation.SourceIdentity(record), callerIndex, catalogue.Identity);
        }
    }

    internal void RequireSource(FalloutPluginStack records, FalloutSleepWaitSource rest,
        FalloutInterfaceSoundCatalogue? retainedCatalogue = null)
    {
        if (this != Read(records, rest, retainedCatalogue))
            throw new InvalidDataException("Rest interface cues changed their selected source/menu/SOUN declarations.");
    }
}
