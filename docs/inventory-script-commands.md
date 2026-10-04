# Inventory script commands

`FalloutInventoryCommands` binds result, reference and quest execution to the
same player or reference inventory. Production hosts supply the actual player
level and actor presentation callback. The quest fallback refuses an NPC query
without that level owner.

`AddItem` and `RemoveItem` use that same retained owner for the player, NPCs,
creatures and containers. Additions share leveled-item expansion, instance
variants and persistent random state. Removals clamp to available contents and
retire depleted equipped identities. Nonplayer changes never publish player HUD
notices. Changed NPC contents invalidate the existing appearance owner; an
absent removal creates no revision. Worn armor removal preserves the actor's
animation owner. Deleting the last worn weapon requires its native retirement
callback before the inventory changes, so an unsupported active attachment
cannot leave a partial mutation.

`RemoveAllItems` deletes or transfers the eligible contents as one prepared
transaction. Player quest items and nonplayable biped items remain. Transfers
retain condition and count; the ownership flag controls only the moved
instances, not items already present in the destination. Equipped identities
removed from the source are retired. The operation does not reroll inventory.

`EquipItem` / `EquipObject` use an existing armor or weapon item. The explicit
no-unequip flag persists with the equipped item, blocks ordinary unequip and
conflicting equipment, and is released by explicit script unequip. Cold loads
reject a locked item that is absent from the equipped set. Legacy schemas cannot
claim this new state. The reached silent form accepts either no-unequip flag.
The prior one-argument silent behavior remains; visible player equip notices,
book/ammo/ingestible consumption and equipment changes during a presented NPC
weapon's lifetime still fail closed.

`UnequipItem` binds the source item, NoEquip and HideEquipMsg operands to this
same owner. The reached `0 1` flags explicitly release an equipment lock and
remove the worn identity while preserving the item and its count. Actor
presentation must admit retirement before mutation; appearance and explicit
unequipped history share reference persistence. Vanilla script unequip does
not dispatch an OnUnequip event. Omitted HideEquipMsg is false; an unowned
visible player notice and NoEquip admission lock fail before equipment changes.
The source argument contract follows [GECK UnequipItem](https://geckwiki.com/index.php/UnequipItem).

`ResetInventory` rebuilds a nonplayer container from the winning source,
retained actor templates and the next persisted inventory random state. It
keeps the existing inventory object so combat and other readers retain their
owner. Script unequip overrides are cleared and NPC appearance is invalidated.
Player reset requires a separate initial-player inventory contract.

`GetEquippedObject` / `GetEqObj` returns a typed base form or null from that same
equipped state. Slots 0–19 use biped bits; weapons occupy slot 5. Other integer
slots use the source wildcard mask. Multiple matches require source entry
ordering and fail closed. An NPC with unresolved default weapon selection does
not report a fabricated empty weapon slot. This query contract follows
[xNVSE's inventory implementation](https://github.com/xNVSE/NVSE/blob/6.4.9/nvse/nvse/Commands_Inventory.cpp)
and its [slot mapping](https://github.com/xNVSE/NVSE/blob/6.4.9/nvse/nvse/GameForms.cpp).

`--inventory-command-contracts` checks production script dispatch, protected
items, transfer variants, lock conflicts, cold equipment, same-object reset,
typed query results, lazy branches and failed-transfer atomicity.
It also checks source-dispatched NPC/creature/container counts, variant
preservation, worn armor removal, unchanged player contents, partial weapon
stacks, rejected native retirement and cold count/equipment continuation.
It also checks original three-argument unequip, forced lock release, unchanged
item counts, rejected admission/notification flags and cold actor overrides.
`--audit-inventory-commands <mod> <root> <game> <quest> <stage> [dependencies...]`
executes each inventory-bearing owned result entry as an isolated component.
It checks source order, cold inventory/random continuation and unchanged source
bytes. This does not certify the surrounding stage, a module initializer,
ordinary campaign traversal or retail visual parity.
The selected original TTW CG02 stage42 RemoveItem is admitted against Dad's
winning default inventory and has identical cold behavior. Its default fixture
has no party hat, so that check establishes the command's actor binding and
absent-item behavior; the synthetic worn-item check covers actual removal.
The surrounding MoveTo and door commands retain their ordinary runtime owners.
