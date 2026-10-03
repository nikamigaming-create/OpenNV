using OpenNV.Runtime.Content;

internal static class AnimationResourceContracts
{
    internal static void Run()
    {
        const string first = "meshes/actors/first";
        const string third = "meshes/actors/third";
        var files = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [first + "/2hrhandgrip2_variant.kf"] = ["HandGrip2_Variant"],
            [third + "/2hrhandgrip2_variant.kf"] = ["HandGrip2_Variant"],
            [first + "/2hrhandgrip20_unrelated.kf"] = ["HandGrip20_Unrelated"],
            [first + "/nested/2hrhandgrip2_other.kf"] = ["HandGrip2_Other"],
            [first + "/2hahandgrip1_variant.kf"] = ["HandGrip1_Variant"],
            [first + "/2hahandgrip1.kf"] = ["HandGrip1"],
            [first + "/2hrhandgrip3_a.kf"] = ["HandGrip3_A"],
            [first + "/2hrhandgrip3_b.kf"] = ["HandGrip3_B"],
            [first + "/2hrhandgrip4_a.kf"] = ["OtherGroup_A"],
            [first + "/2hrhandgrip5_a.kf"] = ["HandGrip5_A", "HandGrip5_A"],
            [first + "/2hrhandgrip6_a.kf"] = [],
        };
        var enumerations = 0; var reads = 0;
        var owner = new FalloutActorAnimationResources(files.ContainsKey, folder =>
        {
            enumerations++;
            return files.Keys.Where(path => path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase))
                .Select(path => path.Replace('/', '\\').ToUpperInvariant()).ToArray();
        }, path => { reads++; return files[path]; });
        Require(owner.Require(first.ToUpperInvariant(), "2hrHandGrip2").Equals(first + "/2hrhandgrip2_variant.kf", StringComparison.OrdinalIgnoreCase),
            "Authored variant lost its group/resource suffix or case-insensitive namespace.");
        Require(reads == 1 && enumerations == 1, "Variant selection scanned or read unrelated groups.");
        _ = owner.Require(first, "2hrhandgrip2");
        Require(reads == 1 && enumerations == 1, "Repeated selection did not reuse the content-owned index.");
        Require(owner.Require(third, "2hrhandgrip2").Equals(third + "/2hrhandgrip2_variant.kf", StringComparison.OrdinalIgnoreCase),
            "First/third-person source folders leaked into one another.");
        var before = reads;
        Require(owner.Require(first, "2hahandgrip1").Equals(first + "/2hahandgrip1.kf", StringComparison.OrdinalIgnoreCase) && reads == before,
            "Exact source resource did not retain priority over its variant.");
        Reject(() => owner.Find(first, "2hrhandgrip3"));
        Reject(() => owner.Find(first, "2hrhandgrip4"));
        Reject(() => owner.Find(first, "2hrhandgrip5"));
        Reject(() => owner.Find(first, "2hrhandgrip6"));
        before = enumerations;
        Require(owner.Find(first, "2hrhandgrip7") is null && owner.Find(first, "2hrhandgrip7") is null && enumerations == before,
            "Absent group was fabricated or repeatedly rescanned.");
        try { owner.Require(first, "2hrhandgrip7"); throw new InvalidOperationException("Missing resource did not fail."); }
        catch (FileNotFoundException error)
        {
            Require(error.Message.Contains("2hrhandgrip7", StringComparison.Ordinal) && error.FileName!.Contains("2hrhandgrip7", StringComparison.Ordinal),
                "Missing-group telemetry discarded its logical path.");
        }
        Console.WriteLine("OPENNV_ACTOR_ANIMATION_RESOURCE_PASS authoredVariants=true exportedGroups=true exactPriority=true folderIsolation=true caseInsensitive=true cached=true ambiguousRefused=true malformedRefused=true missingPath=true");
    }

    private static void Require(bool valid, string error) { if (!valid) throw new InvalidDataException(error); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Unowned animation variant was selected.");
    }
}
