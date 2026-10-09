using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal static partial class FalloutNativeCampaignSave
{
    internal static void ValidateResultAuthorityVersion(FalloutNativeCampaignState state)
    {
        if (state.Schema != ExpectedSchema)
            throw new NotSupportedException("Result authority requires the current campaign state schema.");
        var active = state.FinishedSpeech?.ActiveRadio;
        if (active?.Any(voice => voice.BeginResults is not { Completed: true, Schema: FalloutScriptResultReceipt.ExpectedSchema }) == true)
            throw new NotSupportedException("Active radio has no proven completed shared result receipt.");
    }

    private static void ValidateCompiledScriptStorage(FalloutPluginStack records, FalloutNativeCampaignState state)
    {
        foreach (var reference in state.References ?? [])
            if (reference.Script is { } key)
                Validate(records.GetEffective(key), reference.Variables, reference.Reference.ToString());
        foreach (var quest in state.Quests ?? [])
            if (FalloutScriptLocals.AttachedScript(records, records.GetEffective(quest.Quest)) is { } script)
                Validate(script, quest.Variables, quest.Quest.ToString());

        static void Validate(FalloutPluginRecord script, IReadOnlyDictionary<uint, double> values, string owner)
        {
            if (!FalloutCompiledScriptProgram.HasProgram(script.ReadSubrecords().ToArray())) return;
            var slots = FalloutScriptLocals.ReadStorageKinds(script, FalloutScriptDeclarationAuthority.CompiledVanilla);
            if (!slots.Keys.Order().SequenceEqual(values.Keys.Order()))
                throw new InvalidDataException("Saved compiled variables differ from their original SLSD slots: " + owner);
            foreach (var (index, value) in values) FalloutScriptLocals.RequireCompiledValue(script, index, value);
        }
    }
}
