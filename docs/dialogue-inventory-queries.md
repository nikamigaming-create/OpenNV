# Shared inventory queries

GetItemCount function47 resolves the actual speaker, listener or explicit
reference using the declaring plugin's master order. Conversations and
Say/SayTo share the same player/reference inventory owner used by result,
object and quest expressions. Containers and actor inventories retain their
own contents; a missing inventory owner remains a visible failure. Queries do
not replace contents, reroll initialized source inventory or advance scripts.

The selected owned executable's command accepts one item argument and forwards
to its condition evaluator. Its FormList branch visits direct entries, counts
inventory objects and sums numeric counts. It does not recursively query nested
lists. The C# owner reads winning FLST LNAM entries with declaring-master
adjustment and preserves repeated entries. Notes use GetHasNote instead.
Player inventory conditions also read the shared count owner. This query does
not implement runtime FormList editing or native plugin overrides, and does
not establish the original executable's leveled-inventory bug behavior.

Synthetic checks distinguish player, speaker, container listener and explicit
reference; reorder declaration masters; cover source lists, repeated entries,
nested/non-item exclusions, large numeric sums, live changes, read-only state,
cold inventory, typed receivers and quest fallback. Missing owners, scopes,
explicit references and malformed entries are rejected. The selected installed
TTW GREETING graph passes136 source predicates:9 self,122 target and5 explicit,
including2 FormLists. It uses declared fixture inventories and retains unchanged
source hashes. The required integrated gate passes. A fresh ordinary Continue,
authored door activation and Dad follow reach birthday12. Amata's greeting and
two observed replies deliver the source skill book, reach21 and release dialogue
control without a speech/conversation error. The next ordinary Palmer interaction
selects its INFO but retains a missing native package presentation owner for an
authored creature during the cake result. Earlier failed prefixes remain retained.
Component checks do not establish complete birthday, campaign or retail parity.

Source documentation: [GECK GetItemCount](https://geckwiki.com/index.php/GetItemCount).
Owned records, binary observations, saves and diagnostics remain private.
