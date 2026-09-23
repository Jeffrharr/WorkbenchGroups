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

The price is that every one of those reads answers "the anchor", whichever bench the pawn is
actually at. For most of them that is cosmetic — a message looks at the wrong bench. The
largest real consequence used to be that orders leaving an unfinished item behind (apparel,
weapons, sculptures) could not be shared at all. That exclusion is gone; see below.

### Unfinished-item orders follow the pawn, not the list

`Bill_ProductionWithUft` was refused from shared lists because `WorkGiver_DoBill.FinishUftJob`
aims the resume job at `bill.billStack.billGiver` — the anchor — so a pawn standing at a free
member was sent to the anchor, and the haul-off cleared the anchor's cells. The refusal treated
that as structural. It is one read. Vanilla's own plain path, two screens further down, passes
`giver` for the identical call; the unfinished-item path is the outlier, not the rule.

So `UnfinishedItemSharing` answers "which bench is this item's" for all three vanilla sites that
ask, and each is widened from the anchor to the group rather than rewritten:

| Site | Fix |
|---|---|
| `FinishUftJob` — resume target and haul-off (`WorkGiver_DoBill.cs:175,180`) | Transpiler: each `ldfld billStack; ldfld billGiver` pair becomes `call ResumeGiver(bill)`, which answers the group bench the item is parked at when this pawn can use it, else the bench being scanned when it shares the list and can make the recipe, else exactly vanilla's read. |
| `HaulAIUtility.PawnCanAutomaticallyHaulFast` — "leave a parked item alone" (`:94`) | Postfix that only turns yes into no: the item sits within any group bench's footprint+1, and its bill is next due. |
| `UnfinishedThing.BoundWorkTable` — placement validator and selection line (`HaulAIUtility.cs:313`) | Postfix substituting the group bench the item is parked at. |

`Bill_ProductionWithUft.BoundWorker`'s work-type lookup also reads the anchor's def. That is left
alone: linking requires identical recipe sets, so the anchor's def answers the same, and the
worst it could ever do is unbind a worker.

**Resume where the item is parked, and move only when that bench is unusable.** The first
version resumed at whichever bench was being scanned, on the reasoning that "the pawn goes to
whichever bench is free" is what the mod promises for plain orders. That was replaced after
review. A plain order has no half-made item to carry, but an unfinished one does. Resuming at
the scanned bench carried the item across the room every time the pawn happened to scan a
different bench first, even though the bench it was sitting at was free. The player's steer was
"any bench if it works, but if the job is basically stalled, keep it to the same bench".

So `UnfinishedItemPolicy.Choose` prefers the bench the item is parked at whenever this pawn can
use it. "Use" means the same tests `JobOnThing` makes (not forbidden, not burning, powered and
fuelled, reservable, interaction spot free), plus reachability. Only when the parked bench fails
those does the job move to the scanned bench. The item is then carried there and parked at it,
so the next resume prefers that bench. It cannot ping-pong, because the item moves only when the
bench it sits at is unusable. An item parked nowhere (in a stockpile, being carried) has nowhere
to stay, so it goes to the scanned bench.

The scanned bench and the scanning pawn cross into the private method as statics set by the
`JobOnThing` prefix. The finalizer, not a postfix, clears them, so an exception mid-scan cannot
leave a stale bench for an unrelated scan to redirect towards.

**No setting.** A toggle would leave bills already merged into a shared list while the redirect
that makes them work is off — worse than either end of the switch.

**Fail closed instead.** The transpiler rewrites only when it finds exactly the two reads it
expects; any other count logs an error and returns the original IL. It reports its verdict
through `UnfinishedItemSharing.RedirectInstalled`, and `IsShareableBill` requires that flag
before admitting an unfinished-item bill, so a future RimWorld that reshapes the method reverts
the mod to the old refusal instead of shipping half a feature. The interlock guards only the
*bill*: bench eligibility is decided by the startup injector, and it and our Harmony startup are
both `[StaticConstructorOnStartup]` with no defined order between them. A Cecil test runs the
transpiler's own matcher over the shipped `Assembly-CSharp.dll`, so a changed shape is caught
offline before a player sees the fallback.

**One bench at a time per order.** A `Bill_ProductionWithUft` holds one bound item and one bound
worker, so an unfinished-item order occupies one bench of the group until it finishes. That is
vanilla's rule per bill, not something sharing breaks; the group works its other orders in
parallel.

**Under round robin, such an order rotates when its unit is finished, not when its job starts.**
Plain orders rotate at job start, because several pawns scanning together would otherwise all
take the head bill. An unfinished-item order rotated at start sits at the tail while its item is
half-made. When the maker is interrupted, vanilla's top-down loop starts whatever is now at the
head instead of resuming. Meanwhile the item waits, bound to its maker (other pawns skip a bill
with a bound item), until the order comes round again. Each resume also rotated the list again.
Rotating on completion keeps the order at the head until its unit is done, so the maker resumes
it first, and the count still drops only on completion, as vanilla does.

The decision is `UnfinishedItemPolicy.RotatesAt`. The hook is a postfix on
`Bill_Production.Notify_IterationCompleted`, which `Bill_ProductionWithUft` reaches through its
`base` call (pinned by a Cecil test). The rotation itself is unchanged, so the "do this next"
marker still refuses rotation the same way.

The trade-off is accepted. Between a unit's job starting and its item being created (while
ingredients are gathered) nothing is bound, so a second pawn at another bench can start the same
order. The overshoot guard still caps that by the remaining count. If it happens, vanilla binds
the order to whichever item is made last, and the first maker still finds its own item through
vanilla's creator-bound search.

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

One exception: orders that leave an unfinished item behind rotate on completion. For those,
one job is *not* one iteration, since an interrupted item takes several jobs to finish. See
"Under round robin, such an order rotates when its unit is finished".

The player's authored order is snapshotted by bill load ID when the mode goes on, and
reprojected onto the live list when it goes off, so trying the mode does not permanently
scramble their priorities.

## "Do this next" promotes, it does not force

A button on each bill row moves that order to the head of the group's shared list and marks it
until it is done. It is the same trick as round robin, run the other way: vanilla walks the list
top-down, so putting a bill at index 0 *is* asking for it first, and no selection code of ours
has to exist for it to work. The promotion happens on the click, so unlike an urgency comparator
it costs nothing on the scan path.

**What it cannot promise is that the order is then worked.** `WorkGiver_DoBill` skips any bill
that is suspended, paused, out of reachable ingredients or whose `ShouldDoNow` is false, and
takes the next one down instead. Genuinely forcing a bill means patching that selection loop —
the one thing this whole design exists to avoid. So the marker means "first in line among the
orders that can actually run", and the tooltip says so in those words. A player who marks an
order with no ingredients and watches nothing happen has to be able to find out why from the
tooltip rather than from the issue tracker.

Three things had to be decided rather than derived:

- **Sticky, not one-shot.** "I need twenty meals, now" is the case this exists for, and a marker
  that evaporated on the first job start would mean clicking it nineteen more times. The cost is
  that stickiness needs clearing rules; the cost of the alternative is the feature not doing the
  thing it is for.
- **Exactly one per group.** Marking a second order clears the first. Priority over several bills
  at once is just an ordering mode wearing a button, and one nullable field keeps both the state
  and its semantics trivial.
- **A manual reorder cancels it.** The marker's entire effect is that the order sits at the head,
  so dragging something above it has already overridden it. Promoting it back instead would make
  the reorder arrows feel broken on a bill the player may not connect to the marker at all.

Round robin would otherwise undo the marker once per job start — rotation sends the marked bill
to the tail and the promotion puts it back at the head, so the list visibly jumps twice per craft
to end up exactly where it began. `BillOrdering.TryPlanRotateToTail` therefore takes the marked
flag and refuses. The explicit instruction outranks the automatic cadence, not the reverse.

### When the marker clears itself

A marker is satisfied when a *counted* order runs out of count. RimWorld has no completion event
of its own — a `repeatCount` bill that reaches zero is not removed from the list; vanilla simply
stops starting it, and it sits there at 0 until the player deletes it — so the remaining count is
what "finished" means here.

| Repeat mode | Completion | What the marker does |
|---|---|---|
| Do X times | `repeatCount` hits 0 | Clears itself |
| Do forever | There is none | **Stays marked until the player unmarks it** |
| Do until you have X | Map-wide stock reaches the target | Stays marked; see below |

**A "do forever" order staying priority forever is the right answer, not a shortfall.** An order
that says "do this forever" has no completion to wait for, so there is no moment at which
clearing the marker would be correct — and staying put is what the player gets in vanilla anyway,
where an order moved to the top of the list stays there until they move it. The tooltip says so
in those terms rather than apologising for it.

"Do until you have X" is the one deferred case. It does finish, but only
`RecipeWorkerCounter.CountProducts` can say when — a map-wide walk of every haulable thing for
any bill carrying a quality, hit-point or stuff filter — and this test is consulted on the
bill-drawing path, once per visible row per frame. Calling it there would put the mod's most
expensive possible call in its hottest loop. It becomes nearly free once the stock-aware ordering
in issue #8 §3 lands, since that has to cache those counts anyway.

The repeat-mode check in `BillOrdering.IsNextOrderSpent` is load-bearing rather than defensive.
`repeatCount` is a live field that keeps whatever value it last held, so a bill that ran a count
down to zero and was then switched to "do forever" still reads zero — without the mode test it
would silently unmark itself the instant it was marked.

### Nothing can leave a stale marker behind

The state is one nullable bill load ID on the anchor's `CompBillGroup`, not a `Bill` reference.
That is forced rather than chosen: bills are deep-saved inside the anchor's own `billStack` node
and `Scribe_References` cannot point into a deep-saved graph, so a reference field would come
back null after every reload — a breakage that only shows up after a save and reads as the player
imagining things.

It pays for itself twice over, because it also makes staleness a non-problem. Every read goes
through `NextOrder.Resolve`, which looks the ID up in the live list and drops it when the lookup
fails. There is no reference to dangle and no path that can skip the check, so every way an order
can stop existing ends in the same place:

| How the marked order goes away | What happens |
|---|---|
| Deleted, or suspended and then deleted | `Patch_BillStack_Delete` clears it; `Resolve` would anyway |
| Finished a "do X times" run | `Resolve` reads `repeatCount` and drops it |
| Anchor bench destroyed, group re-anchors | `AdoptGroupState` carries it to the new anchor, and the outgoing one stops claiming it |
| Group unlinked down to one bench | `Resolve` drops it — a lone bench has no group to be first in |
| Gravship jump (every member briefly despawned) | Deliberately *not* dropped; the clearing is gated on the anchor being spawned, the same care membership gets |
| Mod removed and re-added | The ID was never written, or it names nothing; lookup miss |

The delete hook is about latency rather than correctness — it means the highlighted row goes the
instant the order does, rather than the next time something happens to look.

A transient resolved-`Bill` cache sits behind the ID so the per-row-per-frame "is this the marked
one" question is a reference comparison rather than a fresh `GetUniqueLoadID` string. The ID stays
the truth; writing it drops the cache, so the two cannot disagree.

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
  `formingTicks > 0`, else plain `Bill_Production` — so the bill types we can share are
  predictable from the def, with no reference to the bench's class. A bench is offered the
  gizmo when at least one of its recipes makes a plain `Bill_Production` or a
  `Bill_ProductionWithUft`; if none does, a group could never hold anything and the gizmo
  would be a lie.

  This replaced a whitelist of two exact types, which excluded every modded bench with a
  custom `thingClass`. (This paragraph used to say the whitelist also wrongly admitted
  `SubcoreEncoder` because its one recipe has `formingTicks`. The live census says otherwise:
  its only recipe, `SubcoreBasic`, is an unfinished-item recipe, so the encoder was excluded
  for that reason and is groupable now. The mech gestators are what remain excluded.)

  **"At least one" and not "every one" was the load-bearing choice** while unfinished-item
  orders were unshareable, and the reasoning is kept because it still decides mixed benches.
  The stricter form is
  tempting because it makes an unshareable bill impossible on a grouped bench. Measured
  against the loaded def database it excludes every crafting bench in the game: apparel,
  weapons, armour and sculptures all use unfinished things, so tailoring benches (1 of 45
  recipes plain), smithies (3 of 23), the machining table (7 of 70), the crafting spot
  (3 of 19) and the bioferrite shaper (1 of 20) all lose the gizmo. That trades a modded-
  bench gap for a far larger vanilla one. The census the `eligibility_gate` scenario logs is
  what settled it; the rule was written the strict way first and the numbers changed it.

  So the recipe test lives at the bill instead, where the danger actually is:
  `Patch_BillStack_AddBill` refuses a non-shareable bill entry into a shared stack, on every
  route — the tab's dropdown, paste from the clipboard, another mod adding one in code. Since
  unfinished-item orders became shareable, the benches this protected in practice (machining
  table, smithy, tailoring bench) no longer hit it; what remains behind it are the mech bill
  types, and any unfinished-item order if the redirect failed to install.
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
- **Bills that are not exactly `Bill_Production` or `Bill_ProductionWithUft`.** `Bill_Mech`
  (gestation), `Bill_ResurrectMech` and `Bill_Autonomous` (forming) cast the list's owner back
  to their own bench class, which in a shared list is not the bench the pawn is at. Exact type
  tests, so a modded subclass of either admitted type is refused too — it may override exactly
  the member that reads the owner. `Bill_ProductionWithUft` used to be on this list; see
  "Unfinished-item orders follow the pawn, not the list".
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

The single green later split into a small scheme, because a shared list makes "being worked" too
coarse — the useful question at a bench is whether it is being worked *here*. `BillAccentRule`
(pure, tested) picks one accent per row: **green** for work at the bench whose tab is open (a pawn
whose `CurJob.targetA` is this bench), **blue** for the bill that would start next (the first
`ShouldDoNow`, i.e. the work giver's own answer), **faint green** — edge bar only, no fill — for
work at another member, and **grey on top of everything** where the host says nobody can do the
work at all. Both bills tabs colour from `Patch_Bill_DoInterface.AccentFor`, so they cannot drift.
Red is not in this list: **red means "do this next", in both tabs, and nothing else** (see below).

The accent speaks through two surfaces: the left edge bar always, and the row's fill only for the
two claims about this bench (`BillAccentRule.FillsRow`). In vanilla's tab the fill is our wash; in
Nice Bill Tab's it is their row stripes, recoloured. Work elsewhere gets no fill in either — the
first capture of Nice Bill Tab rows had it repainting their stripes the same green as work here,
and the two rows could not be told apart.

**Motion is a third channel, and in Nice Bill Tab it belongs to "worked here".** Their tab scrolls
the stripes of exactly one row: the first bill in the list that any free colonist on the map has
as `CurJob.bill` (`CheckAnyOneDoWork`, refreshed on bench change and every 30 ticks). On an
ungrouped bench that is the bill being made there. In a group the list is shared, so it is
whichever worked bill sits highest — often the other bench's — while the bill being made at the
open bench sits still. `RowMotionRule` (pure, tested) moves the scroll to every row that is
`WorkedHere` and off every other row, using the same classification as the colours, so motion and
hue can't disagree about a row. It works by handing their `DrawBillPreview` a different status
(written back through Harmony's `__args`), not by zeroing the scroll offset. Their status also sets
the stripe strength (0.4 moving, 0.2 still) and greys a Doned row's text, so a row we stop drops to
exactly what their own `GetBillStatus` would have called it, and a row we start gains their full
"being worked" look. Mech-gestation bills (`Bill_Autonomous`) animate from their own state and are
left alone.

Worked elsewhere does not move: it is already the weakest claim, speaking through the edge bar
alone, and a scrolling row is the loudest thing on their tab. Next up does not move either, since
nothing is happening to it yet. The grey `NoOneCanDo` row stays still even with a pawn on it,
because scrolling grey would say "stuck" and "progressing" at once. **The marked order being made
here does move, in red.** The marker owns the hue and the accent owns motion (and the edge bar), so
red still means only "do this next" and the scroll adds "and it's being made here". Turning it
green would take red off the one row it exists for; holding it still would hide the work.

### Which order was asked for next

The marked order gets a red wash, a red outline around the whole row, a PRIORITY badge, and its
arrow button lit in the same red. The accent's edge bar is drawn after the outline so it lands on
top, because a marked order is routinely *also* the one someone is working, and usually also the
blue "next up" row — it sits at the head of the list. The two answer different questions — "what
did I ask for next" and "what is happening now" — so neither is allowed to hide the other.

That is why the marker is **not an accent** and is not ranked in `BillAccentRule.Classify`: folding
it into the precedence would make one of those facts disappear. Each keeps its own channel. The
accent owns the left edge bar; the marker owns the outline and the badge. The one surface both
wanted is the fill, and `BillAccentRule.WashFor` gives each row at most one: the marker's red when
it is marked, otherwise the accent's. Two 13% washes stacked on the marked row came out violet
(red over blue) — a colour that means neither — and the accent loses nothing by yielding, because
its edge bar still says the same thing. Before/after on the marked, next-up row in vanilla's tab:
median ΔE **10.1** over the row.

In Nice Bill Tab's rows the marked row's **stripes are red** — their stripes are that tab's fill,
and `BillAccentRule.StripeFor` applies the same one-fill-per-row rule to them — plus the outline
and a red arrow on the thumbnail's top-left corner, mirroring the chain on its bottom-left. The
first version gave the marked row only the outline and arrow, while Nice Bill Tab's own "nobody can
do this" state (`NoOneCanDo`) kept its red stripes on a different row; the player read that red row
as the priority one, which was the reasonable reading. One colour has to have one meaning across
both tabs.

So `NoOneCanDo` is **the one place we repaint a state of theirs**: to a dark neutral grey, which
still outranks green, blue and the marker's red (a row claiming "starting next" while nobody can
start it is worse than no colour; a marked order nobody can do keeps its outline and arrow). It is
their colour that yields rather than ours because in Nice Bill Tab 1.6 the state never fires —
the enum member and its red stripe exist, but `GetBillStatus` never returns it and nothing calls
the validator that would compute it — so repainting it costs players nothing today, and a future
release that wires it up arrives already distinct from the marker. The stripes keep their alpha as
a floor; the marker's red is lifted to 0.4, the strength their own red had, since their pending
rows are only 0.2. Against the previous capture: the marked row's stripes change over 31% of the
row (median ΔE 9.5 over those pixels), the blocked row's over 32% (11.9), the other rows not at
all, and vanilla's tab not at all. Not the word: at Tiny, PRIORITY is wider
than the thumbnail, and the first capture had its plate cutting "Cook" to "ok".

The **button** to set the mark is not on their row, because their row has no free spot that stays
put: the top line runs leftwards from their delete button through a variable number of other mods'
buttons, the bottom line is their repeat controls, the thumbnail is their pause button and would
take the click first, and right-click opens their own menu. It sits instead in the strip we already
reserve above their list, next to the ordering button, and acts on **their selection**: select a
row (one click, which highlights it), press "Do next"; press again ("Unmark", in the marker's red)
to clear it, the same toggle as vanilla's row button through the same `NextOrder.Toggle`. Their
selection is a list, and shift-click adds to it, so the pure `SelectedBillRule` refuses both "none
selected" and "several selected" rather than guessing, and the tooltip says why. Before and after
a press: the button changes over its whole face (median ΔE 32.1) and the marked row moves to the
head and turns red.

That first live press also found a real bug in the compat layer: **Nice Bill Tab draws from a
cached, filtered copy of the list** and rebuilds it only when its own code flags
`shouldRefreshFilter` — a delete, a drop, a paste, typing a search. The press marked the order and
moved it to the head, and their tab went on drawing it third. The same was true of every round-robin
rotation with their tab open. Every deliberate move of ours already ends in
`RoundRobin.RecordLastKnownOrder`, so that bumps `OwnBillListMoves.Version`, and the left-pane
prefix sets their flag when the version has changed since it last looked — one integer compare per
draw.

The red also lands on top of a third signal. The overshoot guard makes a fully-claimed bill
report "would not start now" and vanilla paints any such bill pink, so a marked bill under work
reads *blocked* from vanilla, *urgent* from us and *being handled* from the green edge, all on
one row. `do_this_next_worked.png` is that row: red outline and badge, green edge bar, vanilla's
pink behind the red wash, and still legible.

Group-only, like the chain icon and the ordering control. On a bench working alone vanilla's own
reorder arrows already put an order first, so a second control doing the same thing would be
clutter claiming to be a feature.

**Vanilla has two things that could be called "how a suspended bill is marked", and the request
conflated them.** The feature request asked for a "P" mirroring the way vanilla shows an "S" for a
suspended bill. The "S" visible on every bill row is `TexButton.Suspend` — a *button* glyph in the
right-hand strip, which looks identical whether or not the bill is suspended and says nothing
about state. The actual state indicator is the word SUSPENDED, written in Medium across a 140x40
plate centred on the row.

So the badge takes the state indicator's construction — `TexUI.GrayTextBG` plate, centred caps
label — and shrinks it into the button strip, where the letter the request was actually pointing
at lives. A second 140x40 plate was never an option: it would land straight on the SUSPENDED one,
and a suspended order can perfectly well also be the marked one.

The button sits at `xMax - 126`, one 22px step left of the chain at `xMax - 100`, with the badge
one step left again at `xMax - 148`. All three are on the row's top line, which is free: the
reorder arrows take the left 24px and the delete/copy/suspend trio the right 76px. The only
competitor is an over-long bill label — vanilla clips labels at `xMax - 40` and lets them run
under its own buttons — which the chain icon already competes with.

**The second line is not free, and the design note said it was.** The badge was first drawn under
the button at `y + 25`, which `Bill.DoConfigInterface` supports: the base method draws only an
info-card button at roughly `(xMax - 32, y + 37)`. But `Bill_Production` *overrides* it and draws
something else entirely — a `WidgetRow` anchored at `(baseRect.xMax, baseRect.y + 29)` running
`LeftThenUp` with "Details...", the repeat-mode button and the +/- controls, sweeping the whole
second line from the right edge leftwards. Every bill this mod can hold is a `Bill_Production`,
so the slot is occupied on every row there is, and the first capture showed the badge sitting half
on top of "Do X times". Anything else wanting a second-line slot needs to know this before it is
drawn — the batched round-robin counter in issue #8 §2 proposes `(xMax - 100, y + 25)`, which is
the same occupied band.

Clicking mutates the list while `BillStack.DoListing` is part-way through its index loop over
that same list, which sounds worse than it is: the move is a remove-and-insert so the count never
changes, and the click is reported during the mouse-up event pass, which paints nothing. Vanilla's
own delete button, a few pixels to the right, genuinely does shrink the list mid-loop.

## Known rough edges

- **Bills render pink while being worked.** `Bill.BaseColor` pinks any bill that would not
  be started right now, and the overshoot guard makes that true of a bill already claimed.
  Surfaced in the settings tooltip; switchable.
- **A group shares vanilla's cap of 15 bills**, not 15 per bench. Refused at link time with
  the actual count rather than silently truncated.
- **Message and dialog look-targets point at the anchor**, not the bench that finished.
- **An unfinished-item order occupies one bench of its group until it finishes**, because
  vanilla binds one item and one worker to such a bill. Under round robin it rotates on completion
  rather than at start, so for a moment two pawns can start it together. See "Unfinished-item
  orders follow the pawn, not the list".
- **A rebuilt bench is a new Thing** and silently leaves its group. Detected and shown on
  the inspect string only.
- **A marked "do this next" order can sit red and idle.** It is at the head of the list, and
  selection is stepping straight over it because it has no ingredients, is suspended or is
  paused. This is the honest cost of promoting rather than forcing, and the only place it is
  explained is the button's tooltip — there is no message, because a message would fire on every
  work scan.
- **A marked "do until you have X" order never un-marks itself.** The one deferred case in the
  table above; it needs a product count this mod cannot afford on the drawing path. "Do forever"
  is *not* in this list — staying marked is the intended answer there, not a defect.

## Cross-mod notes

- **Hauler's Dream** postfixes `WorkGiver_DoBill.JobOnThing` three times and replaces the
  returned job. We never touch that result — our only patch there swaps a field around the
  call — and our counting keys off `job.bill`, so its batch jobs are still seen. Its batch
  crafting queues several iterations under one job, so counts are undercounted there.
- **Nice Bill Tab** reads the `billStack` field, so the swap is transparent, and its
  "is anyone working this" test keys off `pawn.CurJob.bill`, which is already group-correct.
  Everything else it touches needed work — see below.
- **Nice Bill Tab Expansion** postfixes `Building_WorkTable.ExposeData` for unrelated
  state; ordering is declared so the shared target is visible in the load graph.

### Nice Bill Tab: what a replacement tab actually costs

Nice Bill Tab prefixes `ITab_Bills.FillTab`, returns false, and draws a two-pane tab of its own.
The shared list survives that untouched. What does not survive is every assumption about *who is
doing the drawing and the adding*, and two of the four consequences were correctness bugs rather
than cosmetic ones. Worth recording in full, because each was invisible to every probe we had:
they all read state, and three of the four are about drawing.

**Its drag-reorder bypasses `BillStack.Reorder`.** `HandleBillDrop` calls `Bills.Remove` then
`Bills.Insert` on the list itself, so `Patch_BillStack_Reorder` — our hook for "the player
re-authored the order" — never fires. Under round robin the canonical snapshot then went stale
with no trace, and switching back to in-order restored an arrangement from whenever the mode was
switched on, silently discarding the one the player had just made.

The fix deliberately does *not* patch their drag handler. `Core.OrderDivergence` compares the live
list against what we last left it as, which catches any mod that mutates the list directly,
including ones not written yet, and needs nothing from them. The comparison has to distinguish a
reorder from an add or a delete — both are routine between checks — so both sequences are projected
onto the bills they have in common and those projections compared. `CompBillGroup.lastKnownOrderIds`
is the baseline, scribed because a drag can be separated from the next check by a save.

The same bypass hides drags from the "do this next" marker, whose rule is that a drag putting
something above the marked order cancels the mark. Vanilla's arrows reach that rule through
`Patch_BillStack_Reorder`; a direct mutation reaches nothing. Two cheap checks close it without
patching their handler either: `AbsorbExternalReorder` drops a displaced marker whenever it detects
a foreign reorder (round robin, any mod), and the Nice Bill Tab left-pane prefix — already running
per frame for the ordering strip — asks the same head-of-list question once per draw, which covers
in-order groups where the divergence check never runs.

The two mechanisms also have to agree about who moved what. Marking promotes the bill with a bare
list move of our own, so `NextOrder.PromoteToHead` records the new baseline; without that, the next
round-robin job start would read the promotion as a foreign drag and write the marker's position
into the player's authored order, where un-marking could never undo it.

**Its clipboard paste bypasses `BillStack.AddBill`.** `TabBillsDrawer.InsertBill` assigns
`bill.billStack` and calls `Bills.Insert` directly. Our unfinished-thing gate is a prefix on
`AddBill`, justified on the grounds that every route passes through it — and this one does not, so
an assault rifle bill could be pasted into a linked machining table and strand its `UnfinishedThing`
on the anchor. This one *is* fixed by patching their method, reflectively, because a refusal needs a
real chokepoint and there is no lazy equivalent. Their other paste route, `InsertBillBizarre`, pops
the tail and calls `AddBill`, so it stays gated by the existing prefix and is left alone.

**It never calls `Bill.DoInterface`.** Rows are drawn by `DrawBillPreview`, so the chain icons and
the in-progress marker vanished — and the tab then looks exactly like the mod is switched off, which
is why `Patch_Bill_DoInterface` bothers to record the frame it last drew on. The annotations moved
into a shared `DrawRowAnnotations` that their row drawer is postfixed onto. A compact variant drops
the background wash, because their rows already carry a status tint and a second translucent green
over it reads as a rendering fault; the edge bar stays, because their tint marks one bill and ours
marks every bill actually committed to, which in a group is routinely several.

**There is nowhere stable to put the ordering button — so it takes space rather than borrowing
it.** Their tab is a different size and puts a search field exactly where ours goes, and their top
strip shifts by 110 pixels depending on whether a recipe is selected, so no rect found by
inspection is safe; the first attempt drew straight over their search box.

A prefix on `DrawLeftPart` takes the rect by reference and moves its `yMin` down 30px, which pushes
their pane down and shortens it by the same amount, so the pane keeps its bottom edge and its
scroll view shrinks to match instead of overflowing. The strip above is then ours and its position
is fixed. The postfix draws into it — *after* their pane, because their first act is to fill the
panel background. Two details are load-bearing: the strip is only reserved for a bench that is
actually in a group, so no ordinary workbench pays 30px for a control it never shows; and it stops
60px short of the right edge, because their enable checkbox and close button are positioned from
the tab's full size at `y = 0` and so do not move when the pane does.

A gizmo was tried first and works, but reads badly — the control ends up in the opposite corner of
the screen from the list it acts on, which is the complaint that moved it off a gizmo originally.
Both homes share `OrderingMenu`, so the vanilla-tab button and this one cannot drift.

That last decision turns on a detail worth stating: **Nice Bill Tab has a runtime toggle**, a
checkbox in its own tab corner that hands drawing back to vanilla mid-session with no event and no
reload. So "is it installed" is the wrong question for anything layout-related, and both layouts
have to work in one session. `NiceBillTabCompat.IsDrawingTab()` is therefore read per frame rather
than cached at startup.

All of it is resolved by name at runtime. Referencing `NiceBillTab.dll` would turn a soft dependency
into a hard one, and a player without the mod would get a type load failure instead of a file that
simply never runs.

### The shape of our third-party exposure

A compatibility pass over the mods we declare a load order against turned up something worth
stating plainly, because it is not what we expected to find.

The worry going in was **double counting**: every member of a group points its `billStack`
field at one `BillStack`, so a mod summing `billStack.Count` across the colony's benches would
report three linked stoves with four orders as twelve. Nothing measured does this. The mods that
enumerate bills do it per bench from an open tab, not colony-wide, and the one colony-wide walk
found (Hauler's Dream's `MakeColony`) is a debug action.

The real exposure is the other half of the same design: **`bill.billStack.billGiver` resolves to
the anchor, for everyone, not just for us.** This mod exists because vanilla's job code follows
the bench a pawn walked to rather than the bench that owns the bill — but any mod that
reimplements "which bench does this bill belong to" from `billStack.billGiver` gets the anchor,
and has no way to know a group is involved.

Nice Bill Tab's `BillValidator.CanExecuteBill` is the concrete case. It resolves the work table
from `bill.billStack?.billGiver`, then gates on *that* bench being unforbidden, reachable, and
`CurrentlyUsableForBills()`. In a group that is always the anchor, so a bill that a pawn could
happily work at another member is reported unworkable — "Cannot reach work table", "Work table is
not usable" — whenever the anchor alone is unpowered, forbidden or walled off. The bills still
get worked; the tab's explanation of why they are not is wrong.

Not fixable from here, and worth being honest about rather than filing as someone's bug. The
anchor genuinely owns the stack, a long tail of vanilla hard-casts `billStack.billGiver` and would
break if it did not, and anything we did to make the answer per-bench would have to guess which
bench the asker meant. What it changes is where to look first when a report arrives: a *wrong
explanation in another mod's UI* is now a known symptom, not a mystery.

### Why the `AddBill` refusal still returns `false`

`Patch_BillStack_AddBill` returns `false` to keep an unshareable bill out of a shared stack,
which skips vanilla's body *and* every lower-priority prefix *and* the original's postfixes.
Hauler's Dream postfixes the same method, so that was filed as a risk to its bookkeeping. Checked
rather than assumed, and it is fine, for two separate reasons:

- Its `Patch_Bill_Production_Clone.Carry` is a `ConditionalWeakTable`, so the entry for a refused
  bill dies with the bill. There is nothing to leave stale.
- Its other branch calls `SetBatch` for a newly added bill. Skipping that is not a tolerable
  side effect but the *correct* outcome — registering a batch against a bill that is in no stack
  is exactly the divergence the issue was worried about, and our returning `false` prevents it.

What the Harmony framing misses is that a prefix only protects against *patches*. **Everybody Gets
One** does not patch `AddBill` at all: it transpiles `ITab_Bills.FillTab` and replaces the call
site with its own `AddBillAndPasteCounter`, which calls `AddBill` and then, unconditionally,
writes the bill into a saved `Dictionary<Bill_Production, QuerySearch>` on a map component. Our
refusal stops the add — it is the same method — but cannot stop a caller's follow-up. So a refused
paste leaves a strongly-referenced, save-persisted entry keyed on a bill that is in no stack, saved
by reference against nothing.

Only the paste route, only for an unshareable bill, only onto a grouped bench, and no cheap fix:
refusing earlier would mean owning the tab's paste button, which is layout we do not own and which
Nice Bill Tab already rebuilds. Recorded here so that if it is ever reported it is recognised
rather than re-derived.

## Status

Implemented, unit-tested, and exercised in a running game.

**Offline** (`./test.sh`, 136 tests): the pure core in `Source/Core/`, plus Mono.Cecil checks
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
| `nicebilltab_compat` | Runs with Nice Bill Tab active and is **graded on the frame, not the probes** — its one probe passes identically with the compatibility layer absent, because every difference is drawn. Against the same scenario on `main`: chain badges appear on all three rows, the in-progress bar appears on exactly the bill a pawn committed to, and the ordering button stops being drawn across their search box. |
| `accent_states` | Every row state on one frame of vanilla's tab: marked + next up, worked here (twice), worked at the other bench. Probes read `AccentFor` itself, so the probes and the pixels answer from one function. The green "worked here" state is on screen for the first time — `WbgSimulateBillStart` gained `atBench`, which points the held job's `targetA` at a bench. Against the pre-`WashFor` build: **median ΔE 10.1 over the marked row** (violet mix → red), every other row's fill unchanged. |
| `nicebilltab_accents` | The same states in Nice Bill Tab's tab, plus a bill that is both worked here and "nobody can do this" (grey). That state is **forced** (`WbgForceNiceBillTabStatus`): Nice Bill Tab 1.6 declares and colours `NoOneCanDo` but never returns it, so no player can see that state today; the capture shows what our layer does if they ever do. Against the previous build, measured over the pixels that changed (a whole-row median is 0 on these rows, because the stripes cover a quarter of the row and the rest is untouched): the red row loses our green edge bar (0.8% of the row, median ΔE 81.5), the elsewhere row gets its own stripes back (25% of the row, median ΔE 3.9 — visible at a glance, but a tint change, not a new element), and the marked row gains its outline and arrow (8.4%, median ΔE 82.0). |
| `nicebilltab_work_animation` | Which Nice Bill Tab row **scrolls**. Graded on bursts of six frames ~40 rendered frames apart, diffed frame to frame per row, since a still can't show motion. Phase 1 puts the other bench's bill above the one worked here: on `main` the elsewhere row scrolls (35% of its pixels change per step, median ΔE 13.2) and the worked-here row is still (0.0%); on the branch that swaps exactly (elsewhere 0.0%, worked here 36.7% at ΔE 14.6, in green). Phase 2 adds the marked order worked here (scrolls red on both builds, since it is also their chosen row) and a forced-`NoOneCanDo` row worked here (still and grey on both). Rows that should not move measure 0.0% on the branch. The elsewhere row in this scenario is a fully claimed 1x bill, so it rests as their Doned (greyed text): that is what their own `GetBillStatus` calls every other worked row that would not start again. |
| `marker_foreign_reorder` | Where "do this next" meets foreign-reorder detection, graded on probes with a negative control. **A:** mark under round robin, start a job, unmark, switch to in-order — the authored order comes back (head slot 0), not the marker's promotion. **B:** in-order, Nice Bill Tab's tab open, a bare `Remove`/`Insert` puts another order above the marked one (`WbgMoveBillDirect`, standing in for their drag) — the mark clears. Against the build before the fix, both key probes fail (head stays 2; mark stays 2). |
| `nicebilltab_do_next_button` | The "do this next" button above Nice Bill Tab's list. Selection goes through their own `SelectBill`, the press through the button's handler. Refuses with nothing selected and with two selected (the step asserts the press did nothing, and the probe that nothing is marked); marks the single selected order, which moves to the head; a second press unmarks it. Captures before and after the press: the button's face changes at median ΔE 32.1, the marked row at 30.3 as it moves up and turns red; 0.05% of the rest of the frame moves. |
| `shared_save_integrity` | **Zero duplicate load-ID warnings** on save, and sharing intact afterwards. |
| `do_this_next` | Marking promotes to the head; the marked order survives a job start that would otherwise rotate it away; marking a second order replaces the first; clicking the marked one again clears it; deleting the marked order leaves nothing marked. 13 probes. Two identically-framed captures of the second bench's tab, before and after marking — **median ΔE 11.4 over the marked row**, against 0.06% of the map changing at all. |
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

- **Blocked (grey) supersedes, as players would meet it.** Only seen with Nice Bill Tab's status forced by a
  test step, because their 1.6 release never produces `NoOneCanDo` on its own. Vanilla's tab has no
  equivalent state to defer to.
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
