using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutAnimationProcessQueryBinding(Guid Context, Guid Factory, Guid ReferenceProcess,
    Guid CommonProcess, Guid Constructor, long Epoch, Guid Load, string Executable,
    FalloutCombatActorIdentity Actor, Guid Root, Guid Lifetime, Guid Manager)
{
    internal void Validate(FalloutProcessSceneQuery query)
    {
        if (Context == Guid.Empty || Context != query.AnimationContext || Factory == Guid.Empty || ReferenceProcess == Guid.Empty ||
            CommonProcess != query.Process || Constructor == Guid.Empty || Epoch != query.Epoch || Load == Guid.Empty ||
            Executable != query.ExecutableSha256 || Actor != query.Actor || Root == Guid.Empty || Lifetime == Guid.Empty || Manager == Guid.Empty)
            throw new InvalidDataException("Player process query borrowed another real Common AnimData lifetime or model.");
    }
}

internal sealed partial class FalloutSourceAnimationContextState
{
    internal FalloutAnimationProcessQueryBinding ReadCommonProcessQuery(FalloutProcessSceneQuery query)
    {
        RequireIdle();
        if (Construction.Role != FalloutLoadedAnimationRole.Common || query.Actor != Actor || query.Process != CommonProcess ||
            query.Epoch != Construction.Invocation.Epoch || query.AnimationContext != Identity ||
            query.ExecutableSha256 != Source.Scene.Main.EngineSha256 || _failure is not null || _writer is not null ||
            LastBind is not { Returned: true } || _root is null || _manager is null)
            throw new InvalidOperationException("Player process did not read its same returned, current Common AnimData object.");
        _manager.RequireModelBinding(_root.LogicalScene);
        var binding = new FalloutAnimationProcessQueryBinding(Identity, Factory, Process, CommonProcess,
            Construction.Invocation.ReferenceConstructor, Construction.Invocation.Epoch, Construction.Invocation.Call,
            query.ExecutableSha256, Actor, _root.Node.Factory, _root.LogicalScene.Lifetime, _manager.Factory);
        binding.Validate(query); return binding;
    }
}
