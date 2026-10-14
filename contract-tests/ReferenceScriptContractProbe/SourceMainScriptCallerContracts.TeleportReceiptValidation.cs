using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    private static void RequireFollowingTeleportReceiptNegatives(FalloutStandalonePlayerSceneSnapshot saved)
    {
        var call = saved.FollowingServices!;
        var teleport = call.Teleport!;
        RejectReceipt(teleport with { Before = null! });
        RejectReceipt(teleport with { BeforeFields = null! });
        RejectReceipt(teleport with { AfterFields = null! });
        RejectReceipt(teleport with { Trace = null! });
        RejectReceipt(teleport with { Before = teleport.Before with { Actor = null! } });
        RejectReceipt(teleport with { Before = teleport.Before with { Stack = new string('9', 64) } });
        RejectReceipt(teleport with { Before = teleport.Before with { ExecutableSha256 = new string('8', 64) } });
        RejectReceipt(teleport with { Before = teleport.Before with { Process = Guid.NewGuid() } });
        RejectReceipt(teleport with { Before = teleport.Before with { GameplayOwnership = Guid.NewGuid() } });
        RejectReceipt(teleport with { Before = teleport.Before with { Epoch = teleport.Before.Epoch + 1 } });
        RejectReceipt(teleport with { Before = teleport.Before with { Level = FalloutDetectionProcessLevel.MiddleHigh } });
        RejectReceipt(teleport with { Before = teleport.Before with { Actor = teleport.Before.Actor with
            { EnginePlayer = false, ReferenceSignature = "ACHR" } } });
        RejectReceipt(teleport with { Before = teleport.Before with { Actor = teleport.Before.Actor with
            { Base = new FalloutFormKey("ForeignTeleportFixture.esm", 7) } } });
        RejectReceipt(teleport with { AfterFields = teleport.AfterFields with { Stores = teleport.AfterFields.Stores + 1 } });
        RejectReceipt(teleport with { AfterFields = teleport.AfterFields with { FadeBits = 0x3f000000 } });
        RejectReceipt(teleport with { AfterFields = teleport.AfterFields with { State = 4 } });
        RejectReceipt(teleport with { AfterFields = teleport.AfterFields with { Target = new("ForeignTeleportFixture.esm", 12) } });
        RejectReceipt(teleport with { Trace = teleport.Trace with { Owner = "" } });
        RejectReceipt(teleport with { Trace = teleport.Trace with { Changed = teleport.Trace.Changed + 1 } });
        RejectReceipt(teleport with { Trace = teleport.Trace with { OpacityReturned = !teleport.Trace.OpacityReturned } });
        RejectReceipt(teleport with { Trace = teleport.Trace with { Result = teleport.Trace.Result is null ? true : !teleport.Trace.Result } });
        if (teleport.Trace.DeltaBits is not null)
            RejectReceipt(teleport with { Trace = teleport.Trace with { DeltaBits = 0x3f000000 } });
        else
            RejectReceipt(teleport with { Trace = teleport.Trace with { DeltaBits = 0x3e800000 } });
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with { ActorProcessQuery = null }));

        void RejectReceipt(FalloutProcessTeleportFadeUpdate malformed) =>
            Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
            { FollowingServices = call with { Teleport = malformed } }));
    }
}
