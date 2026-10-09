using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessRuntimeState
{
    private FalloutMainScriptCallerSource? _standaloneMainSource;
    private FalloutStandaloneMainConstructor? _standaloneMainConstructor, _standaloneMainPreviousConstructor;
    private FalloutStandaloneMainPreludeCall? _standaloneMainLastPrelude;
    // These are the independently inspected source constructor's initialized
    // value lanes. No original pointer or native extension is reconstructed.
    private byte[]? _standaloneMainBuffer;
    private uint[]? _standaloneMainWords;
    private ushort _standaloneMainWord;
    private byte _standaloneMainByte;
    private bool _standaloneMainRetired;
    internal bool StandaloneMainConstructed => _standaloneMainSource is not null && MainScriptCallerConstructed && MainPlayerCellConstructed;
    internal void ConstructStandaloneMain(FalloutMainScriptCallerSource source, FalloutPlayerPendingSlot pending,
        FalloutStandaloneMainSnapshot? saved, FalloutActorProcessRuntimeSnapshot? runtime)
    {
        RequireNotBusy(); source.Validate();
        if (!FalloutSourceMainFamily.IsFallout3(source.EngineSha256) || source.EngineSha256 != _source.ExecutableSha256 ||
            _standaloneMainSource is not null || (saved is null) != (runtime is null))
            throw new InvalidDataException("Standalone Main must construct its own complete selected current/cold process once.");
        if (saved is not null)
        {
            ValidateStandaloneMain(saved, runtime!);
            if (saved.Source != source) throw new InvalidDataException("Cold standalone Main changed its original executable/runtime declaration.");
            _standaloneMainPreviousConstructor = saved.Constructor ?? saved.PreviousConstructor;
            _standaloneMainLastPrelude = saved.LastPrelude;
        }
        _standaloneMainSource = source;
        var field = new FalloutImmediateScriptSource(source.EngineSha256, source.RuntimeSha256,
            FalloutImmediateScriptSource.ContractForEngine(source.EngineSha256));
        ConstructScriptFrame(field, saved?.MainField, saved?.MainCaller.LastCall is { Disposition: FalloutMainScriptCallerDisposition.Failed });
        ConstructMainScriptCaller(source, saved?.MainCaller);
        ConstructMainPlayerCell(FalloutMainPlayerCellSource.Read(source), pending, saved?.PlayerCell);
    }
    internal void ExecuteStandaloneMainPrologue(FalloutMainScriptInvocation invocation)
    {
        RequireMainScriptChild(invocation, FalloutMainScriptCallerStep.Prologue);
        if (!StandaloneMainConstructed || _standaloneMainRetired || _standaloneMainSource != MainScriptCallerSource() ||
            !FalloutSourceMainFamily.IsFallout3(_standaloneMainSource.EngineSha256))
            throw new InvalidOperationException("FO3 source Prologue has no genuine current Main singleton getter lifetime.");
        if (_standaloneMainLastPrelude?.Main == invocation.Identity)
            throw new InvalidOperationException("FO3 Main Prologue cannot be re-entered in its own invocation.");
        if (_standaloneMainConstructor is null)
        {
            _standaloneMainBuffer = new byte[128];
            _standaloneMainWords = new uint[3];
            _standaloneMainWord = 0;
            _standaloneMainByte = 0;
            _standaloneMainConstructor = new(Guid.NewGuid(), _process, Next());
        }
        if (_standaloneMainBuffer is not { Length: 128 } || _standaloneMainBuffer.Any(value => value != 0) ||
            _standaloneMainWords is not { Length: 3 } || _standaloneMainWords.Any(value => value != 0) ||
            _standaloneMainWord != 0 || _standaloneMainByte != 0)
            throw new InvalidDataException("FO3 singleton constructor lanes changed without an admitted original writer.");
        // The actual selected virtual child is an empty return. This owns that
        // exact arm; it neither executes FNV utilities nor certifies other UI,
        // platform, scene, timed-context or whole-Main children.
        _standaloneMainLastPrelude = new(invocation.Identity, invocation.Ordinal,
            _standaloneMainConstructor.Identity, _process, Next());
    }
    private FalloutStandaloneMainSnapshot? CaptureStandaloneMain()
    {
        if (_standaloneMainSource is null) return null;
        if (!StandaloneMainConstructed) throw new NotSupportedException("Standalone Main retains its actual incomplete constructor prefix.");
        return new(_standaloneMainSource, CaptureMainScriptFrameEvidence(), CaptureMainScriptCaller(), CaptureMainPlayerCell(),
            _standaloneMainConstructor, _standaloneMainPreviousConstructor, _standaloneMainLastPrelude);
    }
    private void RetireStandaloneMain()
    {
        RequireMainScriptClosureBoundary(retiring: true);
        _standaloneMainRetired = true; _standaloneMainBuffer = null; _standaloneMainWords = null;
        // Captured invocation/constructor identities and original failures stay
        // visible. Clearing a C# buffer is not a returned source child.
    }
}
