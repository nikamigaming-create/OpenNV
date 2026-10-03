using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;

internal static class DialoguePackageContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-dialogue-package-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
            var waitLocation = new byte[12]; BinaryPrimitives.WriteUInt32LittleEndian(waitLocation.AsSpan(4), 0x300);
            var currentLocation = new byte[12]; BinaryPrimitives.WriteInt32LittleEndian(currentLocation, 2);
            var editorTrigger = new byte[12]; BinaryPrimitives.WriteInt32LittleEndian(editorTrigger, 3);
            BinaryPrimitives.WriteInt32LittleEndian(editorTrigger.AsSpan(8), 140);
            File.WriteAllBytes(Path.Combine(directory, "Dialogue.esm"), Join(Record("TES4", 0, Field("HEDR", header)),
                Package(1), Package(2, type: 0, topic: 0, location: waitLocation), Package(3, distance: 0),
                Package(4, type: 2), Package(5, topic: 0), Package(6, location: new byte[11]),
                Package(7, duplicateLocation: true), Package(8, fov: float.NaN), Package(9, targetType: 1),
                Package(10, type: 0, topic: 0, location: currentLocation, trigger: editorTrigger, duplicateTrigger: true),
                Package(11, trigger: new byte[11]), Package(12, trigger: editorTrigger, conflictingTrigger: true)));
            using var records = FalloutPluginStack.Load(directory, ["Dialogue.esm"]);
            FalloutPluginRecord Source(uint id) => records.GetEffective(new("Dialogue.esm", id));
            var speech = FalloutDialoguePackage.Read(Source(1));
            if (speech.Target != new FalloutFormKey("Dialogue.esm", 0x14) || speech.Topic != new FalloutFormKey("Dialogue.esm", 0x200) ||
                speech.ActivationDistance != 120 || speech.Type != 1 || !speech.Running || !speech.WeaponDrawn ||
                FalloutScriptPackage.Read(Source(1)).LocationType is not null)
                throw new InvalidDataException("Dialogue invented a location or lost its target, topic, type or movement flags.");
            var conversation = FalloutDialoguePackage.Read(Source(2));
            if (conversation.Type != 0 || conversation.Topic is not null || FalloutScriptPackage.Read(Source(2)).LocationType != 0)
                throw new InvalidDataException("Conversation lost its optional topic or authored location.");
            foreach (var id in Enumerable.Range(3, 7)) Reject(() => FalloutDialoguePackage.Read(Source((uint)id)));
            var trigger = FalloutDialoguePackage.Read(Source(10)).TriggerLocation;
            if (trigger is not { Type: 3, Radius: 140, Reference: null } || FalloutScriptPackage.Read(Source(10)).LocationType != 2)
                throw new InvalidDataException("Dialogue lost the current wait location or repeated source editor trigger.");
            Reject(() => FalloutDialoguePackage.Read(Source(11))); Reject(() => FalloutDialoguePackage.Read(Source(12)));
            var caller = new FalloutFormKey("Dialogue.esm", 0x700);
            var condition = new FalloutCondition(Source(1), 0, 1, 161, 2, 0, 0, 0);
            FalloutFormKey? Query(FalloutFormKey reference) => reference == speech.Target ? Source(2).FormKey :
                reference == new FalloutFormKey("Dialogue.esm", 0x701) ? Source(3).FormKey :
                throw new NotSupportedException("Synthetic actor has no package owner.");
            if (!FalloutAiPackages.IsCurrentPackage(condition, caller, Source(2).FormKey, Query) ||
                !FalloutAiPackages.IsCurrentPackage(condition with { RunOn = 1 }, caller, Source(1).FormKey, Query) ||
                !FalloutAiPackages.IsCurrentPackage(condition with { RunOn = 2, Reference = 0x701, Argument1 = 3 }, caller, Source(1).FormKey, Query) ||
                FalloutAiPackages.IsCurrentPackage(condition with { RunOn = 2, Reference = 0x701 }, caller, Source(2).FormKey, Query))
                throw new InvalidDataException("Current-package conditions queried the wrong actor.");
            Reject(() => FalloutAiPackages.IsCurrentPackage(condition with { RunOn = 2 }, caller, null, Query));
            Reject(() => FalloutAiPackages.IsCurrentPackage(condition with { RunOn = 2, Reference = 0x702 }, caller, null, Query));
            Reject(() => FalloutAiPackages.IsCurrentPackage(condition with { RunOn = 3 }, caller, null, Query));
            FurnitureOrder(directory);
            Console.WriteLine("OPENNV_DIALOGUE_PACKAGE_CONTRACT_PASS optionalLocation=true sourceTarget=true sourceType=true requiredSpeechTopic=true malformedRejected=true");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static byte[] Package(uint id, uint type = 1, uint topic = 0x200, int distance = 120,
        byte[]? location = null, bool duplicateLocation = false, float fov = 100, int targetType = 0,
        byte[]? trigger = null, bool duplicateTrigger = false, bool conflictingTrigger = false)
    {
        var data = new byte[12]; BinaryPrimitives.WriteUInt32LittleEndian(data, 0x802000); data[4] = 15; data[5] = 0xcd;
        var target = new byte[16]; BinaryPrimitives.WriteInt32LittleEndian(target, targetType);
        BinaryPrimitives.WriteUInt32LittleEndian(target.AsSpan(4), 0x14); BinaryPrimitives.WriteInt32LittleEndian(target.AsSpan(8), distance);
        var dialogue = new byte[24]; BinaryPrimitives.WriteSingleLittleEndian(dialogue, fov);
        BinaryPrimitives.WriteUInt32LittleEndian(dialogue.AsSpan(4), topic); BinaryPrimitives.WriteUInt32LittleEndian(dialogue.AsSpan(16), type);
        // Nonzero compiler padding is retained; it is not a new behavior flag.
        dialogue[12] = 0xcc;
        var wait = location is null ? [] : Field("PLDT", location);
        if (duplicateLocation) wait = Join(Field("PLDT", new byte[12]), Field("PLDT", new byte[12]));
        var triggerFields = trigger is null ? [] : Field("PLD2", trigger);
        if (duplicateTrigger) triggerFields = Join(triggerFields, Field("PLD2", trigger!));
        if (conflictingTrigger)
        {
            var conflicting = (byte[])trigger!.Clone(); conflicting[8]++;
            triggerFields = Join(triggerFields, Field("PLD2", conflicting));
        }
        return Record("PACK", id, Field("EDID", Encoding.ASCII.GetBytes("SourceDialogue\0")), Field("PKDT", data), Field("PTDT", target), Field("PKDD", dialogue), wait, triggerFields);
    }

    private static void FurnitureOrder(string directory)
    {
        byte[] Header(params string[] masters) => Record("TES4", 0, Field("HEDR", new byte[12]),
            Join(masters.Select(master => Join(Field("MAST", Encoding.ASCII.GetBytes(master + '\0')), Field("DATA", new byte[8]))).ToArray()));
        byte[] Idle(uint id, uint previous) => Record("IDLE", id, Field("MODL", Encoding.ASCII.GetBytes("Characters/_Male/IdleAnims/test.kf\0")),
            Field("ANAM", Join(new byte[4], BitConverter.GetBytes(previous))), Field("DATA", new byte[6]));
        File.WriteAllBytes(Path.Combine(directory, "OrderBase.esm"), Join(Header(), Idle(100, 101), Idle(101, 0), Idle(102, 100)));
        File.WriteAllBytes(Path.Combine(directory, "OrderExpansion.esm"), Join(Header("OrderBase.esm"), Idle(0x01000200, 101)));
        File.WriteAllBytes(Path.Combine(directory, "OrderPatch.esp"), Join(Header("OrderBase.esm", "OrderExpansion.esm"), Idle(100, 0x01000200)));
        using var records = FalloutPluginStack.Load(directory, ["OrderBase.esm", "OrderExpansion.esm", "OrderPatch.esp"]);
        var branches = records.EffectiveRecordsInRegistrationOrder("IDLE").Select(FalloutFurnitureIdleTree.Read).ToArray();
        var expected = new FalloutFormKey[] { new("OrderBase.esm", 101), new("OrderExpansion.esm", 0x200), new("OrderBase.esm", 100), new("OrderBase.esm", 102) };
        if (!FalloutFurnitureIdleTree.Order(branches).Select(value => value.Record.FormKey).SequenceEqual(expected))
            throw new InvalidDataException("Winning IDLE ordering lost a later-plugin sibling anchor.");
        Reject(() => FalloutFurnitureIdleTree.Order([branches[0] with { Previous = new("OrderBase.esm", 999) }]));
        var first = branches.Single(value => value.Record.FormKey == expected[0]);
        var second = branches.Single(value => value.Record.FormKey == expected[1]);
        Reject(() => FalloutFurnitureIdleTree.Order([first with { Previous = second.Record.FormKey }, second]));
        Console.WriteLine("OPENNV_FURNITURE_SIBLING_CONTRACT_PASS crossPluginAnchor=true winningOverride=true missingRefused=true cycleRefused=true");
    }
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); data.CopyTo(bytes, 24); return bytes;
    }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Invalid dialogue package was admitted.");
    }
}
