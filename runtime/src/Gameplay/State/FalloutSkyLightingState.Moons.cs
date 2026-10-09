using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutSkyLightingState
{
    private FalloutSkyMoonState? _sourceMoons;
    internal FalloutSkyMoonState SourceMoons => _sourceMoons ??
        throw new NotSupportedException("Source Moon factories have no actual selected Sky/process owner.");
    internal object? SourceMoonState => _sourceMoons?.State;
    internal string? SourceMoonSaveBlocker => _sourceMoons?.SaveBlocker;
    private void ConstructSourceMoons()
    {
        if (_sourceTransfer is not { } source) return;
        if (_sourceMoons is not null) throw new InvalidOperationException("Source Moon owner is already constructed.");
        _sourceMoons = new(source, _records, _transferStack ?? throw new InvalidOperationException("Sky selection is absent."), _transferProcess);
    }
    private FalloutSkyMoonSnapshot? CaptureSourceMoons() => _sourceMoons?.Capture();
    private void RestoreSourceMoons(FalloutSkyLightingSnapshot snapshot)
    {
        if (_projectionRestore) return;
        if (_sourceTransfer is null)
        {
            if (snapshot.Moons is not null) throw new InvalidDataException("Saved Moons have no selected Sky declaration.");
            return;
        }
        if (snapshot.Moons is not { } moons || snapshot.SourceTransfer is not { } sky)
            throw new InvalidDataException("Saved Sky omitted its complete Moon field/current-cold join.");
        if (_sourceMoons is not null)
            throw new InvalidOperationException("Cold Sky replacement retains a previous Moon source/native owner.");
        ConstructSourceMoons(); SourceMoons.Restore(moons, sky.CapturedSky, sky.CapturedProcess);
    }
    private void RetireSourceMoons() => _sourceMoons?.Retire();
    private void RetireForSourceMoonReplacement()
    {
        _sourceMoons?.Retire();
        _sourceMoons = null;
    }
    internal void RetireSourceMoonProjection(object lifetime) => _sourceMoons?.RetireNativeFields(lifetime);
    private static void ValidateSourceMoons(FalloutPluginStack records, FalloutSkyLightingSnapshot snapshot)
    {
        if (snapshot.SourceTransfer is not { } sky)
        {
            if (snapshot.Moons is not null) throw new InvalidDataException("Saved Moons have no actual source Sky owner.");
            return;
        }
        var moons = snapshot.Moons ?? throw new InvalidDataException("Current saved Sky omitted its complete Moon field owner.");
        if (moons.CapturedSky != sky.CapturedSky || moons.CapturedProcess != sky.CapturedProcess)
            throw new InvalidDataException("Saved Moons belong to another actual source Sky/process epoch.");
        FalloutSkyMoonState.Validate(moons, FalloutMoonSource.Read(sky.Source), sky.Stack, records);
    }
    private void RetireCombinedSourceSky()
    {
        var failures = new List<Exception>();
        try { RetireSourceMoons(); } catch (Exception error) { failures.Add(error); }
        try { _sourceTransfer?.Retire(); } catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0) throw new AggregateException("Sky retains independent Moon and reset-child retirement failures.", failures);
    }
    internal void StoreMoonCalendarHour(FalloutReferenceWorld world, FalloutGameTime time)
    {
        var player = world.ReadSourceMoonPlayerFrame();
        if (player is null) return;
        (_sourceTransfer ?? throw new NotSupportedException("Source Sky is absent."))
            .StoreSourceMoonCalendarHour(player, time.ReadSourceMoonCalendar());
    }
}
