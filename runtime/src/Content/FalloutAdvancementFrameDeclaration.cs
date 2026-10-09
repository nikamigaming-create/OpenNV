using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Content;

// Neutral declarations bind the actual original consumers. No native field
// address, private function address or reconstructed TESForm is a product input.
internal sealed record FalloutAdvancementFrameDeclaration(string ExecutableSha256,
    string Contract, bool InitialExperienceReady, uint InitialExperienceFlags,
    bool InitialContainerActivationPending, uint ContainerMenu)
{
    internal void Validate()
    {
        if (!FalloutAdvancementRuntimeReceipt.Digest(ExecutableSha256) ||
            !FalloutAdvancementRuntimeReceipt.Digest(Contract) ||
            InitialExperienceReady || InitialExperienceFlags != 1 ||
            InitialContainerActivationPending || ContainerMenu != 1008)
            throw new InvalidDataException("Advancement frame declarations differ from their admitted source semantics.");
    }

    internal static FalloutAdvancementFrameDeclaration Read(FalloutExperienceHudDeclaration experience)
    {
        experience.Validate();
        // Independent original compiler traces bind the HUD flag, its reset
        // and original level-text completion, plus the interface constructor,
        // successful unlocked-container activation and factory entry. A name
        // or a neighboring public query does not select these declarations.
        if (experience.ExecutableSha256 is not
            ("518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57" or
             "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e"))
            throw new NotSupportedException("Selected image has no reviewed advancement frame producer contract.");
        var neutral = experience.ExecutableSha256 + "\0" + experience.Contract +
            "\0hud-initial-false-flags1;level-text-hidden-ready-flags8;level-menu-submit-reset-false-flags1;" +
            "interface-initial-false;unlocked-container-activation-pending;container-factory-entry-clear;menu1008-v1";
        return new(experience.ExecutableSha256,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(neutral))).ToLowerInvariant(),
            false, 1, false, 1008);
    }
}
