using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutPlayerStatusValuesSnapshot(string Schema, FalloutDefaultStatusActorValues Source,
    IReadOnlyDictionary<int, FalloutActorValue> Values);

internal sealed partial class FalloutPlayerActorValues
{
    private const string StatusSchema = "opennv-player-status-values/v1";
    private FalloutDefaultStatusActorValues? _statusSource;
    private Dictionary<int, FalloutActorValue> _statusValues = [];
    private void InitializeStatusValues(FalloutPluginStack records, FalloutDefaultStatusActorValues? declaration)
    {
        if (records.OwnedSource is { Game: RuntimeLiveContentSource.Fallout3Game } content)
        {
            var actual = FalloutExecutableStringTable.ReadDefaultStatusActorValues(content.FalloutExecutablePath);
            if (declaration is not null && declaration.Identity != actual.Identity)
                throw new InvalidDataException("Owned player status source cannot be replaced by a different declaration.");
            declaration = actual;
        }
        if (declaration is null) return;
        declaration.Validate(); _statusSource = declaration;
        _statusValues = declaration.Values.ToDictionary(value => value.Index, _ => new FalloutActorValue(0));
    }
    internal bool IsStatusValue(int value) => _statusValues.ContainsKey(value);
    internal bool IsStatusValue(string name) => IsMoverValue(name) || _statusSource?.Values.Any(value =>
        value.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) == true;
    internal int StatusValue(string name) => IsMoverValue(name) ? MoverValue(name) : _statusSource?.Values.SingleOrDefault(value =>
        value.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Index ??
        throw new NotSupportedException("Player status value has no selected source descriptor: " + name);
    private float ReadCurrentFloat32(int value)
    {
        // Each addition returns to the selected Player's actual Float32 cell.
        var temporary = Pool(value, FalloutActorValuePool.Temporary);
        var permanent = Pool(value, FalloutActorValuePool.Permanent);
        var damage = Pool(value, FalloutActorValuePool.Damage);
        var current = ReadBase(value) + temporary;
        current += permanent;
        return current + damage;
    }
    private FalloutPlayerStatusValuesSnapshot? CaptureStatusValues() => _statusSource is null ? null :
        new(StatusSchema, _statusSource, _statusValues.Where(pair => _moverSource is null || !MoverValueIds.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value));
    private Dictionary<int, FalloutActorValue> RestoreStatusValues(FalloutPlayerStatusValuesSnapshot? saved)
    {
        if (saved is null)
        {
            if (_statusSource is not null)
                throw new InvalidDataException("Cold player omitted its independent status-value pools.");
            return [];
        }
        ValidateStatusValues(saved);
        if (_statusSource is null || saved.Source.Identity != _statusSource.Identity)
            throw new InvalidDataException("Cold player status values changed their selected executable/default declaration.");
        return saved.Values.ToDictionary(pair => pair.Key, pair => pair.Value);
    }
    private static void ValidateStatusValues(FalloutPlayerStatusValuesSnapshot saved)
    {
        if (saved.Schema != StatusSchema || saved.Source is null || saved.Values is null)
            throw new InvalidDataException("Saved player status values omitted their source/pools.");
        saved.Source.Validate();
        if (saved.Values.Count != saved.Source.Values.Count || saved.Source.Values.Any(source => !saved.Values.ContainsKey(source.Index)) ||
            saved.Values.Any(pair => pair.Value is null || !pair.Value.IsFinite))
            throw new InvalidDataException("Saved player status values invented or omitted a declared mutable pool.");
    }
}
