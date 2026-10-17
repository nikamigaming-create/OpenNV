using System.Text.Json;
using OpenNV.Runtime.Gameplay.State;

var directory = Path.Combine(
    Path.GetTempPath(),
    "opennv-save-slots-" + Guid.NewGuid().ToString("N"));
var canonical = Path.Combine(directory, "authoritative.json");
var expectedSchema = "probe-save/v1";
var scene = "source-scene";
try
{
    var catalog = new RuntimeSaveSlotCatalog(
        canonical,
        root =>
        {
            if (root.GetProperty("schema").GetString() != expectedSchema ||
                root.GetProperty("sceneSha256").GetString() != scene)
                throw new InvalidOperationException("Probe save is incompatible.");
        });
    var firstId = Guid.ParseExact("ad0f9bcc168b41aa834fc6f9d2cc415e", "N");
    var first = catalog.Create(firstId, () => Write(17, "Vault Dweller"));
    if (first.Id != firstId.ToString("N") || first.CharacterName != "Vault Dweller" ||
        first.HitPoints != 17 || first.Schema != expectedSchema)
        throw new InvalidOperationException("Slot metadata was not derived from the authoritative envelope.");

    Write(3, "Changed State");
    catalog.Activate(first.Id);
    using (var restored = JsonDocument.Parse(File.ReadAllBytes(canonical)))
    {
        if (restored.RootElement.GetProperty("playerHitPoints").GetInt32() != 17 ||
            restored.RootElement.GetProperty("character").GetProperty("Name").GetString() !=
                "Vault Dweller")
            throw new InvalidOperationException("Selected slot was not promoted to the canonical save.");
    }

    File.WriteAllText(
        Path.Combine(canonical + RuntimeSaveSlotCatalog.SlotDirectorySuffix, Guid.NewGuid().ToString("N") + ".json"),
        "{\"schema\":\"wrong\",\"sceneSha256\":\"other\"}");
    try
    {
        _ = catalog.ReadSlots();
        throw new InvalidOperationException("Incompatible slot did not fail closed.");
    }
    catch (InvalidOperationException exception) when (exception.Message == "Probe save is incompatible.")
    {
    }

    var rejected = 0;
    if (catalog.ReadSlots(true, (_, _) => rejected++).Count != 2 || rejected != 1)
        throw new InvalidOperationException("One rejected slot hid the current/valid saves.");
    Write(4, "Later State");
    var later = File.ReadAllBytes(canonical);
    catalog.Activate(first.Id, preserveCurrent: true);
    if (!catalog.ReadSlots(false, (_, _) => { }).Any(slot => File.ReadAllBytes(slot.Path).SequenceEqual(later)))
        throw new InvalidOperationException("Loading lost the previous Continue save.");
    var before = File.ReadAllBytes(canonical);
    var slotBefore = File.ReadAllBytes(first.Path);
    var duplicateCalled = false;
    try { catalog.Create(firstId, () => { duplicateCalled = true; Write(1, "Duplicate"); }); throw new Exception("Duplicate checkpoint ID accepted."); }
    catch (InvalidOperationException error) when (error.Message == "Save-slot identity already exists.") { }
    if (duplicateCalled || !File.ReadAllBytes(canonical).SequenceEqual(before) || !File.ReadAllBytes(first.Path).SequenceEqual(slotBefore))
        throw new Exception("Rejected duplicate checkpoint changed the current or retained save.");
    if (catalog.ReadSlot(first.Id) != catalog.ReadSlots(false, (_, _) => { }).Single(slot => slot.Id == first.Id))
        throw new Exception("Direct checkpoint lookup lost source-derived metadata or depended on unrelated malformed slots.");
    try { catalog.ReadSlot("../authoritative"); throw new Exception("Unsafe checkpoint lookup accepted."); }
    catch (InvalidOperationException) { }
    try { catalog.Activate("../authoritative"); throw new Exception("Unsafe slot ID accepted."); }
    catch (InvalidOperationException) { }
    if (!File.ReadAllBytes(canonical).SequenceEqual(before)) throw new Exception("Rejected load changed Continue.");

    Write(9, "Continue before rejected load");
    var oldContinue = File.ReadAllBytes(canonical);
    var oldTimestamp = File.GetLastWriteTimeUtc(canonical);
    using (var activation = catalog.BeginActivation(first.Id))
    {
        activation.RequireSelected(canonical);
        if (!File.ReadAllBytes(canonical).SequenceEqual(slotBefore)) throw new Exception("Pending load did not select its complete slot.");
    }
    if (!File.ReadAllBytes(canonical).SequenceEqual(oldContinue) || File.GetLastWriteTimeUtc(canonical) != oldTimestamp)
        throw new Exception("Rejected world load did not restore the original Continue and timestamp.");
    using (var activation = catalog.BeginActivation(first.Id)) { activation.RequireSelected(canonical); activation.Commit(); }
    if (!File.ReadAllBytes(canonical).SequenceEqual(slotBefore)) throw new Exception("Accepted world load was rolled back.");
    var damaged = "{incomplete-continue"u8.ToArray();
    File.WriteAllBytes(canonical, damaged);
    using (var activation = catalog.BeginActivation(first.Id)) { activation.RequireSelected(canonical); activation.Commit(); }
    if (!File.ReadAllBytes(canonical).SequenceEqual(slotBefore) ||
        !Directory.EnumerateFiles(canonical + RuntimeSaveSlotCatalog.SlotDirectorySuffix, "*.rejected")
            .Any(path => File.ReadAllBytes(path).SequenceEqual(damaged)))
        throw new Exception("A damaged Continue prevented recovery or its rejected bytes were lost.");
    using (var activation = catalog.BeginActivation(first.Id))
    {
        Write(11, "Newer concurrent Continue");
        try { activation.RequireSelected(canonical); throw new Exception("Changed selected save accepted."); }
        catch (InvalidDataException) { }
        try { activation.Dispose(); throw new Exception("Concurrent Continue was overwritten during rollback."); }
        catch (IOException) { }
        activation.Commit();
    }
    using (var current = JsonDocument.Parse(File.ReadAllBytes(canonical)))
        if (current.RootElement.GetProperty("playerHitPoints").GetInt32() != 11) throw new Exception("Rejected load lost a newer save.");
    var emptyCalled = false;
    try { catalog.Create(Guid.Empty, () => emptyCalled = true); throw new Exception("Empty checkpoint ID accepted."); }
    catch (ArgumentException) { }
    if (emptyCalled) throw new Exception("Rejected empty checkpoint called its writer.");

    var native = new RuntimeSaveSlotCatalog(Path.Combine(directory, "native.json"), root =>
    {
        if (root.GetProperty("Schema").GetString() != "opennv-native-fnv-campaign-save/v18")
            throw new InvalidDataException("Native schema differs.");
    });
    var nativeSlot = native.Create(() => File.WriteAllText(Path.Combine(directory, "native.json"),
        JsonSerializer.Serialize(new { Schema = "opennv-native-fnv-campaign-save/v18", PlayerName = "Courier",
            ActiveCell = new { OwnerPlugin = "Source.esm", ObjectId = 0x123u }, Vitals = new { HitPoints = 32.5 } })));
    if (nativeSlot.CharacterName != "Courier" || nativeSlot.MapName != "Source.esm:000123" || nativeSlot.HitPoints != 33 ||
        native.ReadSlots(true).Count != 2)
        throw new InvalidOperationException("Native metadata/current save did not survive the catalog.");

    Console.WriteLine("OPENNV_RUNTIME_SAVE_SLOT_PASS classic=true native=true select=true preserveContinue=true failedLoadRollback=true damagedContinueRecovery=true concurrentSavePreserved=true malformedIsolated=true metadata=actual-save");
}
finally
{
    if (Directory.Exists(directory))
        Directory.Delete(directory, recursive: true);
}

void Write(int hitPoints, string name)
{
    Directory.CreateDirectory(directory);
    File.WriteAllText(canonical, JsonSerializer.Serialize(new
    {
        schema = expectedSchema,
        sceneSha256 = scene,
        playerHitPoints = hitPoints,
        character = new { Name = name },
        activeMap = new { mapId = "V13ENT" },
    }));
}
