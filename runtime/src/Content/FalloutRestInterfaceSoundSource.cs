using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutRestInterfaceCue(FalloutFormKey Sound, string EditorId, string RecordSha256)
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

    internal static FalloutRestInterfaceSoundSource Read(FalloutPluginStack records, FalloutSleepWaitSource rest)
    {
        rest.Validate();
        // Both reviewed original button branches enter the shared interface
        // sound selector. These are engine cue declarations, never grants,
        // actor placement, quest outcomes or sound-path overrides.
        return new(rest.Identity, Cue("UIMenuOK"), Cue("UIMenuCancel"));
        FalloutRestInterfaceCue Cue(string editorId)
        {
            var record = FalloutSoundRecordReader.Find(records, editorId);
            _ = FalloutSoundRecordReader.Read(records, record.FormKey);
            return new(record.FormKey, editorId, FalloutRestLocation.SourceIdentity(record));
        }
    }

    internal void RequireSource(FalloutPluginStack records, FalloutSleepWaitSource rest)
    {
        if (this != Read(records, rest))
            throw new InvalidDataException("Rest interface cues changed their selected source/menu/SOUN declarations.");
    }
}
