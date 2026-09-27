using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using System.Security.Cryptography;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private readonly List<FalloutExplosionExposure> _explosionExposure = [];
    private readonly Dictionary<FalloutFormKey, FalloutExplosion> _exposureSources = [];
    private readonly Dictionary<FalloutFormKey, FalloutFormKey> _exposureSpaces = [];
    private Func<FalloutFormKey>? _exposureCell;
    private Action<float>? _applyRadiation;
    private FalloutImageSpaceState? _explosionImageSpace;
    internal IReadOnlyList<FalloutExplosionExposure> CaptureExplosionExposure() =>
        _explosionExposure.Select(value => value with { Position = (float[])value.Position.Clone() }).ToArray();

    internal void ConfigureExplosionExposure(Func<FalloutFormKey> cell, Action<float> radiation,
        FalloutImageSpaceState images, IReadOnlyList<FalloutExplosionExposure>? restore = null)
    {
        _exposureCell = cell; _applyRadiation = radiation; _explosionImageSpace = images;
        foreach (var saved in restore ?? [])
        {
            var source = ExposureSource(saved.Explosion);
            if (saved.SourceSha256 != ExplosionHash(saved.Explosion) || saved.Position is not { Length: 3 } ||
                saved.Position.Any(value => !float.IsFinite(value)) || !double.IsFinite(saved.ElapsedSeconds) ||
                saved.ElapsedSeconds < 0 || saved.ElapsedSeconds >= source.RadiationDissipationSeconds ||
                _presentationRecords!.GetEffective(saved.Cell).Signature != "CELL")
                throw new InvalidDataException("Saved explosion exposure differs from its winning source.");
            _explosionExposure.Add(saved with { Position = (float[])saved.Position.Clone() });
        }
    }

    internal void ReceiveExplosionExposure(FalloutExplosion source, Vector3 point)
    {
        if (source.ImageSpace is { } modifier && source.ImageSpaceRadius > 0 &&
            Camera.GlobalPosition.DistanceTo(point) <= source.ImageSpaceRadius * UnitsToMeters)
        {
            if (_explosionImageSpace is null) throw new NotSupportedException("Explosion has no shared imagespace owner.");
            _explosionImageSpace.Apply(FalloutImageSpaceModifierReader.Read(_presentationRecords!.GetEffective(modifier)));
        }
        if (source.RadiationLevel == 0) return;
        if (_applyRadiation is null || _exposureCell is null) throw new NotSupportedException("Explosion has no retained radiation owner.");
        if (source.RadiationDissipationSeconds <= 0 || source.RadiationRadius <= 0)
            throw new NotSupportedException("Radiating explosion has no finite source exposure volume and lifetime.");
        _explosionExposure.Add(new(source.Form, ExplosionHash(source.Form), _exposureCell(), [point.X, point.Y, point.Z], 0));
    }

    private string ExplosionHash(FalloutFormKey source) => Convert.ToHexString(SHA256.HashData(
        _presentationRecords!.GetEffective(source).ReadData())).ToLowerInvariant();

    private FalloutExplosion ExposureSource(FalloutFormKey key)
    {
        if (!_exposureSources.TryGetValue(key, out var source))
            _exposureSources.Add(key, source = FalloutExplosion.Read(_presentationRecords!, key));
        return source;
    }

    private FalloutFormKey ExposureSpace(FalloutFormKey cell)
    {
        if (!_exposureSpaces.TryGetValue(cell, out var space))
            _exposureSpaces.Add(cell, space = FalloutCellSceneReader.ParentWorldspace(_presentationRecords!.GetEffective(cell)) ?? cell);
        return space;
    }

    private void AdvanceExplosionExposure(double delta)
    {
        for (var index = _explosionExposure.Count - 1; index >= 0; index--)
        {
            var exposure = _explosionExposure[index];
            var source = ExposureSource(exposure.Explosion);
            var step = Math.Min(delta, source.RadiationDissipationSeconds - exposure.ElapsedSeconds);
            if (ExposureSpace(exposure.Cell) == ExposureSpace(_exposureCell!()) && Camera.GlobalPosition.DistanceTo(new(exposure.Position[0], exposure.Position[1], exposure.Position[2])) <= source.RadiationRadius * UnitsToMeters)
                _applyRadiation!((float)(source.RadiationLevel * step));
            var elapsed = exposure.ElapsedSeconds + step;
            if (elapsed >= source.RadiationDissipationSeconds) _explosionExposure.RemoveAt(index);
            else _explosionExposure[index] = exposure with { ElapsedSeconds = elapsed };
        }
    }
}
