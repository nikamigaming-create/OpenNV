using OpenNV.Runtime.Content;

internal static class PlayerHeadVisibilityContracts
{
    internal static void Run(FalloutNpcAppearance appearance)
    {
        var extras = appearance.Models.Where(part => part.Role == "head-addon").ToArray();
        Require(extras.Length == 2 && extras.All(part => part.BipedSlots == 0 &&
            FalloutNpcAppearanceSelfView.RequireWholePartHead(part)),
            "Recursive HDPT with no BMDT slot escaped self-head classification.");
        var unrelated = appearance.Models.First(part => part.Role == "armor");
        Require(!FalloutNpcAppearanceSelfView.IsHead(unrelated with { ModelPath = "meshes/beard-and-hair.nif" }),
            "A model name replaced the source body role.");
        Require(FalloutNpcAppearanceSelfView.IsHead(extras[0] with { ModelPath = "meshes/ordinary.nif" }),
            "A head attachment needed a recognizable model name.");
        foreach (var bit in new[] { 0, 1, 9, 10, 11, 12, 13, 14, 16 })
            Require(FalloutNpcAppearanceSelfView.RequireWholePartHead(unrelated with { BipedSlots = 1u << bit }),
                "An authored head equipment slot escaped the self-camera policy.");
        foreach (var bit in new[] { 2, 3, 4, 6, 7, 8, 15, 17 })
            Require(!FalloutNpcAppearanceSelfView.IsHead(unrelated with { BipedSlots = 1u << bit }),
                "A non-head equipment slot was removed from the body.");
        foreach (var bit in new[] { 2, 3, 4, 6 })
            Throws(() => FalloutNpcAppearanceSelfView.RequireWholePartHead(unrelated with { BipedSlots = 1u | (1u << bit) }),
                "A mixed head/body or device part hid its unrelated source geometry.");
        var mask = 0x001fffffu;
        Require(FalloutNpcAppearanceSelfView.CameraMask(mask, true) == 0x0017ffffu &&
            FalloutNpcAppearanceSelfView.CameraMask(mask, false) == mask &&
            FalloutNpcAppearanceSelfView.CameraMask(0x20, false) == 0x20 &&
            FalloutNpcAppearanceSelfView.CameraMask(0x20, true) == 0x20,
            "Self-camera exclusion changed an unrelated or authored third-person mask.");
        Require(appearance.Models.Count(part => part.Role == "head-addon") == 2 &&
            appearance.Models.Contains(unrelated), "The visibility policy changed the source appearance.");
        Console.WriteLine("OPENNV_PLAYER_HEAD_ROLE_CONTRACT_OK recursiveHdpt=true zeroBipedSlots=true modelNamesIgnored=true mixedPartsRefused=true authoredMasksRetained=true");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws(Action action, string message)
    {
        try { action(); }
        catch (NotSupportedException) { return; }
        throw new InvalidOperationException(message);
    }
}
