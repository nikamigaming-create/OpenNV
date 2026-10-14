using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal enum FalloutOriginalMenuWordWriter { ControlQuery, MenuDestructor }
internal sealed record FalloutOriginalMenuWordWrite(Guid Invocation, FalloutOriginalMenuWordWriter Writer,
    long BufferRevision, ushort Before, ushort After);
internal sealed record FalloutOriginalMenuClose(Guid Invocation, Guid InterfaceProcess, Guid Menu,
    Guid InputFactory, string Source, string Stack, long BufferRevision, ulong Sample, bool? ControllerSelected,
    IReadOnlyList<FalloutOriginalInputObservation> Observations, ushort Before, ushort After,
    int Count, bool Returned, string? Failure);
internal sealed record FalloutOriginalMouseBufferFlush(Guid Invocation, Guid InterfaceProcess, Guid Menu,
    Guid InputFactory, string Stack, Guid? NativeDeviceFactory, bool Present, int SourceElementExtent,
    int? NativeElementExtent, uint? BeforeCount, uint? AfterCount, int? Status, bool Returned);

internal sealed partial class FalloutDirectInputState
{
    private readonly List<FalloutOriginalMenuWordWrite> _originalMenuWordWrites = [];
    private FalloutOriginalMenuClose? _originalMenuClose;
    private readonly List<FalloutOriginalMenuClose> _originalMenuCloses = [];
    private Func<FalloutStandaloneMenuFieldInvocation, FalloutOriginalMouseBufferFlush>? _flushSourceMenuMouse;
    private readonly List<FalloutOriginalMouseBufferFlush> _originalMenuMouseFlushes = [];

    internal void BindSourceMenuMouseBufferFlush(Func<FalloutStandaloneMenuFieldInvocation, FalloutOriginalMouseBufferFlush> flush)
    {
        RequireOriginal(); ArgumentNullException.ThrowIfNull(flush);
        if (_flushSourceMenuMouse is not null) throw new InvalidOperationException("Menu mouse buffer already has an actual device owner.");
        _flushSourceMenuMouse = flush;
    }

    // This query reads the same constructor/poll-owned buffers and the same
    // MenuWord. It has its own genuine menu invocation, with no invented Main
    // or Player call and no device polling as a side effect.
    internal FalloutOriginalMenuClose ApplySourceMenuClose(FalloutStandaloneMenuFieldInvocation invocation)
    {
        RequireOriginal(); invocation.Require(FalloutStandaloneMenuFieldStep.InputClose);
        if (invocation.Stack != Source) throw new InvalidDataException("Menu destructor selected another current input stack.");
        var observations = new List<FalloutOriginalInputObservation>(2); var count = 0;
        _originalMenuClose = new(invocation.Identity, invocation.Process, invocation.Object, Factory,
            _originalSource!.Identity, Source, _bufferRevision, Sample, null, [], _menuWord, _menuWord, 0, false, null);
        _originalBusy = true; _originalFault = false;
        try
        {
            _originalPlatform!.RequireCurrent(); _originalHooks!.RequireOwner(this); CheckOriginalFault();
            var selected = _originalSelection is { Selected: { } value, Failure: null } ? value :
                throw new NotSupportedException("source-menu-close-current-controller-selection-unbound");
            _originalMenuClose = _originalMenuClose with { ControllerSelected = selected };
            if (selected) throw new NotSupportedException("source-menu-close-original-gamepad-count-unbound");
            var bindings = Controls.ReadSourceBinding(14);
            if (bindings.Keyboard != byte.MaxValue) { var read = ReadKeyboard(bindings.Keyboard, 0); observations.Add(read); count += read.Result; }
            if (bindings.Mouse != byte.MaxValue) { var read = ReadMouse(bindings.Mouse, 0); observations.Add(read); count += read.Result; }
            if (!_menuKnown) throw new NotSupportedException("source-menu-close-current-MenuWord-unowned");
            if (count > 0 && (_menuWord & MenuMask) != 0)
            {
                var before = _menuWord; _menuWord &= unchecked((ushort)~MenuMask); _menuWord |= MenuMask;
                _originalMenuWordWrites.Add(new(invocation.Identity, FalloutOriginalMenuWordWriter.ControlQuery,
                    checked(++_bufferRevision), before, _menuWord)); count = 0;
            }
            if (count != 0)
            {
                var before = _menuWord; _menuWord |= MenuMask;
                _originalMenuWordWrites.Add(new(invocation.Identity, FalloutOriginalMenuWordWriter.MenuDestructor,
                    checked(++_bufferRevision), before, _menuWord));
            }
            invocation.Require(FalloutStandaloneMenuFieldStep.InputClose);
            _originalPlatform.RequireCurrent(); CheckOriginalFault();
            return _originalMenuClose = _originalMenuClose with
            {
                Observations = observations.ToArray(),
                Count = count,
                After = _menuWord,
                BufferRevision = _bufferRevision,
                Returned = true
            };
        }
        catch (Exception error)
        {
            _originalMenuClose = _originalMenuClose with
            {
                Observations = observations.ToArray(),
                Count = count,
                After = _menuWord,
                BufferRevision = _bufferRevision,
                Failure = error.Message
            }; throw;
        }
        finally { _originalMenuCloses.Add(_originalMenuClose!); _originalBusy = false; }
    }

    internal FalloutOriginalMouseBufferFlush FlushSourceMenuMouseBuffer(FalloutStandaloneMenuFieldInvocation invocation)
    {
        RequireOriginal(); invocation.Require(FalloutStandaloneMenuFieldStep.QuietReturn);
        if (invocation.Stack != Source) throw new InvalidDataException("Menu quiet return selected another input stack.");
        var flush = _flushSourceMenuMouse ?? throw new NotSupportedException("source-menu-close-current-mouse-device-buffer-owner-unbound");
        _originalBusy = true; _originalFault = false;
        try
        {
            var result = flush(invocation);
            invocation.Require(FalloutStandaloneMenuFieldStep.QuietReturn); CheckOriginalFault();
            if (result.Invocation != invocation.Identity || result.InterfaceProcess != invocation.Process || result.Menu != invocation.Object ||
                result.InputFactory != Factory || result.Stack != Source || !result.Returned || result.SourceElementExtent != 20 ||
                result.Present != (result.NativeDeviceFactory is not null) ||
                result.Present && (result.NativeElementExtent is not (20 or 24) || result.BeforeCount != uint.MaxValue || result.AfterCount is null || result.Status is null) ||
                !result.Present && (result.NativeElementExtent is not null || result.BeforeCount is not null || result.AfterCount is not null || result.Status is not null))
                throw new InvalidDataException("Menu mouse flush changed its genuine nullable device, ABI extent or output prefix.");
            _originalMenuMouseFlushes.Add(result); return result;
        }
        finally { _originalBusy = false; }
    }
}
