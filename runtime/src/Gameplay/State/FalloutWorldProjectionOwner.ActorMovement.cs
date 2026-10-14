namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutWorldProjectionOwner
{
    internal FalloutPlayerFrameCameraRead ReadActorMovementCamera(FalloutPlayerLateFrameInvocation invocation,
        Action<FalloutPlayerLateFrameInvocation> requireActual)
    {
        RequireCurrent(); requireActual(invocation);
        if (Source.Controls.Main.EngineSha256 != invocation.Common.ExecutableSha256 || Stack != invocation.Common.Stack || !ConstructorReturned)
            throw new InvalidDataException("Actor movement borrowed another actual published World camera.");
        var read = new FalloutPlayerFrameCameraRead(Source.Identity, Stack, World, Factory, _sequence,
            Camera.ActorMovementUpdates, Camera.ReadActorMovementTranslation());
        RequireCurrent(); requireActual(invocation); return read;
    }
}
internal sealed partial class FalloutWorldProjectionCamera
{
    internal long ActorMovementUpdates => _updates;
    internal OpenNV.Runtime.World.Physics.FalloutPhysicalVector3 ReadActorMovementTranslation()
    { RequireCurrent(); return _world.Translation; }
}
