using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutGlobalState
{
    private readonly FalloutPluginStack? _conditionRecords;
    private readonly FalloutPluginStackLoadReceipt? _conditionReader;
    private readonly HashSet<FalloutPlugin>? _conditionPlugins;

    private FalloutGlobalState(FalloutPluginStack records, IEnumerable<FalloutGlobal> sources) : this(sources)
    {
        _conditionReader = records.LoadReceipt;
        records.RequireLiveLoadReceipt(_conditionReader);
        _conditionRecords = records;
        _conditionPlugins = new(records.Plugins.Select(context => context.Plugin), ReferenceEqualityComparer.Instance);
    }

    // No condition result or decoded global value is cached. The complete reader
    // owns immutable form normalization; this same global owner supplies Set/Get.
    internal float ReadConditionComparison(FalloutCondition condition)
    {
        var records = _conditionRecords ?? throw new NotSupportedException(
            "CTDA comparison-global requires globals constructed from the actual plugin stack.");
        records.RequireLiveLoadReceipt(_conditionReader ?? throw new InvalidDataException(
            "CTDA comparison-global lost its actual returned plugin reader."));
        if (!condition.UsesComparisonGlobal || condition.Comparison != 0 || condition.GlobalComparison is not { } operand ||
            _conditionPlugins?.Contains(condition.Owner.Plugin) != true)
            throw new InvalidDataException("CTDA comparison-global belongs to another operand or plugin lifetime.");
        var form = condition.Owner.Plugin.AdjustOptionalFormId(operand.RawFormId) ??
            throw new InvalidDataException("CTDA comparison-global has a null source FormID.");
        if (!records.TryGetEffective(form, out var record) || record.Signature != "GLOB" || !_sources.ContainsKey(form))
            throw new InvalidDataException($"CTDA comparison-global {form} has no winning live GLOB.");
        var value = Get(form);
        return float.IsFinite(value) ? value : throw new InvalidDataException("CTDA comparison-global is non-finite.");
    }
}
