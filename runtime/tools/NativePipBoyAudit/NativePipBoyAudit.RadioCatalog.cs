using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;

public partial class NativePipBoyAudit
{
    private static void InspectRadioCatalog(string data)
    {
        RuntimeLiveContentSource.Configure(data, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var records = FalloutPluginStack.Load(RuntimeLiveContentSource.Current!.PluginSources);
        var stations = records.ReadRadioStationIndex();
        var topics = records.EffectiveRecords("DIAL").Where(record => FalloutDialogueTopic.Type(records, record.FormKey) == 7)
            .Select(record => record.FormKey).ToHashSet();
        var orderingErrors = new List<string>();
        foreach (var topic in topics)
        {
            try { _ = FalloutDialogueTopic.Read(records, topic); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException)
            { orderingErrors.Add($"{topic}: {error.Message}"); }
        }
        // Inventory the winning response fields independently of list order;
        // report ordering refusals rather than calling them playback support.
        var infos = records.EffectiveRecords("INFO").Where(record => topics.Contains(record.Plugin.AdjustFormId(
            record.Groups.Single(group => group.Type == 7).LabelAsUInt32))).Select(FalloutDialogueTopic.Decode).ToArray();
        var responses = infos.SelectMany(info => info.Responses).ToArray();
        var sounds = responses.Select(response => response.Sound).OfType<FalloutFormKey>().Distinct()
            .Select(key => FalloutSoundRecordReader.Read(records.GetEffective(key))).ToArray();
        GD.Print("OPENNV_RADIO_SOURCE_CATALOG " + JsonSerializer.Serialize(new
        {
            stations = stations.Stations.Values,
            errors = stations.Errors.Select(pair => new { reference = pair.Key, pair.Value }),
            infos = infos.Length,
            responses = responses.Length,
            orderingErrors,
            animated = responses.Count(response => response.SpeakerAnimation is not null || response.ListenerAnimation is not null),
            sounds = sounds.Select(sound => new
            {
                sound.FormKey,
                sound.EditorId,
                sound.LogicalPath,
                flags = (int)sound.Flags,
                sound.RandomChancePercent,
                sound.FrequencyAdjustment,
                sound.StartTime,
                sound.StopTime,
                sound.StaticAttenuationHundredthsDb,
                sound.LoopStartSample,
                sound.LoopEndSample
            })
        }));
    }
}
