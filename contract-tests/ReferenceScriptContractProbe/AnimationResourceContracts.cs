using OpenNV.Runtime.Content;

internal static class AnimationResourceContracts
{
    internal static void RunOwnedCreatureWeapon(string dataRoot)
    {
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(dataRoot), includeImplicitPlugins: false);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var appearance = FalloutCreatureAppearanceResolver.Resolve(records, records.RuntimeFormKey(0x168bd9));
        var weapon = FalloutWeaponPresentation.Read(records, records.RuntimeFormKey(0x046bdd), firstPerson: false);
        Require(weapon.AttackAnimation == 255 && weapon.AttackGroup == "attackright",
            "Reached source weapon did not resolve its ordinary default group.");
        var variants = content.ActorAnimations.Variants(appearance.SkeletonPath[..appearance.SkeletonPath.LastIndexOf('/')],
            weapon.AnimationGroup + weapon.AttackGroup, appearance.AnimationFiles);
        Require(variants.Count == 3 && variants.All(path => path.Contains("/specialanims/", StringComparison.OrdinalIgnoreCase)) &&
            variants.Select(content.ActorAnimations.SelectedGroup).SequenceEqual(new[] { "AttackRight", "AttackRight_2", "AttackRight_3" }),
            "Reached model-owner declarations did not bind the three source base attack sequences separately from up/down.");
        Console.WriteLine("OPENNV_OWNED_CREATURE_WEAPON_BINDING_PASS defaultPistolGroup=true declaredAlternatives=3 directionalGroupsSeparate=true sourceReadonly=true gameplay=false");
    }

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
            [first + "/h2hattackleft_a.kf"] = ["AttackLeft"],
            [first + "/h2hattackleft_b.kf"] = ["AttackLeft"],
            [first + "/h2hattackright_a.kf"] = ["AttackRight", "A"],
            [first + "/h2hattackpower_a.kf"] = ["AttackPower", "AttackPower_A"],
            [first + "/h2hattackleft0_a.kf"] = ["AttackLeft"],
            [first + "/h2hattackforward_a.kf"] = ["AttackForward", "Forward"],
            [first + "/h2hattackcustom_a.kf"] = ["AttackCustom"],
            [first + "/h2hattackcustom_b.kf"] = ["Idle"],
            [first + "/specialanims/1hpattackright.kf"] = ["AttackRight"],
            [first + "/specialanims/1hpattackright_2.kf"] = ["AttackRight_2"],
            [first + "/specialanims/1hpattackright_3.kf"] = ["AttackRight_3"],
            [first + "/specialanims/1hpattackrightup.kf"] = ["AttackRightUp"],
            [first + "/specialanims/1hpattackrightdown.kf"] = ["AttackRightDown"],
            [first + "/specialanims/h2hattackright_broken.kf"] = ["Idle"],
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
        var complete = owner.Variants(first, "2hahandgrip1");
        Require(complete.Count == 2 && complete.Contains(first + "/2hahandgrip1.kf", StringComparer.OrdinalIgnoreCase) &&
            complete.Contains(first + "/2hahandgrip1_variant.kf", StringComparer.OrdinalIgnoreCase),
            "Explicit action catalog discarded a valid exact or suffixed source sequence.");
        var repeated = owner.Variants(first, "2hrhandgrip3");
        Require(repeated.Count == 2, "Explicit action catalog lost a source alternative.");
        before = reads;
        Require(ReferenceEquals(repeated, owner.Variants(first, "2hrhandgrip3")) && reads == before,
            "Explicit action catalog reread source variants.");
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
        var baseGroups = owner.Variants(first, "h2hattackleft");
        Require(baseGroups.Count == 2 && baseGroups.All(path => owner.SelectedGroup(path) == "AttackLeft"),
            "Authored suffixed files lost their uniquely exported base action group.");
        Reject(() => owner.Find(first, "h2hattackleft"));
        var mixedGroups = owner.Variants(first, "h2hattackright");
        Require(mixedGroups.Count == 1 && owner.SelectedGroup(mixedGroups[0].ToUpperInvariant()) == "AttackRight",
            "A filename suffix selected an unrelated group instead of the catalog's compatible base group.");
        var exactGroup = owner.Variants(first, "h2hattackpower");
        Require(owner.SelectedGroup(exactGroup.Single()) == "AttackPower_A",
            "Exact exported variant did not retain priority over its base group.");
        Reject(() => owner.Variants(first, "h2hattackleft0"));
        Reject(() => owner.Variants(first, "h2hattackforward"));
        Reject(() => owner.Variants(first, "h2hattackcustom"));
        Require(owner.SelectedGroup(first + "/h2hattackcustom_a.kf") is null,
            "Failed catalog validation published a partial sequence binding.");
        var declared = files.Keys.Where(path => path.StartsWith(first + "/specialanims/", StringComparison.Ordinal)).ToArray();
        Require(owner.Find(first, "1hpattackright") is null, "Undeclared nested folders leaked into ordinary lookup.");
        var declaredVariants = owner.DeclaredVariants(declared, "1hpattackright");
        Require(declaredVariants.Count == 3 && declaredVariants.All(path =>
                !path.Contains("up.kf", StringComparison.Ordinal) && !path.Contains("down.kf", StringComparison.Ordinal)) &&
            declaredVariants.All(path => owner.SelectedGroup(path)!.StartsWith("AttackRight", StringComparison.Ordinal)),
            "KFFZ base action variants lost their declared paths or included directional groups.");
        before = reads;
        Require(ReferenceEquals(declaredVariants, owner.DeclaredVariants(declared.Reverse().Select(path => path.ToUpperInvariant()).ToArray(), "1HPATTACKRIGHT")) &&
            reads == before, "Declared source binding was order/case dependent or reread its clips.");
        Require(owner.DeclaredVariants([], "1hpattackright").Count == 0,
            "A missing declaration borrowed another actor's cached variants.");
        var restricted = owner.DeclaredVariants([first + "/specialanims/1hpattackright_2.kf"], "1hpattackright");
        Require(restricted.Count == 1 && restricted[0].EndsWith("/1hpattackright_2.kf", StringComparison.Ordinal) &&
            declaredVariants.Count == 3, "Actors sharing a directory borrowed another model owner's KFFZ alternatives.");
        Require(ReferenceEquals(baseGroups, owner.Variants(first, "h2hattackleft", declared)),
            "Unrelated assigned groups suppressed an ordinary source attack.");
        Reject(() => owner.Variants(first, "h2hattackright", [first + "/specialanims/h2hattackright_broken.kf"]));
        Reject(() => owner.DeclaredVariants([first + "/h2hattackcustom_a.kf", first + "/h2hattackcustom_b.kf"], "h2hattackcustom"));
        Require(owner.SelectedGroup(first + "/h2hattackcustom_a.kf") is null,
            "A failed declaration catalog published a partial binding.");
        var pistol = new FalloutWeaponPresentation(new("Fixture.esm", 1), null, "1hp", 1, 255)
        { AttackAnimation = 255, WeaponAnimationType = 3 };
        Require(pistol.AttackGroup == "attackright" && (pistol with { WeaponAnimationType = 4 }).AttackGroup == "attackright",
            "Ordinary pistol default did not resolve the qualified right attack group.");
        Require((pistol with { AttackAnimation = 26 }).AttackGroup == "attackleft", "Explicit weapon attack group was overwritten.");
        foreach (var type in new uint[] { 10, 11, 12, 13 })
            Reject(() => _ = (pistol with { WeaponAnimationType = type }).AttackGroup);
        Console.WriteLine("OPENNV_ACTOR_ANIMATION_RESOURCE_PASS authoredVariants=true exportedGroups=true exactPriority=true folderIsolation=true caseInsensitive=true cached=true ambiguousRefused=true malformedRefused=true missingPath=true baseGroupVariants=true exactVariantPriority=true explicitGroupBinding=true partialCatalogRefused=true sourceDeclarations=true directionalIsolation=true defaultPistolGroup=true");
    }

    private static void Require(bool valid, string error) { if (!valid) throw new InvalidDataException(error); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Unowned animation variant was selected.");
    }
}
