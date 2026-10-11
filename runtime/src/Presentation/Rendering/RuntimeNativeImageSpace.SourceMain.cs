using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Rendering;

internal partial class RuntimeNativeImageSpace : IFalloutSourceImageSpaceNative
{
    private readonly Guid _sourceNativeIdentity = Guid.NewGuid();
    private Guid _sourceNativeProcess;
    private string? _sourceNativeSource;
    private readonly int _sourceNativeThread = System.Environment.CurrentManagedThreadId;
    private bool _sourceNativeConstructionEntered, _sourceNativeConstructed, _sourceNativeBound, _sourceNativeRetired;
    private FalloutSourceImageSpaceOutput? _sourceNativeEffects;
    private FalloutImageSpaceFrame? _sourceNativeFrame;
    public Guid NativeIdentity => _sourceNativeIdentity;
    public Guid Process => _sourceNativeProcess;
    public string SourceIdentity => _sourceNativeSource ??
        throw new InvalidOperationException("Native image output has no actual selected source manager.");
    internal bool UsesSourceMain => _sourceNativeConstructed;
    internal SourceImageNativeFields? SourceFields => _effect.SourceNativeFields;

    internal void ConfigureSourceMain(FalloutImageSpaceState state, RetailHdrCompositorEffect effect,
        FalloutGameTime time, Guid process)
    {
        if (_sourceNativeConstructed || _sourceNativeRetired || _state is not null)
            throw new InvalidOperationException("Image-space presenter configuration cannot replace living source fields.");
        Name = "NativeImageSpace"; ProcessMode = ProcessModeEnum.Always; ProcessPriority = 100;
        _state = state; _effect = effect; _gameTime = time;
        if (effect.DoubleVisionPhase is not null) BindCurrentPhaseClock(time, process);
        ConstructSourceMainOutput(process);
        SetMeta("opennv_image_space_program_source", effect.SourceProgramIdentity);
        SetMeta("opennv_image_space_kernel_sha256", effect.SourceKernelSha256);
    }

    internal void ConstructSourceMainOutput(Guid process)
    {
        if (_sourceNativeConstructed || _sourceNativeRetired || !_state.HasSourceMainImages || process == Guid.Empty)
            throw new InvalidOperationException("Image native factory changed its actual source constructor/process.");
        if (System.Environment.CurrentManagedThreadId != _sourceNativeThread)
            throw new InvalidOperationException("Image native factory changed its actual construction thread.");
        _sourceNativeProcess = process; _sourceNativeSource = _state.SourceMainImageIdentity;
        _sourceNativeConstructionEntered = true;
        _effect.ConstructSourceReceiverFields(_state.SourceMainImageDofConstructor);
        _sourceNativeConstructed = true;
    }

    internal void BindSourceMainOutput()
    {
        RequireCurrentFactory();
        if (_sourceNativeBound) throw new InvalidOperationException("Image native association cannot replay its current binding.");
        _state.BindSourceMainImageNative(this); _sourceNativeBound = true;
    }

    public void RequireCurrentFactory()
    {
        if (!_sourceNativeConstructed || _sourceNativeRetired || !GodotObject.IsInstanceValid(this) || !IsInsideTree() ||
            !GodotObject.IsInstanceValid(_effect) || System.Environment.CurrentManagedThreadId != _sourceNativeThread ||
            SourceIdentity != _state.SourceMainImageIdentity)
            throw new InvalidOperationException("Image output requires its actual living current native factory/thread/tree.");
        _effect.RequireSourceReceiverFactory();
    }

    public IDisposable EnterSourceImageContext()
    { RequireCurrentFactory(); return _effect.EnterSourceAllocationCategory(); }
    public void ResetOutputScalars()
    { RequireCurrentFactory(); _effect.ResetSourceOutputScalars(); }
    public void PublishEffects(FalloutSourceImageSpaceOutput value)
    {
        RequireCurrentFactory(); RequireOutput(value);
        _effect.StoreSourceEffectFields(value); _sourceNativeEffects = value;
    }
    public void PublishProfile(FalloutSourceImageSpaceOutput value)
    {
        RequireCurrentFactory(); RequireOutput(value);
        if (!ReferenceEquals(_sourceNativeEffects, value))
            throw new InvalidOperationException("Image native profile did not follow its actual same-call effect writes.");
        _effect.StoreSourceProfileGlobals(value);
        _sourceNativeFrame = _state.ReadSourceMainImageFrame(value);
        PublishCurrentSkyHdr(_sourceNativeFrame);
        _effect.SetSourceMainFrame(_sourceNativeFrame, BitConverter.UInt32BitsToSingle(value.Delta));
        Frame = _menuBackground ?? _sourceNativeFrame;
    }
    private void RequireOutput(FalloutSourceImageSpaceOutput value)
    {
        if (!_sourceNativeBound || value.Process != Process || value.Invocation == Guid.Empty || value.Mutation <= 0)
            throw new InvalidDataException("Image native output changed current process or lost its actual Main publication.");
        value.Profile.Validate();
    }
    internal void RetireSourceMainOutput()
    {
        if (!_sourceNativeConstructionEntered || _sourceNativeRetired) return;
        if (_sourceNativeBound) { _state.RetireSourceMainImageNative(this); _sourceNativeBound = false; }
        _effect.RetireSourceReceiverFields(); _sourceNativeRetired = true;
    }
}
