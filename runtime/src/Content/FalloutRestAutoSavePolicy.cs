using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutRestAutoSaveSetting(string Name, FalloutIniCollection Collection,
    uint DefaultBits, string Origin, bool Enabled);

internal sealed record FalloutRestAutoSavePolicy(string RestSourceSha256, string IniSource,
    FalloutRestAutoSaveSetting Sleep, FalloutRestAutoSaveSetting Wait)
{
    internal string Identity => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this))));

    internal static FalloutRestAutoSavePolicy Read(FalloutSleepWaitSource source, FalloutNumericIniSettings ini)
    {
        source.Validate(); ArgumentNullException.ThrowIfNull(ini);
        return new(source.Identity, ini.Source, ReadSetting("bSaveOnRest:GamePlay"), ReadSetting("bSaveOnWait:GamePlay"));

        FalloutRestAutoSaveSetting ReadSetting(string name)
        {
            // The original menu callers consume these preference declarations;
            // a same-named row in another collection is not a substitute.
            var value = ini.Find(FalloutIniCollection.Prefs, name);
            if (value is not { Declaration.Kind: 'b', Number: 0 or 1 } ||
                value.Declaration.Payload > 1 || string.IsNullOrWhiteSpace(value.Origin))
                throw new NotSupportedException("Rest autosave has no actual selected preference declaration: " + name);
            return new(value.Declaration.Name, value.Declaration.Collection, value.Declaration.Payload,
                value.Origin, value.Number == 1);
        }
    }

    internal bool Enabled(FalloutRestKind kind) => kind switch
    {
        FalloutRestKind.Sleep => Sleep.Enabled,
        FalloutRestKind.Wait => Wait.Enabled,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    internal void RequireSource(FalloutSleepWaitSource source)
    {
        source.Validate();
        if (RestSourceSha256 != source.Identity || string.IsNullOrWhiteSpace(IniSource) ||
            Sleep is null || Wait is null || Sleep.Name != "bSaveOnRest:GamePlay" || Wait.Name != "bSaveOnWait:GamePlay" ||
            new[] { Sleep, Wait }.Any(setting => setting.Collection != FalloutIniCollection.Prefs ||
                setting.DefaultBits > 1 || string.IsNullOrWhiteSpace(setting.Origin)))
            throw new InvalidDataException("Rest autosave policy differs from its selected source declarations.");
    }
}
