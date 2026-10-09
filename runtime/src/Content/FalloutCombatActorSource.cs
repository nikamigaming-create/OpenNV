using System.Security.Cryptography;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal static class FalloutCombatActorSource
{
    internal static FalloutCombatActorIdentity Read(FalloutPluginStack records,
        FalloutCombatGroupDeclaration declaration, FalloutFormKey reference)
    {
        declaration.Validate();
        var player = reference == records.RuntimeFormKey(0x14);
        var placed = player ? null : records.GetEffective(reference);
        if (placed is not null && (placed.IsDeleted || placed.Signature is not ("ACHR" or "ACRE")))
            throw new InvalidDataException("Combat group actor has no exact winning placed source.");
        var basis = records.GetEffective(player ? records.RuntimeFormKey(7) : FalloutDialogueTopic.RequiredForm(placed!, "NAME"));
        if (basis.IsDeleted) throw new InvalidDataException("Combat group actor has a deleted winning base.");
        var identity = new FalloutCombatActorIdentity(reference, basis.FormKey,
            player ? "ENGINE_PLAYER" : placed!.Signature, placed?.Flags ?? 0,
            player ? declaration.ExecutableSha256 : Digest(placed!), basis.Signature, basis.Flags, Digest(basis), player);
        identity.Validate(); return identity;
    }

    private static string Digest(FalloutPluginRecord record) =>
        Convert.ToHexString(SHA256.HashData(record.ReadData())).ToLowerInvariant();
}
