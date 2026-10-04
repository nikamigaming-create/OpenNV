using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedRadioBroadcastProbe
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
            Require(begin >= 0, "Selected source has no requested stage.");
            var end = begin + 1;
            while (end < fields.Length && fields[end].Signature is not ("INDX" or "QOBJ")) ++end;
            var entry = fields[(begin + 1)..end];
            var commands = entry.Where(field => field.Signature == "SCTX")
                .SelectMany(field => FalloutDialogueTopic.CodeLines(FalloutDialogueTopic.ScriptText(field.Data.Span)))
                .Where(line => line.Contains(".SetBroadcastState", StringComparison.OrdinalIgnoreCase)).ToArray();
            Require(commands.Length == 1, "Requested result has no unique station broadcast command.");
            var executor = new FalloutReferenceScripts(records, world, new(records),
                new((_, _) => false, _ => throw new InvalidDataException("Broadcast state escaped its shared owner.")));
            executor.ExecuteStage(quest, entry, commands[0]);
            var saved = world.Capture();
            var reference = saved.Single(value => value.BroadcastState is not null);
            var station = FalloutRadioStation.Read(records, records.GetEffective(reference.Reference));
            var sourceReference = records.GetEffective(reference.Reference);
            var sourceBase = records.GetEffective(station.Base);
            var referenceHash = SHA256.HashData(sourceReference.ReadData());
            var baseHash = SHA256.HashData(sourceBase.ReadData());
            Require(station.Continuous && reference.BroadcastState == false && !world.GetBroadcastState(reference.Reference) &&
                records.SoundVoices.ActiveVoices == 0 && world.PipBoyRadio.CurrentStation is null,
                "Source scripted-radio transition changed receiver state or fabricated broadcast audio.");
            executor.ExecuteStage(quest, entry, commands[0]);
            using var cold = new FalloutReferenceWorld(records);
            cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(saved))!);
            Require(!cold.GetBroadcastState(reference.Reference) && JsonSerializer.Serialize(saved) == JsonSerializer.Serialize(cold.Capture()),
                "Cold radio state lost its source station or broadcast override.");
            Require(before.AsSpan().SequenceEqual(SHA256.HashData(quest.ReadData())) &&
                referenceHash.AsSpan().SequenceEqual(SHA256.HashData(sourceReference.ReadData())) &&
                baseHash.AsSpan().SequenceEqual(SHA256.HashData(sourceBase.ReadData())), "Broadcast audit changed owned source bytes.");
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                schema = "opennv-owned-radio-broadcast-audit/v1", quest = quest.FormKey, winner = quest.Plugin.Name, stage,
                station = station.Reference, sourceContinuous = station.Continuous, continuousBroadcast = world.GetBroadcastState(reference.Reference),
                cold = true, sourceReadOnly = true, recording = false,
                boundary = "isolated-original-station-command;broadcast-timeline-and-ordinary-campaign-unverified"
            }));
        }
        finally { RuntimeLiveContentSource.Clear(); }
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
}
