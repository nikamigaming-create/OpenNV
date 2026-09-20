using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutReferenceSnapshot(FalloutFormKey Reference, FalloutFormKey Cell,
    FalloutFormKey Base, FalloutFormKey? Script, string? ScriptSha256,
    IReadOnlyDictionary<uint, double> Variables, string? ScriptError, bool? Enabled = null,
    FalloutReferenceEnableRequest? EnableRequest = null, float Opacity = 1, bool? NoFade = null,
    IReadOnlyDictionary<string, FalloutActorValue>? ActorValues = null, bool Destroyed = false, bool DeletePending = false, bool Deleted = false,
    FalloutReferenceInventorySnapshot? Inventory = null, bool Taken = false, bool DoorOpen = false, bool Unlocked = false,
    ulong? SoundRandomState = null, FalloutActorAnimationSnapshot? Animation = null, bool Unconscious = false,
    FalloutMapMarkerState? MapMarker = null, FalloutActorInjury? Injury = null, FalloutActorRagdollState? Ragdoll = null,
    FalloutActorEngagement? Engagement = null, FalloutActorTemplateSnapshot? Templates = null,
    FalloutReferencePlacement? Placement = null, bool Restrained = false, bool PlayerTeammate = false,
    bool TalkedToPlayer = false, FalloutActorPackageMotion? PackageMotion = null)
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
                snapshot.Deleted && snapshot.DeletePending ||
                !float.IsFinite(snapshot.Opacity) || snapshot.Opacity is < 0 or > 1 ||
                (snapshot.Script is null ? snapshot.ScriptSha256 is not null || snapshot.Variables.Count != 0 :
                    !ValidKey(snapshot.Script.Value) || snapshot.ScriptSha256 is not { Length: 64 } || !snapshot.ScriptSha256.All(Uri.IsHexDigit)))
                throw new InvalidDataException("Saved reference state is invalid or duplicated.");
            foreach (var (name, value) in snapshot.ActorValues ?? new Dictionary<string, FalloutActorValue>())
                if ((name != "health" && FalloutActorValue.UserSlot(name) != name) || value is null || !value.IsFinite)
                    throw new InvalidDataException("Saved actor value is invalid.");
            if (snapshot.Animation is { } animation) FalloutActorAnimationState.Validate(animation);
            snapshot.Placement?.Validate();
            snapshot.Engagement?.Validate();
            snapshot.PackageMotion?.Validate();
            if (snapshot.Ragdoll is { } ragdoll)
            {
                ragdoll.Validate();
                if (snapshot.Injury?.Dead != true) throw new InvalidDataException("Living reference has a saved death ragdoll.");
                if (!(ragdoll.Cuts ?? []).Select(cut => cut.Part).Order().SequenceEqual((snapshot.Injury.SeveredParts ?? []).Order()))
                    throw new InvalidDataException("Saved cut poses disagree with the severed source limbs.");
            }
        }
    }
}

internal sealed class FalloutReferenceInstance
{
    internal FalloutFormKey Reference { get; }
    internal FalloutFormKey Cell { get; }
    internal FalloutFormKey Base { get; }
    internal FalloutReferenceScriptDefinition? Script { get; private set; }
    internal FalloutActorTemplateSelection? Templates { get; set; }
    internal Dictionary<uint, double> Variables { get; }
    internal string? ScriptError { get; set; }
    internal bool Enabled { get; set; }
    internal FalloutReferenceEnableRequest? EnableRequest { get; set; }
    internal float Opacity { get; set; } = 1;
    internal bool NoFade { get; set; }
    internal bool Destroyed { get; set; }
    internal bool DeletePending { get; set; }
    internal bool Deleted { get; set; }
    internal bool Taken { get; set; }
    internal bool DoorOpen { get; set; }
    internal bool Unlocked { get; set; }
    internal bool Unconscious { get; set; }
    internal bool Restrained { get; set; }
    internal bool PlayerTeammate { get; set; }
    internal bool TalkedToPlayer { get; set; }
    internal FalloutActorPackageMotion? PackageMotion { get; set; }
    internal FalloutReferencePlacement? Placement { get; set; }
    internal long PlacementRevision { get; set; }
    internal FalloutReferenceInventory? Inventory { get; set; }
    internal Dictionary<string, FalloutActorValue> ActorValues { get; } = [];
    internal FalloutReferenceEnableParent? EnableParent { get; }
    private FalloutSoundRandomState? _soundRandom;
    internal FalloutSoundRandomState SoundRandom => _soundRandom ??= new(
        BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong))));
    internal FalloutActorAnimationState Animation { get; } = new();
    internal FalloutMapMarkerState? MapMarker { get; set; }
    internal FalloutActorInjury? Injury { get; set; }
    internal FalloutActorRagdollState? Ragdoll { get; set; }
    internal Func<FalloutActorRagdollState>? CaptureRagdoll { get; set; }
    internal FalloutActorEngagement? Engagement { get; set; }
    internal Func<FalloutActorEngagement?>? CaptureEngagement { get; set; }

    internal FalloutReferenceInstance(FalloutPluginRecord reference, FalloutReferenceScriptDefinition? script)
    {
        if (reference.Signature is not ("REFR" or "ACHR" or "ACRE" or "PGRE" or "PMIS"))
            throw new InvalidDataException($"{reference.FormKey} is not a placed reference.");
        Reference = reference.FormKey;
        Cell = FalloutCellSceneReader.ParentCell(reference) ??
            throw new InvalidDataException($"Reference {Reference} has no source CELL.");
        Base = FalloutDialogueTopic.RequiredForm(reference, "NAME");
        Script = script;
        Enabled = (reference.Flags & 0x800) == 0;
        NoFade = (reference.Flags & 0x08000000) != 0;
        EnableParent = FalloutReferenceEnableParent.Read(reference);
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

    internal void BindTemplateScript(FalloutReferenceScriptDefinition? script)
    {
        if (Script?.Record.FormKey == script?.Record.FormKey) return;
        if (Variables.Values.Any(value => value != 0) || ScriptError is not null)
            throw new InvalidDataException("Cannot replace an actor script after its execution has started.");
        Script = script; Variables.Clear();
        foreach (var index in script?.Locals.Values ?? []) Variables.Add(index, 0);
    }

    internal FalloutReferenceSnapshot Capture() => new(Reference, Cell, Base, Script?.Record.FormKey,
        Script?.Sha256, new Dictionary<uint, double>(Variables), ScriptError, Enabled, EnableRequest, Opacity, NoFade,
        new Dictionary<string, FalloutActorValue>(ActorValues), Destroyed, DeletePending, Deleted, Inventory?.Capture(), Taken, DoorOpen, Unlocked,
        _soundRandom?.State, Animation.Capture(), Unconscious, MapMarker,
        Injury is null ? null : Injury with { LimbDamage = new Dictionary<byte, float>(Injury.LimbDamage) }, CaptureRagdoll?.Invoke() ?? Ragdoll,
        CaptureEngagement?.Invoke() ?? Engagement, Templates?.Capture(), Placement?.Copy(), Restrained, PlayerTeammate,
        TalkedToPlayer, PackageMotion);
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
internal sealed partial class FalloutReferenceWorld(FalloutPluginStack records) : IDisposable
{
    private readonly Dictionary<FalloutFormKey, FalloutReferenceInstance> _instances = [];
    private readonly Dictionary<FalloutFormKey, FalloutReferenceScriptDefinition> _definitions = [];
    private readonly Dictionary<FalloutFormKey, IReadOnlyList<FalloutReferenceInstance>> _residentCells = [];
    private readonly Dictionary<FalloutFormKey, int> _residentReferences = [];
    private bool _disposed;

    internal int InstanceCount => _instances.Count;
    internal int ResidentCellCount => _residentCells.Count;
    internal int ScriptDefinitionCount => _definitions.Count;
    internal IEnumerable<FalloutReferenceInstance> ResidentInstances => _residentReferences.Keys.Select(key => _instances[key]);
    internal bool IsResident(FalloutFormKey reference) => _residentReferences.ContainsKey(reference);
    internal bool CanActivate(FalloutFormKey reference) => IsEnabled(reference) && Get(reference).Templates?.Absent != true &&
        Get(reference) is { Destroyed: false, DeletePending: false, Deleted: false };

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
        foreach (var instance in instances.Where(instance => instance.DeletePending && !_residentReferences.ContainsKey(instance.Reference)))
        { instance.Deleted = true; instance.DeletePending = false; }
        // Exterior residency includes persistent references whose source parent
        // is the world cell. Source ancestry remains unchanged in every instance.
        if (instances.Where((instance, index) => instance.Cell != scene.References[index].Cell).Any() ||
            instances.Select(instance => instance.Reference).Distinct().Count() != instances.Length)
            throw new InvalidDataException("Resident cell has conflicting reference ownership.");
        _residentCells.Add(scene.Cell.FormKey, instances);
        foreach (var instance in instances)
            _residentReferences[instance.Reference] = _residentReferences.GetValueOrDefault(instance.Reference) + 1;
        return instances;
    }

    internal void UnloadCell(FalloutFormKey cell)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_residentCells.Remove(cell, out var instances)) throw new InvalidOperationException($"Cell {cell} is not resident.");
        foreach (var instance in instances)
        {
            var remaining = _residentReferences[instance.Reference] - 1;
            if (remaining > 0) { _residentReferences[instance.Reference] = remaining; continue; }
            _residentReferences.Remove(instance.Reference);
            if (instance.DeletePending) { instance.Deleted = true; instance.DeletePending = false; }
        }
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

    internal FalloutActorTemplateSelection InitializeActorTemplates(FalloutFormKey reference, int level,
        FalloutGlobalState? globals = null)
    {
        var instance = Actor(reference);
        if (instance.Templates is { } retained) return retained;
        var selection = new FalloutActorTemplateSelection(level,
            BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong))), globals is null ? null : globals.Get);
        selection.ResolveAll(records, instance.Base);
        var scaled = !selection.Absent && (System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(
            FalloutActorTemplateOwner.Resolve(records, records.GetEffective(instance.Base), 2, selection)
                .ReadSubrecords().Single(field => field.Signature == "ACBS").Data.Span) & 0x80) != 0;
        if ((selection.Capture().Choices.Count != 0 || scaled) &&
            new[] { reference, instance.Placement?.Cell ?? instance.Cell }.Select(records.GetEffective).SelectMany(record => record.ReadSubrecords())
                .Any(field => field.Signature == "XEZN" &&
                    (field.Data.Length != 4 || System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span) != 0)))
            throw new NotSupportedException($"Actor {reference} requires its persistent encounter-zone level owner.");
        instance.Templates = selection;
        BindTemplateScript(instance);
        return selection;
    }

    private void BindTemplateScript(FalloutReferenceInstance instance)
    {
        if (instance.Templates is null || instance.Templates.Absent) return;
        var owner = FalloutActorTemplateOwner.Resolve(records, records.GetEffective(instance.Base), 512, instance.Templates);
        var script = FalloutScriptLocals.AttachedScript(records, owner);
        FalloutReferenceScriptDefinition? definition = null;
        if (script is not null && !_definitions.TryGetValue(script.FormKey, out definition))
            _definitions.Add(script.FormKey, definition = new(script));
        instance.BindTemplateScript(definition);
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
            if (snapshot.Templates is { } templates)
            {
                instance.Templates = new(templates);
                instance.Templates.ResolveAll(records, instance.Base);
                validated.BindTemplateScript(instance);
            }
            if (snapshot.Cell != instance.Cell || snapshot.Base != instance.Base || snapshot.Script != instance.Script?.Record.FormKey ||
                snapshot.ScriptSha256 != instance.Script?.Sha256 || snapshot.Variables is null ||
                !snapshot.Variables.Keys.Order().SequenceEqual(instance.Variables.Keys.Order()))
                throw new InvalidDataException($"Saved reference {snapshot.Reference} differs from its winning source declaration.");
            foreach (var (index, value) in snapshot.Variables) instance.Write(index, value);
            // Older builds incorrectly retained failed engine default actions
            // as source-program faults even on objects with no script.
            // Parsing executes no source statements. Retry it after a cold
            // load so a parser correction can recover an existing save.
            // Reached execution failures retain their applied prefix/error.
            instance.ScriptError = instance.Script is null ||
                snapshot.ScriptError?.StartsWith("Parse:", StringComparison.OrdinalIgnoreCase) == true
                ? null : snapshot.ScriptError;
            instance.Enabled = snapshot.Enabled ?? instance.Enabled;
            instance.EnableRequest = snapshot.EnableRequest;
            instance.Opacity = snapshot.Opacity;
            instance.NoFade = snapshot.NoFade ?? instance.NoFade;
            instance.Destroyed = snapshot.Destroyed;
            instance.DeletePending = snapshot.DeletePending;
            instance.Deleted = snapshot.Deleted;
            instance.Taken = snapshot.Taken;
            instance.DoorOpen = snapshot.DoorOpen;
            instance.Unlocked = snapshot.Unlocked;
            if (snapshot.Placement is { } placement)
            {
                if (records.GetEffective(placement.Cell).Signature != "CELL") throw new InvalidDataException("Saved placement has no winning CELL.");
                instance.Placement = placement.Copy();
            }
            if (snapshot.Restrained || snapshot.PlayerTeammate || snapshot.TalkedToPlayer || snapshot.PackageMotion is not null)
                _ = validated.Actor(snapshot.Reference);
            instance.Restrained = snapshot.Restrained;
            instance.PlayerTeammate = snapshot.PlayerTeammate;
            instance.TalkedToPlayer = snapshot.TalkedToPlayer;
            if (snapshot.PackageMotion is { } motion)
            {
                var package = records.GetEffective(motion.Package);
                if (package.Signature != "PACK" || !Convert.ToHexString(SHA256.HashData(package.ReadData()))
                    .Equals(motion.PackageSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Saved package motion differs from the winning package.");
                instance.PackageMotion = motion with { Position = (float[])motion.Position.Clone(), Rotation = (float[])motion.Rotation.Clone() };
            }
            if (snapshot.SoundRandomState is { } soundRandom) instance.SoundRandom.Restore(soundRandom);
            if (snapshot.Animation is { } animation) instance.Animation.Restore(animation);
            if (snapshot.Unconscious) validated.SetUnconscious(snapshot.Reference, true);
            if (snapshot.MapMarker is { } mapMarker)
            {
                _ = FalloutMapMarker.Read(records.GetEffective(snapshot.Reference));
                instance.MapMarker = mapMarker;
            }
            if (snapshot.Inventory is { } inventory)
            {
                _ = validated.InventoryOwner(snapshot.Reference);
                instance.Inventory = new();
                instance.Inventory.Restore(inventory, records);
            }
            if (snapshot.ActorValues is { Count: > 0 } values)
            {
                _ = validated.Actor(snapshot.Reference);
                foreach (var (name, value) in values) instance.ActorValues.Add(name, value);
            }
            if (snapshot.Injury is { } injury) validated.RestoreInjury(instance, injury);
            else if (instance.ActorValues.ContainsKey("health")) throw new InvalidDataException("Saved health has no actor injury state.");
            instance.Ragdoll = snapshot.Ragdoll;
            instance.Engagement = snapshot.Engagement;
            if (instance.EnableRequest is not null && instance.EnableParent is not null)
                throw new InvalidDataException("Saved child reference has an independent enable request.");
        }
        foreach (var (key, instance) in validated._instances) _instances.Add(key, instance);
        foreach (var (key, definition) in validated._definitions) _definitions.Add(key, definition);
    }

    public void Dispose()
    {
        _residentCells.Clear();
        _residentReferences.Clear();
        _instances.Clear();
        _definitions.Clear();
        _healthSources.Clear();
        _bodyParts.Clear();
        _disposed = true;
    }
}
