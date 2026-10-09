using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal FalloutReferencePlacement ScriptPlacement(FalloutFormKey reference,
        FalloutReferencePlacement? player, float unitsToMetres, bool requireFacing)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return SpatialPlacement(reference, player, unitsToMetres, requireFacing);
    }

    internal static int ScriptAxis(string value) => value.ToUpperInvariant() switch
    {
        "X" => 0,
        "Y" => 1,
        "Z" => 2,
        _ => throw new InvalidDataException("Position/angle axis must be X, Y or Z."),
    };

    internal static double ScriptAngle(float radians, int axis)
    {
        if (!float.IsFinite(radians) || axis is < 0 or > 2)
            throw new InvalidDataException("Reference angle is invalid.");
        var degrees = (double)radians * (180 / Math.PI);
        // A world Z heading is clockwise from the source +Y axis. Pitch and
        // roll retain their signed source Euler component.
        return axis == 2 ? (degrees % 360 + 360) % 360 : degrees;
    }

    internal bool ActorFemaleQuery(FalloutFormKey reference)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (reference == records.RuntimeFormKey(0x14))
            return (_playerAppearance ?? throw new NotSupportedException("Player sex query has no current appearance owner."))().Female ??
                throw new InvalidDataException("Player appearance has no sex value.");
        var actor = Actor(reference);
        var source = records.GetEffective(actor.Base);
        if (source.Signature != "NPC_") throw new NotSupportedException("Creature sex queries have no source traits owner.");
        var selected = actor.Templates is { } selection ? new FalloutActorTemplateSelection(selection.Capture()) : null;
        var traits = FalloutActorTemplateOwner.Resolve(records, source, 1, selected);
        return ActorFemale(reference, traits);
    }

    internal int ActorLevel(FalloutFormKey reference)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (reference == records.RuntimeFormKey(0x14))
            throw new NotSupportedException("Player level must be read from its persistent player owner.");
        var actor = Actor(reference);
        // Queries may inspect existing selected template arms, never perform
        // a new random/chance selection or change the reference's RNG state.
        var selected = actor.Templates is { } selection ? new FalloutActorTemplateSelection(selection.Capture()) : null;
        var stats = FalloutActorTemplateOwner.Resolve(records, records.GetEffective(actor.Base), 2, selected);
        var fields = stats.ReadSubrecords().Where(field => field.Signature == "ACBS").ToArray();
        if (fields.Length != 1 || fields[0].Data.Length != 24)
            throw new InvalidDataException("Actor level source has invalid ACBS.");
        return FalloutActorLevel.Resolve(fields[0].Data.Span, selected?.Level);
    }
}
