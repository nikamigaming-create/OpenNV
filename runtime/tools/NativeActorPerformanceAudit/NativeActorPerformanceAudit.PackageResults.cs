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
        short stage, short? expected, string[] dependencies, bool interruptTravel = false)
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
            var declaration = FalloutScriptPackage.Read(source);
            if (interruptTravel)
            {
                actor._Process(.37);
                if (!actor.Traveling || actor.AnimationError is not null || actor.AiError is not null)
                    throw new InvalidDataException("Interruption fixture requires an actual advancing source walk.");
                var before = actor.GlobalPosition;
                var phase = actor.BaseSourceSeconds;
                actor.BeginConversationFacing(() => before + Vector3.Right, 100);
                actor._Process(.19);
                // A later utterance can renew the same participant lease.
                actor.BeginConversationFacing(() => before + Vector3.Left, 100);
                actor._Process(.23);
                if (actor.GlobalPosition != before || !actor.Traveling)
                    throw new InvalidDataException("Dialogue did not retain the suspended route and stationary root.");
                actor.EndConversationFacing();
                if (actor.GlobalPosition != before || Math.Abs(actor.BaseSourceSeconds - phase) > .00001f)
                    throw new InvalidDataException("Dialogue release reset the source walking phase or replayed accumulated travel.");
                actor._Process(1d / 60);
                if (actor.AnimationError is not null || actor.GlobalPosition.DistanceTo(before) > .15f)
                    throw new InvalidDataException(actor.AnimationError ?? "Resumed walking jumped beyond its source frame displacement.");
            }
            var turnSpeed = Mathf.DegToRad(FalloutGameSettingFloats.Read(records, "fCharacterDefaultTurningSpeed"));
            for (var frame = 0; (expected is { } resultStage ? quests.Stage(quest) != resultStage : actor.Traveling) &&
                 frame < 60 * 120; frame++)
            {
                var before = actor.Basis.Orthonormalized().GetRotationQuaternion();
                actor._Process(1.0 / 60);
                var after = actor.Basis.Orthonormalized().GetRotationQuaternion();
                if (interruptTravel && actor.Traveling && before.AngleTo(after) > turnSpeed / 60 + .001f)
                    throw new InvalidDataException("Walking snapped its body heading beyond the source turn rate.");
            }
            var state = JsonSerializer.SerializeToElement(actor.AiState, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var events = state.GetProperty("packageEvents");
            var expectedResult = expected is { } requested ? quests.Stage(quest) == requested && effects.Count == 1 &&
                effects[0].Stage == requested : quests.Stage(quest) == stage && effects.Count == 0;
            if (!expectedResult || actor.Traveling ||
                actor.AnimationError is not null || actor.AiError is not null || actor.PackageIdleError is not null ||
                events.GetProperty("error").ValueKind != JsonValueKind.Null || !events.GetProperty("done").GetBoolean() ||
                events.GetProperty("lastEvent").GetString() != "POEA" || !hash.SequenceEqual(SHA256.HashData(source.ReadData())))
                throw new InvalidDataException("The source package arrival did not retain its authored completion: " + events);
            if (declaration.LocationRadius > 0)
            {
                var target = cell.References.Single(value => value.FormKey == declaration.LocationReference);
                var endpoint = new[] { actor.Position.X / units, -actor.Position.Z / units, actor.Position.Y / units };
                if (!declaration.ContainsReferenceLocation(cell.Cell.FormKey, endpoint, cell.Cell.FormKey, target.Position) ||
                    state.GetProperty("navigation").GetProperty("locationRadiusGameUnits").GetInt32() != declaration.LocationRadius)
                    throw new InvalidDataException("Package arrival did not remain inside its owned reference radius.");
                // Arrival is consumed once; staying in the source radius cannot
                // replay the package's authored result on later actor frames.
                for (var frame = 0; frame < 12; frame++) actor._Process(1.0 / 60);
                if (effects.Count != (expected is null ? 0 : 1) || actor.AiError is not null || actor.AnimationError is not null)
                    throw new InvalidDataException("Radius arrival repeated its result or lost the actor procedure.");
            }
            GD.Print($"{(expected is null ? "OPENNV_NATIVE_PACKAGE_TRAVEL_PASS" : "OPENNV_NATIVE_PACKAGE_RESULTS_PASS")} " +
                $"actor={caller} package={package} quest={quest} initialStage={stage} resultStage={quests.Stage(quest)} " +
                $"locationRadius={declaration.LocationRadius} endpointInSourceRadius={declaration.LocationRadius > 0} " +
                "sourceNavm=true sourceKf=true ordinaryActorOwner=true " +
                $"completionResult={expected is not null} eventScope=true sourceUnchanged=true recording=false " +
                "boundary=isolated-owned-package-result-fixture stageProgramAndColdLifecycle=unverified parity=unverified");
            if (interruptTravel)
                GD.Print("OPENNV_NATIVE_TRAVEL_INTERRUPTION_PASS sourcePhaseRetained=true repeatedLease=true rootStationary=true sourceTurnRate=true " +
                    "sourceNavm=true sourceKf=true resumedArrival=true sourceUnchanged=true fixture=isolated-owned-actor parity=unverified");
        }
        finally { actor?.Free(); RuntimeLiveContentSource.Clear(); }
    }
}
