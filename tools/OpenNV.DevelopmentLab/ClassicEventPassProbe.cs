using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Campaigns.Classic.Native;
using OpenNV.Runtime.Content;

internal static class ClassicEventPassProbe
{
    internal static void Run(ClassicMapCatalog catalog, ClassicCharacterDraft choice, ClassicMapLevel level)
    {
        var order = level.Map.LiveScripts.Select((row, index) => (row.ScriptId, index)).ToDictionary(row => row.ScriptId, row => row.index);
        var doors = level.Objects.TopLevelObjects.Where(row => ClassicDoorWorld.IsDoor(row) && row.ScriptId != uint.MaxValue)
            .OrderBy(row => order[row.ScriptId]).ToArray();
        var names = Fallout1NativeLists.Read(catalog.Read("scripts/scripts.lst", out _));
        var paths = doors.Select(row => ClassicMapCatalog.Canonical("scripts/" + Path.ChangeExtension(
            names[level.Map.LiveScripts.Single(slot => slot.ScriptId == row.ScriptId).ScriptProgramIndex], ".int"))).Distinct().ToArray();
        if (paths.Length < 2) return;
        // Synthetic scripts over the real owned MAP identities. The first
        // module's entry reads an export declared by a later module's startup.
        // No placement or user source file is changed by this fixture.
        using var varied = new ClassicMapCatalog(new Variation(catalog, paths.ToDictionary(path => path, path => Source(path == paths[^1]))), catalog.Campaign);
        var elevation = level.Map.Elevations.Keys.First(value => new ClassicMapNavigation(level.Map, level.Objects, value).Walkable.Count != 0);
        var tile = new ClassicMapNavigation(level.Map, level.Objects, elevation).Walkable.Order().First();
        var player = new ClassicPlayerSession(varied, level.Path, choice, elevation, tile);
        var states = player.Doors.Save().Doors.Where(row => row.ScriptState is not null).ToArray();
        if (states.Length != doors.Length || player.Doors.Save().Doors.Any(row => row.Failure is not null) ||
            states.Any(row => row.ScriptState!.ProgramVariables.GetValueOrDefault(0) != 41))
            throw new InvalidOperationException("Owned MAP entry ran before all source startup declarations were available.");
        Console.WriteLine($"PASS native event passes map={level.Path} scriptedDoors={states.Length} laterModuleExport=true fixture=true gameplay=false");
    }

    private sealed class Variation(IFalloutClassicOwnedSource source, IReadOnlyDictionary<string, byte[]> scripts) : IFalloutClassicOwnedSource
    {
        public string ProfileId => source.ProfileId;
        public byte[] Read(string path, out int index)
        {
            if (scripts.TryGetValue(ClassicMapCatalog.Canonical(path), out var bytes)) { index = -1; return bytes; }
            return source.Read(path, out index);
        }
        public IReadOnlyList<string> EffectiveLogicalPaths(string prefix, string extension) => source.EffectiveLogicalPaths(prefix, extension);
        public void Dispose() { }
    }

    private static byte[] Source(bool producer)
    {
        using var stream = new MemoryStream();
        void Word(int value)
        {
            Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(bytes, value); stream.Write(bytes);
        }
        void Op(ushort opcode, int? operand = null)
        {
            Span<byte> bytes = stackalloc byte[2]; BinaryPrimitives.WriteUInt16BigEndian(bytes, opcode); stream.Write(bytes);
            if (operand is { } value) Word(value);
        }
        var names = new[] { "..............", "start", "map_enter_p_proc", "entry-ready" };
        var references = new List<int>(); var text = new List<byte>();
        foreach (var name in names) { references.Add(4 + text.Count); text.AddRange(Encoding.ASCII.GetBytes(name + '\0')); }
        stream.Write(new byte[42]); Word(3);
        var bodies = new List<int>();
        for (var index = 0; index < 3; index++)
        { Word(references[index]); Word(0); Word(0); Word(0); bodies.Add((int)stream.Position); Word(0); Word(0); }
        Word(text.Count); stream.Write(text.ToArray()); Word(-1); Word(-1);
        Op(0x802c); Op(0xc001, 0);
        if (producer)
        {
            Op(0x9001, references[3]); Op(0x8016);
            Op(0xc001, 41); Op(0x9001, references[3]); Op(0x8015);
        }
        Op(0x8003); var jump = (int)stream.Position + 2; Op(0xc001, 0); Op(0x8004);
        var startup = (int)stream.Position; Op(0x801c);
        var enter = (int)stream.Position;
        Op(0x9001, references[3]); Op(0x8014); Op(0xc001, 0); Op(0x8013); Op(0x801c);
        var result = stream.ToArray();
        for (var index = 0; index < 3; index++) BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(bodies[index]), index == 2 ? enter : startup);
        BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(jump), startup);
        return result;
    }
}
