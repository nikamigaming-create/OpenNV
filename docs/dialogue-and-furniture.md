# Shared dialogue and furniture contract

The reference event adapter consumes native contact/input facts. It does not
choose quest outcomes. Source scripts query furniture state, change persistent
quest/reference values and issue effects to their owners. See reference-events.md.

## Conversation

INFO admission retains source order within quest priority, current quest
eligibility, speaker identity, conditions and SayOnce state. Multi-line INFOs
run begin results before the first response and end results after the last.
Explicit INFO follow-ups are tried in authored order; the first eligible one
continues the conversation. Linked topics supply choices; response RNAM overrides
the topic FULL prompt. Choice submission rechecks eligibility. Invalid input
cannot choose an unoffered topic. An unsupported reached result preserves the
executed prefix and faults the conversation, preventing repeat execution.

The original dialog menu binds those states and reports input. The existing
speech adapter owns voice/LIP playback and reports response completion. Neither
presentation owner chooses dialogue branches. Quest/stage/object result command
coverage is still incomplete, and their execution hosts need further consolidation.

Named XML tile sources resolve within the menu; absent or ambiguous names fail.
Removed choice subtrees release expression/text/art bindings. Oversized lists
currently fail because scrolling is unbound. Global topic discovery, random and
challenge flags, complete camera/pause policy and conversation snapshots remain
unimplemented. Quest running state and reference enable state are serialized;
this is not mid-interaction persistence.

## Furniture and references

Player entry/loop/exit use source FURN markers, placement settings, IDLE selection
and KF animation. Root accumulation moves the real player, while the player's
actual appearance supplies the body. NPC query shapes bind authored Havok shapes
to the real skeleton pose. They do not stand in for full character physics.
The current single-marker admission, preselected transition idles, straight-line
approach, first-person-only transition camera and missing interaction restoration
remain explicit limitations. Body loop wrap does not reset the separate camera
phase clock.

XESP is an adjusted reference, one flag byte and three unused bytes. Parent
enable state, including inversion, controls children; direct child enable/disable
does nothing. Cycles fail. Mutable root state survives cell unload and JSON
restoration. Resident presentation is built using the same factory on initial
load and later enable. Continue discards new-game prewarming that captured a
different world owner.

SwapTexture targets the authored geometry name and replaces only that instance's
diffuse texture with a winning owned DDS. Changes expire with its presentation,
matching the transient command contract. Controller-owned materials are rejected
until their mutation binding is shared. Non-fading enable/disable controls
visibility, processing and collision queries; source fades remain unsupported.

## Evidence

Synthetic contracts cover response/result order, follow-up selection, choice
filtering, changed eligibility, priority, SayOnce, pure speaker conjunctions,
ordered OR/random conditions, failure prefixes and parent enable restoration.
Owned and native audits are enumerated in current-work.md. None supplies matched
retail pixels, an ordinary opening route or physical OpenXR acceptance.

Format and command references: [xEdit FNV record definitions](https://github.com/TES5Edit/TES5Edit/blob/dev-4.1.5/Core/wbDefinitionsFNV.pas),
[Follow Up](https://geckwiki.com/index.php?title=Follow_Up),
[StartConversation](https://geckwiki.com/index.php?title=StartConversation),
[reference enable parenting](https://geckwiki.com/index.php?title=Reference),
[Enable](https://geckwiki.com/index.php/Enable),
[Disable](https://geckwiki.com/index.php?title=Disable),
[SwapTextureOnRef](https://geckwiki.com/index.php?title=SwapTextureOnRef).
Private owned records confirm these selected layouts and executable source paths;
broader behavior and compiled/source discrepancies require separate evidence.
