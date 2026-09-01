using Godot;


using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Streaming;

internal sealed class CellEnvironmentSet
{
    private readonly WorldEnvironment _world;
    private readonly Dictionary<string, State> _states;
    private readonly List<Update> _updates = new();
    private string _activeCellFormId = "";

    private CellEnvironmentSet(
        WorldEnvironment world,
        IReadOnlyDictionary<string, State> states)
    {
        _world = world;
        _states = states.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    internal string ActiveCellFormId => _activeCellFormId;

    internal IReadOnlyList<Update> Updates => _updates;

    internal void AddContent(
        CellContentLoader.LoadedContent content,
        RuntimeConfiguration configuration)
    {
        var state = content.Interior
            ? BuildInterior(content, configuration)
            : BuildExterior(content, configuration);
        if (!_states.TryAdd(state.CellFormId, state))
            throw new InvalidOperationException(
                $"CELL environment set already contains CELL: {state.CellFormId}");
    }

    internal static CellEnvironmentSet Create(
        WorldEnvironment world,
        IEnumerable<CellContentLoader.LoadedContent> contents,
        RuntimeConfiguration configuration)
    {
        var states = contents.Select(content => content.Interior
                ? BuildInterior(content, configuration)
                : BuildExterior(content, configuration))
            .ToArray();
        var byFormId = states.ToDictionary(
            state => state.CellFormId,
            StringComparer.OrdinalIgnoreCase);
        if (byFormId.Count != states.Length)
            throw new InvalidOperationException(
                "CELL environment set contains duplicate CELL identities.");
        return new CellEnvironmentSet(world, byFormId);
    }

    internal void Activate(string cellFormId)
    {
        if (!_states.TryGetValue(cellFormId, out var state))
            throw new InvalidOperationException(
                $"Cannot activate an unloaded CELL environment: {cellFormId}");
        _world.Environment = state.Environment;
        _activeCellFormId = state.CellFormId;
        _updates.Add(new Update(
            state.CellFormId,
            state.Mode,
            state.WeatherFormId,
            state.WeatherEditorId));
    }

    internal IReadOnlyList<SpaceSnapshot> Snapshot() => _states.Values
        .OrderBy(state => state.CellFormId, StringComparer.OrdinalIgnoreCase)
        .Select(state => new SpaceSnapshot(
            state.CellFormId,
            state.CellFormId.Equals(_activeCellFormId, StringComparison.OrdinalIgnoreCase),
            state.Mode,
            state.GameHour,
            state.WeatherFormId,
            state.WeatherEditorId,
            state.AtmosphereSourceSha256,
            state.CloudsSourceSha256,
            state.BoundCloudTextureLayers))
        .ToArray();

    private static State BuildInterior(
        CellContentLoader.LoadedContent content,
        RuntimeConfiguration configuration)
    {
        var lighting = content.Lighting;
        return new State(
            content.FormId,
            "interior-xcll",
            BuildEnvironment(
                lighting.AmbientColor,
                lighting.FogColor,
                lighting.FogNearGameUnits,
                lighting.FogFarGameUnits,
                lighting.FogPower,
                content.UnitsToMeters,
                configuration),
            null,
            null,
            null,
            null,
            null,
            0);
    }

    private static State BuildExterior(
        CellContentLoader.LoadedContent content,
        RuntimeConfiguration configuration)
    {
        if (configuration.ExteriorEnvironment.Mode != "exterior-bounded-clear-day")
            throw new InvalidOperationException(
                $"Unsupported exterior environment mode: " +
                $"{configuration.ExteriorEnvironment.Mode}");
        var catalog = content.ExteriorEnvironment
            ?? throw new InvalidOperationException(
                $"Exterior CELL has no owned environment catalog: {content.FormId}");
        var resolved = catalog.ResolveConfiguredClearDay();
        var sky = RetailEnvironmentRenderer.AddSky(
            content.Root,
            catalog,
            resolved,
            configuration);
        return new State(
            content.FormId,
            configuration.ExteriorEnvironment.Mode,
            BuildEnvironment(
                resolved.AmbientEncoded,
                resolved.FogEncoded,
                resolved.FogNearGameUnits,
                resolved.FogFarGameUnits,
                resolved.FogPower,
                content.UnitsToMeters,
                configuration),
            resolved.GameHour,
            $"{resolved.WeatherFormId:x8}",
            resolved.WeatherEditorId,
            sky.AtmosphereSourceSha256,
            sky.CloudsSourceSha256,
            sky.CloudLayers.Count(layer => layer.Visible));
    }

    private static Godot.Environment BuildEnvironment(
        Color ambient,
        Color fog,
        float fogNearGameUnits,
        float fogFarGameUnits,
        float fogPower,
        float unitsToMeters,
        RuntimeConfiguration configuration) => new()
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = fog,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = ambient,
            AmbientLightEnergy = configuration.Renderer.AmbientEnergyScale,
            TonemapMode = RuntimeRendering.ParseToneMapper(configuration.Renderer.ToneMapper),
            FogEnabled = true,
            FogMode = Godot.Environment.FogModeEnum.Depth,
            FogLightColor = fog,
            FogLightEnergy = configuration.Renderer.FogLightEnergy,
            FogDensity = configuration.Renderer.FogDensity,
            FogDepthBegin = fogNearGameUnits * unitsToMeters,
            FogDepthEnd = fogFarGameUnits * unitsToMeters,
            FogDepthCurve = fogPower,
        };

    internal readonly record struct Update(
        string CellFormId,
        string Mode,
        string? WeatherFormId,
        string? WeatherEditorId);

    internal readonly record struct SpaceSnapshot(
        string CellFormId,
        bool Active,
        string Mode,
        float? GameHour,
        string? WeatherFormId,
        string? WeatherEditorId,
        string? AtmosphereSourceSha256,
        string? CloudsSourceSha256,
        int BoundCloudTextureLayers);

    private readonly record struct State(
        string CellFormId,
        string Mode,
        Godot.Environment Environment,
        float? GameHour,
        string? WeatherFormId,
        string? WeatherEditorId,
        string? AtmosphereSourceSha256,
        string? CloudsSourceSha256,
        int BoundCloudTextureLayers);
}
