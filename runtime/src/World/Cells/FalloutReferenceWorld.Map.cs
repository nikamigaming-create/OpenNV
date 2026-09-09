using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal FalloutMapMarkerState MapMarker(FalloutFormKey reference)
    {
        var instance = Get(reference);
        if (instance.MapMarker is { } state) return state;
        var source = FalloutMapMarker.Read(records.GetEffective(reference));
        return instance.MapMarker = new((source.Flags & 1) != 0, (source.Flags & 2) != 0);
    }

    internal void ShowMap(FalloutFormKey reference, bool canTravel = false)
    {
        var state = MapMarker(reference);
        // Revealing a known location cannot revoke travel already earned there.
        Get(reference).MapMarker = state with { Visible = true, CanTravel = state.CanTravel || canTravel };
    }

    internal int MapMarkerVisibility(FalloutFormKey reference)
    {
        var state = MapMarker(reference);
        return !state.Visible ? 0 : state.CanTravel ? 2 : 1;
    }

    internal FalloutMapMarkerState MapMarkerState(FalloutMapMarker marker) =>
        _instances.TryGetValue(marker.Reference, out var instance) && instance.MapMarker is { } state
            ? state : new((marker.Flags & 1) != 0, (marker.Flags & 2) != 0);

    internal IEnumerable<(FalloutMapMarker Source, FalloutMapMarkerState State)> KnownMapMarkers => _instances.Values
        .Where(instance => instance.MapMarker is not null).Select(instance =>
            (FalloutMapMarker.Read(records.GetEffective(instance.Reference)), instance.MapMarker!));
}
