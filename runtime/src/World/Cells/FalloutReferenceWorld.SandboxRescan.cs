using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private readonly FalloutSandboxProcessClock _sandboxProcessClock = new();
    private RuntimeLiveContentSource? _sandboxClockQualifiedContent;
    internal void ObserveSandboxProcessClock(bool paused) => _sandboxProcessClock.Observe(paused);
    internal IReadOnlyList<FalloutSandboxCandidateSource> SandboxCandidatesAtRescan(FalloutFormKey actor,
        FalloutSandboxPackage source, int level, FalloutGlobalState globals, float unitsToMetres)
    {
        if (RuntimeLiveContentSource.Current is { } content && _sandboxClockQualifiedContent != content)
        {
            if (FalloutInstallationSettings.Read(content).Unsigned("General", "iFPSClamp") != 0)
                throw new NotSupportedException("Sandbox rescan needs the configured FPS-clamped process timer owner.");
            _sandboxClockQualifiedContent = content;
        }
        var state = Actor(actor);
        var package = records.GetEffective(source.Form);
        var owner = state.SandboxRescan;
        if (owner is null || !owner.Matches(package))
            state.SandboxRescan = owner = new(new(package.FormKey,
                package.ReadDataHash(), 0, []), _sandboxProcessClock);
        if (owner.Due)
        {
            var candidates = SandboxCandidateSources(actor, source, level, globals, unitsToMetres);
            owner.Refresh(candidates, records.NumericSettings.IntegerBits("iMinSandboxRescanSeconds"),
                records.NumericSettings.IntegerBits("iMaxSandboxRescanSeconds"), state.SoundRandom.NextUInt32);
        }
        return owner.Candidates;
    }
}
