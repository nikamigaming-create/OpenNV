using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Content.Definitions;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutIngestibleScriptReceipt(long Generation, FalloutFormKey Item, int EffectOrdinal,
    string ItemHash, string EffectHash, FalloutFormKey Script, string ScriptHash,
    IReadOnlyList<FalloutScriptEffectLocalCell> Locals);

// A consumed, instantaneous script effect has one synchronous Start invocation.
// Execute on the transaction's detached inventory; unknown reached operations
// cannot publish an executed prefix into the actual player/world.
internal sealed class FalloutInstantIngestibleScript
{
    private readonly FalloutPluginStack _records;
    private readonly FalloutPluginRecord _script;
    private readonly FalloutScriptBindings _bindings;
    private readonly FalloutGameModeProgram _program;
    internal string Hash { get; }
    internal FalloutFormKey Script => _script.FormKey;

    internal FalloutInstantIngestibleScript(FalloutPluginStack records, FalloutIngestibleEffect effect)
    {
        _records = records;
        if (effect.Archetype != 1 || effect.Script is not { } script || effect.EffectiveDuration != 0 ||
            effect.Area != 0 || effect.Range != 0 || effect.AdditionalPresentation || (effect.Flags & 0x10) == 0 ||
            (effect.Flags & ~0x23f8u) != 0 || effect.Resistance != -1)
            throw new NotSupportedException("Ingestible script effect needs its delivery, timeline or presentation owner.");
        _script = records.GetEffective(script);
        if (_script.Signature != "SCPT") throw new InvalidDataException("Ingestible script source is not SCPT.");
        var fields = _script.ReadSubrecords().ToArray();
        var headers = fields.Where(field => field.Signature == "SCHR").ToArray();
        var compiled = fields.Where(field => field.Signature == "SCDA").ToArray();
        var sources = fields.Where(field => field.Signature == "SCTX").ToArray();
        if (headers.Length != 1 || headers[0].Data.Length != 20 || compiled.Length != 1 || sources.Length != 1 ||
            FalloutScriptHeader.Read(headers[0].Data.Span).CompiledBytes != compiled[0].Data.Length)
            throw new NotSupportedException("Ingestible script requires its complete compiled identity and shipped source owner.");
        var sourceText = FalloutDialogueTopic.ScriptText(sources[0].Data.Span);
        var blocks = FalloutGameModeProgram.ReadEvents(sourceText);
        if (blocks.Count == 0 || blocks.Any(block =>
            !block.Event.Equals("ScriptEffectStart", StringComparison.OrdinalIgnoreCase) ||
            block.Filter is not null || block.Parameters is not null))
            throw new NotSupportedException("Ingestible script requires its non-Start lifecycle owner.");
        _program = FalloutGameModeProgram.Read(sourceText, "ScriptEffectStart");
        _bindings = new(records, _script, _script, fields);
        _ = new FalloutScriptEffectLocals(_script);
        Hash = Convert.ToHexString(SHA256.HashData(_script.ReadData()));
    }

    internal IReadOnlyList<FalloutScriptEffectLocalCell> Execute(FalloutPlayerInventory staged, Func<int, float> actorValue)
    {
        var locals = new FalloutScriptEffectLocals(_script);
        var player = _records.RuntimeFormKey(FalloutPlayerActorValues.PlayerReference);
        double Read(string name) => locals.Contains(name) ? locals.Read(name).Number :
            throw new NotSupportedException("Ingestible script read has no local numeric owner: " + name);
        void Write(string name, double value)
        {
            if (!locals.Contains(name) || !double.IsFinite(value))
                throw new NotSupportedException("Ingestible script write has no finite local owner: " + name);
            locals.Write(name, value);
        }
        string Operation(string name)
        {
            var parts = name.Split('.');
            if (parts.Length == 1) return parts[0].ToLowerInvariant();
            if (parts.Length != 2 || _bindings.Reference(parts[0]) != player)
                throw new NotSupportedException("Ingestible script receiver has no staged player owner.");
            return parts[1].ToLowerInvariant();
        }
        FalloutScriptFunction? Function(string name) => Operation(name) switch
        {
            "getav" or "getactorvalue" => new([FalloutScriptArgumentKind.Identifier], arguments =>
                actorValue((int)FalloutActorValueNames.Parse(arguments[0].Identifier!)))
            { ReadOnly = true },
            "getweaponhealthperc" => new([], _ => staged.EquippedWeaponHealthPercentage(_records)) { ReadOnly = true },
            _ => null,
        };
        void Call(string name, IReadOnlyList<string> arguments)
        {
            if (Operation(name) != "setweaponhealthperc")
                throw new NotSupportedException("Ingestible script command has no staged owner: " + name);
            if (arguments.Count == 0) throw new InvalidDataException("SetWeaponHealthPerc has no argument.");
            var value = FalloutGameModeProgram.Evaluate(arguments, Read, Function);
            staged.SetEquippedWeaponHealthPercentage(_records, value);
        }
        var budget = new FalloutScriptExecutionBudget();
        _program.Execute(Read, Write, Call, Function, budget: budget);
        return locals.Capture();
    }

    internal void ValidateLocals(IReadOnlyList<FalloutScriptEffectLocalCell> locals) =>
        _ = new FalloutScriptEffectLocals(_script, locals);
}
