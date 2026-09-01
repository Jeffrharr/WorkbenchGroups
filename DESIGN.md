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

- **Benches that are not exactly `Building_WorkTable`.** `Building_WorkTableAutonomous`
  and `Building_MechGestator` derive from it and then cast the bill's owner back to their
  own class. A wrong-class anchor throws every frame rather than degrading.
- **Bills that are not exactly `Bill_Production`.** `Bill_ProductionWithUft` is the
  painful one: an unfinished item left on a non-anchor bench fails `HaulAIUtility`'s
  "inside the owner's footprint" test forever and can never be hauled away.
- **Different recipe sets.** Vanilla's selection has no notion of "this bill is only valid
  at some of these benches"; requiring identical sets makes every bill trivially valid
  everywhere, which is what lets the selection loop stay untouched.

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

Implemented and unit-tested. Offline suite covers the pure core in `Source/Core/` plus
Mono.Cecil checks on every vanilla member the patches depend on. Not yet exercised in a
running game — see the plan's live-harness verification, which is still outstanding.
