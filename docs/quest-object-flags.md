# Quest-object inventory flags

[SetQuestObject](https://geckwiki.com/index.php/SetQuestObject) changes the shared
base object's quest-item flag. [Quest items](https://geckwiki.com/index.php/Quest_Item)
are protected from ordinary player transfer and excluded from carried weight.

FalloutQuestObjectFlags retains an effective overlay on the winning source form.
The ESM/ESP header and payload remain immutable. Result/reference scripts and
fallback quest scripts use the same typed command and form owner. Supported
inventory form types retain their source header, signature and payload hash;
invalid flags, unsupported types and changed source identities reject before
an effective mutation. Actor process priority and cleanup semantics remain
unowned, so actor/TACT flag mutations fail visibly.

Container and barter eligibility, scripted RemoveAllItems and the player's
carried-weight owner read this shared flag. Weight cache invalidation includes
the form-state revision independently of inventory count/equipment revisions.
Explicit script removal retains its separate existing authority. Other source
playability and equipment protections still apply after clearing a quest flag.

Campaign save v33 retains the changes in its shared script session. Restoring
the flag collection validates all rows and their winning headers/payloads before
replacing effective values. Older saves read their established lanes without
being rewritten or acquiring invented overrides. Legacy schemas cannot carry
the new mutable-form lane.

Synthetic checks exercise a winning plugin override, result and fallback script
dispatch, transfer/RemoveAllItems, independent weight invalidation, session cold
restoration and atomic malformed/source-drift rejection. The selected original
TTW stage70 SetQuestObject command passes an isolated source-result-subset/cold
check. Its other stage effects are not executed by that fixture; it does not
establish ordinary roach death, campaign completion or matched retail parity.
