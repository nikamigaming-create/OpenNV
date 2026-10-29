using OpenNV.Runtime.Content.Definitions;

namespace OpenNV.Runtime.Content;

// Bind reserved engine identities into this stack's runtime namespace without
// requiring a placed source record or inventing an actor/base state owner.
internal static class FalloutEngineIdentity
{
    internal static bool IsPlayerReference(FalloutPluginStack records, FalloutFormKey form) =>
        form == records.RuntimeFormKey(FalloutEngineForms.PlayerReference);
}
