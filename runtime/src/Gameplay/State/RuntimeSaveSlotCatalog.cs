using System.Text.Json;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record RuntimeSaveSlotMetadata(
    string Id,
    string Path,
    string Schema,
    string? CharacterName,
    string? MapName,
    int? HitPoints,
    DateTime WrittenUtc);

internal sealed class RuntimeSaveSlotCatalog
{
    internal const string SlotDirectorySuffix = ".slots-v1";
    private readonly string _canonicalSavePath;
    private readonly string _slotDirectory;
    private readonly Action<JsonElement> _validate;

    internal RuntimeSaveSlotCatalog(
        string canonicalSavePath,
        Action<JsonElement> validate)
    {
        if (string.IsNullOrWhiteSpace(canonicalSavePath))
            throw new ArgumentException("A canonical save path is required.", nameof(canonicalSavePath));
        _canonicalSavePath = Path.GetFullPath(canonicalSavePath);
        _slotDirectory = _canonicalSavePath + SlotDirectorySuffix;
        _validate = validate ?? throw new ArgumentNullException(nameof(validate));
    }

    internal IReadOnlyList<RuntimeSaveSlotMetadata> ReadSlots(bool includeCurrent = false,
        Action<string, Exception>? rejected = null)
    {
        var paths = Directory.Exists(_slotDirectory)
            ? Directory.EnumerateFiles(_slotDirectory, "*.json", SearchOption.TopDirectoryOnly) : [];
        if (includeCurrent && File.Exists(_canonicalSavePath)) paths = paths.Prepend(_canonicalSavePath);
        var slots = new List<RuntimeSaveSlotMetadata>();
        foreach (var path in paths)
        {
            try { slots.Add(ReadMetadata(path)); }
            catch (Exception error) when (rejected is not null && error is IOException or JsonException or InvalidOperationException)
            { rejected(path, error); }
        }
        return slots
            .OrderByDescending(slot => slot.WrittenUtc)
            .ThenBy(slot => slot.Id, StringComparer.Ordinal)
            .ToArray();
    }

    internal RuntimeSaveSlotMetadata Create(Action writeAuthoritativeSave) =>
        Create(Guid.NewGuid(), writeAuthoritativeSave);

    internal RuntimeSaveSlotMetadata Create(Guid slotId, Action writeAuthoritativeSave)
    {
        ArgumentNullException.ThrowIfNull(writeAuthoritativeSave);
        writeAuthoritativeSave();
        var bytes = File.ReadAllBytes(_canonicalSavePath);
        using var validated = Validate(bytes);
        Directory.CreateDirectory(_slotDirectory);
        var target = SlotPath(slotId);
        AtomicWrite(target, bytes);
        return ReadMetadata(target);
    }

    internal RuntimeSaveSlotMetadata Activate(string slotId, bool preserveCurrent = false)
    {
        if (slotId == "current") return ReadMetadata(_canonicalSavePath);
        if (!Guid.TryParseExact(slotId, "N", out var parsed))
            throw new InvalidOperationException("Save-slot identity is invalid.");
        var source = SlotPath(parsed);
        var bytes = File.ReadAllBytes(source);
        using var validated = Validate(bytes);
        if (preserveCurrent && File.Exists(_canonicalSavePath) && !File.ReadAllBytes(_canonicalSavePath).AsSpan().SequenceEqual(bytes))
            Create(() => { });
        AtomicWrite(_canonicalSavePath, bytes);
        return ReadMetadata(source);
    }

    private RuntimeSaveSlotMetadata ReadMetadata(string path)
    {
        var bytes = File.ReadAllBytes(path);
        using var document = Validate(bytes);
        var root = document.RootElement;
        var activeMap = root.TryGetProperty("activeMap", out var map) &&
            map.ValueKind == JsonValueKind.Object
                ? map
                : default;
        return new RuntimeSaveSlotMetadata(
            Path.GetFullPath(path).Equals(_canonicalSavePath, StringComparison.OrdinalIgnoreCase) ? "current" : Path.GetFileNameWithoutExtension(path),
            path,
            ReadString(root, "schema") ?? ReadString(root, "Schema")!,
            ReadString(root, "PlayerName") ?? ReadNestedString(root, "character", "Name") ??
                ReadNestedString(root, "character", "name"),
            activeMap.ValueKind == JsonValueKind.Object
                ? ReadString(activeMap, "mapId") ?? ReadString(activeMap, "mapName")
                : NativeCell(root),
            root.TryGetProperty("playerHitPoints", out var hitPoints) &&
                hitPoints.TryGetInt32(out var value)
                    ? value
                    : NativeHealth(root),
            File.GetLastWriteTimeUtc(path));
    }

    private JsonDocument Validate(byte[] bytes)
    {
        var document = JsonDocument.Parse(bytes);
        try
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                string.IsNullOrWhiteSpace(ReadString(root, "schema") ?? ReadString(root, "Schema")))
                throw new InvalidOperationException("Save slot has no authoritative schema identity.");
            _validate(root);
            return document;
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }

    private string SlotPath(Guid slotId) =>
        Path.Combine(_slotDirectory, slotId.ToString("N") + ".json");

    private static string? NativeCell(JsonElement root) =>
        root.TryGetProperty("ActiveCell", out var cell) && cell.ValueKind == JsonValueKind.Object &&
        ReadString(cell, "OwnerPlugin") is { } plugin && cell.TryGetProperty("ObjectId", out var id) && id.TryGetUInt32(out var number)
            ? $"{plugin}:{number:x6}" : null;

    private static int? NativeHealth(JsonElement root) =>
        root.TryGetProperty("Vitals", out var vitals) && vitals.ValueKind == JsonValueKind.Object &&
        vitals.TryGetProperty("HitPoints", out var health) && health.TryGetDouble(out var value) &&
        double.IsFinite(value) && value is >= 0 and <= int.MaxValue ? (int)Math.Ceiling(value) : null;

    private static string? ReadNestedString(JsonElement root, string owner, string name) =>
        root.TryGetProperty(owner, out var nested) && nested.ValueKind == JsonValueKind.Object
            ? ReadString(nested, name)
            : null;

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static void AtomicWrite(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
