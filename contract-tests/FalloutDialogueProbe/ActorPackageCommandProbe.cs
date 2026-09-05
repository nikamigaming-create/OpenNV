using OpenNV.Runtime.Content;

internal static class ActorPackageCommandProbe
{
    internal static void Exercise()
    {
        var rows = FalloutActorPackageCommands.Read("; source comment\nset timer to 1\nSpeaker.ResetAI\nOther . EVP\nThird.EvaluatePackage");
        if (rows.Count != 3 || rows[0] != new FalloutActorPackageCommand(1, "Speaker", true) ||
            rows[1] != new FalloutActorPackageCommand(2, "Other", false) || rows[2].Reset)
            throw new InvalidOperationException("Actor package dispatch lost source identity/order or reset semantics.");
        foreach (var source in new[] { "Speaker.EVP 1", "ResetAI", "if condition\nSpeaker.ResetAI\nendif" })
        {
            try { FalloutActorPackageCommands.Read(source); }
            catch (NotSupportedException) { continue; }
            throw new InvalidOperationException("Unbound actor package command was accepted.");
        }
        Console.WriteLine("OPENNV_ACTOR_PACKAGE_COMMAND_PASS sourceOrder=true resetDistinct=true invalidContextRejected=true");
    }
}
