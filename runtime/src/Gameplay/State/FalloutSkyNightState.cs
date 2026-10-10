using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutSkyNightFields(Guid Owner, uint ParentFlags, FalloutCelestialMatrix Matrix,
    IReadOnlyList<FalloutSkyNightProperty> Properties);
internal sealed record FalloutSkyNightProperty(int Geometry, int Slot, int Property, int SourceType,
    FalloutCelestialRgb? StarsColor, uint? AlphaBits);
internal sealed record FalloutSkyNightCall(Guid Frame, long Entered, long Changed, string Branch,
    IReadOnlyList<string> Completed, string? EnteredChild, bool Returned, string? Failure);
internal interface IFalloutSourceNightGeometry
{
    Guid Identity { get; }
    Guid Sky { get; }
    Guid Process { get; }
    FalloutSkyNightFields ReadFields();
    void AdvanceOriginalAnimation(Guid frame, uint actualDeltaBits);
    void SetHidden(bool hidden);
    void PublishStars(int geometry, int property, FalloutCelestialRgb color, uint alphaBits);
    void StoreParentMatrix(FalloutCelestialMatrix matrix);
    void Retire();
}
internal sealed record FalloutSkyNightColorInput(Guid Sky, Guid Process, Guid Frame, FalloutFormKey Weather,
    string WeatherSha256, string SourceContract, int SourceColorClass, FalloutCelestialRgb Color);

// The original null-weather return precedes the alpha store and animation
// child. Native texture flags, stars palette and animation are distinct
// producers; a logical WTHR sample or source registration cannot supply them.
internal sealed partial class FalloutSkyNightState
{
    private readonly FalloutSkyTransferState _sky;
    private readonly Guid _process;
    private readonly int _thread = System.Environment.CurrentManagedThreadId;
    private long _changed = 1;
    private bool _retired, _executing;
    private IFalloutSourceNightGeometry? _native;
    private FalloutSkyNightCall? _call;
    private FalloutSkyNightFields? _coldGeometry;
    internal Guid Identity { get; } = Guid.NewGuid();
    internal uint AlphaBits { get; private set; }
    internal string? SaveBlocker => _native is not null ? "source-NightSky-native-loader-program-and-current-cold-field-validation-unowned" :
        _coldGeometry is not null ? "source-NightSky-cold-native-geometry-republication-pending" :
        _executing ? "source-NightSky-original-update-entered" :
        _call is { Returned: false } call ? "source-NightSky-entered-prefix/" + call.EnteredChild :
        _retired ? "source-NightSky-retired" : null;
    internal object State => new
    {
        owner = Identity,
        sky = _sky.Identity,
        process = _process,
        changed = _changed,
        alphaBits = AlphaBits,
        native = _native?.Identity,
        call = _call,
        deferredBy = SaveBlocker,
        nativeDraw = "source-loader-program-and-weather-prefix-required;unverified"
    };
    internal FalloutSkyNightState(FalloutSkyTransferState sky, Guid process)
    {
        sky.Source.Validate();
        if (!sky.Source.IsStandalone) throw new NotSupportedException("source-FNV-NightSky-constructor-and-frame-consumers-unowned");
        if (process == Guid.Empty) throw new InvalidDataException("NightSky constructor omitted its actual process.");
        _sky = sky; _process = process; // Actual constructor alpha is positive zero; geometry child is null.
    }
    private long Next() => _changed = checked(_changed + 1);
    private void Require()
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (System.Environment.CurrentManagedThreadId != _thread || _executing || _call is { Returned: false })
            throw new InvalidOperationException("NightSky lost its thread or retains an entered original child.");
    }
    internal void BindGeometry(IFalloutSourceNightGeometry native)
    {
        Require();
        if (native.Identity == Guid.Empty || native.Sky != _sky.Identity || native.Process != _process ||
            _native is not null && !ReferenceEquals(_native, native))
            throw new InvalidDataException("NightSky geometry factory returned another current source owner.");
        var fields = native.ReadFields();
        if (fields.Owner != native.Identity || _coldGeometry is { } cold &&
            (fields.ParentFlags != cold.ParentFlags || fields.Matrix != cold.Matrix || !fields.Properties.SequenceEqual(cold.Properties)))
            throw new InvalidDataException("NightSky cold native factory changed retained source fields.");
        _native = native; _coldGeometry = null; Next();
    }
    internal void Update(Guid actualFrame, uint actualDeltaBits, uint days, FalloutSkyNightSettings settings,
        Func<FalloutSkyNightColorInput> readActualStarsColor, Action requireActualSkyFrame)
    {
        Require(); requireActualSkyFrame();
        if (actualFrame == Guid.Empty || !float.IsFinite(BitConverter.UInt32BitsToSingle(actualDeltaBits)) ||
            BitConverter.UInt32BitsToSingle(actualDeltaBits) < 0)
            throw new InvalidDataException("NightSky update lost its original frame/Float32 delta.");
        var entered = Next(); _call = new(actualFrame, entered, entered, "entered", [], null, false, null);
        var completed = new List<string>(); _executing = true;
        try
        {
            if (_sky.CurrentWeather is null) { Return("current-weather-null-preserves-alpha-and-native-fields"); return; }
            Step("alpha-positive-zero", () => AlphaBits = 0);
            var native = _native ?? throw new NotSupportedException("source-NightSky-original-nonnull-geometry-loader-unowned");
            Step("original-animation-child", () => native.AdvanceOriginalAnimation(actualFrame, actualDeltaBits));
            if (_sky.Mode is not (2 or 3)) { Return("inactive-mode-after-animation"); return; }
            var fields = native.ReadFields();
            if (fields.Owner != native.Identity) throw new InvalidDataException("NightSky texture flag came from another parent.");
            if ((fields.ParentFlags & 0x20) == 0)
            { Step("parent-hide-no-source-texture-flag", () => native.SetHidden(true)); Return("source-texture-flag-clear"); return; }
            var hour = BitConverter.UInt32BitsToSingle(_sky.HourBits);
            Step("time-getters-and-alpha", () => AlphaBits = BitConverter.SingleToUInt32Bits(
                FalloutSkyNightMath.Alpha(_sky.Source, hour, _sky.ReadSourceStandaloneNightTimes())));
            Step("parent-visibility", () => native.SetHidden(!(BitConverter.UInt32BitsToSingle(AlphaBits) > 0)));
            if (!(BitConverter.UInt32BitsToSingle(AlphaBits) > 0)) { Return("zero-opacity"); return; }
            Step("source-stars-palette", () =>
            {
                var color = readActualStarsColor(); color.Color.Validate();
                if (color.Sky != _sky.Identity || color.Process != _process || color.Frame != actualFrame ||
                    color.Weather != _sky.CurrentWeather || color.SourceContract != _sky.Source.Contract || color.SourceColorClass != 6 ||
                    color.WeatherSha256.Length != 64 || !color.WeatherSha256.All(Uri.IsHexDigit))
                    throw new InvalidDataException("NightSky borrowed logical colour or the separate upper-Sky class.");
                // Slot/type mismatches are original skips. Preserve actual child
                // order/duplicates rather than requiring every mesh to be stars.
                foreach (var property in fields.Properties)
                    if (property.Slot == 3 && property.SourceType == 12)
                        native.PublishStars(property.Geometry, property.Property, color.Color, AlphaBits);
            });
            Step("calendar-parent-matrix", () => native.StoreParentMatrix(FalloutSkyNightMath.Rotation(_sky.Source, days, hour, settings)));
            Return("active-source-fields-returned-draw-prefix-independent");
        }
        catch (Exception failure)
        { _call = _call! with { Changed = Next(), Failure = failure.ToString() }; throw; }
        finally { _executing = false; }
        void Step(string name, Action action)
        {
            requireActualSkyFrame(); _call = _call! with { Changed = Next(), EnteredChild = name };
            action(); requireActualSkyFrame(); completed.Add(name);
            _call = _call with { Changed = Next(), Completed = completed.ToArray(), EnteredChild = null };
        }
        void Return(string branch)
        { requireActualSkyFrame(); _call = _call! with { Changed = Next(), Branch = branch, Returned = true }; }
    }
    internal void Retire()
    {
        if (_retired) return;
        if (System.Environment.CurrentManagedThreadId != _thread || _executing)
            throw new InvalidOperationException("NightSky retirement left the actual constructor/thread/update owner.");
        _native?.Retire(); _native = null; _retired = true; Next();
    }
    internal FalloutSkyNightSnapshot Capture()
    {
        Require();
        if (SaveBlocker is { } blocker) throw new NotSupportedException(blocker);
        var saved = new FalloutSkyNightSnapshot(_sky.Source.Contract, Identity, _sky.Identity, _process, _changed,
            AlphaBits, _call, _native?.ReadFields());
        Validate(saved); return saved;
    }
    internal static void Validate(FalloutSkyNightSnapshot saved)
    {
        if (saved.CapturedOwner == Guid.Empty || saved.CapturedSky == Guid.Empty || saved.CapturedProcess == Guid.Empty ||
            saved.Changed < 1 || !float.IsFinite(BitConverter.UInt32BitsToSingle(saved.AlphaBits)) ||
            saved.SourceContract.Length != 64 || !saved.SourceContract.All(Uri.IsHexDigit))
            throw new InvalidDataException("Saved NightSky omitted finite original constructor/field ownership.");
        if (saved.Native is not null || saved.AlphaBits != 0 || saved.Call is { Branch: not "current-weather-null-preserves-alpha-and-native-fields" })
            throw new NotSupportedException("source-NightSky-native-loader-program-and-active-frame-current-cold-validator-unowned");
        if (saved.Call is not { } call)
        {
            if (saved.AlphaBits != 0 || saved.Native is not null)
                throw new InvalidDataException("NightSky introduced a field writer without an actual constructor/frame return.");
        }
        else if (!call.Returned || call.Failure is not null || call.EnteredChild is not null || call.Frame == Guid.Empty ||
            call.Entered < 1 || call.Changed <= call.Entered || call.Changed > saved.Changed || call.Completed is null ||
            call.Branch == "entered" || string.IsNullOrWhiteSpace(call.Branch))
            throw new InvalidDataException("NightSky capture promoted a failed/partial source frame.");
        if (saved.Native is { } fields)
        {
            if (fields.Owner == Guid.Empty || fields.Properties is null) throw new InvalidDataException("NightSky omitted actual native fields.");
            fields.Matrix.Validate();
            foreach (var property in fields.Properties)
            {
                property.StarsColor?.Validate();
                if (property.AlphaBits is { } alpha && !float.IsFinite(BitConverter.UInt32BitsToSingle(alpha)))
                    throw new InvalidDataException("NightSky property retained nonfinite source alpha.");
            }
        }
    }
    internal void Restore(FalloutSkyNightSnapshot saved)
    {
        Require(); Validate(saved);
        if (_changed != 1 || _native is not null || saved.CapturedOwner == Identity || saved.CapturedSky == _sky.Identity ||
            saved.CapturedProcess == _process || saved.SourceContract != _sky.Source.Contract)
            throw new InvalidOperationException("NightSky cold restore needs its actual new source/process constructor.");
        AlphaBits = saved.AlphaBits; _changed = saved.Changed; _call = saved.Call; _coldGeometry = saved.Native; Next();
    }
}
