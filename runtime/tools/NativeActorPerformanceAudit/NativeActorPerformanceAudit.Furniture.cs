using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;

public partial class NativeActorPerformanceAudit
{
    private void ExerciseFurniture(string dataRoot, string cellHex, string actorHex, string questId, string stage)
    {
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var source = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(source.PluginSources);
        var cell = FalloutCellSceneReader.Read(records, records.RuntimeFormKey(Convert.ToUInt32(cellHex, 16)));
        var reference = cell.References.Single(value => value.FormKey == records.RuntimeFormKey(Convert.ToUInt32(actorHex, 16)));
        var actor = RuntimeNativeNpc.Create(records, source, reference, 0.0142875f, (_, _, _, _) => new StandardMaterial3D());
        AddChild(actor);
        try
        {
            var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var quests = new FalloutQuestState(records);
            actor.ConfigureAi(records, quests, cell, Placement);
            if (actor.AiError is not null || actor.SittingState != 1)
                throw new InvalidOperationException($"Furniture audit requires a source initial seat: {actor.AiError}");
            var initial = actor.Transform;
            var initialPackage = actor.CurrentPackage;
            quests.EnterStage(FalloutDialogueTopic.Find(records, "QUST", questId).FormKey,
                short.Parse(stage, System.Globalization.CultureInfo.InvariantCulture));
            actor.EvaluatePackages(true);
            if (actor.Position != initial.Origin || actor.SittingState != 4 || actor.AiError is not null)
                throw new InvalidOperationException($"Furniture package replacement skipped the prior exit: sitting={actor.SittingState}, error={actor.AiError}.");
            var phases = new HashSet<string>();
            var previous = actor.Position;
            var maximumStep = 0.0f;
            var entryFrames = 0;
            var approachFrames = 0;
            var arrivals = 0;
            string? activePackage = null;
            var destination = Vector3.Zero;
            for (var frame = 0; frame < 60 * 120; frame++)
            {
                actor._Process(1.0 / 60);
                if (actor.AiError is not null || actor.AnimationError is not null)
                    throw new InvalidOperationException(actor.AiError ?? actor.AnimationError);
                var state = JsonSerializer.SerializeToElement(actor.AiState, json);
                var phase = state.GetProperty("furniturePhase").GetString()!;
                phases.Add(phase);
                maximumStep = Math.Max(maximumStep, actor.Position.DistanceTo(previous));
                if (maximumStep > 0.25f) throw new InvalidOperationException("Furniture movement jumped more than 25 cm in one frame.");
                previous = actor.Position;
                if (phase is "approaching" or "entering")
                {
                    if (state.GetProperty("packageEvents").GetProperty("done").GetBoolean())
                        throw new InvalidOperationException("Furniture package completed before its entry animation.");
                    if (state.GetProperty("furnitureInitialPlacement").GetBoolean())
                        throw new InvalidOperationException("A later furniture package used initial placement.");
                    var navigation = state.GetProperty("navigation");
                    activePackage = state.GetProperty("package").GetString();
                    if (navigation.GetProperty("package").GetString() != activePackage ||
                        navigation.GetProperty("purpose").GetString() != "furniture-approach")
                        throw new InvalidOperationException("Furniture navigation reports a stale package path.");
                    var target = navigation.GetProperty("target").EnumerateArray().Select(value => value.GetSingle()).ToArray();
                    destination = new(target[0], target[1], target[2]);
                    if (phase == "approaching") approachFrames++;
                    else entryFrames++;
                }
                if (phase == "occupied" && actor.CurrentPackage != initialPackage)
                {
                    if (!state.GetProperty("packageEvents").GetProperty("done").GetBoolean())
                        throw new InvalidOperationException("Occupied furniture did not finish its package.");
                    arrivals++;
                    break;
                }
            }
            if (arrivals != 1 || approachFrames < 2 || entryFrames < 2 ||
                !phases.IsSupersetOf(["exiting", "approaching", "entering", "occupied"]) ||
                actor.Position.DistanceTo(destination) < 0.1f)
                throw new InvalidOperationException("Source furniture approach, entry displacement or occupation was not exercised.");
            var revision = JsonSerializer.SerializeToElement(actor.AiState, json).GetProperty("packageEvents").GetProperty("revision").GetInt64();
            for (var frame = 0; frame < 60; frame++) actor._Process(1.0 / 60);
            if (JsonSerializer.SerializeToElement(actor.AiState, json).GetProperty("packageEvents").GetProperty("revision").GetInt64() != revision)
                throw new InvalidOperationException("A retained occupied package repeated its completion event.");
            GD.Print($"OPENNV_FURNITURE_ENTRY_AUDIT_PASS package={activePackage} approachFrames={approachFrames} " +
                $"entryFrames={entryFrames} maximumStep={maximumStep:R} singleCompletion=true source=navm-furn-nif-idle-kf pixels=unverified");
        }
        finally { actor.Free(); }
    }
}
