# Authored trait-menu presentation

The classic trait screen now renders the winning `menus/trait_menu.xml`, expanded
prefabs, owned bitmap fonts, atlas artwork and installation colors. Its labels,
descriptions and icons come from winning GMST/PERK fields. The distinct legacy
`trait_select_menu.xml` is not the source menu opened by ShowTraitMenu.
TTW's winning birth script requests this menu after naming and appearance;
changing its presentation does not change that quest sequence.

FalloutTraitMenuSelection owns an independent draft. The winning
`iTraitMenuMaxNumTraits` supplies its limit. Selection permits fewer traits,
including none; Reset clears the draft, and Done submits through the existing
shared opening owner. Title/counter strings select their source singular/plural
forms and format the maximum/remaining count. Copied limits register a numeric
setting refresh boundary instead of silently accepting an invalidating mutation.

NativeOwnedTraitMenu projects that draft through the source list template,
selection marker, highlight, description, image, counter and Reset/Done controls.
The admitted equal-level playable pool sorts names case sensitively. Source row
height, or wrapped text plus source vertical spacing, determines the list extent.
Source scrollbar steps distribute overflow; ordinary wheel and arrow/page input
change that owner. The shared tile renderer resolves case-insensitive relative
names, attribute-terminated relative names, copied texture strings, absolute-value
expressions and inherited branch clipping. Owned resources remain unchanged.

Synthetic checks cover winning limit overrides, retained-limit mutation rejection,
foreign/drifted identities, descriptions/icons, capped drafts, reset, acceptance,
source formatting and relative-name/absolute-value expressions. An isolated owned
native check renders the actual menu, tests pointer and keyboard selection, the
limit, Reset and Done, and restores identical pixels at matched focus after reset.
It records no frames. RuntimeNativeTraitEntry owns modal pause while the source
menu keeps processing input. The fixture verifies paused gameplay clocks,
Done/resume, previous-pause preservation, idempotent release and exit cleanup.
Fresh ordinary TTW input reaches the same menu after twelve completed voiced
commands, shows its source artwork and retains identical speech, camera-package
time, quest progress and draft across an extended hold. Done accepts no traits
and resumes source progression; seventeen speeches complete and CG01 stage 0
is entered before its unsupported SetSoundSourceFile. The session then quits
with readers drained. Childhood-room entry, full campaign and matched timing
remain unverified. See [the shared youth and pause owners](player-youth-appearance.md).

Perk conditions/multiple ranks, native unstable sort ties and non-ASCII collation,
description-scrollbar extent, scrollbar dragging, focus/click sound, close/layout
timing, UIO extension composition and matched retail/XR pixels remain unbound.
The source pool still admits its existing bounded playable-trait declaration.
Other generic screens, including tag allocation, crafting and session recovery,
require their own source menu/behavior owners. Replace each reached screen by
tracing its source request and native menu identity, binding its shared state and
dynamic tiles, then checking ordinary input, continuation and pixels. A class name
or Fallout-colored Godot widget does not establish authored presentation.
