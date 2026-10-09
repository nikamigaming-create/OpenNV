using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

// These values belong to an admitted source light wrapper and its actual
// affecting-light list. This calculation does not discover that list from
// render distance, Godot visibility or an arbitrary source CELL scan.
internal sealed record FalloutDetectionLightInput(float[] Position, float[] Diffuse,
    float Dimmer, float Radius, bool RadialAttenuation, bool Disabled, bool Excluded);

internal sealed record FalloutDetectionLightSample(float[] SourcePosition, IReadOnlyList<FalloutDetectionLightInput> Lights,
    bool Interior, float[] Ambient, float[] DirectionalDiffuse, FalloutDetectionLightInput? Sun);

internal sealed record FalloutDetectionLightSnapshot(string Schema, FalloutFormKey Actor,
    float Amount, float CountdownSeconds, long Revision);

// LightAmount and its timer are distinct from the directional detection score.
// Call only at the source light-update phase, after the detection commit pass.
// Paused world frames do not call Advance. No live renderer/sample callback is
// persisted or inferred while restoring the retained scalar/timer.
internal sealed class FalloutDetectionLight
{
    private const string Schema = "opennv-detection-light/v1";
    private readonly FalloutFormKey _actor;
    private float _amount;
    private float _countdown;
    private long _revision;

    internal float Amount => _amount;
    internal float CountdownSeconds => _countdown;

    // Actual HighProcess construction initializes both fields to zero. A
    // caller must independently admit that construction/process transition.
    internal FalloutDetectionLight(FalloutFormKey actor, Func<FalloutFormKey, bool> selectedActor)
    {
        ArgumentNullException.ThrowIfNull(selectedActor);
        if (!selectedActor(actor)) throw new InvalidDataException("Detection lighting requires a selected source actor.");
        _actor = actor;
    }

    internal bool Advance(float sourceFrameElapsedSeconds, Func<FalloutDetectionLightSample> admittedSample) =>
        Advance(sourceFrameElapsedSeconds, () => true, admittedSample);

    internal bool Advance(float sourceFrameElapsedSeconds, Func<bool?> source3D,
        Func<FalloutDetectionLightSample> admittedSample)
    {
        ArgumentNullException.ThrowIfNull(source3D);
        ArgumentNullException.ThrowIfNull(admittedSample);
        if (!float.IsFinite(sourceFrameElapsedSeconds) || sourceFrameElapsedSeconds < 0)
            throw new InvalidDataException("Detection lighting has no finite source frame clock.");
        var revision = checked(_revision + 1);
        if (_countdown > 0)
        {
            // Crossing zero schedules the next update; it does not sample in
            // the same call. The source checks the prior countdown first.
            var next = Store((double)_countdown - sourceFrameElapsedSeconds);
            _countdown = next; _revision = revision;
            return false;
        }
        // The source only tests the actor's 3D after its prior countdown has
        // expired. Known absence publishes zero with a new three-second timer;
        // unknown admission cannot become that source null branch.
        var present = source3D() ??
            throw new NotSupportedException("Detection lighting has no authoritative source 3D owner.");
        var amount = present ? Sample(admittedSample()) : 0;
        var countdown = Store(amount * .01d + 3d);
        _amount = amount; _countdown = countdown; _revision = revision;
        return true;
    }

    internal FalloutDetectionLightSnapshot Capture() => new(Schema, _actor, _amount, _countdown, _revision);

    internal void Restore(FalloutDetectionLightSnapshot snapshot)
    {
        if (snapshot.Schema != Schema || snapshot.Actor != _actor || snapshot.Revision < 0 ||
            !float.IsFinite(snapshot.Amount) || snapshot.Amount is < 0 or > 100 || !float.IsFinite(snapshot.CountdownSeconds) || snapshot.CountdownSeconds > 4 ||
            snapshot.Revision == 0 && (snapshot.Amount != 0 || snapshot.CountdownSeconds != 0))
            throw new InvalidDataException("Saved detection lighting has incompatible source identity, scalar or clock.");
        _amount = snapshot.Amount; _countdown = snapshot.CountdownSeconds; _revision = snapshot.Revision;
    }

    internal static float[] SamplePosition(float[] sourcePosition, float boundMinimumZ, float boundMaximumZ)
    {
        Vector(sourcePosition);
        if (!float.IsFinite(boundMinimumZ) || !float.IsFinite(boundMaximumZ) || boundMinimumZ > boundMaximumZ)
            throw new InvalidDataException("Detection lighting has no admitted source actor bounds.");
        var offset = Store((double)boundMaximumZ - boundMinimumZ - 1d);
        offset = Store(offset / 2d);
        return [sourcePosition[0], sourcePosition[1], Store(sourcePosition[2] + (double)offset)];
    }

    internal static float Sample(FalloutDetectionLightSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        Vector(sample.SourcePosition); Vector(sample.Ambient); Vector(sample.DirectionalDiffuse);
        ArgumentNullException.ThrowIfNull(sample.Lights);
        var sum = 0f;
        foreach (var light in sample.Lights) sum = Store(sum + (double)Contribution(light, sample.SourcePosition));
        if (sample.Interior)
            sum = Store(sum + (double)Store(Max(sample.Ambient) + (double)Max(sample.DirectionalDiffuse)));
        else if (sample.Sun is not null)
            sum = Store(sum + (double)Contribution(sample.Sun, sample.SourcePosition));
        return Math.Clamp(Store(sum * 100d), 0, 100);
    }

    internal static float Contribution(FalloutDetectionLightInput input, float[] sourcePosition)
    {
        ArgumentNullException.ThrowIfNull(input);
        Vector(input.Position); Vector(input.Diffuse); Vector(sourcePosition);
        if (!float.IsFinite(input.Dimmer) || !float.IsFinite(input.Radius))
            throw new InvalidDataException("Detection light has no finite source colour/dimmer/radius.");
        if (input.Disabled || input.Excluded) return 0;
        var red = Store(input.Diffuse[0] * (double)input.Dimmer);
        var green = Store(input.Diffuse[1] * (double)input.Dimmer);
        var blue = Store(input.Diffuse[2] * (double)input.Dimmer);
        var attenuation = 1f;
        if (input.RadialAttenuation)
        {
            if (input.Radius <= 0)
                throw new NotSupportedException("A nonpositive source light radius has no admitted sensory division.");
            var x = Store((double)sourcePosition[0] - input.Position[0]);
            var y = Store((double)sourcePosition[1] - input.Position[1]);
            var z = Store((double)sourcePosition[2] - input.Position[2]);
            var square = Store(x * (double)x + y * (double)y + z * (double)z);
            var distance = Store(Math.Sqrt(square));
            var ratio = Math.Clamp(Store(distance / (double)input.Radius), 0, 1);
            attenuation = Store(1d - ratio * (double)ratio);
        }
        return Store(Math.Max(red, Math.Max(green, blue)) * (double)attenuation);
    }

    private static float Max(float[] value) => Math.Max(value[0], Math.Max(value[1], value[2]));
    private static void Vector(float[]? value)
    {
        if (value is not { Length: 3 } || value.Any(number => !float.IsFinite(number)))
            throw new InvalidDataException("Detection lighting has no finite source vector.");
    }
    private static float Store(double value)
    {
        var result = (float)value;
        return double.IsFinite(value) && float.IsFinite(result) ? result :
            throw new NotSupportedException("Detection lighting exceeds its admitted finite Float32 domain.");
    }
}
