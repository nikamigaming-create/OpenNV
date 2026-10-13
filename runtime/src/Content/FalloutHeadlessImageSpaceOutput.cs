namespace OpenNV.Runtime.Content;

// An actual C# receiver factory for the no-renderer backend. It owns parameter
// arrays, scopes and ordered writes, without a native HWND, compositor or GPU.
internal sealed class FalloutHeadlessImageSpaceOutput : IFalloutSourceImageSpaceNative, IDisposable
{
    private readonly FalloutSourceImageSpaceReceiver _receiver = new();
    private readonly Action<FalloutSourceImageSpaceOutput> _publish;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private bool _retired;
    internal SourceImageNativeFields? Fields => _receiver.SourceNativeFields;
    internal object State => new
    {
        adapter = "headless-source-image-fields",
        outputFactory = NativeIdentity.ToString(),
        process = Process.ToString(),
        retired = _retired,
        cpuAllocations = _receiver.AllocationCount,
        nativeResources = false,
        captureCount = 0,
        drawCount = 0,
        fields = Fields,
        finalPixels = "UNEXECUTED",
    };
    public Guid NativeIdentity { get; } = Guid.NewGuid();
    public Guid Process { get; }
    public string SourceIdentity { get; }
    internal FalloutHeadlessImageSpaceOutput(string source, Guid process, FalloutSourceImageSpaceDofConstructor constructor,
        Action<FalloutSourceImageSpaceOutput> publish)
    {
        if (process == Guid.Empty || source.Length != 64 || !source.All(Uri.IsHexDigit))
            throw new InvalidDataException("Headless image receiver has no selected manager/process.");
        SourceIdentity = source; Process = process; _publish = publish ?? throw new ArgumentNullException(nameof(publish));
        _receiver.ConstructSourceReceiverFields(constructor);
    }
    public void RequireCurrentFactory()
    {
        if (_retired || Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Headless image receiver left its living constructor/thread.");
        _receiver.RequireSourceReceiverFactory();
    }
    public IDisposable EnterSourceImageContext()
    { RequireCurrentFactory(); return _receiver.EnterSourceAllocationCategory(); }
    public void ResetOutputScalars()
    { RequireCurrentFactory(); _receiver.ResetSourceOutputScalars(); }
    public void PublishEffects(FalloutSourceImageSpaceOutput value)
    {
        RequireOutput(value); _receiver.StoreSourceEffectFields(value);
    }
    public void PublishProfile(FalloutSourceImageSpaceOutput value)
    {
        RequireOutput(value); _receiver.StoreSourceProfileGlobals(value); _publish(value);
    }
    private void RequireOutput(FalloutSourceImageSpaceOutput value)
    {
        RequireCurrentFactory();
        if (value.Process != Process || value.Invocation == Guid.Empty || value.Mutation <= 0 ||
            !Enum.IsDefined(value.Caller) || value.Caller != FalloutSourceImageSpaceCaller.Main && value.Delta != 0)
            throw new InvalidDataException("Headless image fields belong to another process/call.");
    }
    public void Dispose()
    {
        if (_retired) return;
        RequireCurrentFactory(); _receiver.RetireSourceReceiverFields(); _retired = true;
    }
}
