using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessRuntimeState
{
    internal static void ValidateStandaloneMain(FalloutStandaloneMainSnapshot saved, FalloutActorProcessRuntimeSnapshot runtime)
    {
        if (saved is null || saved.Source is null || saved.MainField is null || saved.MainCaller is null || saved.PlayerCell is null)
            throw new InvalidDataException("Standalone Main omitted an actual cached field/caller/Player owner.");
        saved.Source.Validate();
        if (!FalloutSourceMainFamily.IsFallout3(saved.Source.EngineSha256) ||
            FalloutActorProcessRuntimeDeclaration.ForExecutable(saved.Source.EngineSha256).Contract != runtime.Contract ||
            saved.MainCaller.Source != saved.Source || saved.MainCaller.Stack != runtime.Stack ||
            saved.MainCaller.CapturedProcess != runtime.CapturedProcess || saved.MainCaller.Changed > runtime.Sequence ||
            saved.PlayerCell.Changed > runtime.Sequence)
            throw new InvalidDataException("Standalone Main changed its exact source/stack/captured process sequence.");
        RequireMainScriptSamples(saved.MainCaller, saved.MainField);
        RequireMainPlayerCellCaller(saved.PlayerCell, saved.MainCaller);
        RequireConstructor(saved.Constructor, current: true); RequireConstructor(saved.PreviousConstructor, current: false);
        if (saved.Constructor is { } constructed && saved.PreviousConstructor?.Identity == constructed.Identity)
            throw new InvalidDataException("Cold standalone Main promoted an old singleton identity.");
        if (saved.LastPrelude is { } call)
        {
            var constructor = new[] { saved.Constructor, saved.PreviousConstructor }.OfType<FalloutStandaloneMainConstructor>()
                .SingleOrDefault(value => value.Identity == call.Constructor);
            if (call.Main == Guid.Empty || call.MainOrdinal < 1 || call.MainOrdinal > saved.MainCaller.Calls ||
                call.Process == Guid.Empty || call.Changed < 1 || call.Changed > runtime.Sequence || constructor is null ||
                call.Process != constructor.Process || call.Changed <= constructor.Changed)
                throw new InvalidDataException("Standalone Main Prologue lost its actual lazy constructor/child epoch.");
        }
        if (saved.MainCaller.LastCall is { } main && main.Children.SingleOrDefault(value => value.Step == FalloutMainScriptCallerStep.Prologue) is { Returned: { } end } child)
        {
            if (saved.LastPrelude is not { } returnedPrelude || returnedPrelude.Main != main.Invocation || returnedPrelude.MainOrdinal != main.Ordinal ||
                returnedPrelude.Process != main.SourceProcess || returnedPrelude.Changed <= child.Entered || returnedPrelude.Changed >= end)
                throw new InvalidDataException("Returned FO3 Main Prologue omitted its exact current constructor/virtual-child return.");
        }
        void RequireConstructor(FalloutStandaloneMainConstructor? constructor, bool current)
        {
            if (constructor is null) return;
            if (constructor.Identity == Guid.Empty || constructor.Process == Guid.Empty || constructor.Changed < 1 ||
                constructor.Changed > runtime.Sequence || (constructor.Process == runtime.CapturedProcess) != current)
                throw new InvalidDataException("Standalone Main singleton retained a false native/current constructor lifetime.");
        }
    }
    internal static void RequireStandaloneMainPlayable(FalloutActorProcessRuntimeSnapshot runtime)
    {
        var selected = FalloutSourceMainFamily.IsFallout3(runtime.Fistp.ExecutableSha256);
        if (!selected)
        {
            if (runtime.StandaloneMain is not null) throw new InvalidDataException("Another source family owns foreign FO3 Main state.");
            return;
        }
        var saved = runtime.StandaloneMain ?? throw new InvalidDataException("Current FO3 save omitted its real Main/Player continuation.");
        ValidateStandaloneMain(saved, runtime);
        if (saved.MainField.Error is not null || saved.MainCaller.LastCall is { Disposition: FalloutMainScriptCallerDisposition.Failed } ||
            saved.PlayerCell.LastCall is { Disposition: FalloutMainPlayerCellDisposition.Failed } ||
            saved.PlayerCell.Pending.Error is not null || saved.PlayerCell.PendingConsumers.Error is not null ||
            saved.PlayerCell.Pending.Pending is { SourcePayload: null } || saved.PlayerCell.Pending.Pending?.SourcePayload?.Callback is not null)
            throw new NotSupportedException("FO3 current save retains a failed/unowned actual Main/Player prefix.");
    }
}
