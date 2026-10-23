using System.Buffers.Binary;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutReputationValueSnapshot(FalloutFormKey Form,
    string SourceSha256, float? Positive, float? Negative);
internal sealed record FalloutReputationsSnapshot(bool FreshBaseline,
    IReadOnlyList<FalloutReputationValueSnapshot> Values);

internal sealed partial class FalloutReferenceWorld
{
    private bool _freshReputationBaseline = true;
    private readonly Dictionary<FalloutFormKey, FalloutReputationValueSnapshot> _reputations = new(FalloutFormKeyComparer.Instance);

    private (FalloutFormKey Form, string Sha256, float Capacity) ReputationSource(FalloutFormKey form)
    {
        var source = records.GetEffective(form);
        var data = source.ReadSubrecords().Where(field => field.Signature == "DATA").ToArray();
        if (source.Signature != "REPU" || data.Length != 1 || data[0].Data.Length != 4 ||
            !float.IsFinite(BinaryPrimitives.ReadSingleLittleEndian(data[0].Data.Span)))
            throw new InvalidDataException("Reputation requires a winning REPU with finite four-byte DATA.");
        return (source.FormKey, RecordHash(source), BinaryPrimitives.ReadSingleLittleEndian(data[0].Data.Span));
    }

    private static bool ReputationAxis(double axis) => axis switch
    {
        0 => false,
        1 => true,
        _ => throw new InvalidDataException("Reputation axis must be zero or one."),
    };

    internal float Reputation(FalloutFormKey form, double axis)
    {
        var positive = ReputationAxis(axis);
        var source = ReputationSource(form);
        _reputations.TryGetValue(source.Form, out var state);
        return (positive ? state?.Positive : state?.Negative) ?? (_freshReputationBaseline ? 0 :
            throw new NotSupportedException("Saved campaign has no authoritative history for this reputation axis."));
    }

    internal float? EvaluateReputationCondition(FalloutCondition condition)
    {
        if (condition.Function is not (573 or 574 or 575)) return null;
        if (condition.RunOn > 2)
            throw new NotSupportedException("Reputation condition run-on context is unbound.");
        if (condition.RunOn == 2 && condition.Reference != 0)
        {
            var reference = condition.Owner.Plugin.AdjustFormId(condition.Reference);
            if (records.RuntimeFormId(reference) != 0x14 &&
                (!records.TryGetEffective(reference, out var placed) || placed.Signature is not ("REFR" or "ACHR" or "ACRE")))
                throw new InvalidDataException("Reputation condition explicit context is not an actual placed reference.");
        }
        // CTDA uses the raw getter, unlike the script command's validated selector.
        // Its explicit REPU argument is independent of speaker/listener identity.
        if (condition.Function == 575)
            return ReputationThreshold(condition.FormArgument1, unchecked((byte)condition.Argument2));
        var value = Reputation(condition.FormArgument1, condition.Argument2 == 1 ? 1 : 0);
        if (condition.Function == 573) return value;
        return ReputationRatio(condition.FormArgument1, value);
    }

    private float ReputationRatio(FalloutFormKey form, float value)
    {
        // The source percentage getter stores the axis/capacity ratio as float32.
        // It neither scales by 100 nor clamps values written by SetReputation.
        var ratio = value / ReputationSource(form).Capacity;
        return float.IsFinite(ratio) ? ratio :
            throw new InvalidDataException("Reputation percentage has no finite source ratio.");
    }

    private int ReputationTier(FalloutFormKey form, int axis)
    {
        var ratio = ReputationRatio(form, Reputation(form, axis));
        float Read(string name) => FalloutGameSettingFloats.Read(records, name);
        if (ratio >= Read("fReputationThresholdOne") && ratio < Read("fReputationThresholdTwo")) return 1;
        if (ratio >= Read("fReputationThresholdTwo") && ratio < Read("fReputationThresholdThree")) return 2;
        return ratio >= Read("fReputationThresholdThree") ? 3 : 0;
    }

    private int ReputationThreshold(FalloutFormKey form, byte selector)
    {
        // Both axes are read in source order, including when history is unknown.
        var positive = ReputationTier(form, 1);
        var negative = ReputationTier(form, 0);
        if (positive == 0 && negative == 0) return 1;
        return (selector, positive, negative) switch
        {
            (0, 1, 1) => 3,
            (0, 3, 2) or (0, 2, 3) => 2,
            (0, 3, 3) => 5,
            (0, 2, 2) => 4,
            (1, 1, 0) => 4,
            (1, 2, 0) => 5,
            (1, 2, 1) => 2,
            (1, 3, 0) => 6,
            (1, 3, 1) => 3,
            (2, 0, 1) => 4,
            (2, 0, 2) => 5,
            (2, 1, 2) => 2,
            (2, 0, 3) => 6,
            (2, 1, 3) => 3,
            _ => 0,
        };
    }

    internal void SetReputation(FalloutFormKey form, double axis, double amount)
    {
        var positive = ReputationAxis(axis);
        if (!double.IsFinite(amount) || !float.IsFinite((float)amount))
            throw new InvalidDataException("Reputation value is not a finite source float.");
        var source = ReputationSource(form);
        var previous = _reputations.GetValueOrDefault(source.Form) ?? new(source.Form, source.Sha256, null, null);
        // The source setter writes one axis directly: no clamp or AddReputation feedback.
        // On a legacy campaign, setting one axis does not invent the other axis's history.
        _reputations[source.Form] = positive ? previous with { Positive = (float)amount } : previous with { Negative = (float)amount };
    }

    internal FalloutReputationsSnapshot CaptureReputations() => new(_freshReputationBaseline,
        _reputations.Values.OrderBy(value => value.Form.ToString(), StringComparer.Ordinal).ToArray());

    internal static void ValidateReputations(FalloutReputationsSnapshot snapshot)
    {
        if (snapshot.Values is null) throw new InvalidDataException("Saved reputation values are absent.");
        var seen = new HashSet<FalloutFormKey>(FalloutFormKeyComparer.Instance);
        foreach (var value in snapshot.Values)
            if (value is null || string.IsNullOrWhiteSpace(value.Form.OwnerPlugin) ||
                value.Form.ObjectId is 0 or > FalloutFormKey.ObjectIdMask || !seen.Add(value.Form) ||
                value.SourceSha256 is not { Length: 64 } || !value.SourceSha256.All(Uri.IsHexDigit) ||
                value.Positive is null && value.Negative is null ||
                value.Positive is { } positive && !float.IsFinite(positive) ||
                value.Negative is { } negative && !float.IsFinite(negative))
                throw new InvalidDataException("Saved reputation identity, axis or value is invalid or duplicated.");
    }

    internal void RestoreReputations(FalloutReputationsSnapshot? snapshot)
    {
        var admitted = new Dictionary<FalloutFormKey, FalloutReputationValueSnapshot>(FalloutFormKeyComparer.Instance);
        if (snapshot is not null)
        {
            ValidateReputations(snapshot);
            foreach (var value in snapshot.Values)
            {
                var source = ReputationSource(value.Form);
                if (!source.Sha256.Equals(value.SourceSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Saved reputation differs from its winning source.");
                admitted.Add(source.Form, value with { Form = source.Form });
            }
        }
        _reputations.Clear();
        foreach (var (form, value) in admitted) _reputations.Add(form, value);
        _freshReputationBaseline = snapshot?.FreshBaseline == true;
    }
}
