using System.Text.Json;
using OpenNV.Runtime.Content;

internal static class OwnedScreenBloodHeadlessProbe
{
    internal static int Run(string root, string game, string[] options)
    {
        var roots = new List<string>(); var plugins = new List<string>();
        for (var index = 0; index < options.Length; index += 2)
        {
            if (index + 1 >= options.Length) throw new ArgumentException("Screen-blood audit option has no value.");
            switch (options[index])
            {
                case "--mod-root": roots.Add(options[index + 1]); break;
                case "--plugin": plugins.Add(options[index + 1]); break;
                default: throw new ArgumentException("Unknown screen-blood audit option: " + options[index]);
            }
        }
        using var source = RuntimeLiveContentSource.Open(root, game, roots, plugins.Count == 0 ? null : plugins);
        using var records = FalloutPluginStack.Load(source.PluginSources);
        var declaration = FalloutCompiledCommandDeclarations.Get(0x1205, records);
        if (declaration is not { Name: "TriggerScreenBlood", Alias: "tsb", RequiresReference: false, Required: 1, VanillaArguments: true } ||
            !declaration.Parameters.SequenceEqual(new byte[] { 1 }) ||
            FalloutCompiledSemanticBinding.Read(declaration).Kind != FalloutCompiledSemanticKind.Effect)
            throw new InvalidDataException("Selected original screen-blood declaration differs from its shared effect owner.");
        var media = FalloutScreenBloodMediaReader.ForSource(source);
        var mask = media.Read(FalloutGameSettingStrings.Read(records, "sBloodSplatterAlpha01OPTFilename"));
        var color = media.Read(FalloutGameSettingStrings.Read(records, "sBloodSplatterColor01OPTFilename"));
        var enabled = FalloutInstallationSettings.Read(source).Boolean("ScreenSplatter", "bBloodSplatterEnabled");
        var owner = new FalloutScreenBlood(records, 123);
        using var adapter = new FalloutHeadlessScreenBlood(owner, enabled, media);
        owner.Trigger(records.RuntimeFormKey(FalloutScriptBindings.PlayerReferenceId), 1);
        var active = owner.ActiveDrops;
        adapter.Advance(10, gameMode: false);
        if (owner.ActiveDrops != active) throw new InvalidDataException("Headless menu pause advanced source blood lifetime.");
        if (owner.LastRequest is { } request)
        {
            adapter.Advance(Math.Max(0, request.Duration) + 1, gameMode: true);
            if (owner.ActiveDrops != 0 || adapter.ActiveGroups != 0)
                throw new InvalidDataException("Headless source blood expiration retained a group.");
        }
        Console.WriteLine("OPENNV_OWNED_HEADLESS_SCREEN_BLOOD_REPORT " + JsonSerializer.Serialize(new
        {
            game, source.StackId, declaration, enabled, selectedMedia = new[] { mask, color }, effect = owner.State,
            sourceReadOnly = true, renderer = "NONE", nativeHostBinding = "UNEXECUTED", finalPixels = "UNEXECUTED", gameplay = "UNEXECUTED"
        }));
        adapter.Dispose();
        if (owner.ActiveDrops != 0 || adapter.ActiveGroups != 0)
            throw new InvalidDataException("Headless screen-blood retirement retained source capacity.");
        Console.WriteLine("OPENNV_OWNED_HEADLESS_SCREEN_BLOOD_PASS sourceDeclaration=true selectedMediaBytes=true " +
            "sharedEffect=true sourceEnabledSetting=true menuClock=true lifetimeRetirement=true nativeResources=false draws=0 parity=unverified gameplay=UNEXECUTED");
        return 0;
    }
}
