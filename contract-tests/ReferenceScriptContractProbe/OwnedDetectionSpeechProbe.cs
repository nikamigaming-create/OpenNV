using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class OwnedDetectionSpeechProbe
{
    internal static void Run(string mod, string root, string game, FalloutFormKey speaker, FalloutFormKey topic,
        FalloutFormKey info, string output, string[] dependencies)
    {
        var path = OwnedSpeechCompletionProbe.OutputPath(output, game, root, dependencies);
        using var content = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(game).OpenSource();
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var world = new FalloutReferenceWorld(records);
        var instance = world.Get(speaker);
        var definition = instance.Script ?? throw new InvalidDataException("Selected detection caller has no attached winning source script.");
        var record = records.GetEffective(speaker);
        if (record.Signature is not ("ACHR" or "ACRE")) throw new InvalidDataException("Selected source detection speaker is not an actor.");
        var script = definition.Record;
        var bindings = new FalloutScriptBindings(records, record, script, script.ReadSubrecords());
        var text = script.ReadSubrecords().Where(field => field.Signature == "SCTX").ToArray();
        if (text.Length != 1) throw new InvalidDataException("Selected detection script has no unique source program.");
        var blocks = FalloutGameModeProgram.ReadEvents(FalloutDialogueTopic.ScriptText(text[0].Data.Span));
        var sites = blocks.SelectMany((block, ordinal) => block.Program.CommandSites("CreateDetectionEvent")
            .Select(site => (Block: block, Ordinal: ordinal, Site: site))).ToArray();
        if (sites.Length == 0) throw new InvalidDataException("Selected source script declares no CreateDetectionEvent command.");
        var receipt = new FalloutSpeechCompletionReceipt(speaker, new HashSet<FalloutFormKey> { topic }, info, 1);
        var identity = FalloutFinishedSpeechSourceBinding.Capture(records, instance, receipt);
        const string lifetimeName = "fDetectionEventExpireTime";
        var lifetimeRecords = records.EffectiveRecords("GMST").Where(value => value.ReadSubrecords().Any(field =>
            field.Signature == "EDID" && FalloutDialogueTopic.Text(field.Data.Span).Equals(lifetimeName, StringComparison.OrdinalIgnoreCase))).ToArray();
        var lifetimeSeconds = FalloutGameSettingFloats.Read(records, lifetimeName);
        var sources = new[] { record, records.GetEffective(instance.Base), script, records.GetEffective(topic), records.GetEffective(info),
            records.GetEffective(instance.Cell) }.Concat(lifetimeRecords).ToArray();
        string Hash(FalloutPluginRecord value) => Convert.ToHexString(SHA256.HashData(value.ReadData()));
        var hashes = sources.Select(Hash).ToArray();
        var executable = lifetimeRecords.Length == 0 ? Path.Combine(Path.GetDirectoryName(content.ContentRoot)!, "FalloutNV.exe") : null;
        string? ExecutableHash()
        {
            if (executable is null) return null;
            using var stream = File.OpenRead(executable);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        var executableHash = ExecutableHash();
        var declared = new List<object>();
        var suffixes = 0;
        foreach (var selected in sites)
        {
            var arguments = selected.Site.Arguments;
            if (arguments.Count is < 2 or > 3) throw new InvalidDataException("Selected detection declaration has an invalid argument count.");
            var owner = bindings.Reference(arguments[0]);
            int Signed(string value) => int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
            var level = Signed(arguments[1]); var type = arguments.Count == 3 ? Signed(arguments[2]) : 3;
            var parts = selected.Site.Command.Split('.');
            var location = parts.Length == 1 ? speaker : parts.Length == 2 ? bindings.Reference(parts[0]) :
                throw new NotSupportedException("Selected detection declaration has an unbound caller path.");
            bool Fixed(string token) => double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) ||
                FalloutScriptBindings.IsPlayer(token) && bindings.HasPlayerReference || bindings.TryForm(token) is not null;
            var error = $"Reached native script command {selected.Site.Command} ({arguments.Count} arguments) has no owner.";
            var suffix = selected.Block.Program.MissingCommandContinuation(error, Fixed, selected.Site.Statement);
            if (suffix is not null && suffix.StartStatement == selected.Site.Statement) ++suffixes;
            declared.Add(new { sourceBlock = selected.Ordinal, sourceEvent = selected.Block.Event, selected.Block.Filter,
                statement = selected.Site.Statement, callingLocation = location.ToString(), processOwner = owner.ToString(),
                signedLevel = level, requestedType = type, retainedFixedArgumentSuffixSupported = suffix is not null });
        }
        // This independent creation fixture uses the original source placement
        // and an explicitly selected process tier. It does not reconstruct any
        // historical invocation, execute its guarded prefix, or manufacture a save.
        var first = sites[0]; var args = first.Site.Arguments;
        var processOwner = bindings.Reference(args[0]);
        var callerParts = first.Site.Command.Split('.');
        var sourceLocation = callerParts.Length == 1 ? speaker : bindings.Reference(callerParts[0]);
        world.Detection.Bind(world.Placement, _ => FalloutDetectionProcessLevel.High);
        var request = world.Detection.Prepare(processOwner, sourceLocation, int.Parse(args[1], CultureInfo.InvariantCulture),
            args.Count == 3 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 3);
        world.Detection.Create(request, first.Site.Command, args.Count);
        var eventSnapshot = JsonSerializer.Deserialize<FalloutDetectionEventsSnapshot>(JsonSerializer.Serialize(world.CaptureDetection()))!;
        var savedReferences = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
        using var cold = new FalloutReferenceWorld(records); cold.Restore(savedReferences); cold.RestoreDetection(eventSnapshot);
        FalloutFinishedSpeechSourceBinding.Require(records, cold.Retained(speaker), identity);
        if (JsonSerializer.Serialize(eventSnapshot) != JsonSerializer.Serialize(cold.CaptureDetection()))
            throw new InvalidDataException("Selected source event creation changed cold state.");
        var receiverRefused = false;
        try { cold.Detection.RequireConsumer(processOwner); }
        catch (NotSupportedException) { receiverRefused = true; }
        if (!receiverRefused || sources.Where((value, index) => Hash(value) != hashes[index]).Any() || ExecutableHash() != executableHash)
            throw new InvalidDataException("Selected source event hid its receiver boundary or modified owned bytes.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, new
        {
            schema = "opennv-owned-detection-speech-contract/v1", capturedUtc = DateTime.UtcNow,
            runtimeMvid = typeof(FalloutReferenceScripts).Module.ModuleVersionId,
            saveCompatibilityId = content.SaveCompatibilityId,
            sourceRecords = sources.Select((value, index) => new { form = value.FormKey.ToString(), value.Signature,
                winner = value.Plugin.Name, masters = value.Plugin.Masters, sourceSha256 = hashes[index] }),
            speaker = speaker.ToString(), script = script.FormKey.ToString(), info = info.ToString(), topic = topic.ToString(),
            creationDeclarations = declared, supportedRetainedSuffixShapes = suffixes,
            winningLifetimeSeconds = lifetimeSeconds,
            lifetimeSource = new { name = lifetimeName, kind = executable is null ? "winning-GMST" : "owned-executable-default",
                sourceSha256 = executableHash, numericSettings = records.NumericSettings.State },
            fixture = new { processLevelExplicit = "High", placement = "winning source reference; not observed native pose",
                simulationClock = "new disposable fixture clock; not a historical invocation", request, coldEventExact = true },
            sourceReceiptBinding = identity.Source, receiverConsumerOwned = false, receiverRefused,
            sourceUnchanged = true, recording = false,
            boundary = "read-only source/independent C# creation fixture; no guarded GameMode/audio/native/input/campaign save; actual fresh completion/cold Continue still required"
        }, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine("OPENNV_OWNED_DETECTION_SPEECH_CONTRACT_PASS sourceDeclarations=true receiptBinding=true " +
            "explicitCreationFixtureCold=true receiverUnbound=true originalSourceUnchanged=true gameplayAndNative=unverified recording=false");
    }
}
