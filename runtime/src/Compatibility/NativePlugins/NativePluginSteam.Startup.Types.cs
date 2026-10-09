using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed record NativePluginSteamStartupDeclaration(NativePluginSteamDeclaration Provider, uint RequestStatisticsSlot,
    IReadOnlyList<NativePluginSteamCallbackShape> Callbacks, string ContractSha256)
{
    private const string Contract = "source-platform-service/v1;lazy-singleton-before-actual-Main-Steam-child;" +
        "received-stored-achievement-callback-registration-before-fields-before-Init;" +
        "initialized-false-statistics-false-independent-byte-true-application-zero;" +
        "Init-actual-byte-store-before-true-branch;initialized-then-first-user-test-fresh-user-logged-on;" +
        "first-utils-test-fresh-utils-appid9;login-true-first-statistics-test-fresh-statistics-request0;" +
        "RequestCurrentStats-return-observed-ignored;false-init-normal-return;" +
        "RunCallbacks-unconditional-at-actual-Main-child;received-gameid-zero-extended-appid-result1;" +
        "stored-and-achievement-handler-noop;alternate-Run-IO-and-callid-observed-ignored;" +
        "Shutdown-before-reverse-registered-flag-unregister-before-provider-unload;" +
        "real-callback-object-token-generation-payload-prefix;failed-entered-prefix-no-replay;" +
        "process-local-platform-fields-not-world-save-or-cold-native-memory-authority";
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Provider.Identity + "\0" + ContractSha256);
    internal static NativePluginSteamStartupDeclaration Read(NativePluginSteamDeclaration provider)
    {
        provider.Validate();
        return new(provider, 0, Array.AsReadOnly(new NativePluginSteamCallbackShape[]
        { new(1101, 24), new(1102, 16), new(1103, 152) }), FalloutAdvancementRuntimeReceipt.Hash(Contract));
    }
    internal void Validate()
    {
        var actual = Read(Provider);
        if (RequestStatisticsSlot != actual.RequestStatisticsSlot || ContractSha256 != actual.ContractSha256 ||
            Callbacks is null || !Callbacks.SequenceEqual(actual.Callbacks))
            throw new InvalidDataException("Original platform service changed its reviewed constructor/callback declaration.");
    }
}
internal sealed record NativePluginSteamCallbackShape(uint Id, uint Bytes);
internal sealed record NativePluginSteamCallbackLifetime(ulong Token, uint CallbackId, byte Flags, bool Live);
internal sealed record NativePluginSteamCallbackToken(ulong Generation, ulong Session, ulong Token, uint CallbackId, byte Flags);
internal sealed record NativePluginSteamCallbackDelivery(ulong Generation, ulong Session, ulong Token, ulong Sequence,
    ulong WaitingRequest, ulong Message, uint CallbackId, byte Flags, bool AlternateRun, bool IoFailure,
    ulong ApiCall, ReadOnlyMemory<byte> Payload);
internal sealed record NativePluginSteamCallbackPrefix(ulong Sequence, ulong Token, uint CallbackId, byte Flags,
    bool AlternateRun, bool IoFailure, ulong ApiCall, bool PayloadCopied, bool HandlerReturned, ReadOnlyMemory<byte> Payload);
internal enum NativePluginSteamServicePhase { Unconstructed, ConstructorEntered, Live, RetirementEntered, Retired, Failed }
internal enum NativePluginSteamServiceStep
{
    RegisterReceived, RegisterStored, RegisterAchievement, InitializeFields, Initialize, StoreInitialized,
    LoginUserTest, LoginUserQuery, LoggedOn, UtilsTest, UtilsQuery, ApplicationId, SourceApplicationAdmission,
    StatisticsTest, StatisticsQuery, RequestCurrentStatistics, ConstructorReturn, Pump,
    Shutdown, UnregisterAchievement, UnregisterStored, UnregisterReceived, HandlerLeaseRetire,
    SourceReadInitialized, SourceStoreIndependentByte,
}
internal sealed record NativePluginSteamServiceEffect(long Entered, long? Returned, NativePluginSteamServiceStep Step,
    Guid? MainInvocation, uint? Result, string? FailureType, string? Error, Guid? SourceCaller = null);
internal sealed record NativePluginSteamSourceCallbackEffect(long Entered, long? Returned,
    NativePluginSteamCallbackDelivery Delivery, bool StatisticsReadyBefore, bool StatisticsReadyAfter,
    bool ApplicationMatched, string? FailureType, string? Error);
