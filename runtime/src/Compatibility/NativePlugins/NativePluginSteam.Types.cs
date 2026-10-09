using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal enum NativePluginSteamOperation : uint
{
    Load = 1, Initialize = 2, IsRunning = 3, User = 4, Statistics = 5,
    LoggedOn = 6, SetAchievement = 7, StoreStatistics = 8,
    RunCallbacks = 9, Shutdown = 10, Unload = 11, ReleaseInterface = 12,
    Utilities = 13, ApplicationId = 14, RequestStatistics = 15, RegisterCallback = 16, UnregisterCallback = 17
}
internal enum NativePluginSteamInterface : uint { User = 1, Statistics = 2, Utilities = 3 }
internal enum NativePluginSteamPhase { Loaded, InitializeEntered, InitializeReturned, ShutdownEntered, ShutdownReturned, Retired, Failed, LoadEntered }
internal sealed record NativePluginSteamDeclaration(FalloutMainUtilitySource Main, string EngineSha256,
    string ProviderSha256, string Library, uint UserLoggedOnSlot, uint SetAchievementSlot, uint StoreStatisticsSlot,
    uint UtilsAppIdSlot, string ContractSha256)
{
    private const string Contract = "selected-x86-Steam-export-provider/v2;exact-selected-original-import-and-export-hashes;" +
        "cdecl-zero-argument-exports;thiscall-user-logged-on1;statistics-set7-store10;utils-appid9;" +
        "no-appid-override-or-default;actual-null-query-only;opaque-child-interface-tokens;" +
        "retained-native-call-and-client-provider-identity;source-owned-lazy-service-constructor-and-callbacks;source-queries-no-hidden-Utils-diagnostic-calls;" +
        "real-init-shutdown-unload;no-replay-of-entered-or-failed-effect";
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Main.Identity + "\0" + EngineSha256 + "\0" + ProviderSha256 + "\0" + ContractSha256);
    internal static NativePluginSteamDeclaration Read(FalloutMainUtilitySource main, string engine, string provider, string library)
    {
        main.Validate();
        // This is a reviewed original provider envelope, selected by owned
        // bytes and the actual import graph, never a game/display/plugin name.
        if (!StringComparer.OrdinalIgnoreCase.Equals(engine, main.Main.EngineSha256) ||
            !StringComparer.OrdinalIgnoreCase.Equals(provider, "59ed854645eaa237463eb22f3c5a25f726d7cb2f29440f00a1ec0a4d73d0207a") ||
            library != "steam_api.dll")
            throw new NotSupportedException("Selected platform source has no reviewed original export/interface envelope.");
        return new(main, engine.ToLowerInvariant(), provider.ToLowerInvariant(), library, 1, 7, 10, 9,
            FalloutAdvancementRuntimeReceipt.Hash(Contract));
    }
    internal void Validate()
    {
        if (this != Read(Main, EngineSha256, ProviderSha256, Library) || UserLoggedOnSlot != 1 ||
            SetAchievementSlot != 7 || StoreStatisticsSlot != 10 || UtilsAppIdSlot != 9)
            throw new InvalidDataException("Selected Steam source changed its genuine original callable layout.");
    }
}
internal sealed record NativePluginSteamToken(ulong Generation, ulong Session, ulong Token,
    NativePluginSteamInterface Kind, ulong InterfaceEpoch);
internal sealed record NativePluginSteamNativeOwner(string Path, string Sha256);
internal enum NativePluginSteamCallStep : uint { SourceExport = 1, ApplicationInterface = 2, ApplicationId = 3, SourceMethod = 4 }
internal enum NativePluginSteamReturnKind : uint { None, BooleanByte, PointerPresence, ApplicationId }
internal sealed record NativePluginSteamForeignPrefix(NativePluginSteamCallStep Step, NativePluginSteamReturnKind Kind,
    bool Returned, uint Result, int StackDelta, uint PreservedRegisters, uint ExceptionCode,
    NativePluginSteamNativeOwner CallableOwner);
internal sealed record NativePluginSteamCallableLifetime(string? Path, string? Sha256, bool ModuleRetained, bool SourceRetained);
internal sealed record NativePluginSteamFaultPrefix(ulong Generation, ulong Session, NativePluginSteamOperation? Attempted,
    NativePluginSteamPhase Phase, bool ModuleRetained, bool SourceRetained, uint CurrentAppId,
    IReadOnlyList<NativePluginSteamForeignPrefix> Calls, IReadOnlyList<NativePluginSteamCallableLifetime> CallableLifetimes,
    IReadOnlyList<NativePluginSteamCallbackPrefix> CallbackPrefixes, IReadOnlyList<NativePluginSteamCallbackLifetime> CallbackLifetimes);
internal sealed record NativePluginSteamReceipt(ulong Generation, ulong Session, ulong Sequence,
    NativePluginSteamOperation Operation, ulong Token, uint NativeThread, uint Result,
    bool AbiMeasured, int StackDelta, uint PreservedRegisters, uint ExceptionCode, uint CurrentAppId,
    NativePluginSteamNativeOwner? CallableOwner, NativePluginSteamPhase Phase,
    IReadOnlyList<NativePluginSteamForeignPrefix> Calls, IReadOnlyList<NativePluginSteamCallbackPrefix> CallbackPrefixes);
