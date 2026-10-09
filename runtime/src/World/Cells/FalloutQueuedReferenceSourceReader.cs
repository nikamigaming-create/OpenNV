using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal static class FalloutQueuedReferenceSourceReader
{
    internal static FalloutQueuedReferenceSource Read(FalloutPluginStack records,
        FalloutActorProcessQueueDeclaration declaration, FalloutFormKey reference)
    {
        declaration.Validate();
        if (reference == records.RuntimeFormKey(0x14))
        {
            var actor = FalloutCombatActorSource.Read(records,
                FalloutCombatGroupDeclaration.ForExecutable(declaration.ExecutableSha256), reference);
            return new(reference, actor.ReferenceSignature, actor.ReferenceSha256, actor.Base, actor.BaseSignature,
                actor.BaseSha256, FalloutQueuedReferenceKind.Player, true);
        }
        var record = records.GetEffective(reference);
        if (record.Signature is not ("REFR" or "ACHR" or "ACRE") || record.IsDeleted)
            throw new NotSupportedException("Original queued-reference source class is missing/deleted or has no admitted factory.");
        var form = FalloutDialogueTopic.RequiredForm(record, "NAME"); var basis = records.GetEffective(form);
        var kind = record.Signature switch
        {
            "ACHR" when basis.Signature == "NPC_" => FalloutQueuedReferenceKind.Character,
            "ACRE" when basis.Signature == "CREA" => FalloutQueuedReferenceKind.Creature,
            "REFR" when basis.Signature == "TREE" => FalloutQueuedReferenceKind.Tree,
            "REFR" when basis.Signature is not ("NPC_" or "CREA") => FalloutQueuedReferenceKind.Reference,
            _ => throw new NotSupportedException("Original Actor queued factory has an unsupported winning reference/base relationship.")
        };
        return new(reference, record.Signature, Hash(record), form, basis.Signature, Hash(basis), kind, false);
    }
    private static string Hash(FalloutPluginRecord record) => Convert.ToHexString(SHA256.HashData(record.ReadData())).ToLowerInvariant();
}
