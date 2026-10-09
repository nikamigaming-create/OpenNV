using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerIngestibles
{
    private FalloutPlayerAbilityScripts? _scripted;
    internal void BindScriptedEffects(FalloutPlayerAbilityScripts owner)
    {
        if (_scripted is not null) throw new InvalidOperationException("Aid script effects already have their shared instance owner.");
        _scripted = owner;
    }

    private FalloutScriptedIngestibleUse? PrepareScriptedEffects(FalloutIngestible source, FalloutIngestibleEffect[] selected)
    {
        if (!selected.Any(effect => effect.Archetype == 1)) return null;
        if (selected.Where(effect => effect.Archetype == 1).Any(effect => effect.Area != 0 || effect.Range != 0 || effect.AdditionalPresentation))
            throw new NotSupportedException("Scripted Aid still requires its source delivery/presentation owner.");
        return (_scripted ?? throw new NotSupportedException("Scripted Aid has no genuine shared active-effect owner."))
            .PrepareIngestible(source, selected);
    }
}
