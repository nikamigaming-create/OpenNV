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
    private void PackageResults(string baseRoot, string mod, string root, string actorId, string questId,
        short stage, short expected, string[] dependencies)
    {
        RuntimeNativeNpc? actor = null;
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
            var cell = FalloutCellSceneReader.Read(records, world.Get(caller).Cell); world.LoadCell(cell);
            var placed = cell.References.Single(value => value.FormKey == caller);
            var units = RuntimeConfiguration.Load().World.GameUnitsToMeters;
            var templates = world.InitializeActorTemplates(caller, 1);
            actor = RuntimeNativeNpc.Create(records, content, placed, units, (_, _, _, _) => new StandardMaterial3D(),
                world.EquippedArmor(caller, 1), templates);
            Transform3D Placement(FalloutPlacedReference reference) => new(GamebryoCoordinate.ConvertReferenceEuler(
                new(reference.RotationRadians[0], reference.RotationRadians[1], reference.RotationRadians[2]), reference.Scale),
                GamebryoCoordinate.ConvertVector(new(reference.Position[0], reference.Position[1], reference.Position[2])) * units);
            actor.Transform = Placement(placed); AddChild(actor); actor.SetProcess(false); actor.SetPhysicsProcess(false);
            var effects = new List<FalloutReferenceScriptEffect>();
            var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, effect =>
            {
                if (effect.Kind != FalloutReferenceEffectKind.SetStage || effect.Source != caller || effect.Target != quest)
                    throw new InvalidDataException("The isolated package fixture reached an unrelated effect.");
                effects.Add(effect); quests.EnterStage(quest, effect.Stage);
            }));
            actor.ExecutePackageEvent = scripts.ExecutePackageEvent;
            actor.ConfigureAi(records, quests, cell, Placement, world: world);
            var package = actor.CurrentPackage ?? throw new InvalidDataException("The owned fixture selected no package.");
            var source = records.GetEffective(package); var hash = SHA256.HashData(source.ReadData());
            for (var frame = 0; quests.Stage(quest) != expected && frame < 60 * 120; frame++) actor._Process(1.0 / 60);
            var state = JsonSerializer.SerializeToElement(actor.AiState, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var events = state.GetProperty("packageEvents");
            if (quests.Stage(quest) != expected || effects.Count != 1 || effects[0].Stage != expected || actor.Traveling ||
                actor.AnimationError is not null || actor.AiError is not null || actor.PackageIdleError is not null ||
                events.GetProperty("error").ValueKind != JsonValueKind.Null || !events.GetProperty("done").GetBoolean() ||
                events.GetProperty("lastEvent").GetString() != "POEA" || !hash.SequenceEqual(SHA256.HashData(source.ReadData())))
                throw new InvalidDataException("The source package arrival did not execute exactly one authored completion: " + events);
            GD.Print($"OPENNV_NATIVE_PACKAGE_RESULTS_PASS actor={caller} package={package} quest={quest} initialStage={stage} resultStage={expected} " +
                "sourceNavm=true sourceKf=true ordinaryActorOwner=true completionResult=true eventScope=true sourceUnchanged=true recording=false " +
                "boundary=isolated-owned-package-result-fixture stageProgramAndColdLifecycle=unverified parity=unverified");
        }
        finally { actor?.Free(); RuntimeLiveContentSource.Clear(); }
    }
}
