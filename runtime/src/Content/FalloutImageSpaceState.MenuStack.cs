using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal enum FalloutSourceMenuImageField { Enabled, Progress }
internal sealed record FalloutSourceMenuImageConstructor(Guid Process, Guid Factory, long Changed);
internal sealed record FalloutSourceMenuImageWrite(Guid Invocation, Guid Interface, Guid Menu,
    FalloutSourceMenuImageField Field, long Changed, byte? Enabled, uint? Progress, Guid Process, Guid Factory);
internal sealed record FalloutSourceMenuImageFields(FalloutStandaloneMenuLifecycleSource Source, Guid Process, Guid Factory,
    byte? Enabled, uint? Progress, IReadOnlyList<FalloutSourceMenuImageWrite> Writes,
    IReadOnlyList<FalloutSourceMenuImageConstructor> Constructors);

internal sealed partial class FalloutImageSpaceState
{
    private FalloutStandaloneMenuLifecycleSource? _sourceMenuStack;
    private readonly Guid _sourceMenuStackFactory = Guid.NewGuid();
    private byte? _sourceMenuStackEnabled;
    private uint? _sourceMenuStackProgress;
    private readonly List<FalloutSourceMenuImageWrite> _sourceMenuStackWrites = [];
    private readonly List<FalloutSourceMenuImageConstructor> _sourceMenuStackConstructors = [];

    internal void BindSourceMenuStackEffect(FalloutStandaloneMenuLifecycleSource source)
    {
        RequireImageThread(); source.Validate();
        if (_sourceMenuStack is not null || _sourceImageMain is null || source.Interface.Main != _sourceImageMain)
            throw new InvalidOperationException("Menu image effect changed the actual shared ImageSpace manager.");
        _sourceMenuStack = source;
        // The selected constructor owns a null resource pointer. Its later
        // scalar and byte are unwritten until an actual Interface child stores.
        _sourceMenuStackEnabled = null; _sourceMenuStackProgress = null;
        _sourceMenuStackConstructors.Add(new(_sourceImageProcess, _sourceMenuStackFactory, checked(++_sourceImageChanged)));
    }

    private void RestoreSourceMenuStackEffect(FalloutSourceMenuImageFields? saved)
    {
        if (saved is null) return;
        ValidateSourceMenuStackEffect(saved);
        var expected = FalloutStandaloneMenuLifecycleSource.Reviewed(FalloutStandaloneInterfaceSource.Read(_sourceImageMain!));
        if (saved.Source != expected || saved.Constructors.Any(row => row.Factory == _sourceMenuStackFactory || row.Process == _sourceImageProcess))
            throw new InvalidDataException("Cold menu image effect changed source or reused a current construction owner.");
        _sourceMenuStackConstructors.AddRange(saved.Constructors); _sourceMenuStackWrites.AddRange(saved.Writes);
        // Original fields are history. A fresh real effect constructor still
        // leaves its current scalar and byte unknown; no old callback replays.
    }

    internal FalloutSourceMenuImageWrite StoreSourceMenuStackByte(FalloutStandaloneMenuFieldInvocation invocation, byte value)
    {
        RequireImageThread(); invocation.Require(invocation.Step);
        if (_sourceMenuStack is null || invocation.Process != _sourceImageProcess ||
            invocation.Owner.Source != _sourceMenuStack.Interface ||
            invocation.Step is not (FalloutStandaloneMenuFieldStep.Push or FalloutStandaloneMenuFieldStep.Remove or FalloutStandaloneMenuFieldStep.Transition) || value > 1)
            throw new InvalidOperationException("Image menu-byte store lost its actual Interface operation.");
        _sourceMenuStackEnabled = value;
        var write = new FalloutSourceMenuImageWrite(invocation.Identity, invocation.Owner.Constructor,
            invocation.Object, FalloutSourceMenuImageField.Enabled, checked(++_sourceImageChanged), value, null, _sourceImageProcess, _sourceMenuStackFactory);
        _sourceMenuStackWrites.Add(write); return write;
    }

    internal byte ReadSourceMenuStackEnabled(FalloutStandaloneMenuFieldInvocation invocation)
    {
        RequireImageThread(); invocation.Require(FalloutStandaloneMenuFieldStep.Transition);
        if (_sourceMenuStack is null || invocation.Process != _sourceImageProcess || invocation.Owner.Source != _sourceMenuStack.Interface)
            throw new InvalidOperationException("Menu effect byte read changed its current actual owner.");
        return _sourceMenuStackEnabled ?? throw new NotSupportedException("source-menu-effect-current-enabled-byte-unwritten");
    }
    internal FalloutSourceMenuImageWrite StoreSourceMenuStackProgress(FalloutStandaloneMenuFieldInvocation invocation, uint value)
    {
        RequireImageThread(); invocation.Require(invocation.Step);
        if (_sourceMenuStack is null || invocation.Process != _sourceImageProcess ||
            invocation.Owner.Source != _sourceMenuStack.Interface || invocation.Step != FalloutStandaloneMenuFieldStep.Transition)
            throw new InvalidOperationException("Image menu-progress store lost its actual current Interface transition.");
        if (_sourceMenuStackEnabled != 1)
            throw new InvalidOperationException("Menu transition must reach the actual enabled-byte arm before its scalar store.");
        _sourceMenuStackProgress = value;
        var write = new FalloutSourceMenuImageWrite(invocation.Identity, invocation.Owner.Constructor,
            invocation.Object, FalloutSourceMenuImageField.Progress, checked(++_sourceImageChanged), null, value, _sourceImageProcess, _sourceMenuStackFactory);
        _sourceMenuStackWrites.Add(write); return write;
    }

    internal FalloutSourceMenuImageFields CaptureSourceMenuStackEffect()
    {
        RequireImageThread();
        if (_sourceMenuStack is null) throw new InvalidOperationException("Source menu effect is unbound.");
        var result = new FalloutSourceMenuImageFields(_sourceMenuStack, _sourceImageProcess,
            _sourceMenuStackFactory, _sourceMenuStackEnabled, _sourceMenuStackProgress, _sourceMenuStackWrites.ToArray(), _sourceMenuStackConstructors.ToArray());
        ValidateSourceMenuStackEffect(result); return result;
    }
    internal static void ValidateSourceMenuStackEffect(FalloutSourceMenuImageFields fields)
    {
        fields.Source.Validate();
        if (fields.Process == Guid.Empty || fields.Factory == Guid.Empty || fields.Writes is null || fields.Constructors is null || fields.Constructors.Count == 0)
            throw new InvalidDataException("Image menu effect omitted its actual source/process constructor.");
        var constructors = fields.Constructors.ToDictionary(row => row.Factory);
        long prior = 0; var processes = new HashSet<Guid>();
        foreach (var row in fields.Constructors)
        {
            if (row.Process == Guid.Empty || row.Factory == Guid.Empty || row.Changed <= prior || !processes.Add(row.Process))
                throw new InvalidDataException("Menu effect repeated or omitted an actual fresh constructor.");
            prior = row.Changed;
        }
        var current = fields.Constructors[^1];
        if (current.Process != fields.Process || current.Factory != fields.Factory)
            throw new InvalidDataException("Menu effect fields selected another current constructor.");
        byte? enabled = null; uint? progress = null; long sequence = 0; Guid? activeFactory = null;
        foreach (var write in fields.Writes)
        {
            if (activeFactory != write.Factory) { enabled = null; progress = null; activeFactory = write.Factory; }
            if (!constructors.TryGetValue(write.Factory, out var owner) || owner.Process != write.Process || write.Changed <= owner.Changed ||
                write.Invocation == Guid.Empty || write.Interface == Guid.Empty || write.Menu == Guid.Empty || write.Changed <= sequence ||
                !Enum.IsDefined(write.Field) || write.Field == FalloutSourceMenuImageField.Enabled && (write.Enabled is null or > 1 || write.Progress is not null) ||
                write.Field == FalloutSourceMenuImageField.Progress && (write.Progress is null || write.Enabled is not null || enabled != 1))
                throw new InvalidDataException("Menu image fields changed their real ordered enabled/scalar writer.");
            if (write.Field == FalloutSourceMenuImageField.Enabled) enabled = write.Enabled; else progress = write.Progress;
            sequence = write.Changed;
        }
        if (activeFactory != fields.Factory) { enabled = null; progress = null; }
        if (fields.Enabled != enabled || fields.Progress != progress)
            throw new InvalidDataException("Menu image field has no actual writer; constructor silence cannot be a zero default.");
    }
}
