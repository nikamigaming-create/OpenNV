using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutPlayerControlDisplayWrite(Guid Observation, string Producer, long Changed,
    int Width, int Height, byte? Wide, bool Returned, bool DimensionsStored = true);
internal sealed record FalloutPlayerControlDisplaySnapshot(string Source, string Stack, Guid Process, Guid Factory,
    long Sequence, int Width, int Height, byte Wide, bool Available, string? Failure,
    FalloutIniValue? WidthSetting, FalloutIniValue? HeightSetting, IReadOnlyList<FalloutPlayerControlDisplayWrite> Writes);
internal sealed record FalloutPlayerControlDisplayRead(Guid Process, Guid Factory, long Sequence,
    int Width, int Height, byte Wide, uint LogicalWidthBits, uint LogicalHeightBits);

// Source UI dimensions are retained integer fields. The renderer borrows them;
// a Godot viewport or camera does not become their constructor or writer.
internal sealed class FalloutPlayerControlDisplayState
{
    internal const string ModeBoundary = "source-display-selected-mode-dimension-writer-unbound";
    private readonly FalloutPlayerControlHudSource _source;
    private readonly string _stack;
    private readonly Guid _process, _factory = Guid.NewGuid();
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly List<FalloutPlayerControlDisplayWrite> _writes = [];
    private readonly HashSet<Guid> _observations = [];
    private int _width = FalloutPlayerControlHudSource.InitialWidth, _height = FalloutPlayerControlHudSource.InitialHeight;
    private byte _wide;
    private long _sequence;
    private bool _available = true;
    private string? _failure;
    private FalloutIniValue? _widthSetting, _heightSetting;

    internal FalloutPlayerControlDisplayState(FalloutPlayerControlHudSource source, string stack, Guid process,
        FalloutPlayerControlDisplaySnapshot? saved = null)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        if (process == Guid.Empty) throw new InvalidDataException("Source display fields have no actual process.");
        _source = source; _stack = stack; _process = process;
        if (saved is null) return;
        Validate(saved, source, stack);
        if (saved.Process == process) throw new InvalidDataException("Cold display reused its previous actual process.");
        _sequence = saved.Sequence; _width = saved.Width; _height = saved.Height; _wide = saved.Wide;
        _available = saved.Available; _failure = saved.Failure; _widthSetting = saved.WidthSetting; _heightSetting = saved.HeightSetting;
        _writes.AddRange(saved.Writes); _observations.UnionWith(saved.Writes.Select(value => value.Observation));
    }
    internal void ApplyDeclaredConfiguration(FalloutNumericIniSettings settings)
    {
        RequireCurrent(); ArgumentNullException.ThrowIfNull(settings);
        if (!_available || _writes.Count != 0 || _widthSetting is not null || _heightSetting is not null || !settings.Source.StartsWith(_stack + ":", StringComparison.Ordinal) ||
            !settings.Source.EndsWith(":" + _source.Controls.Main.EngineSha256, StringComparison.Ordinal))
            throw new InvalidOperationException("Source display configuration replayed or changed its actual selected declarations.");
        var width = Setting(FalloutPlayerControlHudSource.WidthSetting, FalloutPlayerControlHudSource.InitialWidth);
        var height = Setting(FalloutPlayerControlHudSource.HeightSetting, FalloutPlayerControlHudSource.InitialHeight);
        // The source performs these two independent stores before its native
        // window and mode selector. Selection has its own later publication.
        _widthSetting = width; _width = checked((int)width.Number!.Value); Next();
        _heightSetting = height; _height = checked((int)height.Number!.Value); Next();
        FalloutIniValue Setting(string name, int initial)
        {
            var field = settings.Find(FalloutIniCollection.Renderer, name);
            if (field is null || field.Declaration.Payload != (uint)initial || field.Declaration.Kind != 'i' ||
                field.Number is not { } value || value != Math.Truncate(value) || value is < int.MinValue or > int.MaxValue ||
                string.IsNullOrWhiteSpace(field.Origin))
                throw new InvalidDataException("Source display lost its declared Renderer integer: " + name);
            return field;
        }
    }
    internal void PublishSelectedMode(Guid observation, string producer, int width, int height, byte wide,
        Action requireActualCurrent)
    {
        RequireCurrent(); ArgumentNullException.ThrowIfNull(requireActualCurrent); requireActualCurrent();
        if (!_available || observation == Guid.Empty || string.IsNullOrWhiteSpace(producer) || wide != WideFor(width, height) || !_observations.Add(observation))
            throw new InvalidDataException("Source display mode has no fresh actual producer.");
        _width = width; Next(); _height = height; Next(); _wide = wide;
        _writes.Add(new(observation, producer, Next(), width, height, wide, true));
    }
    internal void PublishCurrentLayout(Guid observation, string producer, Action requireActualCurrent)
    {
        RequireCurrent(); ArgumentNullException.ThrowIfNull(requireActualCurrent); requireActualCurrent();
        if (!_available || observation == Guid.Empty || string.IsNullOrWhiteSpace(producer) || !_observations.Add(observation))
            throw new InvalidDataException("Source layout omitted a fresh actual current-dimension producer.");
        _wide = WideFor(_width, _height);
        _writes.Add(new(observation, producer, Next(), _width, _height, _wide, true, false));
    }
    internal void ObserveUnboundSelection(Guid observation, string producer)
    {
        RequireCurrent();
        if (!_available || observation == Guid.Empty || string.IsNullOrWhiteSpace(producer) || !_observations.Add(observation))
            throw new InvalidDataException("Source display observation omitted or replayed its actual producer.");
        _available = false; _failure = ModeBoundary;
        _writes.Add(new(observation, producer, Next(), _width, _height, null, false, false));
    }
    internal FalloutPlayerControlDisplayRead Read()
    {
        RequireCurrent();
        if (!_available) throw new NotSupportedException(_failure);
        // The original first narrows Int32 inputs to Float32, evaluates its
        // division/multiply in x87 precision and stores the returned Float32.
        var (x, y) = LogicalDimensions(_width, _height);
        return new(_process, _factory, _sequence, _width, _height, _wide,
            BitConverter.SingleToUInt32Bits(x), BitConverter.SingleToUInt32Bits(y));
    }
    internal FalloutPlayerControlDisplaySnapshot Capture()
    {
        RequireCurrent(); var saved = new FalloutPlayerControlDisplaySnapshot(_source.Identity, _stack, _process, _factory,
            _sequence, _width, _height, _wide, _available, _failure, _widthSetting, _heightSetting, _writes.ToArray());
        Validate(saved, _source, _stack); return saved;
    }
    internal static void Validate(FalloutPlayerControlDisplaySnapshot saved, FalloutPlayerControlHudSource source, string stack)
    {
        source.Validate();
        if (saved.Source != source.Identity || saved.Stack != stack || saved.Process == Guid.Empty || saved.Factory == Guid.Empty ||
            saved.Sequence < 0 || saved.Wide > 1 || saved.Available != (saved.Failure is null) ||
            saved.Failure is not (null or ModeBoundary) || (saved.WidthSetting is null) != (saved.HeightSetting is null) ||
            saved.Writes is null || saved.Writes.Select(item => item.Observation).Distinct().Count() != saved.Writes.Count)
            throw new InvalidDataException("Source display lost its constructor, configured fields or observed writer prefix.");
        var width = FalloutPlayerControlHudSource.InitialWidth; var height = FalloutPlayerControlHudSource.InitialHeight;
        byte wide = 0; long sequence = 0; var available = true;
        if (saved.WidthSetting is { } widthSetting && saved.HeightSetting is { } heightSetting)
        {
            width = ValidateSetting(widthSetting, FalloutPlayerControlHudSource.WidthSetting, width);
            height = ValidateSetting(heightSetting, FalloutPlayerControlHudSource.HeightSetting, height); sequence = 2;
        }
        foreach (var write in saved.Writes)
        {
            if (!available || write.Observation == Guid.Empty || string.IsNullOrWhiteSpace(write.Producer) ||
                write.Changed != sequence + (write.DimensionsStored ? 3 : 1) ||
                write.Returned != (write.Wide is not null) || write.Wide is > 1 ||
                !write.DimensionsStored && (write.Width != width || write.Height != height) ||
                !write.Returned && write.DimensionsStored)
                throw new InvalidDataException("Source display lost an actual ordered mode writer.");
            if (write.DimensionsStored) { width = write.Width; height = write.Height; }
            if (write.Returned)
            {
                wide = WideFor(width, height);
                if (write.Wide != wide) throw new InvalidDataException("Source layout byte changed its independent Float32 ratio writer.");
            }
            else available = false;
            sequence = write.Changed;
        }
        if (saved.Width != width || saved.Height != height || saved.Wide != wide || saved.Sequence != sequence || saved.Available != available)
            throw new InvalidDataException("Source display differs from its actual loader and ordered writer history.");
        static int ValidateSetting(FalloutIniValue setting, string name, int initial)
        {
            if (setting.Declaration.Name != name || setting.Declaration.Collection != FalloutIniCollection.Renderer ||
                setting.Declaration.Kind != 'i' || setting.Declaration.Payload != (uint)initial || string.IsNullOrWhiteSpace(setting.Origin) ||
                setting.Number is not { } value || value != Math.Truncate(value) || value is < int.MinValue or > int.MaxValue)
                throw new InvalidDataException("Source display snapshot lost its actual declared Renderer setting.");
            return (int)value;
        }
    }
    internal static byte WideFor(int width, int height)
    {
        var ratio = (float)height / (float)width;
        return (byte)(!float.IsNaN(ratio) && ratio == 0.75f ? 0 : 1);
    }
    internal static (float Width, float Height) LogicalDimensions(int sourceWidth, int sourceHeight)
    {
        var width = (float)sourceWidth; var height = (float)sourceHeight;
        return (width > height ? (float)((double)width / height * FalloutPlayerControlHudSource.LogicalHeight) : FalloutPlayerControlHudSource.LogicalWidth,
            height > width ? (float)((double)height / width * FalloutPlayerControlHudSource.LogicalWidth) : FalloutPlayerControlHudSource.LogicalHeight);
    }
    private void RequireCurrent()
    {
        if (_thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Source display changed its actual current-thread field owner.");
    }
    private long Next() => _sequence = checked(_sequence + 1);
}
