using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;

internal static class OwnedRadioHudProbe
{
    internal static void Run(string installation, string[] options)
    {
        var command = DevelopmentLabSource.ParseCommand(["radio-hud", installation, "declarations", .. options]);
        DevelopmentLabSource.Configure(installation, command.Selection);
        try
        {
            var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned radio audit has no selected source.");
            using var records = FalloutPluginStack.Load(source.PluginSources);
            var executable = source.FalloutExecutablePath;
            var before = SHA256.HashData(File.ReadAllBytes(executable));
            var declaration = FalloutExecutableStringTable.ReadRadioHudDeclaration(executable);
            var inventoryNotice = FalloutExecutableStringTable.ReadHudMessageDeclarations(executable);
            var sound = FalloutDialogueTopic.Find(records, "SOUN", declaration.SoundEditorId);
            var soundPath = FalloutSoundRecordReader.Read(records, sound.FormKey).LogicalPath;
            if (soundPath.Length == 0 || !source.TryRead(soundPath, null, out var audio, out _) ||
                !source.TryRead("textures/" + declaration.Icon.Replace('\\', '/'), null, out var icon, out _) ||
                !FalloutGameSettingStrings.Read(records, "sRadioStationDiscovered").Contains("%s", StringComparison.Ordinal))
                throw new InvalidDataException("Owned radio discovery has missing winning sound/icon/text media.");
            if (!source.TryRead("textures/" + inventoryNotice.ItemIcon.Replace('\\', '/'), null, out var inventoryIcon, out _))
                throw new InvalidDataException("Owned inventory notice has no winning icon media.");
            var inventoryLabels = new Dictionary<string, string>();
            foreach (var name in new[] { "sAddItemtoInventory", "sRemoveItemfromInventory", "sPlural" })
                inventoryLabels.Add(name, FalloutGameSettingStrings.Read(records, name));
            if (!SHA256.HashData(File.ReadAllBytes(executable)).AsSpan().SequenceEqual(before))
                throw new InvalidDataException("Radio audit changed its read-only executable.");
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                schema = "opennv-owned-radio-hud-declaration/v1", game = source.Game, stack = source.StackId,
                executableSha256 = Convert.ToHexString(before), declaration, inventoryNotice, inventoryLabels,
                sound = sound.FormKey, soundPath, soundSha256 = Convert.ToHexString(SHA256.HashData(audio)),
                iconSha256 = Convert.ToHexString(SHA256.HashData(icon)),
                inventoryIconSha256 = Convert.ToHexString(SHA256.HashData(inventoryIcon)), sourceReadOnly = true, recording = false,
                boundary = "selected-source-declarations-and-media;ordinary-discovery-and-retail-audio-pixels-independent"
            }));
        }
        finally { RuntimeLiveContentSource.Clear(); }
    }
}
