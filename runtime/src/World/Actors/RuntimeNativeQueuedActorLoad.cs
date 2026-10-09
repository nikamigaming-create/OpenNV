using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

// The real reference caller, pure owned-file Task, native assembly and actual
// CELL publication share one retained queued object. Partial native work is
// never destroyed on a worker thread or by a mere cancellation request.
internal sealed partial class RuntimeNativeQueuedActorLoad : IDisposable
{
    private readonly FalloutReferenceWorld _world;
    private readonly RuntimeLiveContentSource _source;
    private readonly FalloutFormKey _reference;
    private readonly FalloutQueuedReferenceRead<PreparedOwnedInput> _read;
    private readonly int _thread = System.Environment.CurrentManagedThreadId;
    private RuntimeNativeNpc.Assembly? _assembly;
    private RuntimeNativeNpc? _transferred;
    private bool _assemblyEntered, _published, _disposed;
    internal FalloutNpcAppearance Appearance { get; }
    internal Task ReadTask => _read.ReadTask;
    internal RuntimeNativeQueuedActorLoad(FalloutReferenceWorld world, FalloutNpcAppearance appearance,
        RuntimeLiveContentSource source, string owner, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(world); ArgumentNullException.ThrowIfNull(appearance);
        ArgumentNullException.ThrowIfNull(source);
        _reference = appearance.Reference ?? throw new InvalidDataException("Source native queued actor load has no actual reference.");
        (_world, Appearance, _source) = (world, appearance, source);
        _read = world.BeginSourceNativeLoad(_reference, owner, token =>
            FalloutContentWorkers.Run(() => new PreparedOwnedInput(FalloutNpcPreparedGeometry.Read(appearance, source, token)), token),
            RetireEnteredNative, cancellation);
    }
    internal bool Advance(float units,
        Func<FalloutNpcAppearance, FalloutNpcAppearancePart, FalloutNifFile, FalloutNifGeometry, Material?> material,
        out RuntimeNativeNpc? actor)
    {
        RequireCurrent(); ArgumentNullException.ThrowIfNull(material); actor = null;
        if (_transferred is not null) throw new InvalidOperationException("Actual queued native actor was already transferred to its CELL caller.");
        if (!_read.ReadTask.IsCompleted) return false;
        if (!_assemblyEntered)
        {
            var prepared = _read.BeginAssembly().Take(); _assemblyEntered = true;
            _assembly = new(prepared, _source, units, material);
        }
        if (!(_assembly ?? throw new InvalidOperationException("Entered queued native assembly retains its constructor failure.")).Advance()) return false;
        _transferred = _assembly.Take();
        _assembly.Dispose(); _assembly = null;
        // Retain the exact transferred actor before entering the next source
        // consumer, so a failure cannot orphan the caller-owned native root.
        _read.AssemblyTransferred(); actor = _transferred; return true;
    }
    internal void PublicationReturned()
    {
        RequireCurrent();
        var actor = _transferred ?? throw new InvalidOperationException("Actual queued publication has no transferred actor.");
        if (_published || !GodotObject.IsInstanceValid(actor) || actor.IsQueuedForDeletion() || actor.GetParent() is null ||
            actor.Appearance.Reference != _reference)
            throw new InvalidDataException("Queued actor publication lost its exact living source/reference root.");
        _world.RequireSourceQueuedActorPublication(_reference, actor.GetInstanceId());
        _read.PublicationReturned(); _world.ForgetRetiredSourceQueuedRead(_read.Identity);
        _published = true; _transferred = null; _assembly?.Dispose(); _assembly = null;
    }
    private void RetireEnteredNative()
    {
        RequireThread();
        if (_transferred is { } actor && GodotObject.IsInstanceValid(actor))
            throw new NotSupportedException("Queued actor retirement still owns its caller-transferred actual native CELL child.");
        _assembly?.Dispose(); _assembly = null; _transferred = null;
        DiscardReturnedDecoderInput();
    }
    private void RequireThread()
    {
        if (System.Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Queued native actor assembly/publication/retirement has a foreign scene thread.");
    }
    private void RequireCurrent() { RequireThread(); ObjectDisposedException.ThrowIf(_disposed, this); }
    public void Dispose()
    {
        if (_disposed) return; RequireCurrent();
        if (!_published)
        {
            _read.RequestCancellation();
            if (!_read.ReadTask.IsCompleted)
                throw new NotSupportedException("Actual queued actor read remains entered; cancellation is not native retirement.");
            _read.RetireAfterReadReturned(RetireEnteredNative);
            _world.ForgetRetiredSourceQueuedRead(_read.Identity);
        }
        else RetireEnteredNative();
        _disposed = true;
    }
}
