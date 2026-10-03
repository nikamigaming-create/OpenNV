using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class ReferenceIdentityQueryContracts
{
    internal static void Verify(FalloutPluginStack records, FalloutReferenceWorld world)
    {
        static FalloutFormKey Key(uint id) => new("Actors.esm", id);
        var script = records.GetEffective(Key(0x891));
        void Query(FalloutReferenceWorld state, string condition, bool expected, uint caller = 0x904)
        {
            var effects = 0;
            var scripts = new FalloutReferenceScripts(records, state, new(records), new((_, _) => false, effect =>
            {
                if (effect.Kind != FalloutReferenceEffectKind.DefaultActivate || effect.Target != Key(caller))
                    throw new InvalidDataException("Reference query changed its calling object.");
                effects++;
            }));
            scripts.ExecuteProgram(records.GetEffective(Key(caller)), script,
                FalloutGameModeProgram.Read($"begin GameMode\nif {condition}\nActivate\nendif\nend"), 0);
            if (effects != (expected ? 1 : 0)) throw new InvalidDataException($"Reference query confused a caller, base or compiled argument: {condition}.");
        }
        Query(world, "GetIsReference SourceMapMarker", false);
        Query(world, "SourceMapMarker.GetIsReference SourceMapMarker", true);
        Query(world, "SourceMapMarker.GetIsReference OtherMapMarker", false);
        Query(world, "SourceMapMarker.GetIsID 0x844 == 1 && OtherMapMarker.GetIsID 0x844 == 1", true);
        Query(world, "player.GetIsReference player", true);
        Query(world, "GetIsReference player", false);
        Query(world, "( GetSelf ).GetIsReference player", false);
        Query(world, "GetIsReference player == 0 || ( SourceMapMarker.GetIsReference OtherMapMarker == 1 && GetIsReference player )", true);
        foreach (var (condition, caller) in new[] { ("GetIsReference CreatureLoot", 0x904u),
            ("GetIsReference UncompiledMarker", 0x904u), ("GetIsReference player", 0x871u) })
        {
            try { Query(world, condition, false, caller); throw new InvalidDataException("Reference query accepted a missing calling/compiled reference."); }
            catch (Exception error) when (error is NotSupportedException ||
                error is InvalidDataException && error.Message == "Reference comparison has no winning placed source.") { }
        }
        using var cold = new FalloutReferenceWorld(records);
        cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!);
        Query(cold, "SourceMapMarker.GetIsReference OtherMapMarker", false);
        Query(cold, "player.GetIsReference player", true);
        Console.WriteLine("OPENNV_REFERENCE_IDENTITY_QUERY_PASS implicitCaller=true explicitCaller=true typedReceiver=true distinctBase=true compiledOnly=true absentCallerRefused=true cold=true");
    }
}
