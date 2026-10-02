using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeActorPerformanceAudit
{
    private void ReferencePackageEvents(string baseRoot, string mod, string root, string actorId,
        string questId, short stage, short expected, string[] dependencies)
    {
        Node3D? presentation = null;
        var previousPause = GetTree().Paused;
        try
        {
            var installation = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(baseRoot);
            RuntimeLiveContentSource.Configure(baseRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
                installation.ContentRoots.Skip(1).ToArray(), installation.ActivePlugins, installation.Settings);
            var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            using var world = new FalloutReferenceWorld(records);
            var caller = FalloutDialogueTopic.Find(records, "ACHR", actorId).FormKey;
            var quest = FalloutDialogueTopic.Find(records, "QUST", questId).FormKey;
            var quests = new FalloutQuestState(records); quests.EnterStage(quest, stage);
            // Diagnostic preparation supplies the reached enabled state. This
            // fixture does not execute the preceding campaign stage programs.
            world.Get(caller).Enabled = true;
            var cell = FalloutCellSceneReader.Read(records, world.Get(caller).Cell); world.LoadCell(cell);
            var placed = cell.References.Single(value => value.FormKey == caller);
            var units = RuntimeConfiguration.Load().World.GameUnitsToMeters;
            var templates = world.InitializeActorTemplates(caller, 1);
            var attached = world.Get(caller).Script!.Record;
            var scriptHash = SHA256.HashData(attached.ReadData());
            presentation = new Node3D(); AddChild(presentation);
            var actor = RuntimeNativeNpc.Create(records, content, placed, units,
                (_, _, _, _) => new StandardMaterial3D(), world.EquippedArmor(caller, 1), templates);
            Transform3D Placement(FalloutPlacedReference reference) => new(GamebryoCoordinate.ConvertReferenceEuler(
                new(reference.RotationRadians[0], reference.RotationRadians[1], reference.RotationRadians[2]), reference.Scale),
                GamebryoCoordinate.ConvertVector(new(reference.Position[0], reference.Position[1], reference.Position[2])) * units);
            actor.Transform = Placement(placed); presentation.AddChild(actor);
            actor.SetProcess(false); actor.SetPhysicsProcess(false);
            var embedded = new List<FalloutReferenceScriptEffect>();
            var embeddedScripts = new FalloutReferenceScripts(records, world, quests,
                new((_, _) => false, effect => embedded.Add(effect)));
            actor.ExecutePackageEvent = embeddedScripts.ExecutePackageEvent;
            actor.ConfigureAi(records, quests, cell, Placement, world: world);
            var package = actor.CurrentPackage ?? throw new InvalidDataException("The event fixture selected no source package.");
            var packageHash = SHA256.HashData(records.GetEffective(package).ReadData());
            if (world.PendingPackageEventCount == 0 || quests.Stage(quest) != stage)
                throw new InvalidDataException("Initial source package mark was lost before reference-event binding.");
            RefusePendingSave(world);

            var effects = new List<FalloutReferenceScriptEffect>();
            var events = new RuntimeNativeReferenceEvents();
            // The NPC still traverses the complete owned NAVM. This isolated
            // event adapter admits only the actual selected actor's script.
            events.Configure(records, world, quests, cell with { References = [placed] }, presentation,
                new((_, _) => false, effect =>
                {
                    if (effect.Kind != FalloutReferenceEffectKind.SetStage || effect.Source != caller ||
                        effect.Target != quest || effect.Stage != expected)
                        throw new InvalidDataException("Attached source package event reached an unrelated effect.");
                    effects.Add(effect); quests.EnterStage(quest, effect.Stage);
                }), Placement, units, RuntimeConfiguration.Load().Player.CollisionLayer);
            presentation.AddChild(events);
            var failures = new List<string>(); events.ReportDivergence = failures.Add;
            GetTree().Paused = true;
            var bootstrapCount = world.PendingPackageEventCount;
            events._Process(1.0 / 60);
            if (world.PendingPackageEventCount != bootstrapCount || effects.Count != 0)
                throw new InvalidDataException("Paused actor script consumed a source package mark.");
            GetTree().Paused = previousPause;
            events._Process(1.0 / 60);
            if (world.PendingPackageEventCount != 0 || effects.Count != 0)
                throw new InvalidDataException("Initial source actor frame did not consume only its admitted start mark.");
            events.SetProcess(false);
            for (var frame = 0; actor.Traveling && frame < 60 * 120; frame++) actor._Process(1.0 / 60);
            var state = JsonSerializer.SerializeToElement(actor.AiState, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (actor.Traveling || actor.AiError is not null || actor.AnimationError is not null ||
                !state.GetProperty("packageEvents").GetProperty("done").GetBoolean() ||
                world.PendingPackageEventCount == 0 || effects.Count != 0 || quests.Stage(quest) != stage)
                throw new InvalidDataException("Owned NAVM/KF completion did not retain its deferred actual-actor event.");
            RefusePendingSave(world);
            events._Process(1.0 / 60);
            if (effects.Count != 0 || world.PendingPackageEventCount == 0)
                throw new InvalidDataException("Suspended reference owner consumed a package event.");
            events.SetProcess(true);
            events._Process(1.0 / 60);
            if (effects.Count != 1 || quests.Stage(quest) != expected || world.PendingPackageEventCount != 0 ||
                world.Get(caller).ScriptError is not null || failures.Count != 0 || embedded.Count != 0)
                throw new InvalidDataException("Attached source OnPackageDone did not execute its typed PACK filter once.");
            for (var frame = 0; frame < 12; frame++) events._Process(1.0 / 60);
            if (effects.Count != 1 || !scriptHash.SequenceEqual(SHA256.HashData(attached.ReadData())) ||
                !packageHash.SequenceEqual(SHA256.HashData(records.GetEffective(package).ReadData())))
                throw new InvalidDataException("Actor package event replayed or owned source changed.");
            _ = world.Capture();
            VerifyPackageResultFailure(records, content, cell, placed, units, quest, stage, Placement);
            GD.Print($"OPENNV_NATIVE_REFERENCE_PACKAGE_EVENTS_PASS actor={caller} package={package} " +
                $"quest={quest} initialStage={stage} resultStage={quests.Stage(quest)} " +
                "sourceNavm=true sourceKf=true sourceAttachedScript=true typedPackage=true " +
                "bootstrapRetained=true pausedRetained=true suspendedRetained=true once=true pendingSaveRefused=true " +
                "injectedResultFaultRetained=true " +
                "embeddedResultEffects=0 sourceUnchanged=true recording=false " +
                "boundary=isolated-owned-actor-event-fixture stageProgramsAndColdActor=unverified parity=unverified");
        }
        finally
        {
            GetTree().Paused = previousPause;
            presentation?.Free();
            RuntimeLiveContentSource.Clear();
        }
    }

    private void VerifyPackageResultFailure(FalloutPluginStack records, RuntimeLiveContentSource content,
        FalloutCellScene cell, FalloutPlacedReference placed, float units, FalloutFormKey quest, short stage,
        Func<FalloutPlacedReference, Transform3D> placement)
    {
        using var world = new FalloutReferenceWorld(records);
        world.LoadCell(cell); world.Get(placed.FormKey).Enabled = true;
        var quests = new FalloutQuestState(records); quests.EnterStage(quest, stage);
        var root = new Node3D(); AddChild(root);
        try
        {
            var actor = RuntimeNativeNpc.Create(records, content, placed, units,
                (_, _, _, _) => new StandardMaterial3D(), world.EquippedArmor(placed.FormKey, 1),
                world.InitializeActorTemplates(placed.FormKey, 1));
            actor.Transform = placement(placed); root.AddChild(actor);
            actor.SetProcess(false); actor.SetPhysicsProcess(false);
            var attempts = 0;
            // Inject an adapter failure while executing the unchanged source
            // PACK. This is a failure-path probe, not an owned gameplay error.
            actor.ExecutePackageEvent = (_, _) =>
            {
                ++attempts;
                throw new NotSupportedException("Injected package-result adapter failure.");
            };
            actor.ConfigureAi(records, quests, cell, placement, world: world);
            var pending = world.PendingPackageEventCount;
            if (attempts != 1 || pending == 0 || actor.PackageEventError is null ||
                world.Get(placed.FormKey).ScriptError is not { } error ||
                !error.Contains("Injected package-result adapter failure", StringComparison.Ordinal))
                throw new InvalidDataException("Own package failure did not retain its source prefix and actor fault.");
            var effects = 0;
            var events = new RuntimeNativeReferenceEvents { ReportDivergence = _ => { } };
            events.Configure(records, world, quests, cell with { References = [placed] }, root,
                new((_, _) => false, _ => ++effects), placement, units, RuntimeConfiguration.Load().Player.CollisionLayer);
            root.AddChild(events);
            for (var frame = 0; frame < 12; frame++)
            {
                actor._Process(1.0 / 60);
                events._Process(1.0 / 60);
            }
            if (attempts != 1 || effects != 0 || quests.Stage(quest) != stage ||
                world.PendingPackageEventCount != pending || world.Get(placed.FormKey).ScriptError != error)
                throw new InvalidDataException("Own package failure replayed or released the attached actor script suffix.");
            RefusePendingSave(world);
        }
        finally { root.Free(); }
    }

    private static void RefusePendingSave(FalloutReferenceWorld world)
    {
        try { _ = world.Capture(); }
        catch (NotSupportedException error) when (error.Message.Contains("pending actor package events", StringComparison.Ordinal))
        { return; }
        throw new InvalidDataException("Campaign capture discarded pending actor package events.");
    }
}
