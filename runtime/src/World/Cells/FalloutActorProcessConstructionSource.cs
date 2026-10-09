using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutActorConstructorIniReceipt(string Name, FalloutIniCollection Collection,
    uint OriginalPayload, int CurrentValue, string Origin, string Source);
internal sealed record FalloutActorConstructorSourceReceipt(string Stack, string ExecutableSha256,
    string ProcessContract, Guid ConstructedProcess, FalloutCombatActorIdentity Actor, string ReferenceSignature,
    string ReferenceSha256, FalloutActorConstructorIniReceipt HardwareThreads,
    FalloutActorRegistrationThreadObservation? ThreadObservation, bool? AlternateMode, string? ModeFailure);

// Character and Creature forward their original registration request to Actor.
// The original mode gate owns an independent thread/reference-processing path.
// Native body residency never provides either constructor argument.
internal sealed partial class FalloutActorProcessConstructionSource
{
    private readonly FalloutPluginStack _records;
    private readonly FalloutActorProcessDeclaration _declaration;
    private readonly string _stack;
    private readonly Guid _process;
    private readonly Func<FalloutFormKey, FalloutCombatActorIdentity> _identity;
    private readonly Func<FalloutActorRegistrationThreadObservation>? _observeThreads;

    internal FalloutActorProcessConstructionSource(FalloutPluginStack records,
        FalloutActorProcessDeclaration declaration, string stack, Guid process,
        Func<FalloutFormKey, FalloutCombatActorIdentity> identity,
        Func<FalloutActorRegistrationThreadObservation>? observeThreads = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(identity);
        declaration.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        if (process == Guid.Empty) throw new InvalidDataException("Actor constructor source has no living process identity.");
        if (records.OwnedSource is { } owned && owned.StackId != stack)
            throw new InvalidDataException("Actor constructor source differs from the exact winning installation selection.");
        _records = records; _declaration = declaration; _stack = stack; _process = process;
        _identity = identity; _observeThreads = observeThreads;
    }

    internal FalloutActorProcessConstruction Read(FalloutFormKey actor)
    {
        var source = _identity(actor); source.Validate();
        if (source.Reference != actor || source.EnginePlayer)
            throw new InvalidDataException("Nonplayer constructor registration has a foreign or engine-Player owner.");
        var reference = _records.GetEffective(actor);
        var expected = source.BaseSignature switch
        {
            "NPC_" => "ACHR", "CREA" => "ACRE",
            _ => throw new InvalidDataException("Actor constructor base has no Character/Creature source factory."),
        };
        if (reference.Signature != expected || FalloutDialogueTopic.RequiredForm(reference, "NAME") != source.Base)
            throw new InvalidDataException("Actor constructor reference differs from the winning Character/Creature placement factory.");
        ValidateSourceIdentity(reference, source);
        var ini = ReadHardwareThreadSetting();
        var owner = "original-" + expected + "-constructor-registration:" + actor;
        FalloutActorRegistrationThreadObservation? observation = null;
        bool? alternate;
        string? failure = null;
        if (ini.CurrentValue <= 1)
        {
            // Both selected original getters return before all thread queries.
            alternate = false;
        }
        else if (_observeThreads is null)
        {
            alternate = null;
            failure = "original-actor-registration-thread-task-and-reference-processing-owner-unbound";
        }
        else
        {
            try
            {
                observation = _observeThreads();
                if (observation.Process != _process)
                    throw new InvalidDataException("Actor registration thread producer belongs to another process epoch.");
                alternate = EvaluateThreadMode(_declaration, observation);
            }
            catch (NotSupportedException error)
            {
                alternate = null;
                failure = string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message;
            }
        }
        var receipt = new FalloutActorConstructorSourceReceipt(_stack, _declaration.ExecutableSha256,
            _declaration.Contract, _process, source, expected, Hash(reference), ini, observation, alternate, failure);
        return new(actor, new(true, owner), new(alternate, "original-registration-mode:" + ini.Name, failure),
            owner, receipt);
    }

    private FalloutActorConstructorIniReceipt ReadHardwareThreadSetting()
    {
        const string name = "iNumHWThreads:General";
        // This native source consumer uses its Main SettingT directly. The
        // NVSE preference-first convenience lookup is a different operation.
        var ini = _records.IniSettings;
        var found = ini.Find(FalloutIniCollection.Main, name) ??
            throw new NotSupportedException("Owned actor registration hardware-thread setting is absent.");
        if (found.Declaration.Name != name || found.Declaration.Collection != FalloutIniCollection.Main ||
            found.Declaration.Kind != 'i' || found.Number is not { } number || !double.IsFinite(number) ||
            number < int.MinValue || number > int.MaxValue || number != Math.Truncate(number) ||
            string.IsNullOrWhiteSpace(found.Origin) || string.IsNullOrWhiteSpace(ini.Source) ||
            !ini.Source.EndsWith(":" + _declaration.ExecutableSha256, StringComparison.Ordinal))
            throw new InvalidDataException("Actor registration hardware-thread setting lost its exact source type/value/provenance.");
        return new(name, FalloutIniCollection.Main, found.Declaration.Payload, checked((int)number), found.Origin, ini.Source);
    }

    private static string Hash(FalloutPluginRecord record) =>
        Convert.ToHexString(SHA256.HashData(record.ReadData())).ToLowerInvariant();

    private void ValidateSourceIdentity(FalloutPluginRecord reference, FalloutCombatActorIdentity actor)
    {
        var basis = _records.GetEffective(actor.Base);
        if (reference.FormKey != actor.Reference || reference.Signature != actor.ReferenceSignature ||
            reference.Flags != actor.ReferenceFlags || Hash(reference) != actor.ReferenceSha256 ||
            basis.FormKey != actor.Base || basis.Signature != actor.BaseSignature ||
            basis.Flags != actor.BaseFlags || Hash(basis) != actor.BaseSha256)
            throw new InvalidDataException("Actor constructor did not retain the exact winning reference/master/base bytes.");
    }
}
