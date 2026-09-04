using System.Text;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutDoorTransition(
    FalloutCellScene SourceScene,
    FalloutPlacedReference SourceDoor,
    FalloutCellScene DestinationScene,
    FalloutPlacedReference DestinationDoor,
    FalloutFormKey DestinationWorldspace,
    string DestinationWorldspaceEditorId);

internal static class FalloutDoorTransitionResolver
{
    private const byte InteriorCellFlag = 0x01;
    private const uint PersistentReferenceFlag = 0x0000_0400;
    private const uint SupportedTeleportFlags = 0;

    internal static FalloutDoorTransition ResolveSingleInteriorExit(
        FalloutPluginStack stack,
        FalloutCellScene sourceScene)
    {
        var candidates = sourceScene.References.Where(reference =>
            reference.Teleport is not null &&
            sourceScene.BaseObjects.TryGetValue(reference.Base, out var baseObject) &&
            baseObject.Signature == "DOOR" &&
            !FalloutCellSceneReader.IsInitiallyDisabled(reference)).ToArray();
        if (candidates.Length != 1)
            throw new InvalidDataException(
                $"Native CELL {sourceScene.Cell.FormKey} has {candidates.Length} active XTEL doors; " +
                "the bounded live exit requires exactly one.");
        return Resolve(stack, sourceScene, candidates[0].FormKey);
    }

    internal static FalloutDoorTransition Resolve(
        FalloutPluginStack stack,
        FalloutCellScene sourceScene,
        FalloutFormKey sourceDoorKey)
    {
        if ((sourceScene.Cell.Flags & InteriorCellFlag) == 0 ||
            sourceScene.Cell.Worldspace is not null || sourceScene.Cell.Coordinates is not null)
            throw new NotSupportedException(
                $"Native XTEL source CELL {sourceScene.Cell.FormKey} is not the admitted interior contract.");
        var sourceMatches = sourceScene.References.Where(value => value.FormKey == sourceDoorKey).ToArray();
        if (sourceMatches.Length != 1)
            throw new InvalidDataException(
                $"Native XTEL source {sourceDoorKey} occurs {sourceMatches.Length} times in {sourceScene.Cell.FormKey}.");
        var sourceDoor = sourceMatches[0];
        ValidateDoorReference(sourceScene, sourceDoor, "source");
        var sourceTeleport = sourceDoor.Teleport!;

        var destinationRecord = stack.GetEffective(sourceTeleport.Door);
        if (destinationRecord.Signature != "REFR")
            throw new InvalidDataException(
                $"Native XTEL destination {sourceTeleport.Door} is {destinationRecord.Signature}, not REFR.");
        var destinationCell = FalloutCellSceneReader.ParentCell(destinationRecord) ??
            throw new InvalidDataException(
                $"Native XTEL destination {sourceTeleport.Door} has no CELL ancestry.");
        var destinationScene = FalloutCellSceneReader.Read(stack, destinationCell);
        var destinationMatches = destinationScene.References.Where(value =>
            value.FormKey == sourceTeleport.Door).ToArray();
        if (destinationMatches.Length != 1)
            throw new InvalidDataException(
                $"Native XTEL destination {sourceTeleport.Door} occurs {destinationMatches.Length} times " +
                $"in {destinationCell}.");
        var destinationDoor = destinationMatches[0];
        ValidateDoorReference(destinationScene, destinationDoor, "destination");
        if (destinationDoor.Teleport!.Door != sourceDoor.FormKey)
            throw new InvalidDataException(
                $"Native XTEL pair is not reciprocal: {sourceDoor.FormKey} -> {destinationDoor.FormKey} -> " +
                $"{destinationDoor.Teleport.Door}.");
        if ((destinationScene.Cell.Flags & InteriorCellFlag) != 0 ||
            destinationScene.Cell.Worldspace is not { } worldspace ||
            destinationScene.Cell.Coordinates is null)
            throw new NotSupportedException(
                $"Native XTEL destination CELL {destinationCell} is not the admitted exterior-world contract.");
        var worldRecord = stack.GetEffective(worldspace);
        if (worldRecord.Signature != "WRLD")
            throw new InvalidDataException(
                $"Native XTEL destination world {worldspace} is {worldRecord.Signature}, not WRLD.");
        return new FalloutDoorTransition(
            sourceScene,
            sourceDoor,
            destinationScene,
            destinationDoor,
            worldspace,
            ReadOptionalEditorId(worldRecord));
    }

    private static void ValidateDoorReference(
        FalloutCellScene scene,
        FalloutPlacedReference reference,
        string label)
    {
        if (!scene.BaseObjects.TryGetValue(reference.Base, out var baseObject) ||
            baseObject.Signature != "DOOR")
            throw new InvalidDataException(
                $"Native XTEL {label} {reference.FormKey} does not target a DOOR base.");
        if (reference.Teleport is null)
            throw new InvalidDataException(
                $"Native XTEL {label} {reference.FormKey} has no teleport destination.");
        if (reference.Flags != PersistentReferenceFlag ||
            FalloutCellSceneReader.IsInitiallyDisabled(reference) ||
            reference.EnableParent is not null || reference.Scale != 1.0f ||
            reference.Teleport.Flags != SupportedTeleportFlags)
            throw new NotSupportedException(
                $"Native XTEL {label} {reference.FormKey} is outside the active persistent portal contract: " +
                $"flags=0x{reference.Flags:x8} enableParent={reference.EnableParent} " +
                $"scale={reference.Scale:R} xtelFlags=0x{reference.Teleport.Flags:x8}.");
    }

    private static string ReadOptionalEditorId(FalloutPluginRecord record)
    {
        var matches = record.ReadSubrecords().Where(value => value.Signature == "EDID").ToArray();
        if (matches.Length == 0)
            return string.Empty;
        if (matches.Length != 1)
            throw new InvalidDataException(
                $"Native {record.Signature} {record.FormKey} contains {matches.Length} EDID subrecords.");
        var data = matches[0].Data.Span;
        var terminator = data.IndexOf((byte)0);
        if (terminator != data.Length - 1 || data[..terminator].IndexOfAnyInRange((byte)0x80, byte.MaxValue) >= 0)
            throw new InvalidDataException(
                $"Native {record.Signature} {record.FormKey} EDID is not null-terminated ASCII.");
        return Encoding.ASCII.GetString(data[..terminator]);
    }
}
