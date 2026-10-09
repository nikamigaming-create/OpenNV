using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutAdvancementRuntimeReceipt(string EngineSha256, string DependenciesSha256,
    string ConfigurationSha256, string ContractSha256, FalloutSkillPointRate SkillRate,
    FalloutPermanentIntelligenceGetter IntelligenceGetter, FalloutPerkAwardCadence PerkCadence)
{
    internal string SourceSha256 => Hash(EngineSha256 + "\0" + DependenciesSha256 + "\0" +
        ConfigurationSha256 + "\0" + ContractSha256);
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(SkillRate); SkillRate.Validate();
        ArgumentNullException.ThrowIfNull(IntelligenceGetter); IntelligenceGetter.Validate();
        ArgumentNullException.ThrowIfNull(PerkCadence); PerkCadence.Validate();
        if (!Digest(EngineSha256) || !Digest(DependenciesSha256) || !Digest(ConfigurationSha256) ||
            !Digest(ContractSha256))
            throw new InvalidDataException("Player advancement runtime receipt is invalid.");
    }
    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    internal static bool Digest(string? value) => value is { Length: 64 } && value.All(character =>
        character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

// A bounded reviewed source consumer catalogue. Keys are original image
// digests, not game/plugin/display names or a present GMST. Reading this owner
// executes no original image and makes no original-plugin execution claim.
internal sealed partial class FalloutAdvancementRuntimeSource : IDisposable
{
    private sealed record EngineContract(FalloutSkillPointRate SkillRate,
        FalloutPermanentIntelligenceGetter IntelligenceGetter, FalloutPerkAwardCadence PerkCadence, string NeutralContract);
    private readonly List<FileStream> _leases = [];
    private IReadOnlyList<string> _unreviewedModuleImages = [];
    private bool _disposed;
    internal FalloutAdvancementRuntimeReceipt Receipt { get; private set; } = null!;
    internal RuntimeLiveContentSource OwnedSource { get; }
    internal object State => new
    {
        source = Receipt,
        originalNativePluginExecution = "unowned",
        unreviewedModuleImages = _unreviewedModuleImages,
        activity = "living-owner-observations-required;missing-owner-refuses",
        originalActivityFrameGates = "unresolved;never-default-ready",
        arithmetic = "source-declared-real-or-signed-int32;negative-menu-budget-refuses",
    };

    private static readonly IReadOnlyDictionary<string, EngineContract> Engines =
        new Dictionary<string, EngineContract>(StringComparer.Ordinal)
        {
            ["518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57"] = new(
                new(FalloutSkillPointOperand.Literal(10), FalloutSkillPointOperand.Literal(1),
                    0, 2, 1, 10, FalloutSkillPointRounding.Floor, new(2, 1, 2, 0, 1)),
                new(1, 10, FalloutPermanentIntelligenceInteger.Floor),
                new("iLevelsPerPerk"),
                "integer-permanent-floor;clamp1..10;base10;scale1/2;floor;odd-intelligence-even-gained-level-plus1"),
            ["c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e"] = new(
                new(FalloutSkillPointOperand.GameSetting("iLevelUpSkillPointsBase"),
                    FalloutSkillPointOperand.GameSetting("iLevelUpSkillPointsInterval"),
                    -1, 1, null, 10, FalloutSkillPointRounding.Truncate,
                    Arithmetic: FalloutSkillPointArithmetic.SignedInteger32),
                new(1, 10, FalloutPermanentIntelligenceInteger.Floor),
                new(null),
                "float32-permanent-clamp1..10;integer-floor;subtract1-once;term-min10;int32-multiply-live-interval-plus-live-base"),
        };

    private FalloutAdvancementRuntimeSource(RuntimeLiveContentSource source) => OwnedSource = source;

    internal static FalloutAdvancementRuntimeSource Open(FalloutPluginStack records)
    {
        var source = records.OwnedSource ?? throw new NotSupportedException("Advancement has no selected installation source.");
        var owner = new FalloutAdvancementRuntimeSource(source);
        try
        {
            var engine = owner.LeaseImage(source.FalloutExecutablePath, dll: false);
            if (!Engines.TryGetValue(engine.Sha256, out var declaration))
                throw new NotSupportedException("Selected executable has no reviewed advancement consumer contract.");
            var dependencies = owner.ReadDependencies(new(source.ContentRoots), engine.Sha256);
            owner._unreviewedModuleImages = dependencies.UnreviewedImages;
            var rate = dependencies.Rate ?? declaration.SkillRate;
            rate.Validate();
            owner.Receipt = new(engine.Sha256, FalloutAdvancementRuntimeReceipt.Hash(dependencies.Images),
                FalloutAdvancementRuntimeReceipt.Hash(dependencies.Configuration),
                FalloutAdvancementRuntimeReceipt.Hash(declaration.NeutralContract + "\0" + dependencies.Contract + "\0" +
                    JsonSerializer.Serialize(rate) + "\0" + JsonSerializer.Serialize(declaration.IntelligenceGetter) + "\0" +
                    JsonSerializer.Serialize(declaration.PerkCadence)),
                rate, declaration.IntelligenceGetter, declaration.PerkCadence);
            owner.Receipt.Validate();
            // Admit all consumed live settings before returning a configured
            // source. Default-setting presence does not choose their consumer.
            _ = FalloutLevelUpRules.Read(records, owner.Receipt);
            return owner;
        }
        catch { owner.Dispose(); throw; }
    }

    internal FalloutPlayerAdvancementSource Player(FalloutPlayerActorValueSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ObjectDisposedException.ThrowIf(_disposed, this); Receipt.Validate();
        return new(source.Player, source.PlayerSha256, source.StatsOwner, source.StatsSha256, Receipt);
    }

    // These are required live product joins, not guessed retail frame flags.
    // Original opaque frame predicates are separately required until their
    // source consumer and living runtime equivalents have been admitted.
    internal FalloutAdvancementActivity Activity(
        Func<FalloutAdvancementActivityFact, FalloutAdvancementActivityObservation> observe)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(observe);
        var required = new List<FalloutAdvancementActivityFact>
        {
            FalloutAdvancementActivityFact.AdmittedPlayerUpdate,
            FalloutAdvancementActivityFact.CharacterGenerationEnded,
            FalloutAdvancementActivityFact.NoActiveMenu,
            FalloutAdvancementActivityFact.PlayerAlive,
            FalloutAdvancementActivityFact.PlayerAwake,
            FalloutAdvancementActivityFact.PlayerUpright,
            FalloutAdvancementActivityFact.CombatEnded,
            FalloutAdvancementActivityFact.NotificationSequenceSettled,
            FalloutAdvancementActivityFact.OriginalUiFrameGate,
            FalloutAdvancementActivityFact.OriginalPlayerFrameGate,
        };
        if (_unreviewedModuleImages.Count != 0)
            required.Add(FalloutAdvancementActivityFact.SelectedDependencyAdvancementEffects);
        return new(Array.AsReadOnly(required.ToArray()), fact =>
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return observe(fact);
        });
    }

    private (byte[] Bytes, string Sha256) LeaseImage(string path, bool dll)
    {
        var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            using (var pe = new PEReader(source, PEStreamOptions.LeaveOpen))
                if (pe.PEHeaders.CoffHeader.Machine != Machine.I386 || pe.PEHeaders.PEHeader?.Magic != PEMagic.PE32 ||
                    pe.PEHeaders.CorHeader is not null || pe.PEHeaders.CoffHeader.Characteristics.HasFlag(Characteristics.Dll) != dll)
                    throw new InvalidDataException("Advancement source requires an original unmanaged PE32/I386 image of its declared kind.");
            if (source.Length is < 1 or > 128 * 1024 * 1024)
                throw new InvalidDataException("Advancement image exceeds its bounded source extent.");
            source.Position = 0;
            var bytes = new byte[checked((int)source.Length)]; source.ReadExactly(bytes);
            _leases.Add(source);
            return (bytes, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }
        catch { source.Dispose(); throw; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var lease in _leases) lease.Dispose();
        _leases.Clear();
    }
}
