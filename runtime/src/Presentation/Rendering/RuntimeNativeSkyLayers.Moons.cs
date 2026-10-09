using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Presentation.Rendering;

internal partial class RuntimeNativeSkyLayers
{
    private FalloutSkyLightingState? _moonSky;
    private FalloutReferenceWorld? _moonWorld;
    private FalloutGameTime? _moonCalendar;
    private float _moonUnits;
    internal void BindSourceMoons(FalloutSkyLightingState sky, FalloutReferenceWorld world, FalloutGameTime calendar, float units)
    {
        if (_moonSky is not null || !IsInsideTree() || !float.IsFinite(units) || units <= 0)
            throw new InvalidOperationException("Moon factory needs a fresh actual Sky scene/current-world publication.");
        var transfer = sky.SourceTransfer ?? throw new NotSupportedException("Moon scene has no selected source Sky.");
        transfer.BindPresentationThread();
        if (world.ActualSourceProcessIdentity == Guid.Empty)
            throw new InvalidOperationException("Moon scene has no actual process owner.");
        _moonSky = sky; _moonWorld = world; _moonCalendar = calendar; _moonUnits = units;
        sky.SourceMoons.BindProjection(this);
        SynchronizeSourceMoonFactories();
    }
    private void SynchronizeSourceMoonFactories()
    {
        if (_moonSky is not { } sky) return;
        var transfer = sky.SourceTransfer ?? throw new NotSupportedException("Moon scene lost its source Sky owner.");
        if ((transfer.Flags & 0x40) == 0) return;
        var climate = transfer.Climate ?? throw new NotSupportedException("Dirty Sky has no actual climate pointer.");
        var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Moon scene lost its selected resource lifetime.");
        sky.SourceMoons.SynchronizeFactories(this, climate, (row, mode) => RuntimeNativeMoon.Create(sky.SourceMoons,
            row, mode, source, this, _moonUnits, ConstructSourceMoonProperty));
    }
    private static ShaderMaterial ConstructSourceMoonProperty(int role)
    {
        if (role is not (6 or 7)) throw new InvalidDataException("Moon factory requested a foreign source Sky property role.");
        // The actual constructor/resource/phase fields are owned. The selected
        // program-to-technique and live constant providers are a distinct next
        // owner: never publish these quads with a default material.
        throw new NotSupportedException("source-Moon-actual-shader-technique-constant-and-render-state-owner-unbound");
    }
    private void ProcessSourceMoonCalendar()
    {
        if (_moonSky is not { } sky) return;
        sky.StoreMoonCalendarHour(_moonWorld ?? throw new InvalidOperationException("Moon scene lost its real world."),
            _moonCalendar ?? throw new InvalidOperationException("Moon scene lost its real calendar."));
        SynchronizeSourceMoonFactories();
        // Source weather/NightSky frame prefixes must return before Moon
        // Advance. Existing sampled WTHR colours are not that native receipt.
    }
    private void RetireSourceMoonProjection() => _moonSky?.RetireSourceMoonProjection(this);
}
