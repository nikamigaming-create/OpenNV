using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal static partial class FalloutNativeCampaignSave
{
    private static void ValidateActorConstructorSource(FalloutPluginStack records,
        FalloutActorProcessDeclaration declaration, FalloutCombatGroupDeclaration groups,
        FalloutActorProcessesSnapshot saved)
    {
        // A validation lifetime has no producer callbacks. Historical captured
        // thread relationships cannot certify any new-process native owner.
        var constructor = new FalloutActorProcessConstructionSource(records, declaration,
            saved.Stack, Guid.NewGuid(), actor => FalloutCombatActorSource.Read(records, groups, actor));
        constructor.ValidateRetained(saved);
    }
}
