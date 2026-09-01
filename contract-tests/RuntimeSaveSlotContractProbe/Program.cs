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

    Console.WriteLine("OPENNV_RUNTIME_SAVE_SLOT_PASS create=1 select=1 canonical-load=1 metadata=actual-save");
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
