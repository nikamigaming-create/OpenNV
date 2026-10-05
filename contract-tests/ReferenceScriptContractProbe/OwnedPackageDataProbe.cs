using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedPackageDataProbe
{
    internal static void Run(string game, string mod, string root, FalloutFormKey reference,
        FalloutFormKey expectedPackage, string checkpoint, string[] dependencies)
    {
        var setup = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(game);
        RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
            setup.ContentRoots.Skip(1).ToArray(), setup.ActivePlugins, setup.Settings);
        try
        {
            RunOwned(RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned source is not configured."),
                game, reference, expectedPackage, checkpoint);
        }
        finally { RuntimeLiveContentSource.Clear(); }
    }

    private static void RunOwned(RuntimeLiveContentSource content, string game, FalloutFormKey reference,
        FalloutFormKey expectedPackage, string checkpoint)
    {
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var headers = records.EffectiveRecords("PACK").Select(record =>
            (Record: record, Data: FalloutPackageData.Read(record), Hash: Hash(record))).ToArray();
        if (headers.Length == 0) throw new InvalidDataException("Owned package-data audit has no winning PACK records.");
        var checkpointBytes = File.ReadAllBytes(checkpoint);
        var openingControls = FalloutOpeningPlayerControlResolver.Resolve(records, ["VCG00", "VCG01"]);
        var openingCell = FalloutCellSceneReader.Read(records, new("FalloutNV.esm", 0x103df9));
        var saved = FalloutNativeCampaignSave.Read(checkpoint, content.SaveCompatibilityId, records,
            FalloutNativeVigorResolver.Resolve(records, openingCell), FalloutNativeTagSkillResolver.Resolve(records, openingControls),
            FalloutOpeningInventoryGrantResolver.Resolve(records, openingControls, "VCG01"),
            FalloutNativeTraitFarewellResolver.Resolve(records, openingControls, openingCell)).State;
        if (saved.SaveCompatibilityId != content.SaveCompatibilityId || saved.Quests is null || saved.Globals is null ||
            saved.GameTime is null || saved.References is null)
            throw new InvalidDataException("Package priority proof needs a complete checkpoint from the selected owned source graph.");
        var quests = new FalloutQuestState(records); quests.Restore(saved.Quests);
        var globals = FalloutGlobalState.Read(records); globals.Restore(saved.Globals);
        var clock = new FalloutGameTime(globals, FalloutGameTimeBindings.Read(records),
            FalloutCalendar.Read(Path.Combine(game, "FalloutNV.exe")));
        clock.Restore(saved.GameTime);
        using var world = new FalloutReferenceWorld(records);
        world.RestoreEncounterZones(saved.EncounterZones); world.Restore(saved.References);
        world.RestoreActorOverrides(saved.ActorOverrides); world.RestoreFactionRelations(saved.FactionRelations);
        if (records.GetEffective(reference).Signature != "ACHR")
            throw new InvalidDataException("Owned package priority reference is not a winning ACHR.");
        var actor = world.Get(reference);
        if (records.GetEffective(actor.Base).Signature != "NPC_")
            throw new InvalidDataException("Owned package priority reference is not an actual NPC.");
        var owner = FalloutActorTemplateOwner.Resolve(records, records.GetEffective(actor.Base), 32, actor.Templates);
        var candidates = owner.ReadSubrecords().Where(field => field.Signature == "PKID").Select(field =>
        {
            if (field.Data.Length != 4) throw new InvalidDataException("Owned package priority link has an invalid extent.");
            return records.GetEffective(owner.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span)));
        }).ToArray();
        var evaluations = new List<object>();
        float Evaluate(FalloutCondition condition)
        {
            var value = condition.Function switch
            {
                1 or 14 => world.EvaluateActorReferenceCondition(reference, condition, null, 1) ??
                    throw new NotSupportedException("Owned package condition lacks its shared reference query."),
                56 => FalloutAiPackages.QuestRunning(condition, quests),
                58 or 59 or 79 or 546 when condition.RunOn == 0 => quests.Evaluate(condition),
                _ => throw new NotSupportedException($"Owned package priority reached unbound condition {condition.Function}/{condition.RunOn}."),
            };
            evaluations.Add(new { package = condition.Owner.FormKey.ToString(), condition.Function, value });
            return value;
        }
        var selected = FalloutAiPackages.Select(records, actor.Base, Evaluate, actor.Templates, clock, evaluateRunOn: true,
            eligible: package => world.PackageEligible(reference, package, clock, actor.PackageAssignment?.Package, actor.PackageAssignment?.Done == true));
        if (selected is null || selected.FormKey != expectedPackage || !candidates.Any(package => package.FormKey == expectedPackage))
            throw new InvalidDataException("Owned checkpoint priority selection differs from its expected source package.");
        var header = FalloutPackageData.Read(selected);
        var declaration = FalloutScriptPackage.Read(selected);
        if (header.SourceExtent != 8 || header.SpecificFlags is not null || declaration.Procedure != 6 || declaration.LocationType != 3)
            throw new InvalidDataException("Selected owned priority proof is not the shorter editor-location Travel declaration.");
        var editorTravel = FalloutEditorTravelPackage.Read(selected);
        var progress = editorTravel.Start(world, reference);
        editorTravel.Validate(world, reference, progress);
        var destination = world.EditorPlacement(reference);
        if (progress.Cell != destination.Cell || !progress.Location.SequenceEqual(destination.Position) || progress.Complete)
            throw new InvalidDataException("Owned editor Travel replaced its winning destination or granted arrival.");
        var sourceSchedule = FalloutPackageSchedule.Read(selected);
        if (!sourceSchedule.IsActive(clock)) throw new InvalidDataException("Owned priority proof lost its authored schedule.");
        var snapshots = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
        using var cold = new FalloutReferenceWorld(records);
        cold.RestoreEncounterZones(world.CaptureEncounterZones()); cold.Restore(snapshots);
        cold.RestoreActorOverrides(world.CaptureActorOverrides()); cold.RestoreFactionRelations(world.CaptureFactionRelations());
        var coldProgress = JsonSerializer.Deserialize<FalloutEditorTravelProgress>(JsonSerializer.Serialize(progress))!;
        editorTravel.Validate(cold, reference, coldProgress);
        if (coldProgress.Complete || !cold.Placement(reference).Position.SequenceEqual(world.Placement(reference).Position))
            throw new InvalidDataException("Cold editor Travel lost actual placement or fabricated completion.");
        var events = new List<string>();
        var lifecycle = new FalloutPackageEvents((package, kind) =>
        {
            package.EventPrograms[kind].RequireEmptyScript(); events.Add(kind);
        });
        lifecycle.Change(declaration); lifecycle.Complete(); lifecycle.Complete();
        var restored = new FalloutPackageEvents((_, kind) => events.Add(kind));
        restored.Restore(declaration, true); restored.Complete();
        if (!events.SequenceEqual(["POBA", "POEA"]))
            throw new InvalidDataException("Owned package lifecycle or restoration replayed an event.");
        if (headers.Any(row => Hash(row.Record) != row.Hash) ||
            !SHA256.HashData(checkpointBytes).SequenceEqual(SHA256.HashData(File.ReadAllBytes(checkpoint))))
            throw new InvalidDataException("Owned package-data audit changed its source or checkpoint bytes.");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema = "opennv-owned-package-data-audit/v1", content.SaveCompatibilityId,
            checkpointSha256 = Convert.ToHexString(SHA256.HashData(checkpointBytes)),
            admittedCheckpointSchema = saved.Schema,
            runtimeBuild = typeof(FalloutPackageData).Assembly.ManifestModule.ModuleVersionId,
            packages = headers.Length,
            extents = headers.GroupBy(row => row.Data.SourceExtent).OrderBy(group => group.Key)
                .Select(group => new { extent = group.Key, count = group.Count(),
                    procedures = group.GroupBy(row => row.Data.Procedure).OrderBy(procedure => procedure.Key)
                        .Select(procedure => new { procedure = procedure.Key, count = procedure.Count() }) }),
            reference = reference.ToString(), actorBase = actor.Base.ToString(), packageOwner = owner.FormKey.ToString(),
            candidates = candidates.Select(package => new { form = package.FormKey.ToString(), winner = package.Plugin.Name, sha256 = Hash(package) }),
            selected = selected.FormKey.ToString(), selectedSha256 = Hash(selected),
            header, sourceSchedule, scheduleTime = clock.ScheduleTime(), evaluations,
            editorDestination = progress, cold = true, eventOnce = true, sourceUnchanged = true,
            nativeExecution = "unverified by this source-only audit", campaign = false, framesRecorded = false,
        }));
        Console.WriteLine("OPENNV_OWNED_PACKAGE_DATA_PASS winningHeaders=true checkpointPriority=true absentSpecificFlags=true authoredSchedule=true editorDestination=true cold=true eventOnce=true sourceUnchanged=true nativeExecution=false campaign=false framesRecorded=false");
    }

    private static string Hash(FalloutPluginRecord record) => Convert.ToHexString(SHA256.HashData(record.ReadData()));
}
