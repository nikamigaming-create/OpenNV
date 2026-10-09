namespace OpenNV.Runtime.Content;

// Binary parameter IDs bind to the existing named gameplay owners.
// These immutable API identities grant no animation/value behavior.
internal static class FalloutCompiledParameterNames
{
    private static readonly string[] AnimationGroups =
    [
        "Idle", "DynamicIdle", "SpecialIdle", "Forward", "Backward", "Left",
        "Right", "FastForward", "FastBackward", "FastLeft", "FastRight", "DodgeForward",
        "DodgeBack", "DodgeLeft", "DodgeRight", "TurnLeft", "TurnRight", "Aim",
        "AimUp", "AimDown", "AimIS", "AimISUp", "AimISDown", "Holster",
        "Equip", "Unequip", "AttackLeft", "AttackLeftUp", "AttackLeftDown", "AttackLeftIS",
        "AttackLeftISUp", "AttackLeftISDown", "AttackRight", "AttackRightUp", "AttackRightDown", "AttackRightIS",
        "AttackRightISUp", "AttackRightISDown", "Attack3", "Attack3Up", "Attack3Down", "Attack3IS",
        "Attack3ISUp", "Attack3ISDown", "Attack4", "Attack4Up", "Attack4Down", "Attack4IS",
        "Attack4ISUp", "Attack4ISDown", "Attack5", "Attack5Up", "Attack5Down", "Attack5IS",
        "Attack5ISUp", "Attack5ISDown", "Attack6", "Attack6Up", "Attack6Down", "Attack6IS",
        "Attack6ISUp", "Attack6ISDown", "Attack7", "Attack7Up", "Attack7Down", "Attack7IS",
        "Attack7ISUp", "Attack7ISDown", "Attack8", "Attack8Up", "Attack8Down", "Attack8IS",
        "Attack8ISUp", "Attack8ISDown", "AttackLoop", "AttackLoopUp", "AttackLoopDown", "AttackLoopIS",
        "AttackLoopISUp", "AttackLoopISDown", "AttackSpin", "AttackSpinUp", "AttackSpinDown", "AttackSpinIS",
        "AttackSpinISUp", "AttackSpinISDown", "AttackSpin2", "AttackSpin2Up", "AttackSpin2Down", "AttackSpin2IS",
        "AttackSpin2ISUp", "AttackSpin2ISDown", "AttackPower", "AttackForwardPower", "AttackBackPower", "AttackLeftPower",
        "AttackRightPower", "AttackCustom1Power", "AttackCustom2Power", "AttackCustom3Power", "AttackCustom4Power", "AttackCustom5Power",
        "PlaceMine", "PlaceMineUp", "PlaceMineDown", "PlaceMineIS", "PlaceMineISUp", "PlaceMineISDown",
        "PlaceMine2", "PlaceMine2Up", "PlaceMine2Down", "PlaceMine2IS", "PlaceMine2ISUp", "PlaceMine2ISDown",
        "AttackThrow", "AttackThrowUp", "AttackThrowDown", "AttackThrowIS", "AttackThrowISUp", "AttackThrowISDown",
        "AttackThrow2", "AttackThrow2Up", "AttackThrow2Down", "AttackThrow2IS", "AttackThrow2ISUp", "AttackThrow2ISDown",
        "AttackThrow3", "AttackThrow3Up", "AttackThrow3Down", "AttackThrow3IS", "AttackThrow3ISUp", "AttackThrow3ISDown",
        "AttackThrow4", "AttackThrow4Up", "AttackThrow4Down", "AttackThrow4IS", "AttackThrow4ISUp", "AttackThrow4ISDown",
        "AttackThrow5", "AttackThrow5Up", "AttackThrow5Down", "AttackThrow5IS", "AttackThrow5ISUp", "AttackThrow5ISDown",
        "Attack9", "Attack9Up", "Attack9Down", "Attack9IS", "Attack9ISUp", "Attack9ISDown",
        "AttackThrow6", "AttackThrow6Up", "AttackThrow6Down", "AttackThrow6IS", "AttackThrow6ISUp", "AttackThrow6ISDown",
        "AttackThrow7", "AttackThrow7Up", "AttackThrow7Down", "AttackThrow7IS", "AttackThrow7ISUp", "AttackThrow7ISDown",
        "AttackThrow8", "AttackThrow8Up", "AttackThrow8Down", "AttackThrow8IS", "AttackThrow8ISUp", "AttackThrow8ISDown",
        "Counter", "stomp", "BlockIdle", "BlockHit", "Recoil", "ReloadWStart",
        "ReloadXStart", "ReloadYStart", "ReloadZStart", "ReloadA", "ReloadB", "ReloadC",
        "ReloadD", "ReloadE", "ReloadF", "ReloadG", "ReloadH", "ReloadI",
        "ReloadJ", "ReloadK", "ReloadL", "ReloadM", "ReloadN", "ReloadO",
        "ReloadP", "ReloadQ", "ReloadR", "ReloadS", "ReloadW", "ReloadX",
        "ReloadY", "ReloadZ", "JamA", "JamB", "JamC", "JamD",
        "JamE", "JamF", "JamG", "JamH", "JamI", "JamJ",
        "JamK", "JamL", "JamM", "JamN", "JamO", "JamP",
        "JamQ", "JamR", "JamS", "JamW", "JamX", "JamY",
        "JamZ", "Stagger", "Death", "Talking", "PipBoy", "JumpStart",
        "JumpLoop", "JumpLand", "HandGrip1", "HandGrip2", "HandGrip3", "HandGrip4",
        "HandGrip5", "HandGrip6", "JumpLoopForward", "JumpLoopBackward", "JumpLoopLeft", "JumpLoopRight",
        "PipBoyChild", "JumpLandForward", "JumpLandBackward", "JumpLandLeft", "JumpLandRight",
    ];

    internal static string? Read(byte sourceType, double number) => sourceType switch
    {
        8 => number is 88 or 89 or 90 ? ((char)number).ToString() :
            throw new InvalidDataException("Compiled axis is not X/Y/Z."),
        10 => number >= 0 && number < AnimationGroups.Length && number == Math.Truncate(number)
            ? AnimationGroups[(int)number] : throw new InvalidDataException("Compiled animation group ID is invalid."),
        18 => number switch { 0 => "Male", 1 => "Female", _ => throw new InvalidDataException("Compiled sex ID is invalid.") },
        5 => ActorValue(number),
        // Type41 retains the original UInt16 statistic ordinal. The living
        // source catalogue owns its name/extent; no localized-name mapping.
        41 => double.IsFinite(number) && number == Math.Truncate(number) && number >= 0 && number <= ushort.MaxValue
            ? null : throw new InvalidDataException("Compiled statistic enum is not UInt16."),
        22 or 28 or 32 or 44 or 45 or 46 or 51 or 52 or 55 =>
            throw new NotSupportedException("Compiled enum/variable parameter has no shared named owner."),
        _ => null
    };

    private static string ActorValue(double number) => number switch
    {
        0 => "Aggression",
        5 => "Strength",
        6 => "Perception",
        7 => "Endurance",
        8 => "Charisma",
        9 => "Intelligence",
        10 => "Agility",
        11 => "Luck",
        12 => "ActionPoints",
        16 => "Health",
        24 => "XP",
        25 => "PerceptionCondition",
        26 => "EnduranceCondition",
        27 => "LeftAttackCondition",
        28 => "RightAttackCondition",
        29 => "LeftMobilityCondition",
        30 => "RightMobilityCondition",
        31 => "BrainCondition",
        32 => "Barter",
        33 => "BigGuns",
        34 => "EnergyWeapons",
        35 => "Explosives",
        36 => "Lockpick",
        37 => "Medicine",
        38 => "MeleeWeapons",
        39 => "Repair",
        40 => "Science",
        41 => "Guns",
        42 => "Sneak",
        43 => "Speech",
        44 => "Survival",
        45 => "Unarmed",
        >= 62 and <= 71 when number == Math.Truncate(number) =>
            "Variable" + ((int)number - 61).ToString("D2", System.Globalization.CultureInfo.InvariantCulture),
        _ => throw new NotSupportedException("Compiled actor-value ID has no shared named pool binding.")
    };
}
