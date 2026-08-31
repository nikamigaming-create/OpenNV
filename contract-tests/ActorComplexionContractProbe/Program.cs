using OpenNV.Runtime.Presentation.Actors;

using Surface = OpenNV.Runtime.Presentation.Actors.ActorComplexionJoin.SurfaceCoverage;

var complete = new[]
{
    new Surface("head", true, true, true, false),
    new Surface("body", true, true, false, true),
    new Surface("exposed-arm", true, true, false, true),
    new Surface("left-hand", true, true, false, true),
    new Surface("right-hand", true, true, false, true),
    new Surface("outfit", false, true, false, false),
    new Surface("hidden-skin", true, false, false, false),
};
ActorComplexionJoin.ValidateCoverage(complete);

ExpectFailure(
    complete.Select(surface => surface.RuntimeNodeName == "exposed-arm"
        ? surface with { RuntimeTransfer = false }
        : surface).ToArray(),
    "exposed-arm");
ExpectFailure(
    complete.Select(surface => surface.RuntimeNodeName == "outfit"
        ? surface with { RuntimeTransfer = true }
        : surface).ToArray(),
    "outfit");
ExpectFailure(
    complete.Select(surface => surface.RuntimeNodeName == "head"
        ? surface with { FaceGenAuthority = false }
        : surface).ToArray(),
    "exactly one live FaceGen head authority");

Console.WriteLine(
    "OPENNV_ACTOR_COMPLEXION_CONTRACT_PASS visibleSkin=5 outfitExcluded=1 hiddenExcluded=1");

static void ExpectFailure(IReadOnlyList<Surface> surfaces, string expectedMessage)
{
    try
    {
        ActorComplexionJoin.ValidateCoverage(surfaces);
    }
    catch (InvalidOperationException exception)
        when (exception.Message.Contains(expectedMessage, StringComparison.Ordinal))
    {
        return;
    }

    throw new InvalidOperationException(
        $"Actor complexion contract did not fail closed for {expectedMessage}.");
}
