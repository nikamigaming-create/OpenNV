namespace OpenNV.Runtime.Content;

internal static class FalloutLandscapeNativeInputs
{
    internal static void Validate(FalloutLandscapeTransport source, float gameUnitsToMeters)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!float.IsFinite(gameUnitsToMeters) || gameUnitsToMeters <= 0)
            throw new ArgumentOutOfRangeException(nameof(gameUnitsToMeters), "Native LAND scale must be finite and positive.");
        const int side = FalloutLandscapeTransportResolver.VertexSide;
        const int vertices = side * side;
        if (source.Heights is null || source.Heights.Length != vertices || source.Heights.Any(value => !float.IsFinite(value)) ||
            source.Normals is null || source.Normals.Length != vertices * 3 || source.Normals.Any(value => !float.IsFinite(value)) ||
            source.Colors is null || source.Colors.Length != vertices * 3 || source.BaseLayers is null || source.AlphaLayers is null ||
            source.Textures is null || source.BaseLayers.Count != 4 ||
            !source.BaseLayers.Select(value => value.Quadrant).ToHashSet().SetEquals(new byte[] { 0, 1, 2, 3 }) ||
            source.AlphaLayers.Any(layer => layer.Quadrant > 3))
            throw new InvalidDataException("Native LAND buffers/layers differ from the complete source 33x33/four-quadrant transport.");
        foreach (var layer in source.BaseLayers.Concat(source.AlphaLayers))
            if (!source.Textures.TryGetValue(layer.Texture, out var texture) || string.IsNullOrWhiteSpace(texture.DiffusePath) ||
                texture.NormalPath is not null && string.IsNullOrWhiteSpace(texture.NormalPath))
                throw new InvalidDataException("Native LAND layer lacks its actual declared texture owner.");
        for (byte quadrant = 0; quadrant < 4; quadrant++)
            _ = FalloutLandscapeMaterialInputs.Weights(source.AlphaLayers.Where(layer => layer.Quadrant == quadrant)
                .OrderBy(layer => layer.LayerIndex).ToArray());
        // Evaluate in the same Float32 source-to-native order as BuildQuadrant.
        // No world bounds, radius, vertex cap or geometry omission is introduced.
        var cellX = source.ActiveCoordinates.X * FalloutLandscapeTransportResolver.ExteriorCellSideGameUnits;
        var cellY = source.ActiveCoordinates.Y * FalloutLandscapeTransportResolver.ExteriorCellSideGameUnits;
        for (var y = 0; y < side; y++)
            for (var x = 0; x < side; x++)
                if (!float.IsFinite((cellX + x * FalloutLandscapeTransportResolver.VertexSpacingGameUnits) * gameUnitsToMeters) ||
                    !float.IsFinite((cellY + y * FalloutLandscapeTransportResolver.VertexSpacingGameUnits) * gameUnitsToMeters) ||
                    !float.IsFinite(source.Heights[y * side + x] * gameUnitsToMeters))
                    throw new InvalidDataException("Source LAND conversion produced a nonfinite native vertex.");
    }
}
