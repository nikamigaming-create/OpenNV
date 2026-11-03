using System.Text.Json;
using OpenNV.Runtime.Content;

internal static partial class ScriptSoundContracts
{
    private static void InventoryBatch(FalloutPluginStack records)
    {
        var menus = new FalloutScriptMenus(); menus.Publish(true);
        var sounds = new FalloutScriptSounds(records, menus, 19);
        var prepared = new List<(FalloutScriptSoundRequest Request, Voice Voice)>();
        using var binding = sounds.Bind(_ => ["sound/fx/variants/a.wav", "sound/fx/variants/b.wav"], request =>
        {
            var voice = new Voice(); prepared.Add((request, voice)); return voice.Prepare(request);
        }, true);
        var before = JsonSerializer.Serialize(sounds.State);
        using (sounds.PrepareInventorySounds([new(Key(0x601), Key(0x105))]))
            Require(before == JsonSerializer.Serialize(sounds.State) && prepared.Single().Voice.Starts == 0,
                "Inventory sound preparation published a request/RNG change or started playback.");
        Require(prepared.Single().Voice.Releases == 1 && sounds.ActiveVoices == 0,
            "Discarded inventory sound preparation leaked its owned stream.");
        using (var rollback = sounds.PrepareInventorySounds([new(Key(0x601), Key(0x105))]))
        {
            Require(JsonSerializer.Serialize(prepared[0].Request) == JsonSerializer.Serialize(prepared[1].Request),
                "Discarded sound preparation consumed the shared source selection or request ID.");
            rollback.Commit();
            Require(sounds.ActiveVoices == 1 && prepared[1].Voice.Starts == 0,
                "Inventory sound commit started a voice before successful inventory completion.");
        }
        Require(before == JsonSerializer.Serialize(sounds.State) && prepared[1].Voice.Releases == 1,
            "Inventory sound rollback retained a voice, request, RNG draw or last-request receipt.");
        using (var committed = sounds.PrepareInventorySounds([new(Key(0x601), Key(0x105))]))
        {
            committed.Commit(); committed.Complete();
            Require(prepared[2].Voice.Starts == 1 && sounds.ActiveVoices == 1,
                "Successful inventory sound publication did not start its one prepared stream.");
        }
        sounds.Clear();
        Require(prepared.All(item => item.Voice.Releases == 1), "Inventory sound retirement released an owned stream incorrectly.");
    }
}
