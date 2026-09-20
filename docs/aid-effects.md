# Ingestible health effects

The Items/Aid page selects a source ALCH item and exposes the owned Use label.
Flat and wrist menus call the same C# transaction. It validates every selected
effect and resolves the consumption sound before removing one inventory item.
A rejected effect leaves inventory and vitals unchanged. Preparation also rejects
stale inventory/vitals and duplicate commits.

The reader binds ENIT(20), EFID, EFIT(20), CTDA and MGEF DATA(72), resolving links
through the winning plugin stack. Value Modifier and Value and Parts effects are
admitted for Health. No Duration and No Magnitude flags are honored. Medicine and
Food use the winning fMagicMedicineSkill and fMagicSurvivalSkill settings.
Value and Parts also restores source BPTD limb-condition percentages. Health
cannot exceed its current maximum. HasPerk and IsHardcore select the supported
branches; source actor perk ranks and acquired progression remain unsupported.

Duration effects preserve their selected magnitude, elapsed time, duration and
source hashes. They advance on the shared gameplay clock, cap the final increment
at expiration, pause with flat gameplay and continue while the wrist UI leaves
the XR world live. Save v14 carries these timers; legacy v13 has none. Source
drift and malformed timer state reject restoration.

Synthetic contracts exercise atomic rejection, duplicate/stale input, limb
healing, max-health clamping, fractional frames, expiration and cold continuation.
The owned-data probe resolves actual Stimpak normal and hardcore branches,
Medicine settings and its folder-backed consumption sound. Native menu and
final-eye acceptance are separate from these component checks. Exported flat and
Elliott Tate simulator wrist input both consumed one Stimpak from nine to eight;
flat F5 saved that count in v14. Both final eyes were inspected. These native
checks began at full health and do not prove post-combat healing or ergonomics.

Radiation, timed attributes/skills, addiction, scripted effects (including
Super Stimpak's debuff), Limb Condition archetype and additional effect
presentation remain unbound and reject before consumption. Exact retail timing,
stack ordering, sound variation and physical-headset ergonomics are unproven.

Published format/semantic references:
[Ingestible](https://geckwiki.com/index.php/Ingestible),
[Base Effect](https://geckwiki.com/index.php/Base_Effect), and
[xEdit FNV definitions](https://github.com/TES5Edit/TES5Edit/blob/dev-4.1.6/Core/wbDefinitionsFNV.pas).
