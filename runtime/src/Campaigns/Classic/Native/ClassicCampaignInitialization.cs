using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

internal sealed record ClassicInitialItem(ClassicItemOrigin Origin, Fallout1NativeMapObject Object, ClassicItemLocation Location);
internal sealed record ClassicCampaignInitialization(string Identity, string Map, string? Program, int? Light,
    ClassicIntMapStartOverride? Arrival, IReadOnlyList<ClassicInitialItem> Items, ClassicIntProcedureState? ScriptState)
{
    /// <summary>Runs the actual campaign entry program, with no prepared program or item list.</summary>
    internal static ClassicCampaignInitialization Execute(ClassicMapCatalog catalog, ClassicCharacterDraft choice, ClassicRetailRandomContract randomContract)
    {
        if (catalog.Campaign != "fallout-2") throw new NotSupportedException("This campaign still requires its own source initialization contract.");
        var level = catalog.Load(catalog.StartingMap);
        if (level.Map.MapScriptIndex == 0)
            return new(ClassicPremadeReader.Hash(Encoding.UTF8.GetBytes(level.Sha256)), level.Path, null, null, null, [], null);
        var names = Fallout1NativeLists.Read(catalog.Read("scripts/scripts.lst", out _));
        var index = level.Map.MapScriptIndex - 1;
        if (index < 0 || index >= names.Count) throw new InvalidDataException("Campaign entry script is outside scripts.lst.");
        var path = ClassicMapCatalog.Canonical("scripts/" + names[index]);
        var bytes = catalog.Read(path, out _);
        var program = ClassicNativeIntReader.Read(bytes, path);
        var globalsBytes = catalog.Read("data/vault13.gam", out _);
        var globals = Variables(globalsBytes, "GAME_GLOBAL_VARS");
        var mapBytes = catalog.Read(level.Path, out _);
        var mapVariables = MapVariables(mapBytes);
        var random = ClassicRetailRandomLifecycle.ResetForNewGame(ClassicRetailRandomLifecycle.Initialize(0, randomContract), randomContract);
        var state = new ClassicIntProcedureState(program.InitialVariables, new Dictionary<int, int>(), new Dictionary<int, int>(), mapVariables, globals, [], random);
        var factory = new ItemFactory(catalog, level);
        const int dude = int.MaxValue;
        var world = ClassicIntWorldObjectState.Empty with
        {
            Objects = new Dictionary<int, ClassicIntWorldObject> { [dude] = new(dude, 0x01000000, level.Map.EnteringTile, level.Map.EnteringElevation, true) },
        };
        // Stock FO2 starts with 302400 decisecond ticks (08:24), in July.
        // A running game clock and modded start-date overrides remain separate owners.
        var stats = ClassicStartingStats.From(choice.Character);
        var critterStats = Enumerable.Range(0, 7).ToDictionary(stat => (dude, stat), stat => stats.Special[stat]);
        critterStats[(dude, 34)] = choice.Character.Female ? 1 : 0;
        var context = new ClassicIntExpressionContext(state.ProgramVariables, state.LocalVariables, state.ScriptLocalVariables,
            state.MapVariables, state.GlobalVariables, dude, 0, null, null, critterStats,
            new Dictionary<(int, int), int> { [(14, 0)] = 1 }, new Dictionary<int, int>(), new Dictionary<(int, int), int>(),
            new Dictionary<string, int>(), 302400, 824, 7, factory, CurrentMapIndex: level.Map.MapIndex);
        var executed = 0;
        foreach (var procedure in new[] { program.StartupProcedure!, "map_enter_p_proc" })
        {
            if (!program.Procedures.ContainsKey(procedure)) continue;
            var result = ClassicIntProcedureVm.Execute(program, procedure, state, context, world, randomContract, 100000);
            // No partially applied event: reached effects without an owner keep
            // the campaign entry closed, including messages, timers and actors.
            if (result.MessageEffects.Count != 0 || result.SoundEffects.Count != 0 || result.WorldObjects.Doors.Count != 0 ||
                result.WorldObjects.Movements.Count != 0 || result.WorldObjects.TraitAssignments.Count != 0 ||
                result.WorldObjects.AttackRequests.Count != 0 || result.WorldObjects.Timers.Pending.Count != 0 ||
                result.WorldObjects.DialogueSystemEntered || result.WorldObjects.DialogueStart is not null ||
                result.WorldObjects.ScriptOverrides || result.State.ValueStack.Count != 0 ||
                result.WorldObjects.Objects[dude] != world.Objects[dude])
                throw new NotSupportedException($"Campaign entry reached unbound world effects: {path}:{procedure}.");
            state = result.State; world = result.WorldObjects; executed += result.ExecutedInstructions;
        }
        if (world.Inventory.Any(row => row.OwnerHandle != dude) || world.CreatedObjects.Count != world.Inventory.Count ||
            world.Inventory.Select(row => row.ObjectHandle).Distinct().Count() != world.Inventory.Count)
            throw new NotSupportedException("Campaign entry created world objects or non-player inventories requiring their runtime owner.");
        var items = world.Inventory.Select(entry => factory.Item(entry.ObjectHandle, entry.Quantity)).ToArray();
        var identity = ClassicPremadeReader.Hash(JsonSerializer.SerializeToUtf8Bytes(new
        {
            level.Path,
            level.Sha256,
            script = path,
            scriptSha256 = ClassicPremadeReader.Hash(bytes),
            globalsSha256 = ClassicPremadeReader.Hash(globalsBytes),
            state,
            world = world.Save(),
            executed,
            creations = factory.Requests,
        }));
        return new(identity, level.Path, path, world.LightLevel, world.MapStartOverride, items, state);
    }

    internal static Dictionary<int, int> Variables(byte[] bytes, string section)
    {
        var result = new Dictionary<int, int>(); var active = false; var found = false;
        foreach (var line in Encoding.Latin1.GetString(bytes).Replace("\r", "", StringComparison.Ordinal).Split('\n'))
        {
            var text = line.Split("//", 2)[0].Trim();
            if (text.Length == 0 || text.StartsWith(';')) continue;
            if (text.EndsWith(':')) { active = text[..^1] == section; found |= active; continue; }
            if (!active) continue;
            var match = Regex.Match(text, @"^[A-Za-z_][A-Za-z_0-9]*\s*:=\s*(-?\d+)\s*;?\s*$", RegexOptions.CultureInvariant);
            if (!match.Success) throw new InvalidDataException("Unsupported GAM variable declaration: " + text);
            result.Add(result.Count, int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
        }
        if (!found) throw new InvalidDataException("GAM has no " + section + " section.");
        return result;
    }

    internal static Dictionary<int, int> MapVariables(byte[] bytes)
    {
        var count = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(0x30));
        if (count < 0 || count > (bytes.Length - 0xec) / 4) throw new InvalidDataException("MAP global variable extent is invalid.");
        return Enumerable.Range(0, count).ToDictionary(index => index, index => BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(0xec + index * 4)));
    }

    private sealed class ItemFactory(ClassicMapCatalog catalog, ClassicMapLevel level) : IClassicIntObjectFactory
    {
        private readonly Dictionary<int, ClassicIntObjectCreationRequest> _requests = [];
        private readonly ClassicItemDefinitions _definitions = new(catalog);
        internal IReadOnlyDictionary<int, ClassicIntObjectCreationRequest> Requests => _requests;
        public int Create(ClassicIntObjectCreationRequest request)
        {
            var definition = _definitions.Read(request.Source.Pid);
            if (request.Source.ScriptId != -1 || definition.Script != -1) throw new NotSupportedException("Created item requires its own script lifecycle.");
            var handle = checked(_requests.Count + 1); _requests.Add(handle, request); return handle;
        }
        internal ClassicInitialItem Item(int handle, int quantity)
        {
            if (quantity <= 0) throw new InvalidDataException("Created source item has invalid quantity.");
            var request = _requests[handle]; var creation = request.Source;
            var prototype = Fallout1NativeObjectGraphReader.ResolvePrototype(catalog, creation.Pid);
            var bytes = catalog.Read(prototype.LogicalPath!, out _);
            var definition = _definitions.Read(creation.Pid);
            int[] values = definition.Subtype switch
            {
                3 => [definition.Weapon!.Capacity, definition.Weapon.AmmoPid],
                4 => [definition.Ammo!.PackSize],
                5 => [definition.Misc!.Charges],
                6 => [definition.KeyCode!.Value],
                _ => [],
            };
            var serial = -handle; var origin = new ClassicItemOrigin(level.Path, level.Sha256, serial, creation.Pid);
            var instance = new Fallout1NativeMapObject(serial, request.InstructionOffset, "source-INT-created", -handle,
                creation.Tile, 0, 0, 0, 0, prototype.Fid!.Value, BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20)),
                creation.Elevation, creation.Pid, uint.MaxValue, 0, 0, values, 0, prototype, [], quantity,
                BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(12)), BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16)));
            return new(origin, instance, ClassicItemLocation.Player);
        }
    }
}
