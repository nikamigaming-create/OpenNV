using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Actors;

public partial class NativeActorPerformanceAudit
{
    private void ExercisePackageArrival(string baseRoot, string mod, string modRoot, string cellHex,
        string actorHexes, string questId, string stageValues, string[] dependencies)
    {
        var selection = new FalloutModStackSelection([new(mod, modRoot, dependencies)]).Resolve(baseRoot);
        RuntimeLiveContentSource.Configure(baseRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            selection.ContentRoots.Skip(1).ToArray(), selection.ActivePlugins, selection.Settings);
        using var source = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(source.PluginSources);
        var cell = FalloutCellSceneReader.Read(records, records.RuntimeFormKey(Convert.ToUInt32(cellHex, 16)));
        var quest = FalloutDialogueTopic.Find(records, "QUST", questId).FormKey;
        var stages = stageValues.Split(',').Select(value => short.Parse(value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var changes = 0; var initialArrivals = 0; var traces = new List<object>();
        foreach (var hex in actorHexes.Split(','))
        {
            var reference = cell.References.Single(value => value.FormKey == records.RuntimeFormKey(Convert.ToUInt32(hex, 16)));
            var actor = RuntimeNativeNpc.Create(records, source, reference, 0.0142875f, (_, _, _, _) => new StandardMaterial3D());
            try
            {
                AddChild(actor);
                actor.SetProcess(false); actor.SetPhysicsProcess(false);
                actor.Transform = Placement(reference);
                var quests = new FalloutQuestState(records);
                actor.ConfigureAi(records, quests, cell, Placement);
                foreach (var stage in stages)
                {
                    var prior = actor.CurrentPackage;
                    var revision = JsonSerializer.SerializeToElement(actor.AiState, json).GetProperty("packageEvents").GetProperty("revision").GetInt64();
                    quests.EnterStage(quest, stage);
                    actor.EvaluatePackages(false);
                    var entered = JsonSerializer.SerializeToElement(actor.AiState, json);
                    var zeroArrival = !actor.Traveling && entered.GetProperty("packageEvents").GetProperty("done").GetBoolean();
                    for (var frame = 0; actor.Traveling && frame < 60 * 120; frame++) actor._Process(1.0 / 60);
                    if (actor.AiError is not null || actor.AnimationError is not null || actor.PackageIdleError is not null || actor.Traveling)
                        throw new InvalidOperationException("Source arrival did not settle: " + (actor.AiError ?? actor.AnimationError ?? actor.PackageIdleError));
                    var state = JsonSerializer.SerializeToElement(actor.AnimationState, json);
                    var ai = state.GetProperty("ai"); var events = ai.GetProperty("packageEvents"); var route = ai.GetProperty("navigation");
                    if (!events.GetProperty("done").GetBoolean() || events.GetProperty("error").ValueKind != JsonValueKind.Null ||
                        route.GetProperty("arrivalPending").GetBoolean() || state.GetProperty("baseSequence").GetString() == "Forward")
                        throw new InvalidOperationException("Arrived actor retained movement or lost its completion event.");
                    if (prior != actor.CurrentPackage)
                    {
                        changes++;
                        if (zeroArrival)
                        {
                            initialArrivals++;
                            if (events.GetProperty("revision").GetInt64() != revision + 3)
                                throw new InvalidOperationException("Zero-distance replacement lost the change/begin/end event order.");
                        }
                    }
                    if (actor.CurrentPackage is not { } active) throw new InvalidOperationException("Source selected no package.");
                    var package = FalloutScriptPackage.Read(records.GetEffective(active));
                    if (package.Events.GetValueOrDefault("POBA") is { } declared)
                    {
                        if (actor.ActiveIdle != declared) throw new InvalidOperationException("Begin event did not bind its owned IDLE.");
                        var before = BonePoses(actor); var position = actor.Position;
                        actor._Process(.7);
                        if (actor.AnimationError is not null || actor.Position != position || BonePoses(actor).SequenceEqual(before))
                            throw new InvalidOperationException("Stationary event idle failed to publish source bone motion.");
                        var idle = FalloutActorIdleSource.Resolve(records, records.GetEffective(declared));
                        if (!source.TryRead(idle.AnimationPath, null, out var bytes, out var identity)) throw new FileNotFoundException(idle.AnimationPath);
                        var nif = FalloutNifFile.Read(bytes);
                        var sequence = nif.Roots.Select(nif.ReadObject).OfType<FalloutNifControllerSequence>().Single();
                        if (FalloutIdleAnimationData.Read(records.GetEffective(declared)).LoopMinimum == byte.MaxValue)
                        {
                            actor._Process((sequence.StopTime - sequence.StartTime) / sequence.Frequency + .5);
                            if (actor.ActiveIdle != declared || actor.AnimationError is not null || actor.Position != position)
                                throw new InvalidOperationException("Source forever-loop idle was treated as a package completion barrier.");
                        }
                        traces.Add(new
                        {
                            reference = reference.FormKey.ToString(),
                            stage,
                            package = active.ToString(),
                            idle = declared.ToString(),
                            identity,
                            sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                            zeroArrival,
                            eventRevision = events.GetProperty("revision").GetInt64(),
                            stationaryAnimatedBones = true
                        });
                    }
                }
            }
            finally { actor.Free(); }
        }
        if (changes < 2 || initialArrivals < 2) throw new InvalidOperationException("Owned fixture did not exercise changed packages arriving in their initial sample.");
        GD.Print(JsonSerializer.Serialize(new
        {
            source = selection.ActivePlugins,
            changes,
            initialArrivals,
            traces,
            recording = false,
            retainedFrames = 0,
            parity = "unverified"
        }));
        GD.Print("OPENNV_NATIVE_PACKAGE_ARRIVAL_PASS sourcePlacement=true sourceNavm=true sourceKf=true initialSampleCompletion=true changeIdles=true stationaryAnimatedBones=true recording=false");
    }
}
