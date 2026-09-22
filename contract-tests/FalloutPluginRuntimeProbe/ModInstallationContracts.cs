using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World.Cells;

internal static class ModInstallationContracts
{
    internal static void OwnedExecution(string id, string selected, string game, string[] dependencies)
    {
        var setup = FalloutModInstallation.Detect(id, selected, game, dependencies);
        using var content = setup.OpenSource();
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var world = new FalloutReferenceWorld(records);
        var quests = new FalloutQuestState(records);
        var events = new FalloutScriptEvents();
        var globals = FalloutGlobalState.Read(records);
        var excluded = records.EffectiveRecords("QUST").Where(quest =>
            FalloutScriptLocals.AttachedScript(records, quest)?.Plugin.Name != setup.EntryPlugin).Select(quest => quest.FormKey).ToHashSet();
        var scripts = new FalloutQuestScripts(records, quests, excluded, new FalloutPlayerInventory(), globals,
            FalloutInstallationSettings.Read(content).Number("MAIN", "fQuestScriptDelayTime"), world, events);
        var executor = new FalloutReferenceScripts(records, world, quests, new(
            (_, _) => throw new NotSupportedException("Headless audit has no furniture input."),
            effect => throw new NotSupportedException($"Headless audit has no presentation effect host: {effect.Kind}."), Globals: globals, Events: events));
        scripts.Host = new((_, _) => throw new NotSupportedException("Headless audit has no native quest-stage host."),
            _ => throw new NotSupportedException("Headless audit has no player actor-value host."), executor.ExecuteProgram, executor.InvokeFunction);
        events.LoadGame();
        for (var frame = 0; frame < 360; ++frame)
        {
            scripts.Advance(1.0 / 60);
            events.Advance(1.0 / 60, true, executor.InvokeFunction);
        }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema = "opennv-mod-script-execution-audit/v1", setup.Id,
            plugins = records.Plugins.Select(plugin => new { name = plugin.Plugin.Name, plugin.Sha256 }),
            scripts = scripts.State,
            boundary = "Owned initialization through shared quest/reference/function/event owners. Headless presentation and player input are absent; no gameplay acceptance.",
        }));
    }

    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), $"opennv-mod-install-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var game = Path.Combine(root, "Game");
            var data = Path.Combine(game, "Data");
            var jam = Path.Combine(root, "JAM");
            var ttw = Path.Combine(root, "TTW");
            var packages = Path.Combine(root, "Dependencies");
            foreach (var directory in new[] { data, jam, ttw, packages }) Directory.CreateDirectory(directory);
            WritePlugin(data, "FalloutNV.esm");
            WritePlugin(jam, "JustAssortedMods.esp", "FalloutNV.esm", "Dependency.esm");
            WritePlugin(data, "Dependency.esm", "Nested.esm");
            var setup = FalloutModInstallation.Detect("jam", jam, game);
            Require(setup.MissingDependencies.Any(value => value.LogicalPath == "Nested.esm"), "Missing transitive master was hidden.");
            WritePlugin(packages, "Nested.esm");
            foreach (var dependency in setup.Dependencies.Where(value => value.SourcePath is null && !value.LogicalPath.EndsWith(".esm", StringComparison.Ordinal)))
            {
                var file = Path.Combine(packages, dependency.LogicalPath);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, "synthetic presence fixture; never loaded as a library");
            }
            var complete = FalloutModInstallation.Detect("jam", jam, game, [packages]);
            Require(complete.MissingDependencies.Count == 0, "Separate dependency packages did not resolve.");
            Require(complete.SetupStatus.Contains("under development", StringComparison.Ordinal), "Package presence claimed gameplay support.");
            var options = new Dictionary<string, string>();
            new FalloutModSelection("jam", jam, [packages]).WriteOptions(options);
            var resumed = FalloutModSelection.ReadOptions(options)!.Resolve(game);
            Require(resumed.ContentRoots.SequenceEqual(complete.ContentRoots) && resumed.ActivePlugins.SequenceEqual(complete.ActivePlugins),
                "An in-process or cold restart dropped mod content selection.");
            WritePlugin(packages, "Nested.esm", "Dependency.esm");
            Reject(() => FalloutModInstallation.Detect("jam", jam, game, [packages]));
            WritePlugin(packages, "Nested.esm");
            WritePlugin(ttw, "FalloutNV.esm");
            WritePlugin(ttw, "Fallout3.esm", "FalloutNV.esm");
            WritePlugin(ttw, "TaleOfTwoWastelands.esm", "FalloutNV.esm", "Fallout3.esm");
            var ttwGame = NativeGameInstallation.Detect(ttw);
            Require(ttwGame.Game == NativeGame.FalloutNewVegas && ttwGame.IsTaleOfTwoWastelands,
                "TTW was classified as standalone Fallout 3.");
            Require(FalloutModInstallation.Detect("ttw", ttw, null).Dependencies.Any(value => value.LogicalPath == "Fallout3.esm"),
                "TTW source masters were not inspected.");
            Reject(() => FalloutModInstallation.Detect("jam", ttw, game));
            Reject(() => FalloutModInstallation.ResolveFile(jam, "../Game/Data/FalloutNV.esm"));
            Reject(() => FalloutModInstallation.ResolveFile(jam, "C:/outside.esm"));
            WritePlugin(jam, "JustAssortedMods.esp", "../FalloutNV.esm");
            Reject(() => FalloutModInstallation.Detect("jam", jam, game));
            WritePlugin(jam, "JustAssortedMods.esp", "FalloutNV.esm");
            var pluginBytes = File.ReadAllBytes(Path.Combine(jam, "JustAssortedMods.esp"));
            BinaryPrimitives.WriteUInt32LittleEndian(pluginBytes.AsSpan(4), uint.MaxValue);
            File.WriteAllBytes(Path.Combine(jam, "JustAssortedMods.esp"), pluginBytes);
            Reject(() => FalloutModInstallation.Detect("jam", jam, game));
            ProfileRoundTrip(root, game, jam, ttw, packages);
            PackageLayouts(root, game);
            ModStackContracts.Run(root, game);
            Console.WriteLine("OPENNV_MOD_INSTALL_CONTRACT_PASS masters=transitive dependencies=separate-folders ttwEngine=NewVegas saves=isolated profileRoundTrip=true readiness=not-claimed");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void PackageLayouts(string root, string game)
    {
        var wrapped = Path.Combine(root, "Wrapped");
        var data = Path.Combine(wrapped, "Release Name", "Data");
        Directory.CreateDirectory(data);
        WritePlugin(data, "NewVegasBounties.esp", "FalloutNV.esm", "SomeguySeries.esm");
        Require(FalloutModInstallation.Detect("bounties", wrapped, game).ContentRoot == data, "A nested extracted Data folder was not recognized.");
        var eve = Path.Combine(root, "Eve");
        Directory.CreateDirectory(eve);
        WritePlugin(eve, "EVE FNV - ALL DLC.esp", "FalloutNV.esm", "GunRunnersArsenal.esm");
        WritePlugin(eve, "EVE FNV - NO DLC.esp", "FalloutNV.esm");
        var noDlc = FalloutModInstallation.Detect("eve", eve, game);
        Require(noDlc.EntryPlugin == "EVE FNV - NO DLC.esp" && noDlc.ActivePlugins.Count(name => name.StartsWith("EVE ", StringComparison.Ordinal)) == 1, "Alternative DLC plugins were activated together.");
        WritePlugin(Path.Combine(game, "Data"), "GunRunnersArsenal.esm", "FalloutNV.esm");
        Require(FalloutModInstallation.Detect("eve", eve, game).EntryPlugin == "EVE FNV - ALL DLC.esp", "The installed DLC variant was not selected.");
        var duplicate = Path.Combine(wrapped, "Another Release", "Data");
        Directory.CreateDirectory(duplicate);
        WritePlugin(duplicate, "NewVegasBounties.esp", "FalloutNV.esm");
        Reject(() => FalloutModInstallation.Detect("bounties", wrapped, game));
        var modStore = GodotLauncherProfileStore.Open(Path.Combine(root, "all-mods.json"), Path.Combine(root, "all-saves"));
        var saves = FalloutModCatalog.All.Select(definition => modStore.Save(definition.Id, wrapped, game).SavePath).ToArray();
        Require(saves.Distinct().Count() == FalloutModCatalog.All.Count, "Mod profiles shared a save namespace.");
        var reopened = GodotLauncherProfileStore.Open(Path.Combine(root, "all-mods.json"), Path.Combine(root, "all-saves"));
        Require(FalloutModCatalog.All.All(definition => reopened.TryGet(definition.Id, out _)), "A target mod disappeared after profile restart.");
    }

    internal static void Owned(string id, string selected, string game, string[] dependencies)
    {
        var setup = FalloutModInstallation.Detect(id, selected, game, dependencies);
        var pluginPath = setup.EntryPlugin is null ? null : setup.Dependencies.Single(value => value.LogicalPath == setup.EntryPlugin).SourcePath!;
        using var plugin = pluginPath is null ? null : FalloutPlugin.Open(pluginPath);
        var scripts = (plugin?.Records ?? []).Where(record => record.Signature == "SCPT").Select(record =>
        {
            var fields = record.ReadSubrecords().ToArray();
            var editorId = fields.SingleOrDefault(field => field.Signature == "EDID").Data;
            var source = fields.Where(field => field.Signature == "SCTX").ToArray();
            string? failure = null;
            try
            {
                if (source.Length != 1) throw new InvalidDataException("Script source is absent or ambiguous.");
                _ = FalloutGameModeProgram.Read(source[0].Data.Span);
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException)
            { failure = error.Message; }
            return new { editorId = Encoding.Latin1.GetString(editorId.Span).TrimEnd('\0'), form = record.RawFormId,
                parserAccepted = failure is null, failure };
        }).ToArray();
        using var content = setup.OpenSource();
        content.ArchiveWarmup.GetAwaiter().GetResult();
        using var stack = FalloutPluginStack.Load(content.PluginSources);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema = "opennv-mod-source-audit/v1", setup.Id, setup.SelectedRoot,
            engine = setup.BaseInstallation.Game.ToString(),
            setup.ContentRoots, setup.EntryPlugin, setup.ActivePlugins, content.SaveCompatibilityId,
            plugins = stack.Plugins.Select(value => new { name = value.Plugin.Name, value.Sha256 }),
            archives = content.ArchivePaths,
            dependencies = setup.Dependencies,
            missingPackages = setup.MissingDependencies.Select(value => value.LogicalPath),
            scripts, runtimeReady = false, gameplay = "unverified", parity = "unverified",
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void ProfileRoundTrip(string root, string game, string jam, string ttw, string packages)
    {
        var file = Path.Combine(root, "launcher", "profiles.json");
        var saves = Path.Combine(root, "saves");
        var store = GodotLauncherProfileStore.Open(file, saves);
        var standalone = store.Save("newvegas", game);
        var jamProfile = store.Save("jam", jam, game, [packages]);
        var ttwProfile = store.Save("ttw", ttw, game);
        Require(new[] { standalone.SavePath, jamProfile.SavePath, ttwProfile.SavePath }.Distinct().Count() == 3,
            "JAM, TTW and vanilla profiles share a save.");
        var restored = GodotLauncherProfileStore.Open(file, saves);
        Require(restored.TryGet("jam", out var saved) && saved.InstallRoot == jam && saved.BaseInstallRoot == game &&
            saved.DependencyRoots!.SequenceEqual([packages]) && saved.SavePath == jamProfile.SavePath,
            "Mod folder, base installation, dependencies or save path were lost after restart.");
        File.WriteAllText(file, JsonSerializer.Serialize(new { schema = "opennv-godot-launcher-profiles/v1",
            profiles = new Dictionary<string, GodotLauncherProfile> { ["newvegas"] = standalone } }));
        Require(GodotLauncherProfileStore.Open(file, saves).TryGet("newvegas", out var legacy) && legacy == standalone,
            "Existing PascalCase v1 registrations were not recovered.");
        File.WriteAllText(file, JsonSerializer.Serialize(new { schema = "opennv-godot-launcher-profiles/v1",
            profiles = new Dictionary<string, object> { ["jam"] = ttwProfile, ["newvegas"] = new { CampaignId = "newvegas", InstallRoot = 4 } } }));
        Require(!GodotLauncherProfileStore.Open(file, saves).TryGet("jam", out _), "A mismatched campaign registration was accepted.");
        Reject(() => store.Save("../outside", game));
    }

    internal static void WritePlugin(string root, string name, params string[] masters)
    {
        using var payload = new MemoryStream();
        using (var writer = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true))
            foreach (var master in masters)
            {
                writer.Write("MAST"u8);
                var nameBytes = Encoding.UTF8.GetBytes(master + "\0");
                writer.Write((ushort)nameBytes.Length);
                writer.Write(nameBytes);
            }
        var bytes = new byte[FalloutPlugin.RecordHeaderSize + payload.Length];
        "TES4"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), name.EndsWith(".esm", StringComparison.OrdinalIgnoreCase) ? 1u : 0u);
        payload.ToArray().CopyTo(bytes, FalloutPlugin.RecordHeaderSize);
        File.WriteAllBytes(Path.Combine(root, name), bytes);
    }

    private static void Require(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException(message);
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is IOException or ArgumentException or InvalidDataException) { return; }
        throw new InvalidOperationException("Invalid mod installation or profile was accepted.");
    }
}
