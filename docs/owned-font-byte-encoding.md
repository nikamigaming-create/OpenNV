# Owned font byte encoding

Record strings are decoded from Windows-1252, while the owned FNT stores 256
byte-indexed glyphs. The previous lookup treated Unicode codepoints as those
indices and rejected curly punctuation reached during birthday speech.

The shared reader now reverses the source decoding exactly. The source text
path normalizes the four curly single/double quote bytes to the corresponding
straight-quote glyphs before measurement and drawing. That bounded rule applies
to every owned font; other characters retain their byte slot, original metrics
and atlas coordinates. Empty glyphs retain their authored advance. Unknown
Unicode, encoder best-fit and replacement glyphs remain unbound.

The direct bitmap painter and Godot FontFile use the same glyph selection.
Godot registers Unicode characters with the selected source metrics and UVs,
with system fallback disabled. Fixed size is assigned before the font name;
the name setter can instantiate a native cache before glyph registration, and
Godot's character lookup consults the first size cache. The owned audit exposed
an empty earlier cache hiding otherwise correctly registered glyphs.
The [pinned Godot text server](https://github.com/godotengine/godot/blob/4.7.2-stable/modules/text_server_adv/text_server_adv.cpp)
and [FontFile resource](https://github.com/godotengine/godot/blob/4.7.2-stable/scene/resources/font.cpp)
provide the native cache contract. Private original-executable observation
provides the quote normalization contract; no binary output is public input.

## Component proof

- `FalloutDialogueProbe --font-encoding-contracts` reads a synthetic complete
  FNT and decoded record strings across all 256 slots. It covers exact quote
  normalization, distinct Latin/punctuation identities, empty-glyph spacing and
  unsupported Unicode refusal.
- `NativeRenderedMenuAudit --font-encoding` reads the selected mod stack's
  actual FNT/TEX and dialogue responses. The selected birthday topic contains
  curly apostrophes. All 224 slots from space onward bind through native
  character lookup with exact advances, offsets, sizes, atlas UVs and texture
  ownership. Native quote shaping agrees with the source normalized metrics.
  Source bytes remain unchanged; recording is off.

The separate ordinary checkpoint retry remains at toddler stage80 because the
Escort reports Lead without destination progress after the player moves clear.
It has not reached the repaired birthday subtitle. The previous verified
birthday state remains stage7; no fault is cleared or consumed prefix replayed.

These checks establish the selected Windows-1252 text/font binding. They do not
establish other language encodings, complete dialogue, campaign progression,
matched retail text pixels or physical OpenXR acceptance.
