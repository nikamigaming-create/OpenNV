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
    private readonly Dictionary<FalloutFormKey, FalloutReputationValueSnapshot> _reputations = [];

    private string ReputationSource(FalloutFormKey form)
    {
        var source = records.GetEffective(form);
        var data = source.ReadSubrecords().Where(field => field.Signature == "DATA").ToArray();
        if (source.Signature != "REPU" || data.Length != 1 || data[0].Data.Length != 4 ||
            !float.IsFinite(BinaryPrimitives.ReadSingleLittleEndian(data[0].Data.Span)))
            throw new InvalidDataException("Reputation requires a winning REPU with finite four-byte DATA.");
        return RecordHash(source);
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
        _ = ReputationSource(form);
        _reputations.TryGetValue(form, out var state);
        return (positive ? state?.Positive : state?.Negative) ?? (_freshReputationBaseline ? 0 :
            throw new NotSupportedException("Saved campaign has no authoritative history for this reputation axis."));
    }

    internal void SetReputation(FalloutFormKey form, double axis, double amount)
    {
        var positive = ReputationAxis(axis);
        if (!double.IsFinite(amount) || !float.IsFinite((float)amount))
            throw new InvalidDataException("Reputation value is not a finite source float.");
        var hash = ReputationSource(form);
        var previous = _reputations.GetValueOrDefault(form) ?? new(form, hash, null, null);
        // The source setter writes one axis directly: no clamp or AddReputation feedback.
        // On a legacy campaign, setting one axis does not invent the other axis's history.
        _reputations[form] = positive ? previous with { Positive = (float)amount } : previous with { Negative = (float)amount };
    }

    internal FalloutReputationsSnapshot CaptureReputations() => new(_freshReputationBaseline,
        _reputations.Values.OrderBy(value => value.Form.ToString(), StringComparer.Ordinal).ToArray());

    internal static void ValidateReputations(FalloutReputationsSnapshot snapshot)
    {
        if (snapshot.Values is null) throw new InvalidDataException("Saved reputation values are absent.");
        var seen = new HashSet<FalloutFormKey>();
        foreach (var value in snapshot.Values)
            if (value is null || !seen.Add(value.Form) || string.IsNullOrWhiteSpace(value.Form.OwnerPlugin) ||
                value.Form.ObjectId is 0 or > FalloutFormKey.ObjectIdMask ||
                value.SourceSha256 is not { Length: 64 } || !value.SourceSha256.All(Uri.IsHexDigit) ||
                value.Positive is null && value.Negative is null ||
                value.Positive is { } positive && !float.IsFinite(positive) ||
                value.Negative is { } negative && !float.IsFinite(negative))
                throw new InvalidDataException("Saved reputation identity, axis or value is invalid or duplicated.");
    }

    internal void RestoreReputations(FalloutReputationsSnapshot? snapshot)
    {
        var admitted = new Dictionary<FalloutFormKey, FalloutReputationValueSnapshot>();
        if (snapshot is not null)
        {
            ValidateReputations(snapshot);
            foreach (var value in snapshot.Values)
            {
                if (!ReputationSource(value.Form).Equals(value.SourceSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Saved reputation differs from its winning source.");
                admitted.Add(value.Form, value);
            }
        }
        _reputations.Clear();
        foreach (var (form, value) in admitted) _reputations.Add(form, value);
        _freshReputationBaseline = snapshot?.FreshBaseline == true;
    }
}
