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

    private (FalloutFormKey Form, string Sha256) ReputationSource(FalloutFormKey form)
    {
        var source = records.GetEffective(form);
        var data = source.ReadSubrecords().Where(field => field.Signature == "DATA").ToArray();
        if (source.Signature != "REPU" || data.Length != 1 || data[0].Data.Length != 4 ||
            !float.IsFinite(BinaryPrimitives.ReadSingleLittleEndian(data[0].Data.Span)))
            throw new InvalidDataException("Reputation requires a winning REPU with finite four-byte DATA.");
        return (source.FormKey, RecordHash(source));
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
