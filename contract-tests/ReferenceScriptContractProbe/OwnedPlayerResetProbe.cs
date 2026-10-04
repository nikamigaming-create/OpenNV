using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedPlayerResetProbe
{
    internal static void Run(string mod, string root, string baseRoot, string questId, short stage, string[] dependencies)
    {
        var setup = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(baseRoot);
        RuntimeLiveContentSource.Configure(baseRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            setup.ContentRoots.Skip(1).ToArray(), setup.ActivePlugins, setup.Settings);
        try
        {
            using var records = FalloutPluginStack.Load(RuntimeLiveContentSource.Current!.PluginSources);
            using var world = new FalloutReferenceWorld(records);
            var quest = FalloutDialogueTopic.Find(records, "QUST", questId);
            var before = SHA256.HashData(quest.ReadData());
            var fields = quest.ReadSubrecords().ToArray();
            var begin = Array.FindIndex(fields, field => field.Signature == "INDX" && field.Data.Length == 2 &&
                BinaryPrimitives.ReadInt16LittleEndian(field.Data.Span) == stage);
            if (begin < 0) throw new InvalidDataException("Requested player-reset source stage is absent.");
            var end = begin + 1;
            while (end < fields.Length && fields[end].Signature is not ("INDX" or "QOBJ")) ++end;
            var entry = fields[(begin + 1)..end];
            var command = entry.Where(field => field.Signature == "SCTX")
                .SelectMany(field => FalloutDialogueTopic.CodeLines(FalloutDialogueTopic.ScriptText(field.Data.Span)))
                .Single(line => line.Equals("player.ResetHealth", StringComparison.OrdinalIgnoreCase));
            var values = new FalloutPlayerActorValues(records);
            values.BindConstantModifiers((_, _) => []); // Isolated reset; ability evaluation is a separate lane.
            var vitals = FalloutPlayerVitals.FromActorValues(records, values);
            vitals.Damage(23.75f, 6, 1);
            vitals.Publish(vitals.State with { ActionPoints = 3, ExperiencePoints = 17, RadiationRads = 75 });
            var executor = new FalloutReferenceScripts(records, world, new(records),
                new((_, _) => false, _ => throw new InvalidDataException("Player reset escaped its C# owner."),
                    ResetPlayerHealth: vitals.ResetHealth));
            executor.ExecuteStage(quest, entry, command);
            var state = vitals.State;
            if (state.ExactHitPoints != state.MaximumHitPoints || state.LimbDamage is not null ||
                state.ActionPoints != 3 || state.ExperiencePoints != 17 || state.RadiationRads != 75 || world.InstanceCount != 0)
                throw new InvalidDataException("Source reset failed its player/limb state or created an engine-player record.");
            var saved = JsonSerializer.Deserialize<GameplayVitals>(JsonSerializer.Serialize(state))!;
            var cold = FalloutPlayerVitals.FromActorValues(records, values, saved);
            if (JsonSerializer.Serialize(cold.State) != JsonSerializer.Serialize(state) ||
                !before.AsSpan().SequenceEqual(SHA256.HashData(quest.ReadData())))
                throw new InvalidDataException("Cold reset changed vitals or source bytes.");
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                schema = "opennv-owned-player-reset-audit/v1", quest = quest.FormKey, winner = quest.Plugin.Name, stage,
                player = values.Source.Player, hp = state.ExactHitPoints, limbsCured = true, unrelatedVitalsRetained = true,
                enginePlayerRecordCreated = false, cold = true, sourceReadOnly = true, recording = false,
                boundary = "isolated-original-command-and-shared-vitals;complete-campaign-ability-and-retail-parity-unverified"
            }));
        }
        finally { RuntimeLiveContentSource.Clear(); }
    }
}
