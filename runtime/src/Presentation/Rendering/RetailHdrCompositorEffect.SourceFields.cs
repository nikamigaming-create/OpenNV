using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Rendering;

internal partial class RetailHdrCompositorEffect
{
    private readonly FalloutSourceImageSpaceReceiver _sourceReceiver = new();
    private bool _sourceReceiverFactory;
    internal SourceImageNativeFields? SourceNativeFields => _sourceReceiver.SourceNativeFields;
    private Exception? _sourceDrawFailure;
    internal string? SourceDrawFailure => Volatile.Read(ref _sourceDrawFailure)?.ToString();

    internal void RetainSourceDrawFailure(Exception failure)
    {
        if (_sourceReceiverFactory) Interlocked.CompareExchange(ref _sourceDrawFailure, failure, null);
    }

    internal void SetSourceMainFrame(FalloutImageSpaceFrame frame, float delta)
    {
        RequireSourceReceiverFactory();
        Volatile.Write(ref _parameters, SourceParameters(frame, delta));
    }

    internal void SetSourceMenuBackground(long serial, FalloutImageSpaceFrame frame, SourceImageNativeFields fields)
    {
        RequireSourceReceiverFactory();
        Volatile.Write(ref _menuBackground, new(serial, SourceParameters(frame, 0, fields)));
    }

    private FrameParameters SourceParameters(FalloutImageSpaceFrame frame, float delta, SourceImageNativeFields? retained = null)
    {
        var fields = retained ?? SourceNativeFields ?? throw new InvalidOperationException("Source image call did not write receiver fields.");
        if (fields.Globals.Length != 5)
            throw new InvalidOperationException("Source image call has not completed the ordered profile/global child.");
        static Godot.Vector4 Vector(System.Numerics.Vector4 value) => new(value.X, value.Y, value.Z, value.W);
        return new(frame.TargetLuminance, Vector(frame.Cinematic), Vector(frame.Tint), Vector(frame.Fade), delta,
            frame.BlurRadius, new(frame.DoubleVisionOffset.X, frame.DoubleVisionOffset.Y), fields);
    }

    internal void ConstructSourceReceiverFields(FalloutSourceImageSpaceDofConstructor dof)
    {
        if (!Godot.GodotObject.IsInstanceValid(this) || Volatile.Read(ref _released) != 0 || _sourceEffects is null)
            throw new InvalidOperationException("Source image factory has no living compositor/program resource.");
        _sourceReceiver.ConstructSourceReceiverFields(dof); _sourceReceiverFactory = true;
    }

    internal void RequireSourceReceiverFactory()
    {
        if (!_sourceReceiverFactory || !Godot.GodotObject.IsInstanceValid(this) ||
            Volatile.Read(ref _released) != 0 || _sourceEffects is null || !Enabled)
            throw new InvalidOperationException("Source image receiver/program factory is not currently alive.");
        _sourceReceiver.RequireSourceReceiverFactory();
        if (Volatile.Read(ref _sourceDrawFailure) is { } failure)
            throw new InvalidOperationException("Source image-space draw retains its actual native consumer failure.", failure);
    }

    internal IDisposable EnterSourceAllocationCategory()
    { RequireSourceReceiverFactory(); return _sourceReceiver.EnterSourceAllocationCategory(); }
    internal void ResetSourceOutputScalars()
    { RequireSourceReceiverFactory(); _sourceReceiver.ResetSourceOutputScalars(); }
    internal void StoreSourceEffectFields(FalloutSourceImageSpaceOutput value)
    { RequireSourceReceiverFactory(); _sourceReceiver.StoreSourceEffectFields(value); }
    internal void StoreSourceProfileGlobals(FalloutSourceImageSpaceOutput value)
    { RequireSourceReceiverFactory(); _sourceReceiver.StoreSourceProfileGlobals(value); }
    internal void RetireSourceReceiverFields()
    { _sourceReceiver.RetireSourceReceiverFields(); _sourceReceiverFactory = false; }
}
