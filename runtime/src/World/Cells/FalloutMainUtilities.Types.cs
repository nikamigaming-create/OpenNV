using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal enum FalloutMainUtilityStep
{
    ConstructUtility, ConstructAchievements, RetirePayload, RangeDiagnostic,
    StatisticsTest, StatisticsQuery, SetAchievement, StoreStatisticsTest,
    StoreStatisticsQuery, StoreStatistics, ClearQueue, ConstructLogin,
    PlatformRunning, UserTest, UserQuery, LoggedOn, StoreLogin,
    ChangedCallback, ConstructThird, ThirdNoOp, SuppressionByte, QueueAchievement,
    RegisterCallback, InitialCallback
}
internal sealed record FalloutMainUtilityEffect(FalloutMainUtilityStep Step, long Entered,
    long? Returned = null, long? Request = null, bool? Boolean = null, string? Argument = null,
    string? FailureType = null, string? Error = null);
internal sealed record FalloutMainUtilityCall(Guid Identity, Guid SourceProcess, Guid? MainInvocation,
    long? MainOrdinal, long Entered, long Changed, IReadOnlyList<FalloutMainUtilityEffect> Effects,
    bool Returned, string? FailureType = null, string? Error = null);
internal sealed record FalloutPlatformAchievementRequest(long Request, int Id, long Queued,
    long? PayloadRetired = null);
internal sealed record FalloutMainUtilityCallbackReceipt(long Registration, string Owner,
    long Changed, bool Registered, FalloutMainUtilityCall Call);
internal sealed record FalloutMainUtilitySnapshot(string Schema, FalloutMainUtilitySource Source,
    string Stack, Guid CapturedProcess, long Changed, bool UtilityConstructed,
    bool AchievementsConstructed, bool LoginConstructed, bool ThirdConstructed,
    bool LoggedOn, long LoginChanged, long Requests, long ClearedThrough,
    IReadOnlyList<FalloutPlatformAchievementRequest> Pending, long Calls,
    FalloutMainUtilityCall? LastFrame, FalloutMainUtilityCall? LastCommand,
    FalloutMainUtilityCallbackReceipt? Callback, IReadOnlyList<FalloutPlatformAchievementRequest> LastCleared,
    long Registrations, FalloutMainUtilityCall? LastCallbackAttempt);

// Null must be an actual returned platform pointer. An absent provider throws
// before it can invent a null interface, false login or successful award.
internal interface IFalloutMainUtilityPlatform
{
    FalloutMainUtilitySource Source { get; }
    string Owner { get; }
    bool IsSteamRunning();
    IFalloutMainUtilityUser? SteamUser();
    IFalloutMainUtilityStatistics? SteamUserStats();
}
internal interface IFalloutMainUtilityUser
{
    string Owner { get; }
    bool BLoggedOn();
}
internal interface IFalloutMainUtilityStatistics
{
    string Owner { get; }
    bool SetAchievement(string actualSourceIdentifier);
    bool StoreStats();
}

// The original suppression byte and console diagnostic have separate owners
// from Steam and from save completion. Neither is inferred from a menu.
internal interface IFalloutMainUtilityCommandHost
{
    FalloutMainUtilitySource Source { get; }
    string Owner { get; }
    bool AchievementSuppressionByte();
    void OutOfRangeAchievement(int actualSignedId);
}
