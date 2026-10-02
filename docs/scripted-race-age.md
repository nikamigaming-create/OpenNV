# Scripted race aging

`AgeRace` accepts a signed step count and traverses the winning RACE
YNAM/ONAM links through the shared reference world. Missing endpoints saturate,
and repeated links use bounded cycle traversal. Wrong record types and counts
outside the signed integer range remain visible failures. The
original character-creation selection remains separate from the effective race,
so a nonplayable child tier never becomes a playable selection.

Actor height is mutable persisted state. A zero NPC height uses the selected
sex-specific race height. A race transition replaces the stored height only
when it equals the old race's height; custom height remains. Source hair fallback
and an explicit bald override survive cold restoration. NPC appearance changes
invalidate the cached native movement envelope.

Focused contracts and the selected owned CG02 -1/+1 command audit pass, including
cold race/height/hair and source appearance resolution. An ordinary bot hall
continuation executes the birthday -1 command before the missing-actor failure.
The native child camera/capsule, full outfit presentation and matched retail
visual behavior remain unverified. No guessed camera-height multiplier is added.
