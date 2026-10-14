using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal void BindStandalonePlayerOpacity(Func<FalloutActorLogicalScene?> readFirstPersonScene)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(readFirstPersonScene);
        var scene = CampaignStandalonePlayerScene;
        var owned = records.OwnedSource ?? throw new NotSupportedException("source-Player-opacity-owned-executable-unbound");
        var source = FalloutExecutableStringTable.ReadPlayerOpacity(owned.FalloutExecutablePath,
            FalloutPlayerMovementControlSource.Reviewed(scene.Source.Main));
        var owner = new FalloutPlayerOpacityState(source, scene.Stack, scene.Process,
            scene.ReadOpacityFirstPerson, readFirstPersonScene, scene.ReadOpacityScaledTimerBits,
            name => BitConverter.SingleToUInt32Bits(FalloutGameSettingFloats.Read(records, name)),
            scene.RequireOpacityInvocation, scene.PreparedPlayerOpacityRestore);
        scene.BindPlayerOpacity(owner, Update);

        FalloutProcessTeleportFadeUpdate? Update(Guid main, long ordinal, Guid invocation, FalloutPlayerOpacityState opacity)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            scene.RequireOpacityInvocation(main, ordinal);
            var actor = ActorProcesses.Read(_enginePlayer);
            if (actor.Retired || actor.Level != FalloutDetectionProcessLevel.High || !actor.Source.EnginePlayer)
                throw new NotSupportedException("source-Player-teleport-current-High-process-constructor-unbound");
            // The original state getter is conditional on the independent
            // loader gates. The ordinary zero-gate arm enters the update directly.
            if (opacity.SkipTeleportUpdate(() => ProcessCommon.ReadOpacity(_enginePlayer, actor.Epoch).Teleport?.State ??
                throw new NotSupportedException("source-Player-current-teleport-state-getter-unbound"))) return null;
            try
            {
                return ProcessCommon.UpdateTeleportFade(source, _enginePlayer, actor.Epoch, invocation,
                    (before, readCurrent, storeEffective) =>
                    {
                        if (before.Process.Actor != actor.Source || before.Process.Epoch != actor.Epoch ||
                            before.Process.Stack != scene.Stack || before.Process.Level != actor.Level)
                            throw new InvalidDataException("Teleport update changed the actual Actor process registration.");
                        return opacity.BindTeleportConsumers(main, ordinal, invocation, readCurrent, storeEffective);
                    });
            }
            finally
            {
                if (ProcessCommon.ReadTeleportFadeReceipt(_enginePlayer, actor.Epoch) is { } receipt &&
                    receipt.Trace.Invocation == invocation) scene.ObservePlayerTeleportFade(receipt);
            }
        }
    }
}
