using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal static class FalloutProcessTeleportFadeReceipt
{
    internal static void Validate(FalloutProcessTeleportFadeUpdate receipt, string stack,
        string executable, bool? enginePlayer = null)
    {
        if (receipt is null || receipt.Before is not { Actor: not null } before ||
            before.Process == Guid.Empty || before.Stack != stack || before.ExecutableSha256 != executable ||
            !FalloutSourceMainFamily.IsFallout3(executable) ||
            before.Epoch < 1 || before.Level != FalloutDetectionProcessLevel.High ||
            before.GameplayOwnership == Guid.Empty || before.Changed < 1 || receipt.Changed < before.Changed ||
            before.AnimationContext == Guid.Empty || before.IdlePrimaryPackage == Guid.Empty || before.ActorValueCache == Guid.Empty ||
            enginePlayer is { } player && before.Actor.EnginePlayer != player ||
            receipt.BeforeFields is null || receipt.AfterFields is null || receipt.Trace is null)
            throw new InvalidDataException("Teleport receipt changed its source, current High owner or retained fields.");
        before.Actor.Validate();
        FalloutActorTeleportFade.ValidateTransition(receipt.BeforeFields, receipt.AfterFields, receipt.Trace, before.Actor.EnginePlayer);
        if (receipt.Changed - before.Changed < receipt.AfterFields.Stores - receipt.BeforeFields.Stores)
            throw new InvalidDataException("Teleport receipt lost the Common sequence of its committed High stores.");
    }
}
