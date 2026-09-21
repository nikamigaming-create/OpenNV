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
    try { catalog.Activate("../authoritative"); throw new Exception("Unsafe slot ID accepted."); }
    catch (InvalidOperationException) { }
    if (!File.ReadAllBytes(canonical).SequenceEqual(before)) throw new Exception("Rejected load changed Continue.");

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

    Console.WriteLine("OPENNV_RUNTIME_SAVE_SLOT_PASS classic=true native=true select=true preserveContinue=true malformedIsolated=true metadata=actual-save");
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
