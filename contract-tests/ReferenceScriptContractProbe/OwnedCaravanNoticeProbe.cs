using OpenNV.Runtime.Content;

internal static class OwnedCaravanNoticeProbe
{
    internal static void Run(string game)
    {
        RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(game), includeImplicitPlugins: false);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var declaration = FalloutExecutableStringTable.ReadCaravanMinimumNotice(content.FalloutExecutablePath);
        var menuPath = FalloutExecutableStringTable.ReadCaravanMenuPath(content.FalloutExecutablePath);
        var menu = FalloutMenuSourceProgram.Read(menuPath, path => content.TryRead(path, null, out var bytes, out _)
            ? bytes : throw new FileNotFoundException("Caravan menu source is missing.", path));
        var text = FalloutGameSettingStrings.Read(records, declaration.Setting);
        var sound = FalloutSoundRecordReader.Find(records, declaration.SoundEditorId);
        if (string.IsNullOrEmpty(text)) throw new InvalidDataException("Caravan minimum notice setting is empty.");
        if (sound.Signature != "SOUN") throw new InvalidDataException("Caravan minimum notice sound is not SOUN.");
        if (!content.TryRead("textures/" + declaration.Icon, null, out var icon, out _) || icon.Length == 0)
            throw new InvalidDataException($"Caravan minimum notice icon is absent: {declaration.Icon}.");
        Console.WriteLine($"OPENNV_OWNED_CARAVAN_NOTICE_PASS setting=true icon=true sound=true menuEntries={menu.Count(value => value.Kind == FalloutMenuSourceEventKind.EnterSource)} seconds={declaration.Seconds:R} runtimeDelivery=unverified");
    }
}
