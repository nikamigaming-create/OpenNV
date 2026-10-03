using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedRadioProbe
{
    internal static void Run(string mod, string root, string baseRoot, string questId, string[] dependencies)
    {
        var setup = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(baseRoot);
        RuntimeLiveContentSource.Configure(baseRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            setup.ContentRoots.Skip(1).ToArray(), setup.ActivePlugins, setup.Settings);
        try
        {
            using var records = FalloutPluginStack.Load(RuntimeLiveContentSource.Current!.PluginSources);
            using var world = new FalloutReferenceWorld(records);
            var quest = FalloutDialogueTopic.Find(records, "QUST", questId);
            var questHash = SHA256.HashData(quest.ReadData());
            var fields = quest.ReadSubrecords().ToArray();
            var index = Array.FindIndex(fields, value => value.Signature == "INDX" && BinaryPrimitives.ReadInt16LittleEndian(value.Data.Span) == 12);
            Require(index >= 0, "Selected quest has no reached stage12 result.");
            var end = index + 1;
            while (end < fields.Length && fields[end].Signature is not ("INDX" or "QOBJ")) end++;
            var entry = fields[(index + 1)..end];
            Require(entry.Count(value => value.Signature == "SCTX") == 1, "Reached result source is absent or ambiguous.");
            var commands = FalloutDialogueTopic.CodeLines(FalloutDialogueTopic.ScriptText(entry.Single(value => value.Signature == "SCTX").Data.Span))
                .Where(line => line.Equals("ForceRadioStationUpdate", StringComparison.OrdinalIgnoreCase)).ToArray();
            Require(commands.Length == 1, "Reached result has no single global radio refresh command.");
            var transmitter = FalloutDialogueTopic.Find(records, "REFR", "RadioVault101REF");
            var radioSource = FalloutRadioStation.Read(records, transmitter);
            var sourceHash = SHA256.HashData(transmitter.ReadData());
            var position = world.EditorPlacement(transmitter.FormKey);
            var queue = new FalloutHudNotifications(); var sounds = 0;
            var radio = new FalloutRadioStations(records, world, queue) { SignalDiscovered = () => ++sounds };
            radio.Refresh(position, notify: false);
            Require(radio.SourceErrors.Count == 0 && radio.Reception.Count > 0, "Installed transmitter corpus has an unbound layout.");
            Require(!radio.Reception.Single(value => value.Station.Reference == transmitter.FormKey).Available, "Authored disabled station was available.");
            world.SetEnabled(transmitter.FormKey, true);
            var executor = new FalloutReferenceScripts(records, world, new(records), new((_, _) => false, _ => { },
                Command: (_, _, command, arguments) =>
                {
                    Require(command.Equals("ForceRadioStationUpdate", StringComparison.OrdinalIgnoreCase) && arguments.Count == 0,
                        "Radio result changed its global operation or arguments.");
                    radio.Refresh(position, force: true);
                }));
            executor.ExecuteStage(quest, entry, commands[0]);
            Require(!radio.Reception.Single(value => value.Station.Reference == transmitter.FormKey).Available && radio.ForcedUpdates == 1,
                "Forced update applied an enqueued enable early or had no shared owner effect.");
            world.AdvanceEnableChanges(0, new(1, 1), _ => false);
            executor.ExecuteStage(quest, entry, commands[0]);
            var reception = radio.Reception.Single(value => value.Station.Reference == transmitter.FormKey);
            Require(reception.Available && reception.Error is null && radio.Available.Any(value => value.Reference == transmitter.FormKey),
                "Applied source enable did not expose the linked-interior station.");
            Require(sounds == 1 && queue.Capture().Pending.Single().Event == new FalloutHudEvent(FalloutHudEventKind.RadioDiscovered, transmitter.FormKey, 0),
                "Radio discovery lost source identity, order or once-only effects.");
            var retained = JsonSerializer.Deserialize<FalloutRadioStationsSnapshot>(JsonSerializer.Serialize(radio.Capture()))!;
            using var coldWorld = new FalloutReferenceWorld(records);
            coldWorld.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!);
            var coldQueue = new FalloutHudNotifications(); coldQueue.Restore(queue.Capture());
            var cold = new FalloutRadioStations(records, coldWorld, coldQueue) { SignalDiscovered = () => throw new InvalidDataException("Cold radio discovery replayed.") };
            cold.Restore(retained); cold.Refresh(position, force: true);
            Require(cold.Available.Any(value => value.Reference == transmitter.FormKey) && coldQueue.Capture().LastOrdinal == 1,
                "Cold radio refresh lost enable/discovery or replayed a notice.");
            var declaration = FalloutExecutableStringTable.ReadRadioHudDeclaration(Path.Combine(baseRoot, "FalloutNV.exe"));
            var sound = FalloutDialogueTopic.Find(records, "SOUN", declaration.SoundEditorId);
            Require(FalloutSoundRecordReader.Read(sound).LogicalPath.Length != 0 && RuntimeLiveContentSource.Current!.TryRead(
                "textures/" + declaration.Icon.Replace('\\', '/'), null, out _, out _) &&
                FalloutGameSettingStrings.Read(records, "sRadioStationDiscovered").Contains("%s", StringComparison.Ordinal),
                "Owned radio discovery has no source sound, icon, text or timing declaration.");
            Require(SHA256.HashData(quest.ReadData()).AsSpan().SequenceEqual(questHash) &&
                SHA256.HashData(transmitter.ReadData()).AsSpan().SequenceEqual(sourceHash), "Radio audit changed owned source bytes.");
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                schema = "opennv-owned-radio-refresh-audit/v1", quest = quest.FormKey, winner = quest.Plugin.Name,
                transmitter = transmitter.FormKey, stationBase = radioSource.Base, sourceRange = radioSource.Range.ToString(),
                transmitters = radio.Reception.Count, ranges = radio.Reception.GroupBy(value => value.Station.Range).Select(group => new { range = group.Key.ToString(), count = group.Count() }),
                retainedDistanceDivergences = radio.Reception.Count(value => value.Error is not null), appliedEnable = true,
                forcedSourceCommand = true, onceOnlyDiscovery = true, coldRefresh = true, ownedHudDeclarations = true,
                sourceReadOnly = true, recording = false,
                boundary = "isolated-owned-result-command-and-cold-state;ordinary-campaign-radio-tuning-playback-cadence-and-retail-parity-unverified"
            }));
        }
        finally { RuntimeLiveContentSource.Clear(); }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
}
