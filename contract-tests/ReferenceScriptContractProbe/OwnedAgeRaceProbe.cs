using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedAgeRaceProbe
{
    internal static void Run(string mod, string root, string baseRoot, string questId, string[] dependencies)
    {
        var setup = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(baseRoot);
        RuntimeLiveContentSource.Configure(baseRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            setup.ContentRoots.Skip(1).ToArray(), setup.ActivePlugins, setup.Settings);
        try
        {
            using var records = FalloutPluginStack.Load(RuntimeLiveContentSource.Current!.PluginSources);
            var quest = FalloutDialogueTopic.Find(records, "QUST", questId);
            var hash = SHA256.HashData(quest.ReadData());
            var creation = FalloutNativeRaceSexResolver.Resolve(records);
            var fields = quest.ReadSubrecords().ToArray();
            var samples = new List<object>(); short stage = -1;
            for (var index = 0; index < fields.Length; index++)
            {
                if (fields[index].Signature == "INDX") stage = BinaryPrimitives.ReadInt16LittleEndian(fields[index].Data.Span);
                if (fields[index].Signature != "QSDT") continue;
                var next = index + 1;
                while (next < fields.Length && fields[next].Signature is not ("QSDT" or "INDX" or "QOBJ")) next++;
                var entry = fields[index..next];
                var source = entry.SingleOrDefault(field => field.Signature == "SCTX").Data;
                if (source.IsEmpty) continue;
                foreach (var command in FalloutDialogueTopic.CodeLines(FalloutDialogueTopic.ScriptText(source.Span))
                    .Where(line => line.StartsWith("player.AgeRace ", StringComparison.OrdinalIgnoreCase)))
                {
                    var tokens = command.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    Require(tokens.Length == 2 && int.TryParse(tokens[1], CultureInfo.InvariantCulture, out _), "Owned age command needs another argument contract.");
                    var steps = int.Parse(tokens[1], CultureInfo.InvariantCulture);
                    using var world = new FalloutReferenceWorld(records);
                    var original = FalloutNativeCharacterCreation.ActorState(records, creation.Player, creation.Initial);
                    world.BindPlayerAppearance(() => original);
                    var player = records.RuntimeFormKey(0x14);
                    var expected = original.Race!.Value;
                    for (var remaining = Math.Abs((long)steps); remaining > 0; remaining--)
                    {
                        var race = records.GetEffective(expected);
                        var links = race.ReadSubrecords().Where(field => field.Signature == (steps < 0 ? "YNAM" : "ONAM")).ToArray();
                        if (links.Length == 0) break;
                        Require(links.Length == 1 && links[0].Data.Length == 4, "Owned age-family link is malformed.");
                        if (race.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(links[0].Data.Span)) is not { } linked) break;
                        expected = linked;
                    }
                    var executor = new FalloutReferenceScripts(records, world, new(records), new((_, _) => false,
                        _ => throw new InvalidDataException("AgeRace emitted an unrelated effect.")));
                    executor.ExecuteStage(quest, entry, command);
                    Require(world.ActorRace(player) == expected && original.Race == records.RuntimeFormKey(creation.Initial.RaceRuntimeFormId),
                        "Owned age command missed its winning link or rewrote the original selection.");
                    var effective = original with { Race = world.ActorRace(player), Height = world.ActorHeight(player), PlayerYoung = true };
                    var appearance = FalloutNpcAppearanceResolver.Resolve(records, creation.Player, equippedArmor: [], appearanceState: effective);
                    Require(appearance.Race == expected && appearance.Height == world.ActorHeight(player) && creation.Contains(creation.Initial),
                        "Owned effective appearance disagrees with saved identity/height.");
                    using var cold = new FalloutReferenceWorld(records);
                    cold.BindPlayerAppearance(() => original);
                    cold.RestoreActorOverrides(JsonSerializer.Deserialize<FalloutActorOverrides[]>(JsonSerializer.Serialize(world.CaptureActorOverrides()))!);
                    Require(cold.ActorRace(player) == expected && cold.ActorHeight(player) == world.ActorHeight(player), "Owned cold appearance differs.");
                    samples.Add(new { sourceStage = stage, steps, from = original.Race, to = expected, height = appearance.Height, cold = true });
                }
            }
            Require(samples.Count > 0 && SHA256.HashData(quest.ReadData()).AsSpan().SequenceEqual(hash), "No owned age command audited or source changed.");
            Console.WriteLine(JsonSerializer.Serialize(new { schema = "opennv-owned-age-race-audit/v1", quest = quest.FormKey, samples,
                recording = false, boundary = "isolated-owned-command;ordinary-route-camera-collision-and-retail-parity-unverified" }));
        }
        finally { RuntimeLiveContentSource.Clear(); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
