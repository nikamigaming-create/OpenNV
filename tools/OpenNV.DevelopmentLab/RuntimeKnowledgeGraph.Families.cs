using System.Text.RegularExpressions;

internal static partial class RuntimeKnowledgeGraph
{
    // Search routing across implementations; these are not equivalence claims.
    private sealed record Family(string Name, string Pattern);
    private static readonly Family[] Families =
    [
        new("scripts", "script|expression|condition|command"),
        new("quests-dialogue", "quest|dialogue|dialog|topic|conversation|speech"),
        new("actors-ai", "actor|npc|creature|package|processlist|combatcontroller|perception|companion"),
        new("combat-weapons", "weapon|ammo|projectile|combat|damage|armor|armour|hit|explosion|vats"),
        new("inventory-services", "inventory|container|barter|merchant|repair|craft|recipe|leveleditem"),
        new("progression", "perk|skill|actorvalue|levelup|chargen|race|reputation|faction|crime"),
        new("effects-survival", "magic|effect|spell|ingest|radiation|hardcore|addiction|vitals"),
        new("world-streaming", "cell|worldspace|worldmanager|stream|terrain|landscape|grid|portal|door"),
        new("navigation-physics", "navmesh|path|collision|havok|physics|bullet|hkp|bhk|movement"),
        new("rendering", "render|shader|material|texture|lighting|shadow|geometry|nif|speedtree|vegetation|grass"),
        new("animation", "anim|skeleton|skin|bone|facegen|facial|ragdoll"),
        new("audio", "audio|sound|voice|radio|music"),
        new("ui-input", "menu|interface|pipboy|hud|input|control|tile|mouse|keyboard|gamepad"),
        new("persistence", "save|loadgame|snapshot|persistence|continuation|serialize"),
        new("resources-formats", "archive|bsa|plugin|record|form|resource|file|reader|loader"),
        new("xr", "openxr|native.xr|wrist|tracked|stereo"),
        new("platform", "thread|task|memory|allocator|lock|mutex|atomic|win32|heap"),
        new("minigames", "caravan|blackjack|roulette|casino|hacking|lockpick"),
    ];

    private static void Classify(Graph graph, string id, string path)
    {
        var matched = false;
        foreach (var family in Families)
            if (Regex.IsMatch(path, family.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                graph.Edge(id, "candidate-family", "family:" + family.Name, "path-name-heuristic"); matched = true;
            }
        if (!matched) graph.Edge(id, "unclassified-family", "family:unclassified", "unknown");
    }
}
