using OpenNV.Runtime.Content;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunLateFramePrefixContracts()
    {
        RunLateFramePrefixValidationContracts();
        var main = FalloutMainScriptCallerSource.Read(new(FalloutSourceMainFamily.Fallout3, new('1', 64),
            FalloutImmediateScriptSource.ContractForEngine(FalloutSourceMainFamily.Fallout3)));
        var following = FalloutPlayerFollowingContinuationSource.Reviewed(FalloutPlayerMovementControlSource.Reviewed(main));
        var source = FalloutPlayerLateFramePrefixSource.Reviewed(following); source.Validate();
        Require(FalloutPlayerLateFramePrefixSource.InitialCameraViewRequest == 0 &&
            FalloutPlayerLateFramePrefixSource.InhibitionThresholdBits == BitConverter.SingleToUInt32Bits(15f) &&
            FalloutPlayerLateFramePrefixSource.ViewDisabledMask == 0x10,
            "Late Player prefix changed its independent packed constructor byte, threshold or control mask.");
        Reject(() => (source with { Contract = new('a', 64) }).Validate());
        Console.WriteLine("OPENNV_LATE_FRAME_PREFIX_DECLARATION_PASS currentFields=INDEPENDENT actualBody=UNEXECUTED");
    }

    internal static void AuditOwnedLateFramePrefixSources(string dataRoot)
    {
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.Fallout3Game);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var runtime = FalloutAdvancementRuntimeSource.Open(records);
        var main = FalloutMainScriptCallerSource.Read(FalloutImmediateScriptSource.Read(runtime.Receipt));
        var following = FalloutExecutableStringTable.ReadPlayerFollowingContinuation(content.FalloutExecutablePath,
            FalloutPlayerMovementControlSource.Reviewed(main));
        var prefix = FalloutExecutableStringTable.ReadPlayerLateFramePrefix(content.FalloutExecutablePath, following);
        Require(prefix.Following == following && prefix == FalloutPlayerLateFramePrefixSource.Reviewed(following),
            "Owned late prefix selected a different Player/POV/mover/Actor-time source.");
        Console.WriteLine("OPENNV_OWNED_LATE_FRAME_PREFIX_SOURCE_PASS source=" + prefix.Identity + " gameplay=UNEXECUTED");
    }
}
