using Godot;

namespace OpenNV.Runtime.World.Interactions;

internal sealed partial class FurnitureInstance : Node3D
{
    private Marker _marker;

    internal string ReferenceFormId { get; private set; } = "";
    internal string DisplayName { get; private set; } = "";
    internal int MarkerId => _marker.Id;

    internal void Configure(
        string referenceFormId,
        string displayName,
        IReadOnlyList<Marker> markers,
        float unitsToMeters)
    {
        if (string.IsNullOrWhiteSpace(referenceFormId) ||
            markers.Count == 0 ||
            markers.Any(marker =>
                marker.Id < 0 ||
                !marker.OffsetGodotGameUnits.IsFinite() ||
                !marker.RotationGodot.IsNormalized()) ||
            !float.IsFinite(unitsToMeters) || unitsToMeters <= 0.0f)
            throw new InvalidOperationException("Owned furniture interaction is invalid.");
        var usable = markers
            .Where(marker => marker.AnimationType > 0)
            .OrderBy(marker => marker.Index)
            .ToArray();
        if (usable.Length == 0)
            throw new InvalidOperationException(
                $"Owned furniture has no usable sit marker: {referenceFormId}");
        ReferenceFormId = referenceFormId;
        DisplayName = displayName;
        _marker = usable[0] with
        {
            OffsetGodotGameUnits = usable[0].OffsetGodotGameUnits * unitsToMeters,
        };
        Name = $"FURNITURE_{referenceFormId}";
    }

    internal Transform3D SeatTransform()
    {
        var local = new Transform3D(
            new Basis(_marker.RotationGodot),
            _marker.OffsetGodotGameUnits);
        var seat = GlobalTransform * local;
        if (!seat.IsFinite() || seat.Basis.Determinant() <= 0.0f)
            throw new InvalidOperationException(
                $"Owned furniture seat transform is invalid: {ReferenceFormId}");
        return new Transform3D(seat.Basis.Orthonormalized(), seat.Origin);
    }

    internal readonly record struct Marker(
        int Id,
        int Index,
        Vector3 OffsetGodotGameUnits,
        Quaternion RotationGodot,
        int AnimationType);
}
