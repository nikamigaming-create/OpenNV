# Source player starting inventory

Ordinary New Game initializes inventory from winning Player NPC CNTO/LVLI
declarations before intro results run. The engine player reference (`000014`)
has that NPC base (`000007`) independently of authored ACHR placements. The
reference world and native player bind the same persistent inventory owner;
source scripts, equipment, queries and Pip-Boy availability consume it.

Default equipment retains source nonplayable armor behavior. In the selected
TTW graph the Player NPC supplies a Pip-Boy and glove. The original birthday
RemoveAllItems, child suit and EquipPipBoy results operate on that initial state.
Later AddItem and ResetPipboyManager results keep their original meaning.
Availability still requires actual equipment; item ownership alone cannot open
the device.

Cold restoration binds the saved inventory directly and never draws starting
LVLI entries again. Legacy snapshots that omitted initialization retain their
saved contents. Missing historical state cannot be reconstructed by silently
adding equipment or replaying quest results.

`InventoryCommandContracts` covers the engine player, shared mutations,
source defaults and cold binding without an authored player placement.
`--audit-player-start-inventory` verifies the selected vanilla winning base,
equipment and shared owner. The installed TTW CG02:5 inventory command audit
verifies original removal/equipment results and cold inventory locks. These are
component audits. Fresh ordinary birth, toddler gate escape and SPECIAL input
execute with the source initial inventory. Cold Continue from the reached
CG01:80 save completes Dad's ordinary Escort and enters the birthday. At CG02:12,
ordinary Tab opens the original Pip-Boy Stats menu and source controls. Complete
menu behavior, exact actor cold recovery and matched retail pixels remain open.
