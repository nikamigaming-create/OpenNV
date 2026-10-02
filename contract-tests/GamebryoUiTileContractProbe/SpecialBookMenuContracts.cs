using System.Buffers.Binary;
using OpenNV.Runtime.Content;

internal static class SpecialBookMenuContracts
{
    internal static void Run()
    {
        Declarations();
        var values = Enumerable.Repeat(5, FalloutSpecialAllocationSession.AttributeCount).ToArray();
        var modifiers = new float[values.Length]; var writes = new List<(int Slot, int Value)>();
        var binding = new FalloutSpecialAllocationBinding("synthetic-base-and-permanent-owner", slot => values[slot - 5] + modifiers[slot - 5],
            (slot, value) => { writes.Add((slot, value)); values[slot - 5] = value; });
        var normal = new FalloutSpecialAllocationSession(35, binding);
        Check(normal.CanFinish && normal.Remaining == 0, "Default source total was not read live.");
        Check(!normal.Change(5, 1) && writes.Count == 0, "A full budget wrote another point.");
        Check(normal.Change(5, -1) && values[0] == 4 && !normal.CanFinish, "Decrease did not write the live base immediately.");
        var reopened = new FalloutSpecialAllocationSession(35, binding);
        Check(reopened.Values[0] == 4, "Closing/reopening rolled back an immediate source edit.");
        Check(reopened.Change(5, 1) && normal.CanFinish && writes.Count == 2, "A second menu did not share its authoritative owner.");
        var ttw = new FalloutSpecialAllocationSession(40, binding);
        modifiers[0] = 1.75f;
        Check(ttw.Values[0] == 6 && ttw.Remaining == 4, "The permanent integer getter did not floor before budgeting.");
        Check(ttw.Change(5, 1) && values[0] == 7 && ttw.Values[0] == 8 && writes[^1] == (5, 7),
            "Permanent modifiers were subtracted from the absolute source base write.");
        modifiers[0] = -8.25f;
        Check(ttw.Values[0] == -2, "Negative permanent values truncated instead of flooring.");
        Check(ttw.Change(5, -1) && values[0] == 1, "Source decrease did not clamp the base target to one.");
        modifiers[0] = 0; values[0] = 10; values[1] = 4;
        Check(ttw.Change(5, 1) && values[0] == 10, "Source increase did not clamp the base target to ten.");
        var savedBase = values.ToArray();
        values[0] = 4;
        var restored = new FalloutSpecialAllocationSession(40, new("synthetic-restored-base-owner", slot => savedBase[slot - 5],
            (slot, value) => savedBase[slot - 5] = value));
        Check(restored.Values[0] == 10, "A new session retained a retired menu's local draft.");
        Reject(() => new FalloutSpecialAllocationSession(6, binding));
        Reject(() => new FalloutSpecialAllocationSession(71, binding));
        Reject(() => new FalloutSpecialAllocationSession(40, binding with { Owner = "" }));
        modifiers[1] = float.NaN; Reject(() => _ = ttw.Values); modifiers[1] = 0;
        var failing = new FalloutSpecialAllocationSession(40, binding with { WriteBaseInteger = (_, _) => throw new NotSupportedException("unbound-base-owner") });
        Reject(() => failing.Change(5, -1));
        Check(values[0] == 4, "A failed source write mutated a hidden menu draft.");
        Console.WriteLine("PASS SPECIAL source declarations, all eighteen transitions, live permanent/base editing, default/explicit budgets, retained cancellation and unbound-owner rejection.");
    }

    private static void Declarations()
    {
        var strings = new Dictionary<uint, string>(); var memory = new Dictionary<uint, byte[]>(); var code = new List<byte>(); uint cursor = 100;
        uint Literal(string value) { var id = cursor++; strings.Add(id, value); return id; }
        uint Float(float value) { var id = cursor++; memory.Add(id, BitConverter.GetBytes(value)); return id; }
        uint Double(double value) { var id = cursor++; memory.Add(id, BitConverter.GetBytes(value)); return id; }
        void Push(string value) { code.Add(0x68); code.AddRange(BitConverter.GetBytes(Literal(value))); }
        void Operand(byte first, byte second, uint address) { code.Add(first); code.Add(second); code.AddRange(BitConverter.GetBytes(address)); }
        code.AddRange(new byte[] { 0x55, 0x8b, 0xec, 0x6a, 35, 0xe8 });
        code.AddRange(BitConverter.GetBytes(6)); code.AddRange(new byte[] { 0x83, 0xc4, 4, 0x5d, 0xc3, 0xcc });
        code.AddRange(new byte[] { 0x55, 0x8b, 0xec }); Push("Data/Menus/CharGen/SPECIALBookMenu.xml");
        code.AddRange(new byte[] { 0x6a, 0, 0x6a, 0, 0x6a, 0, 0x6a, 1, 0x6a, 0 }); Push("Meshes/Terminals/Babybook02.NIF");
        var scale = Float(.05f);
        Operand(0xd9, 5, scale); Operand(0xd9, 5, Float(-60)); Operand(0xd9, 5, Float(MathF.PI / 2));
        var forward = Enumerable.Range(1, 9).Select(index => "Forward" + index).ToArray();
        var backward = Enumerable.Range(0, 9).Select(index => "Backward" + index).ToArray();
        Push(forward[0]); Operand(0xdc, 0x0d, Double(20));
        for (var index = 0; index < 3; index++) Operand(0xd9, 5, Float(.85f));
        Push("LookInside_Btn:0");
        code.AddRange(new byte[] { 0x55, 0x8b, 0xec, 0x6a, 0xff });
        Operand(0xd9, 5, Float(.75f)); Operand(0xdc, 0x0d, Double(Math.PI / 180)); Operand(0xdc, 0x0d, Double(.15)); Push("Surgery3DCamera");
        const uint table = 10000;
        var names = backward.Concat(forward).ToArray();
        for (var index = 0; index < names.Length; index++) memory.Add(table + (uint)index * 4, BitConverter.GetBytes(Literal(names[index])));
        memory.Add(table + (uint)names.Length * 4, new byte[4]);
        var firstOperand = code.Count; code.AddRange(new byte[] { 0x8b, 0x14, 0x8d }); code.AddRange(BitConverter.GetBytes(table));
        var secondOperand = code.Count; code.AddRange(new byte[] { 0x8b, 0x0c, 0x85 }); code.AddRange(BitConverter.GetBytes(table + 32)); code.AddRange(new byte[8]);
        byte[] Read(uint address, int count) => memory.TryGetValue(address, out var bytes) && bytes.Length == count ? bytes : throw new InvalidDataException("Synthetic declaration extent is absent.");
        FalloutSpecialBookPresentation Decode(byte[] bytes, IReadOnlyCollection<string>? sequenceNames = null) =>
            FalloutExecutableStringTable.ReadSpecialBookDeclarations(bytes, address => strings.GetValueOrDefault(address), Read, sequenceNames ?? names);
        var result = Decode(code.ToArray());
        Check(result.DefaultBudget == 35 && result.ModelScale == .05f && result.Depth == -60 && result.RotationRadians == MathF.PI / 2,
            "SPECIAL book source pose/default declarations were replaced.");
        Check(result.LightIntensity == .85f && result.LightRadiusMultiple == 20 && result.LastPage == 9, "SPECIAL book light/page declaration changed.");
        for (var page = 1; page <= 9; page++) Check(result.Transition(page, 1) == forward[page - 1], "A forward source transition was lost.");
        for (var page = 0; page < 9; page++) Check(result.Transition(page, -1) == backward[page], "A backward source transition was lost.");
        Check(Math.Abs(result.HorizontalSlope(75) - MathF.Tan(75 * .15f * MathF.PI / 180) * .75f) < 1e-7f, "Source projection factors were replaced.");
        var missingForward = code.ToArray(); missingForward[secondOperand] = 0; Reject(() => Decode(missingForward));
        var missingBackward = code.ToArray(); missingBackward[firstOperand] = 0; Reject(() => Decode(missingBackward));
        Reject(() => Decode(code.ToArray(), names[..^1]));
        Check(Decode(code.ToArray(), names.Append("UnusedModSequence").ToArray()).LastPage == 9, "An unused NIF sequence imposed an artificial book restriction.");
        memory[scale] = BitConverter.GetBytes(float.NaN); Reject(() => Decode(code.ToArray())); memory[scale] = BitConverter.GetBytes(.05f);
        var defaultDrift = code.ToArray(); defaultDrift[4] = 40; Check(Decode(defaultDrift).DefaultBudget == 40, "A declared default budget was guessed.");
        BinaryPrimitives.WriteUInt32LittleEndian(defaultDrift.AsSpan(6), 0); Reject(() => Decode(defaultDrift));
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Unsupported SPECIAL book input was accepted.");
    }
}
