using System.Security.Cryptography;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutReferenceSnapshot(FalloutFormKey Reference, FalloutFormKey Cell,
    FalloutFormKey Base, FalloutFormKey? Script, string? ScriptSha256,
    IReadOnlyDictionary<uint, double> Variables, string? ScriptError)
{
    internal static void Validate(IReadOnlyList<FalloutReferenceSnapshot> snapshots)
    {
        var seen = new HashSet<FalloutFormKey>();
        static bool ValidKey(FalloutFormKey key) => !string.IsNullOrWhiteSpace(key.OwnerPlugin) && key.ObjectId is > 0 and <= FalloutFormKey.ObjectIdMask;
        foreach (var snapshot in snapshots)
        {
            if (snapshot is null || !seen.Add(snapshot.Reference) || !ValidKey(snapshot.Reference) ||
                !ValidKey(snapshot.Cell) || !ValidKey(snapshot.Base) || snapshot.Variables is null ||
                snapshot.Variables.Values.Any(value => !double.IsFinite(value)) ||
                (snapshot.Script is null ? snapshot.ScriptSha256 is not null || snapshot.Variables.Count != 0 :
                    !ValidKey(snapshot.Script.Value) || snapshot.ScriptSha256 is not { Length: 64 } || !snapshot.ScriptSha256.All(Uri.IsHexDigit)))
                throw new InvalidDataException("Saved reference state is invalid or duplicated.");
        }
    }
}

internal sealed class FalloutReferenceInstance
{
    internal FalloutFormKey Reference { get; }
    internal FalloutFormKey Cell { get; }
    internal FalloutFormKey Base { get; }
    internal FalloutReferenceScriptDefinition? Script { get; }
    internal Dictionary<uint, double> Variables { get; }
    internal string? ScriptError { get; set; }

    internal FalloutReferenceInstance(FalloutPluginRecord reference, FalloutReferenceScriptDefinition? script)
    {
        if (reference.Signature is not ("REFR" or "ACHR" or "ACRE" or "PGRE" or "PMIS"))
            throw new InvalidDataException($"{reference.FormKey} is not a placed reference.");
        Reference = reference.FormKey;
        Cell = FalloutCellSceneReader.ParentCell(reference) ??
            throw new InvalidDataException($"Reference {Reference} has no source CELL.");
        Base = FalloutDialogueTopic.RequiredForm(reference, "NAME");
        Script = script;
        Variables = script?.Locals.Values.ToDictionary(index => index, _ => 0d) ?? [];
    }

    internal double Read(uint index) => Variables.TryGetValue(index, out var value) ? value :
        throw new NotSupportedException($"Reference {Reference} has no declared variable {index}.");

    internal void Write(uint index, double value)
    {
        _ = Read(index);
        if (!double.IsFinite(value)) throw new InvalidDataException("Reference variable is non-finite.");
        Variables[index] = value;
    }

    internal FalloutReferenceSnapshot Capture() => new(Reference, Cell, Base, Script?.Record.FormKey,
        Script?.Sha256, new Dictionary<uint, double>(Variables), ScriptError);
}

internal sealed class FalloutReferenceScriptDefinition(FalloutPluginRecord record)
{
    internal FalloutPluginRecord Record { get; } = record;
    internal string Sha256 { get; } = Convert.ToHexString(SHA256.HashData(record.ReadData())).ToLowerInvariant();
    internal IReadOnlyDictionary<string, uint> Locals { get; } = FalloutScriptLocals.Read(record);
}

// World lifetime is independent of draw/resource lifetime. A disabled or model-less
// reference still owns its script state. Unloading a cell suspends its residency;
// it cannot reset variables shared with scripts in other cells.
internal sealed class FalloutReferenceWorld(FalloutPluginStack records) : IDisposable
{
    private readonly Dictionary<FalloutFormKey, FalloutReferenceInstance> _instances = [];
    private readonly Dictionary<FalloutFormKey, FalloutReferenceScriptDefinition> _definitions = [];
    private readonly Dictionary<FalloutFormKey, IReadOnlyList<FalloutReferenceInstance>> _residentCells = [];
    private bool _disposed;

    internal int InstanceCount => _instances.Count;
    internal int ResidentCellCount => _residentCells.Count;
    internal int ScriptDefinitionCount => _definitions.Count;
    internal IEnumerable<FalloutReferenceInstance> ResidentInstances => _residentCells.Values.SelectMany(cell => cell);
    internal bool IsResident(FalloutFormKey reference) => _instances.TryGetValue(reference, out var instance) &&
        _residentCells.ContainsKey(instance.Cell);

    internal double ReadVariable(FalloutQuestState quests, FalloutFormKey owner, uint index) =>
        records.GetEffective(owner).Signature == "QUST" ? quests.Variable(owner, index) : Get(owner).Read(index);

    internal void WriteVariable(FalloutQuestState quests, FalloutFormKey owner, uint index, double value)
    {
        if (records.GetEffective(owner).Signature == "QUST") quests.SetVariable(owner, index, value);
        else Get(owner).Write(index, value);
    }

    internal IReadOnlyList<FalloutReferenceInstance> LoadCell(FalloutCellScene scene)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_residentCells.ContainsKey(scene.Cell.FormKey))
            throw new InvalidOperationException($"Cell {scene.Cell.FormKey} is already resident.");
        var instances = scene.References.Select(reference => Get(reference.FormKey)).ToArray();
        if (instances.Any(instance => instance.Cell != scene.Cell.FormKey) ||
            instances.Select(instance => instance.Reference).Distinct().Count() != instances.Length)
            throw new InvalidDataException("Resident cell has conflicting reference ownership.");
        _residentCells.Add(scene.Cell.FormKey, instances);
        return instances;
    }

    internal void UnloadCell(FalloutFormKey cell)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_residentCells.Remove(cell)) throw new InvalidOperationException($"Cell {cell} is not resident.");
    }

    internal FalloutReferenceInstance Get(FalloutFormKey key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_instances.TryGetValue(key, out var instance)) return instance;
        var record = records.GetEffective(key);
        var baseKey = FalloutDialogueTopic.RequiredForm(record, "NAME");
        // Engine primitive bases need no ESM base record; their XPRM reference
        // still has a real identity and lifetime. Other missing bases fail closed.
        var script = records.TryGetEffective(baseKey, out _) ? FalloutScriptLocals.AttachedScript(records, record) :
            record.ReadSubrecords().Any(field => field.Signature == "XPRM") ? null :
            throw new InvalidDataException($"Reference {key} has no winning base {baseKey}.");
        FalloutReferenceScriptDefinition? definition = null;
        if (script is not null && !_definitions.TryGetValue(script.FormKey, out definition))
            _definitions.Add(script.FormKey, definition = new(script));
        instance = new(record, definition);
        _instances.Add(key, instance);
        return instance;
    }

    internal IReadOnlyList<FalloutReferenceSnapshot> Capture()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _instances.Values.OrderBy(instance => records.RuntimeFormId(instance.Reference))
            .Select(instance => instance.Capture()).ToArray();
    }

    internal void Restore(IReadOnlyList<FalloutReferenceSnapshot> snapshots)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_instances.Count != 0) throw new InvalidOperationException("Reference restoration requires a fresh world.");
        FalloutReferenceSnapshot.Validate(snapshots);
        using var validated = new FalloutReferenceWorld(records);
        foreach (var snapshot in snapshots)
        {
            if (snapshot is null || validated._instances.ContainsKey(snapshot.Reference))
                throw new InvalidDataException("Saved reference is absent or duplicated.");
            var instance = validated.Get(snapshot.Reference);
            if (snapshot.Cell != instance.Cell || snapshot.Base != instance.Base || snapshot.Script != instance.Script?.Record.FormKey ||
                snapshot.ScriptSha256 != instance.Script?.Sha256 || snapshot.Variables is null ||
                !snapshot.Variables.Keys.Order().SequenceEqual(instance.Variables.Keys.Order()))
                throw new InvalidDataException($"Saved reference {snapshot.Reference} differs from its winning source declaration.");
            foreach (var (index, value) in snapshot.Variables) instance.Write(index, value);
            instance.ScriptError = snapshot.ScriptError;
        }
        foreach (var (key, instance) in validated._instances) _instances.Add(key, instance);
        foreach (var (key, definition) in validated._definitions) _definitions.Add(key, definition);
    }

    public void Dispose()
    {
        _residentCells.Clear();
        _instances.Clear();
        _definitions.Clear();
        _disposed = true;
    }
}
