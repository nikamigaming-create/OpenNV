using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Rendering;

public partial class NativeSkyHdrCapabilityAudit : Node
{
    public override void _Ready()
    {
        try
        {
            // Source identities are an authored contract fixture. Only the
            // host's absent device and production provider lifecycle are native.
            const string engine = "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57";
            var declaration = new FalloutSkyTransferDeclaration(engine, FalloutSkyTransferDeclaration.CurrentContractSha256);
            var settings = new FalloutNumericIniSettings(
                [new(FalloutSkyHdrSource.SettingName, FalloutIniCollection.Main, 1)], [], "authored-headless-HDR-settings");
            var source = new FalloutSkyHdrState(declaration, Guid.NewGuid(), Guid.NewGuid(), settings);
            var provider = new RuntimeHeadlessSkyHdrCapability(source.Source, source.Process);
            Require(!provider.Returned && !provider.Retired, "Headless HDR fabricated a completed constructor.");
            Reject<InvalidOperationException>(() => provider.Read());
            Reject<InvalidOperationException>(() => _ = provider.Retirement);
            Reject<InvalidDataException>(() => new RuntimeHeadlessSkyHdrCapability(source.Source, Guid.Empty));

            source.EnterRendererCapability(provider);
            provider.Begin();
            Require(provider.Returned, "Headless HDR incorrectly requires a render-thread wait.");
            Reject<InvalidOperationException>(provider.Begin);
            source.SelectRenderer(provider);
            var receipt = provider.Read();
            Require(receipt.Current && !receipt.NativeResourcesCurrent && !receipt.HalfFloatRgbaBlendSupported &&
                receipt.Provider == FalloutSkyHdrCapabilityProvider.HeadlessNoRenderer && source.EnabledByte == 0,
                "Headless HDR claimed renderer resources or skipped source format selection.");
            Reject<NotSupportedException>(() => source.ReadShader(Guid.NewGuid()));

            Exception? foreignFailure = null;
            var foreign = new Thread(() => { try { provider.Read(); } catch (Exception failure) { foreignFailure = failure; } });
            foreign.Start(); foreign.Join();
            Require(foreignFailure is InvalidOperationException, "Headless HDR escaped its product thread.");

            var saved = source.Capture();
            var cold = new FalloutSkyHdrState(declaration, Guid.NewGuid(), Guid.NewGuid(), settings);
            cold.Restore(saved);
            var fresh = new RuntimeHeadlessSkyHdrCapability(cold.Source, cold.Process);
            cold.EnterRendererCapability(fresh); fresh.Begin(); cold.RebindRenderer(fresh);
            Require(fresh.Identity != provider.Identity && cold.Capture().Selection?.Identity == fresh.Identity,
                "Cold headless HDR rebound the prior process provider.");
            Retire(source, provider);
            Retire(cold, fresh);
            GD.Print("OPENNV_NATIVE_HEADLESS_HDR_CAPABILITY_PASS synchronous=true resources=none thread=true coldProvider=fresh retirement=true fixture=true gameplay=UNEXECUTED recording=false");
            GetTree().Quit();
        }
        catch (Exception failure)
        {
            GD.PushError("OPENNV_NATIVE_HEADLESS_HDR_CAPABILITY_FAILURE " + failure);
            GetTree().Quit(1);
        }
    }

    private static void Retire(FalloutSkyHdrState source, IRuntimeSkyHdrCapability provider)
    {
        source.BeginCapabilityRetirement(provider);
        provider.BeginRetirement();
        Require(provider.Retirement.IsCompletedSuccessfully && provider.Retired && !provider.Returned,
            "Headless HDR retirement waited on a nonexistent renderer.");
        source.CompleteCapabilityRetirement(provider, () => Require(provider.Retired, "Provider is not retired."));
        source.Retire();
        Reject<ObjectDisposedException>(() => provider.Read());
        Reject<ObjectDisposedException>(provider.Begin);
        provider.BeginRetirement();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected refusal: " + typeof(T).Name);
    }
}
