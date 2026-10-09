using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static class PlayerAdvancementRateContracts
{
    internal static FalloutSkillPointRate HalfFloor => new(FalloutSkillPointOperand.Literal(10),
        FalloutSkillPointOperand.Literal(1), 0, 2, 1, 10, FalloutSkillPointRounding.Floor, new(2, 1, 2, 0, 1));
    internal static FalloutSkillPointRate HalfCeiling => new(FalloutSkillPointOperand.GameSetting("iLevelUpSkillPointsBase"),
        FalloutSkillPointOperand.Literal(1), 0, 2, 1, 10, FalloutSkillPointRounding.Ceiling);
    internal static FalloutSkillPointRate Affine => new(FalloutSkillPointOperand.GameSetting("iLevelUpSkillPointsBase"),
        FalloutSkillPointOperand.GameSetting("iLevelUpSkillPointsInterval"), -1, 1, null, 10,
        FalloutSkillPointRounding.Truncate, Arithmetic: FalloutSkillPointArithmetic.SignedInteger32);
    internal static FalloutPermanentIntelligenceGetter Getter => new(1, 10, FalloutPermanentIntelligenceInteger.Floor);

    // Authored state identities exercise persistence joins. They are not
    // evidence that an owned executable/dependency has been admitted or run.
    internal static FalloutAdvancementRuntimeReceipt Receipt(FalloutSkillPointRate rate) =>
        new(new string('1', 64), new string('2', 64), new string('3', 64), new string('4', 64), rate, Getter);

    internal static void Run()
    {
        IntegerGetterAndRates(); TypedActivity();
        Console.WriteLine("OPENNV_PLAYER_ADVANCEMENT_RATE_CONTRACT_PASS sourceNeutral=true getterBeforeRate=true offsetOnce=true liveOperands=true typedActivity=true ownedSelection=unexecuted");
    }

    private static void IntegerGetterAndRates()
    {
        var getter = Getter;
        Require(getter.Read(5.9f) == 5 && getter.Read(.5f) == 1 && getter.Read(12.75f) == 10,
            "The Float32 descriptor bounds or integer-floor getter moved into the rate calculation.");
        Require(HalfFloor.Points(getter.Read(5.9f), 2, 10, 1) == 13 &&
            HalfFloor.Points(getter.Read(5.9f), 3, 10, 1) == 12 &&
            HalfCeiling.Points(getter.Read(5.9f), 3, 8, 1) == 11,
            "Declared floor/cadence and ceiling consumers were flattened into one rate.");
        Require(Affine.Points(getter.Read(5), 2, 11, 1) == 15 &&
            Affine.Points(getter.Read(10), 2, 11, 1) == 20 &&
            Affine.Points(getter.Read(11), 2, 11, 1) == 20 &&
            Affine.Points(11, 2, 11, 1) == 21 && Affine.Points(20, 2, 11, 1) == 21,
            "The rate offset was applied twice, or its term bound was substituted for the descriptor getter.");
        var settings = new Dictionary<string, int> { ["iLevelUpSkillPointsBase"] = 7, ["iLevelUpSkillPointsInterval"] = 2 };
        int Points() => Affine.Points(getter.Read(10), 2, Affine.Base.Resolve(name => settings[name]),
            Affine.IntelligenceMultiplier.Resolve(name => settings[name]));
        Require(Points() == 25, "Declared setting operands were replaced by an inferred formula.");
        settings["iLevelUpSkillPointsBase"] = 11; settings["iLevelUpSkillPointsInterval"] = 1;
        Require(Points() == 20 && JsonSerializer.Deserialize<FalloutAdvancementRuntimeReceipt>(
            JsonSerializer.Serialize(Receipt(Affine))) == Receipt(Affine),
            "Live setting values or the current source declaration failed their cold join.");
        Require(Affine.Points(10, 2, int.MaxValue, 2) == unchecked(int.MaxValue + 18),
            "A declared signed32 multiply/add consumer silently changed to wider arithmetic.");
        Reject(() => getter.Read(float.NaN)); Reject(() => getter.Read(float.PositiveInfinity));
        Reject(() => (getter with { Conversion = (FalloutPermanentIntelligenceInteger)99 }).Read(5));
        Reject(() => (Affine with { Divisor = 2 }).Validate());
        Reject(() => (HalfFloor with { Rounding = (FalloutSkillPointRounding)99 }).Validate());
        Reject(() => (HalfFloor with { MinimumTerm = 11, MaximumTerm = 10 }).Validate());
        Reject(() => HalfFloor.Points(5, 1, 10, 1));
        Reject(() => new FalloutLevelUpRules(HalfFloor, 50, 2, 1, 11).Validate());
    }

    private static void TypedActivity()
    {
        var required = Enum.GetValues<FalloutAdvancementActivityFact>();
        var facts = required.ToDictionary(fact => fact,
            fact => new FalloutAdvancementActivityObservation(FalloutAdvancementActivityState.Satisfied, "authored-live-" + fact));
        var readOrder = new List<FalloutAdvancementActivityFact>();
        var activity = new FalloutAdvancementActivity(required, fact => { readOrder.Add(fact); return facts[fact]; });
        Require(activity.Read().Ready && readOrder.SequenceEqual(required), "An explicitly owned live activity fact was skipped.");
        facts[FalloutAdvancementActivityFact.NotificationSequenceSettled] =
            new(FalloutAdvancementActivityState.Held, "authored-notification-awaiting-hidden-frame");
        facts[FalloutAdvancementActivityFact.OriginalPlayerFrameGate] =
            new(FalloutAdvancementActivityState.Unowned, "authored-missing-original-frame-owner");
        var unknown = activity.Read();
        Require(!unknown.Ready && unknown.UnsupportedOwner?.Contains("OriginalPlayerFrameGate", StringComparison.Ordinal) == true,
            "An earlier owned hold concealed an unowned source activity predicate.");
        facts[FalloutAdvancementActivityFact.OriginalPlayerFrameGate] =
            new(FalloutAdvancementActivityState.Satisfied, "authored-owned-frame");
        Require(!activity.Read().Ready && activity.Read().UnsupportedOwner is null,
            "An owned held notification was reported as absent or ready.");
        Reject(() => new FalloutAdvancementActivity([required[0], required[0]], _ => facts[required[0]]));
        Reject(() => new FalloutAdvancementActivity([], _ => facts[required[0]]));
        facts[required[0]] = new((FalloutAdvancementActivityState)99, "authored-invalid-state");
        Reject(() => activity.Read());
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException) { return; }
        throw new InvalidOperationException("An unowned advancement declaration was accepted.");
    }
}
