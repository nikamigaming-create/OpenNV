using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutSkyHdrState
{
    private readonly FalloutSkyTransferDeclaration _skySource;
    private readonly FalloutNumericIniSettings _settings;
    private readonly int _thread = System.Environment.CurrentManagedThreadId;
    private readonly Dictionary<Guid, long> _imageOrdinals = [];
    private IFalloutSkyHdrCapability? _nativeCapability;
    private IFalloutSkyHdrCapability? _enteredCapability;
    private Guid? _retiringCapability;
    private readonly HashSet<Guid> _borrowers = [];
    private long _changed = 1;
    private bool _selected, _retired;
    private string? _failure;
    private FalloutSkyHdrWrite _main = new(1, FalloutSkyHdrWriteKind.Constructor, 0x3f800000, null, null, null, null, null);
    private FalloutSkyHdrWrite _secondary = new(1, FalloutSkyHdrWriteKind.Constructor, 0x3f800000, null, null, null, null, null);
    private FalloutSkyHdrWrite? _copy;
    private FalloutSkyHdrCapability? _selection;
    private FalloutSkyHdrIni _ini;
    internal Guid Identity { get; } = Guid.NewGuid();
    internal Guid ShaderIdentity { get; } = Guid.NewGuid();
    internal Guid Sky { get; }
    internal Guid Process { get; }
    internal FalloutSkyHdrSource Source { get; }
    internal byte EnabledByte { get; private set; }
    internal uint MainScalarBits => _main.ValueBits;
    internal uint SecondaryScalarBits => _secondary.ValueBits;
    internal uint? ShaderScalarBits => _copy?.ValueBits;
    internal string? SaveBlocker => _failure ?? (_retired ? "source-Sky-HDR-retired" :
        _enteredCapability is not null ? "source-Sky-HDR-native-selection-entered-not-returned" :
        _retiringCapability is not null ? "source-Sky-HDR-native-retirement-entered-not-returned" :
        _selected && _nativeCapability is null ? "source-Sky-HDR-current-native-capability-republication-pending" : null);
    internal object State => new
    {
        source = Source,
        owner = Identity,
        shader = ShaderIdentity,
        sky = Sky,
        process = Process,
        changed = _changed,
        enabledByte = EnabledByte,
        selected = _selected,
        ini = _ini,
        main = _main,
        secondary = _secondary,
        shaderCopy = _copy,
        capability = _selection,
        unowned = _failure,
        nativeDraw = "unverified",
        headset = "unverified"
    };

    internal FalloutSkyHdrState(FalloutSkyTransferDeclaration source, Guid sky, Guid process, FalloutNumericIniSettings settings)
    {
        source.Validate();
        if (sky == Guid.Empty || process == Guid.Empty) throw new InvalidDataException("Sky HDR constructor omitted actual source Sky/process ownership.");
        _skySource = source; _settings = settings; Source = FalloutSkyHdrSource.Read(source);
        Sky = sky; Process = process; _ini = Source.ReadSetting(settings);
    }
    private long Next() => _changed = checked(_changed + 1);
    private void RequireLifetime()
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (System.Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("Sky HDR left its actual source/presentation-thread lease.");
        Source.Require(_skySource);
    }
    private void Require()
    {
        RequireLifetime();
        if (_failure is not null) throw new InvalidOperationException("Sky HDR retains a failed original producer prefix.", new InvalidOperationException(_failure));
    }
    internal void SelectRenderer(IFalloutSkyHdrCapability native)
    {
        Require(); ArgumentNullException.ThrowIfNull(native);
        if (_nativeCapability is not null && !ReferenceEquals(_nativeCapability, native))
            throw new InvalidOperationException("A different actual HDR capability still owns the renderer-selection field.");
        var capability = native.Read(); RequireCapability(capability);
        var ini = Source.ReadSetting(_settings);
        // This is the original producer's Boolean store, not shader or scene
        // readiness inferred from an installed callback or renderer name.
        EnabledByte = (byte)(ini.Requested && capability.HalfFloatRgbaBlendSupported ? 1 : 0);
        if (_enteredCapability is not null && !ReferenceEquals(_enteredCapability, native))
            throw new InvalidDataException("HDR renderer selection returned another entered native owner.");
        _ini = ini; _selection = capability; _nativeCapability = native; _enteredCapability = null; _selected = true; Next();
    }
    internal bool RendererBoundTo(IFalloutSkyHdrCapability native) => _selected && ReferenceEquals(_nativeCapability, native);
    internal void EnterRendererCapability(IFalloutSkyHdrCapability native)
    {
        Require();
        if (_retiringCapability is not null || _enteredCapability is not null || _nativeCapability is not null)
            throw new InvalidOperationException("HDR already has an actual entered/current/retiring renderer owner.");
        _enteredCapability = native; Next();
    }
    internal void AttachImageBorrower(Guid presenter)
    {
        Require();
        if (presenter == Guid.Empty || _retiringCapability is not null || !_borrowers.Add(presenter))
            throw new InvalidOperationException("HDR image borrower is duplicate, empty or retiring.");
    }
    internal void DetachImageBorrower(Guid presenter)
    {
        RequireLifetime();
        if (!_borrowers.Remove(presenter)) throw new InvalidOperationException("HDR image retirement lost its actual borrower.");
    }
    private void RequireCapability(FalloutSkyHdrCapability capability)
    {
        if (capability.Identity == Guid.Empty || capability.Process != Process || capability.EngineSha256 != Source.EngineSha256 ||
            capability.Contract != Source.Contract || capability.Returned < 1 || !capability.Current ||
            string.IsNullOrWhiteSpace(capability.Producer))
            throw new InvalidDataException("HDR selection borrowed a missing, foreign or retired actual format-query return.");
    }
    private FalloutSkyHdrCapability CurrentCapability()
    {
        if (!_selected || _nativeCapability is null) throw new NotSupportedException("source-Sky-HDR-actual-renderer-format-query-not-returned");
        var result = _nativeCapability.Read(); RequireCapability(result);
        if (_selection is null || result.Identity != _selection.Identity || result.HalfFloatRgbaBlendSupported != _selection.HalfFloatRgbaBlendSupported ||
            result.Returned != _selection.Returned || result.Provider != _selection.Provider || Source.ReadSetting(_settings) != _ini)
            throw new InvalidDataException("HDR capability or typed setting changed without the original renderer-selection writer.");
        return result;
    }
    internal void PublishImage(FalloutSkyHdrImageInput input)
    {
        Require(); _ = CurrentCapability();
        if (input.Process != Process || input.Frame == Guid.Empty || input.ImageOwner == Guid.Empty || input.Ordinal < 1 ||
            input.EngineSha256 != Source.EngineSha256 || input.Contract != Source.Contract || input.DnamSha256.Length != 64 ||
            !input.DnamSha256.All(Uri.IsHexDigit) || !float.IsFinite(BitConverter.UInt32BitsToSingle(input.ScalarBits)))
            throw new InvalidDataException("HDR image publication changed its actual source/form/frame/Float32 owner.");
        if ((input.Form is null) != (input.FormVersion is null) ||
            (input.Form is null) != (input.AnonymousImageSource is not null) ||
            input.AnonymousImageSource is { } anonymous && (anonymous != input.DnamSha256 || !FalloutSourceMainFamily.IsFallout3(Source.EngineSha256)))
            throw new InvalidDataException("HDR image input changed its winning or actual anonymous source constructor.");
        if (input.Unowned is { } unowned) throw new NotSupportedException(unowned);
        if (_imageOrdinals.TryGetValue(input.ImageOwner, out var previous) && input.Ordinal <= previous)
            throw new InvalidOperationException("HDR image writer replayed or overtook its actual publication ordinal.");
        var write = new FalloutSkyHdrWrite(Next(), input.Alternate ? FalloutSkyHdrWriteKind.AlternateImage : FalloutSkyHdrWriteKind.OrdinaryImage,
            input.ScalarBits, input.Frame, input.ImageOwner, input.Form, input.DnamSha256, _selection!.Identity,
            input.Ordinal, input.FormVersion, input.AnonymousImageSource);
        _imageOrdinals[input.ImageOwner] = input.Ordinal;
        if (input.Alternate) _secondary = write;
        else
        {
            _main = write;
            _secondary = write with { Ordinal = Next(), ValueBits = 0x3f800000 };
        }
    }
    internal void PublishSkyResourceLoad(Guid actualLoad, Action requireActualLoad)
    {
        Require(); _ = CurrentCapability(); ArgumentNullException.ThrowIfNull(requireActualLoad); requireActualLoad();
        if (actualLoad == Guid.Empty) throw new InvalidDataException("Sky resource scalar has no actual entered source loader.");
        if (EnabledByte == 0) return; // Original branch preserves the global cell.
        _main = new(Next(), FalloutSkyHdrWriteKind.SkyResourceLoad, 0x3f800000, actualLoad, null, null, null, _selection!.Identity);
        requireActualLoad();
    }
    internal FalloutMoonShaderFieldSample ReadShader(Guid actualFrame)
    {
        Require(); var capability = CurrentCapability();
        if (capability.Provider == FalloutSkyHdrCapabilityProvider.HeadlessNoRenderer)
            throw new NotSupportedException("Headless simulation has no returned Sky shader draw.");
        if (actualFrame == Guid.Empty) throw new InvalidDataException("Sky shader has no actual calling frame.");
        if (EnabledByte != 0) _copy = _main with { Ordinal = Next(), Kind = FalloutSkyHdrWriteKind.ShaderCopy, Frame = actualFrame };
        var copy = _copy ?? throw new NotSupportedException("source-Sky-HDR-disabled-shader-field-has-no-returned-prior-writer");
        var readAt = Next();
        var provenance = new FalloutSkyHdrShaderRead(Identity, ShaderIdentity, copy.Ordinal, readAt, copy, capability);
        return new(Source.EngineSha256, Source.SkyContract, Sky, Process, actualFrame, readAt, EnabledByte != 0, copy.ValueBits, provenance);
    }
    internal void BeginCapabilityRetirement(IFalloutSkyHdrCapability owner)
    {
        RequireLifetime();
        if (_borrowers.Count != 0 || !ReferenceEquals(_nativeCapability, owner) && !ReferenceEquals(_enteredCapability, owner))
            throw new InvalidOperationException("HDR retirement has foreign or living actual image borrowers.");
        _nativeCapability = null; _enteredCapability = null; _retiringCapability = owner.Identity; Next();
    }
    internal void CompleteCapabilityRetirement(IFalloutSkyHdrCapability owner, Action requireActualNativeRetirement)
    {
        RequireLifetime();
        if (_retiringCapability != owner.Identity || _borrowers.Count != 0)
            throw new InvalidOperationException("HDR native retirement returned another actual owner.");
        requireActualNativeRetirement(); _retiringCapability = null; Next();
    }
    internal void RetainFailure(Exception failure)
    {
        _failure ??= failure.ToString(); Next();
    }
    internal void Retire()
    {
        if (_retired) return;
        if (System.Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("HDR retirement left its source thread.");
        if (_nativeCapability is not null || _enteredCapability is not null || _retiringCapability is not null || _borrowers.Count != 0)
            throw new InvalidOperationException("HDR still has an actual native capability/retirement/image borrower.");
        _retired = true; Next();
    }
}
