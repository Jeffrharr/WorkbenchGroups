# Workbench Groups — DESIGN

## Goal

Let several crafting stations share one list of work orders, so any pawn can carry out
any order at whichever station is free, and let a group choose how it works through
that list — top-down as vanilla does, or one item from each order in turn.

## Why this is possible at all

`WorkGiver_DoBill` looks like it ties a bill to a bench, but it does not. Selection
iterates `giver.BillStack`, and everything downstream — ingredient search radius, the
interaction cell, haul-off, product spawning — is anchored on the bench the pawn
*walked to*, passed through as `giver` and stored as the job's `targetA`.
`JobDriver_DoBill.BillGiver` reads that target, not `bill.billStack.billGiver`.

So a bill worked at a bench that does not own it already behaves correctly. Sharing a
`BillStack` between benches needs none of that rewritten, which is what makes this a
small mod rather than a reimplementation of crafting.

## Why the shared list must live on a real bench

A long tail of vanilla hard-casts `bill.billStack.billGiver` to `Thing` or to a specific
building class: `Bill.DeletedOrDereferenced`, `Bill.Map`, the "bill complete" message
target, `Dialog_BillConfig`'s ingredient-radius ring, `BillUtility.GetBillGiverContainer`,
`UnfinishedThing`, `HaulAIUtility`.

That rules out the tidy-looking design where a group object owns the stack and points
`billGiver` at a synthetic owner. Instead one member — the **anchor** — owns the list,
and the others point at it. Every cast keeps seeing a real, spawned bench.

## Why a field swap rather than a patched property

`Building_WorkTable.BillStack` is a property, but `ITab_Bills` reads the `billStack`
*field* directly, and so does Nice Bill Tab's replacement drawer. `WorkGiver_DoBill`
reads the property. A field read cannot be patched, so the only mechanism that satisfies
both readers is genuinely pointing them at the same object: on link, a member's
`billStack` field is assigned the anchor's stack, and its own list is set aside.

The cost is one unavoidable patch. `Building_WorkTable.ExposeData` deep-saves `billStack`
unconditionally, so N members would write the same bills N times — which warns on save,
hard-errors on load with a duplicate load ID, and leaves `job.bill` resolving to an
arbitrary copy. A prefix swaps each member's own list back in for the duration of the
save; a **finalizer**, not a postfix, restores it, so an exception anywhere upstream
cannot leave a bench silently unlinked.

## Why no MapComponent owns anything

All persistent state is on `CompBillGroup`, injected onto every groupable work table at
startup. Two reasons:

- **Gravships.** Odyssey moves the same Thing instances to a different `Map`. Thing-local
  state travels for free; map-scoped state would have to be migrated by hand or dissolved
  on every jump. Vanilla's own `StorageGroup` is map-bound and simply dissolves — tolerable
  for a storage filter, not for a dozen work orders.
- **Removability.** Every bench's list stays in its own ordinary vanilla save node, so
  uninstalling the mod leaves valid bills everywhere and no dangling references.

`BillGroupIndex` is a `MapComponent`, but it saves nothing — it only caches the
anchor→members direction, which the comps cannot answer on their own, and is rebuilt from
them on demand.

## Round robin rotates the list, at job start

Rotation is implemented by really moving the started bill to the bottom of the shared
list. Vanilla works the list top-down, so that *is* round robin, with no selection code of
our own to keep correct — and it avoids patching `WorkGiver_DoBill`'s selection loop,
which is private and already rewritten wholesale by Hauler's Dream.

The trigger is `Pawn_JobTracker.StartJob`, not `Bill.Notify_IterationCompleted`. Rotating
on completion is correct with one worker and wrong with several: three pawns scanning
together all see the same bill at the head and all take it, rotating only afterwards —
"three of A, then three of B". One `DoBill` job is exactly one iteration, so rotating at
the start is equivalent for one worker and right for many.

The player's authored order is snapshotted by bill load ID when the mode goes on, and
reprojected onto the live list when it goes off, so trying the mode does not permanently
scramble their priorities.

## Overshoot prevention

Linking creates a problem vanilla cannot have: several pawns starting the same "make 5"
order at once. The counters only move when a craft *finishes*, far too late.

The fix is to count work already underway as if it were produced. `InFlightTracker` keys
off `job.bill` — deliberately not the job's def, so bill work run under another mod's
JobDef still counts — and walks `AllPawnsSpawned` rather than free colonists, because
mechs and slaves do bills and a bill can be restricted to exactly those.

It is maintained incrementally (increment on `StartJob`, decrement on the private
`CleanupCurrentJob`, which is the one funnel every job ending passes through) with a
periodic full reconcile as a backstop. Computing it on demand inside `ShouldDoNow` was
rejected: that method runs for every bill giver, for every pawn, on every work scan, and
once per bill per frame while the tab is open.

## Deliberate exclusions

- **Benches with no shareable recipe at all.** Eligibility is decided by what a bench
  *makes*, not by its C# class. `BillUtility.MakeNewBill` picks the `Bill` subclass from the
  `RecipeDef` alone — `UsesUnfinishedThing`, `mechResurrection`, `gestationCycles > 0`,
  `formingTicks > 0`, else plain `Bill_Production` — so the only bill type we can share is
  predictable from the def, with no reference to the bench's class. A bench is offered the
  gizmo when at least one of its recipes makes a plain `Bill_Production`; if none does, a
  group could never hold anything and the gizmo would be a lie.

  This replaced a whitelist of two exact types, which was wrong in both directions. It
  excluded every modded bench with a custom `thingClass`, and it *included*
  `SubcoreEncoder` — a plain `Building_WorkTable` whose one recipe has `formingTicks`, so
  the old rule would have put a `Bill_Autonomous` into a shared stack.

  **"At least one" and not "every one" is the load-bearing choice.** The stricter form is
  tempting because it makes an unshareable bill impossible on a grouped bench. Measured
  against the loaded def database it excludes every crafting bench in the game: apparel,
  weapons, armour and sculptures all use unfinished things, so tailoring benches (1 of 45
  recipes plain), smithies (3 of 23), the machining table (7 of 70), the crafting spot
  (3 of 19) and the bioferrite shaper (1 of 20) all lose the gizmo. That trades a modded-
  bench gap for a far larger vanilla one. The census the `eligibility_gate` scenario logs is
  what settled it; the rule was written the strict way first and the numbers changed it.

  So the recipe test lives at the bill instead, where the danger actually is:
  `Patch_BillStack_AddBill` refuses a non-shareable bill entry into a shared stack, on every
  route — the tab's dropdown, paste from the clipboard, another mod adding one in code.
  A grouped machining table still offers "make assault rifle" and refuses it with a message
  naming the bill, which is a smaller surprise than the gizmo being absent from every
  crafting bench in the colony.
- **Bench classes assignable to `Building_WorkTableAutonomous`.** A safety net, not the
  rule. That class and its descendant `Building_MechGestator` cast the bill's owner back to
  their own type, so a wrong-class anchor throws every frame rather than degrading. Their
  recipes already give them away, which makes the check redundant today; it is there for a
  future vanilla subclass whose recipes do not.
- **Anything the player names.** A mod setting holds `thingClass` names to leave alone,
  matched on either the qualified or the bare form. This is the escape hatch for the one
  thing no def-level rule can see: a modded bench class hard-casting `billStack.billGiver`
  to its own type inside its own code. No rule over defs can detect that before it throws,
  and the stack trace the player is already looking at names the class, so pasting it into a
  box is a same-evening fix rather than a wait for a release. `BillGroupOps.Link` also rolls
  back if anything throws partway through, so a bench class we admitted on trust cannot cost
  the player their work orders.
- **Bills that are not exactly `Bill_Production`.** `Bill_ProductionWithUft` is the
  painful one: an unfinished item left on a non-anchor bench fails `HaulAIUtility`'s
  "inside the owner's footprint" test forever and can never be hauled away.
- **Different recipe sets.** Vanilla's selection has no notion of "this bill is only valid
  at some of these benches"; requiring identical sets makes every bill trivially valid
  everywhere, which is what lets the selection loop stay untouched.

## Showing a group on the map

A selected bench draws two things: a yellow outline around its groupmates, and `GenDraw.DrawLineBetween` to each of them. The line deliberately uses the same default material vanilla uses between a workbench and its facilities, so a group reads as "these are connected" in a visual language players already have, rather than in a second convention of our own. Both draw off a *single* selected bench, which turns out to matter.

Selecting one bench can also select the whole group (`Patch_Selector_Select`), which is what people expect after using linked storage. **It ships off**, for a reason no probe could have found:

> RimWorld shows no ITab for a multi-selection. Two selected stoves give an inspect pane reading "Electric stove x2" and no tabs at all — so auto-selecting the group makes the bills tab unreachable by clicking a bench, and the bills tab is the whole point of this mod.

That surfaced the first time the feature was screenshotted, and it is why the visual sequence exists. The second consequence stands on its own and is the reason vanilla's storage groups do not auto-select either: gizmos act on the whole selection, so clicking one bench and pressing Deconstruct deconstructs the group.

The setting is kept rather than dropped because the group-at-a-glance reading is genuinely useful when arranging a workshop rather than editing orders — and because the informative half, the line and the outline, is available either way. `wbg_selected_count` pins the shipped default, so a change that silently turned expansion on would fail rather than quietly take the bills tab away from everyone.

## Marking bills in the list

Each row in a grouped bench's bill list carries a chain icon, drawn by a postfix on
`Bill.DoInterface` — which ends its own `BeginGroup` before returning and hands back the row's
rect in absolute coordinates, so the postfix needs no offset arithmetic. It sits left of the
suspend/copy/delete trio that occupies the row's top-right 76 pixels.

The textures are vanilla's own `LinkStorageSettings` and `UnlinkStorageSettings`, the pair this
mod's link and unlink gizmos already use. A player who has linked storage knows what a chain
and a broken chain mean, and that is worth more than a bespoke icon.

Nothing is drawn for an ungrouped bench. "Not linked" is not the same as "linked to nothing":
a broken chain on every bill of every workbench in a colony that has never used this mod would
be noise standing in for information, so the icon appears exactly when there is a group for it
to describe.

**The broken chain is scaffolding and currently unreachable.** Linking requires identical
recipe sets, so a group cannot hold a bill only some of its benches can work. It is written now
because per-bill linkage (`TODO.md` item 1) is the change that makes the state reachable, and an
icon added at the same time as the feature is an icon nobody checked. The rule itself is a pure
function in `Source/Core/BillLinkage.cs` with unit tests, so what is untested is the three lines
that put a texture on screen rather than the decision behind them.

### Which order is being worked

Each row is highlighted — a green left edge and a faint wash — while a pawn is working that bill,
read from the same `InFlightTracker` the overshoot guard uses.

Vanilla never needed this: it works the list top-down, so the order being worked is the one at
the top. Round robin breaks that by design, because rotating the started bill to the bottom is
precisely what makes vanilla's top-down selection produce round-robin behaviour. After the
rotation the top of the list answers "what happens next", and nothing answers "what is happening
now".

It also disambiguates a rough edge listed below. The overshoot guard makes a fully-claimed bill
report "would not start now", and vanilla paints any such bill pink — which reads as *blocked*
when it means *already being handled*. A green edge on the same row separates those readings.

Drawn on ungrouped benches too: the tracker counts every bill a pawn commits to, so there is no
reason to withhold an indicator vanilla lacks entirely.

## Known rough edges

- **Bills render pink while being worked.** `Bill.BaseColor` pinks any bill that would not
  be started right now, and the overshoot guard makes that true of a bill already claimed.
  Surfaced in the settings tooltip; switchable.
- **A group shares vanilla's cap of 15 bills**, not 15 per bench. Refused at link time with
  the actual count rather than silently truncated.
- **Message and dialog look-targets point at the anchor**, not the bench that finished.
- **A rebuilt bench is a new Thing** and silently leaves its group. Detected and shown on
  the inspect string only.

## Cross-mod notes

- **Hauler's Dream** postfixes `WorkGiver_DoBill.JobOnThing` three times and replaces the
  returned job. We never touch that result — our only patch there swaps a field around the
  call — and our counting keys off `job.bill`, so its batch jobs are still seen. Its batch
  crafting queues several iterations under one job, so counts are undercounted there.
- **Nice Bill Tab** reads the `billStack` field, so the swap is transparent. Its
  drag-reorder mutates `BillStack.Bills` directly and can fight an in-flight rotation.
- **Nice Bill Tab Expansion** postfixes `Building_WorkTable.ExposeData` for unrelated
  state; ordering is declared so the shared target is visible in the load graph.

## Status

Implemented, unit-tested, and exercised in a running game.

**Offline** (`./test.sh`, 109 tests): the pure core in `Source/Core/`, plus Mono.Cecil checks
on every vanilla member the patches depend on — including the four `RecipeDef` members the
eligibility gate reads, and the set of `Bill` types `BillUtility.MakeNewBill` constructs. That
second one is the gate's real dependency: a fifth branch added there would let a new bill type
into shared stacks with nothing else failing.

**Live** (`RimWorldTestHarness`, scenarios in `Tests/Scenarios/`, probe bridge in `TestMod/`).
All probes pass:

| Scenario | What it establishes |
|---|---|
| `eligibility_gate` | The recipe-shaped rule against the real def database, all DLC loaded: 14 named benches groupable, 5 not. Logs a census of every work-table def with its verdict and plain-recipe count, so a RimWorld update that shifts the rule is diagnosable from one run. |
| `link_smoke` | The mod loads. 19 work tables get the comp; no errors, no failed patches. |
| `round_robin_rotation` | Group size 2; mode toggle takes; **3 bills visible from the second bench**, which is the field swap working; head bill cycles 0 → 1 → 2 → 0 across three starts. |
| `overshoot_guard` | A `repeatCount = 1` bill goes from "would start" to "would not" the moment one pawn claims it. |
| `shared_save_integrity` | **Zero duplicate load-ID warnings** on save, and sharing intact afterwards. |
| `reload_roundtrip_save` + `reload_roundtrip_load` | The save/reload round-trip, run as two game loads by `Tests/run_roundtrip.sh` (kept in `Tests/Scenarios/roundtrip/`, since it needs a fixture the rest of the suite does not) — phase A links, adds three bills, switches on round robin and saves; the script copies that save into the harness's `Fixtures/`; phase B boots with it and only probes. **After the load the two benches' `billStack` fields are the same object**, all three bills are visible from the second bench, and the group is still in round robin. |

The rotation and overshoot scenarios drive real jobs carrying real bills through
`Pawn_JobTracker.StartJob`, so the shipped Harmony postfix is in the path rather than
being bypassed by the test calling our own code.

Three captures in `Tests/Screenshots/` walk the round-trip: the second bench's empty tab
before linking, the same bench showing the group's three bills after, and the same again in a
second game load. Everything this mod does is invisible on the map — two stoves look identical
linked or not — so the bills tab is the only frame worth taking, and `WbgFocusBench` exists to
frame it.

That sequence immediately earned itself: the "after link" frame read **"Linked: 2 stations (in
order)"** on a group that was in round robin. `ordering` is anchor-only state, and
`CompInspectStringExtra` was reading the selected bench's own copy, so every non-anchor bench in
a round-robin group told the player the opposite of what the group did. No probe could catch it,
because every probe read the anchor; `wbg_member_reported_mode` now reads what a follower
reports, and is asserted on both sides of the save.

The round-trip's key probe is `wbg_stacks_reference_equal`, not a bill count. Counting bills
would pass on two benches that each came back holding their own deep-loaded copy of the same
three bills, which is exactly what a missing redirect produces. Nothing about object identity
is visible in a frame, so there is no screenshot; the check that the scenario is not vacuously
green is a negative control — pointed at `minimal_colony.rws` instead, every probe fails and
`WbgTrackGroup` reports no group on the map.

### What is not yet verified

- **The full craft loop.** The live scenarios test the decision made when a pawn commits
  to a bill, holding the job as a `Wait` rather than `DoBill`. Whether pawns then walk to
  the right bench and produce the right number of items is untested; it would make the
  result depend on the fixture colony's food, power, pathing and work priorities.
- **Anchor handover, gravship transport, and minify/reinstall.** All reasoned about
  carefully and none exercised.
- **Interaction with the conflicting mods** listed above. Only the baseline load was run.
- **The `AddBill` refusal, in play.** The predicate behind it is unit-tested and the
  eligibility census proves which benches can reach the case, but no scenario has yet added
  an unfinished-thing bill to a grouped bench and watched it be refused.
- **The link rollback.** Written against a throw we cannot reproduce on demand, so it has
  never run.
