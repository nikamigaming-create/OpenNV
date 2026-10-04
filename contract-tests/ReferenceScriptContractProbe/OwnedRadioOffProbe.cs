using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedRadioOffProbe
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
                .Where(line => line.Equals("PipBoyRadioOff", StringComparison.OrdinalIgnoreCase)).ToArray();
            Require(commands.Length == 1, "Requested result has no unique zero-argument receiver Off command.");
            var stationBases = records.EffectiveRecords("TACT").Where(source => (source.Flags & 0x20000) != 0)
                .Select(source => source.FormKey).ToHashSet();
            var station = records.EffectiveRecords("REFR")
                .Where(reference => stationBases.Contains(FalloutDialogueTopic.RequiredForm(reference, "NAME")))
                .Select(reference => FalloutRadioStation.Read(records, reference)).First(source => source.PipBoy);
            var receiverStops = 0; var unrelatedStops = 0;
            using var receiver = world.PipBoyRadio.Bind(_ => new Lease(() => ++receiverStops));
            using var unrelated = records.SoundVoices.Register(records.EffectiveRecords("SOUN").First().FormKey,
                null, "isolated-owned-world-voice", () => true, () => ++unrelatedStops);
            world.PipBoyRadio.Select(station);
            var executor = new FalloutReferenceScripts(records, world, new(records),
                new((_, _) => false, _ => throw new InvalidDataException("Receiver Off escaped its shared owner.")));
            executor.ExecuteStage(quest, entry, commands[0]);
            executor.ExecuteStage(quest, entry, commands[0]);
            Require(receiverStops == 1 && unrelatedStops == 0 && records.SoundVoices.ActiveVoices == 1 &&
                world.PipBoyRadio.CurrentStation is null && world.PipBoyRadio.LastStation == station.Reference &&
                world.PipBoyRadio.OffRequests == 2, "Source Off changed another voice, repeated retirement or lost last station.");
            var saved = JsonSerializer.Deserialize<FalloutPipBoyRadioSnapshot>(JsonSerializer.Serialize(world.PipBoyRadio.Capture()))!;
            using var cold = new FalloutReferenceWorld(records);
            cold.PipBoyRadio.Restore(saved);
            Require(JsonSerializer.Serialize(saved) == JsonSerializer.Serialize(cold.PipBoyRadio.Capture()) &&
                cold.PipBoyRadio.CurrentStation is null, "Cold receiver changed source identity or replayed playback.");
            Require(before.AsSpan().SequenceEqual(SHA256.HashData(quest.ReadData())), "Receiver audit changed owned source bytes.");
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                schema = "opennv-owned-radio-off-audit/v1", quest = quest.FormKey, winner = quest.Plugin.Name, stage,
                station = station.Reference, receiverStops, unrelatedStops, coldOff = true, sourceReadOnly = true, recording = false,
                boundary = "isolated-source-command-with-synthetic-playback-lease;broadcast-timeline-and-ordinary-campaign-unverified"
            }));
        }
        finally { RuntimeLiveContentSource.Clear(); }
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private sealed class Lease(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() { var callback = _release; _release = null; callback?.Invoke(); }
    }
}
