using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class InteriorQueryContracts
{
    internal static void Verify(FalloutPluginStack records, FalloutReferenceWorld world)
    {
        static FalloutFormKey Key(uint id) => new("Base.esm", id);
        static void Require(bool condition, string message)
        { if (!condition) throw new InvalidDataException(message); }
        Require(world.IsInInterior(Key(0x900)) && world.IsInInterior(Key(0x14), Key(0x800)) &&
            !world.IsInInterior(Key(0x14), Key(0x804)), "Interior query ignored the authoritative CELL flag.");
        try { world.IsInInterior(Key(0x14)); throw new InvalidDataException("Player query invented a cell."); }
        catch (NotSupportedException) { }
        var scripts = new FalloutReferenceScripts(records, world, new(records), new((_, _) => false, _ => { },
            IsInInterior: actor => world.IsInInterior(actor, Key(0x800))));
        var program = FalloutGameModeProgram.Read("begin GameMode\nset count to IsInInterior\nset timer to player.IsInInterior\nend");
        void Query() => scripts.ExecuteProgram(records.GetEffective(Key(0x900)), records.GetEffective(Key(0x500)), program, 0);
        Query();
        Require(world.Get(Key(0x900)).Read(1) == 1 && world.Get(Key(0x900)).Read(2) == 1, "Script interior query lost its target.");
        world.Get(Key(0x900)).CaptureEngagement = null;
        world.SetPlacement(Key(0x900), new(Key(0x804), [0, 0, 0], [0, 0, 0]));
        Query();
        Require(world.Get(Key(0x900)).Read(1) == 0 && world.Get(Key(0x900)).Read(2) == 1,
            "Moved reference inherited the player's interior state or its original cell.");
        using var cold = new FalloutReferenceWorld(records);
        cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!);
        Require(!cold.IsInInterior(Key(0x900)) && cold.IsInInterior(Key(0x901)), "Cold interior query lost moved placement.");
        Console.WriteLine("OPENNV_INTERIOR_QUERY_PASS sourceFlags=true distinctPlayer=true movedReference=true coldRestore=true");
    }
}
