using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutPlayerMoverActorValuesSnapshot(FalloutPlayerMoverActorValueSource Source,
    IReadOnlyDictionary<int, FalloutActorValue> Values, IReadOnlyList<int> BaseOverrides);

internal sealed partial class FalloutPlayerActorValues
{
    private FalloutPlayerMoverActorValueSource? _moverSource;
    private FalloutPluginStack? _moverRecords;
    private readonly HashSet<int> _moverBaseOverrides = [];
    internal void BindLoadedMoverValues(FalloutPlayerMoverActorValueSource source, FalloutPluginStack records, FalloutPluginRecord actualAssignedBase)
    {
        source.Validate();
        if (source.Player.Player != _source.Player || source.Player.PlayerSha256 != _source.PlayerSha256 ||
            source.Player.StatsOwner != _source.StatsOwner || source.Player.StatsSha256 != _source.StatsSha256 ||
            actualAssignedBase.FormKey != _source.Player || actualAssignedBase.Signature != "NPC_" ||
            FalloutPlayerMoverActorValueSource.RecordHash(actualAssignedBase) != _source.PlayerSha256)
            throw new InvalidDataException("Mover AVs lost the real loaded Player/Base/stats source association.");
        if (_moverSource is not null)
        {
            if (_moverSource.Identity != source.Identity) throw new InvalidDataException("Mover AVs changed actual current source/setting inputs.");
            _moverRecords = records; return;
        }
        if (_statusSource is null || MoverValueIds.Any(_statusValues.ContainsKey))
            throw new InvalidDataException("Mover AVs attempted to shadow existing Player value fields or bind without selected status pools.");
        _moverSource = source; _moverRecords = records;
        _statusValues.Add(21, new(source.Speed));
        _statusValues.Add(29, new(BitConverter.UInt32BitsToSingle(source.BodyConditionBits)));
        _statusValues.Add(30, new(BitConverter.UInt32BitsToSingle(source.BodyConditionBits)));
        _statusValues.Add(72, new(0));
    }
    private static readonly int[] MoverValueIds = [21, 29, 30, 72];
    private static readonly IReadOnlyDictionary<string, int> MoverNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["SpeedMult"] = 21,
        ["LeftMobilityCondition"] = 29,
        ["RightMobilityCondition"] = 30,
        ["IgnoreCrippledLimbs"] = 72
    };
    private bool IsMoverValue(string name) => _moverSource is not null && MoverNames.ContainsKey(name);
    private int MoverValue(string name) => _moverSource is not null && MoverNames.TryGetValue(name, out var value) ? value :
        throw new NotSupportedException("Player mover AV has no actual loaded source: " + name);
    private bool MoverValueHasCallback(int value) => _moverSource is not null && value is 29 or 30 or 72;
    private FalloutPlayerMoverActorValuesSnapshot? CaptureMoverValues() => _moverSource is null ? null :
        new(_moverSource, MoverValueIds.ToDictionary(value => value, value => _statusValues[value]), _moverBaseOverrides.Order().ToArray());
    private float ReadMoverBase(int value)
    {
        if (_moverSource is null || !MoverValueIds.Contains(value) || _moverBaseOverrides.Contains(value)) return Value(value).Base;
        if (value is 29 or 30) return FalloutGameSettingFloats.Read(_moverRecords ??
            throw new NotSupportedException("source-cold-mover-AV-current-GMST-records-binding-unavailable"),
            "fAVDDefaultBodyPartCondition");
        return value == 21 ? _moverSource.Speed : 0;
    }
    private void MarkMoverBaseOverride(int value)
    { if (_moverSource is not null && MoverValueIds.Contains(value)) _moverBaseOverrides.Add(value); }
    private void RestoreMoverValues(FalloutPlayerMoverActorValuesSnapshot? saved)
    {
        _moverBaseOverrides.Clear();
        if (saved is null) { _moverSource = null; _moverRecords = null; return; }
        ValidateMoverValues(saved);
        if (saved.Source.Player.Player != _source.Player || saved.Source.Player.PlayerSha256 != _source.PlayerSha256 ||
            saved.Source.Player.StatsOwner != _source.StatsOwner || saved.Source.Player.StatsSha256 != _source.StatsSha256)
            throw new InvalidDataException("Cold mover AVs changed their actual inherited Player stats.");
        _moverSource = saved.Source;
        _moverRecords = null; _moverBaseOverrides.UnionWith(saved.BaseOverrides);
        foreach (var value in MoverValueIds)
            if (!_statusValues.TryAdd(value, saved.Values[value]))
                throw new InvalidDataException("Cold mover AVs duplicated another existing mutable value.");
    }
    private static void ValidateMoverValues(FalloutPlayerMoverActorValuesSnapshot saved)
    {
        saved.Source.Validate();
        if (saved.Values is null || saved.Values.Count != MoverValueIds.Length || saved.BaseOverrides is null ||
            saved.BaseOverrides.Distinct().Count() != saved.BaseOverrides.Count || saved.BaseOverrides.Any(value => !MoverValueIds.Contains(value)) ||
            MoverValueIds.Any(value => !saved.Values.TryGetValue(value, out var fields) || fields is null || !fields.IsFinite))
            throw new InvalidDataException("Mover AV snapshot omitted real base/modifier pools or fabricated a current value.");
    }
}
