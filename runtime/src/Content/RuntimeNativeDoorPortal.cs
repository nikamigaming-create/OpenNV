using Godot;

namespace OpenNV.Runtime.Content;

internal partial class RuntimeNativeDoorPortal : Node
{
    private Action? _activate;

    internal FalloutFormKey Reference { get; private set; }
    internal FalloutFormKey Destination { get; private set; }
    internal FalloutFormKey DestinationCell { get; private set; }
    internal FalloutFormKey? DestinationWorldspace { get; private set; }

    internal void Configure(
        FalloutFormKey reference,
        FalloutFormKey destination,
        FalloutFormKey destinationCell,
        FalloutFormKey? destinationWorldspace,
        Action activate)
    {
        if (_activate is not null || reference == destination)
            throw new InvalidOperationException("Native door portal configuration is invalid or repeated.");
        Reference = reference;
        Destination = destination;
        DestinationCell = destinationCell;
        DestinationWorldspace = destinationWorldspace;
        _activate = activate ?? throw new ArgumentNullException(nameof(activate));
        Name = $"NativeDoorPortal_{reference}";
        SetMeta("opennv_xtel_reference", reference.ToString());
        SetMeta("opennv_xtel_destination", destination.ToString());
        SetMeta("opennv_xtel_destination_cell", destinationCell.ToString());
        if (destinationWorldspace is { } worldspace)
            SetMeta("opennv_xtel_destination_world", worldspace.ToString());
        SetMeta("opennv_source", "live-owned-stack");
    }

    internal void Activate()
    {
        if (_activate is null)
            throw new InvalidOperationException("Native door portal is not configured.");
        _activate();
    }
}
