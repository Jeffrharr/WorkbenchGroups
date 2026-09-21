# TODO — and context for a new agent

## Where things stand

`WorkbenchGroups` (packageId `joof.workbenchgroups`) links crafting stations so they share
one bill list, plus a per-group round-robin ordering mode and a per-bill "do this next"
marker. Built, symlinked into the game's Mods folder, 136 offline tests green, eight live
scenarios green.

### Three ways a live run lies to you

All three were hit while building "do this next". Each produces a plausible-looking result.

1. **Steam logged out voids the entire run.** With Steam unreachable RimWorld cannot enumerate
   Workshop mods, `brrainz.harmony` goes invisible, our `[HarmonyPatch]` attributes cannot
   resolve `0Harmony`, and RimWorld quietly resets the mod config and reloads **Core-only**. No
   mods means no probes means no report, and the run fails with "exited before writing a report"
   — which presents exactly like build skew or a missing `--mod` flag and is neither. Check
   `grep -c "S_API FAIL" <rundir>/Player.log` before touching your flags; non-zero voids the run
   however green it looks. Do *not* work around it by symlinking Harmony out of the Workshop
   folder into the local `Mods/` folder — the player's real game would then see a duplicate
   `brrainz.harmony`.

2. **`--mod-overlay` carries assemblies only, not `Languages/`.** It installs
   `<worktree>/1.6/Assemblies` and nothing else, so a new keyed string added in a worktree
   renders as its missing-key fallback in every capture while the mod itself behaves perfectly.
   The first "P" badge capture came out as a garbled two-line smudge for exactly this reason and
   read as a font bug. Add the folder explicitly:

   ```bash
   --install <worktree>/Languages:<main-checkout>/Languages
   ```

   It is claimed and rolled back under the same lock as everything else.

3. **A held lock is refused, not queued.** `run_test.sh` exits with
   `FAIL: another run_test.sh holds /tmp/rwth-run-1000.lock` — and piped through `tail` that
   still exits 0, so a run that never started reads as a run that passed. Retry the *run* in a
   loop rather than polling the lock beforehand (poll-then-launch still races), and judge a pass
   by reading `Pass` out of the report JSON, never by exit code.

Read `DESIGN.md` first — it carries the reasoning. The short version of the load-bearing
facts, so they don't have to be re-derived from the decompile:

- `WorkGiver_DoBill` already follows the bench the pawn **walked to** (`giver`, stored as
  `job.targetA`), not `bill.billStack.billGiver`. That is the only reason sharing a
  `BillStack` is a small mod.
- A long tail of vanilla **hard-casts** `bill.billStack.billGiver`, so the shared stack
  must be owned by a real spawned bench (the *anchor*).
- Installation is a **field swap** on `Building_WorkTable.billStack`. It cannot be a
  patched property: `ITab_Bills` and Nice Bill Tab read the field, `WorkGiver_DoBill`
  reads the property.
- That forces the `Building_WorkTable.ExposeData` prefix/finalizer. Without it every
  member deep-saves the same bills and the save is corrupt on load.
- Round robin rotates at **job start**, never on iteration completion.
- Eligibility is decided from **recipes, not bench classes**: a bench is groupable if at
  least one of its recipes would make a plain `Bill_Production`
  (`BillUtility.MakeNewBill` picks the subclass from the `RecipeDef` alone). "At least one"
  and not "every one" — the strict form excludes every crafting bench in the game, which
  the `eligibility_gate` census measures. The per-bill half is enforced by
  `Patch_BillStack_AddBill`.

Layout: pure dependency-free logic in `Source/Core/` (compiled into the test project by
`<Compile Include>`, so tests run the shipped files); Verse adapter in `Source/`; Harmony
patches in `Source/Patches/`; dev-only probes and scenario steps in `Source/Probes/`,
excluded from the shipped DLL and compiled by `TestMod/`.

Build: `./build.sh`, then `./TestMod/build.sh` (the bridge references built output, so
order matters). Test offline: `./test.sh`. Test live:

```bash
Runner/run_test.sh --mod <repo> --mod <repo>/TestMod --no-profiler <scenario.json>
```

### Two live-testing traps that each cost an hour here

**Steam must be logged in, not merely installed.** Harmony is a Workshop mod (2009463077), and
RimWorld can only enumerate Workshop content through the Steam API. Lose the Steam login — say by
publishing a mod — and the run fails like this, in this order: `[S_API FAIL] SteamAPI_Init()
failed`, then `Mod RimWorld Test Harness dependency (brrainz.harmony) needs to have <downloadUrl>`,
then `Could not resolve type ... 'HarmonyLib.HarmonyPatch' in assembly '0Harmony'`, then `Caught
exception while loading play data ... Resetting mods config and trying again`. RimWorld disables
every mod, reloads Core-only, and the run dies with "exited before writing a report".

The trap is that the wreckage looks like several unrelated bugs: missing DLC textures
(`MarshPollutionOverlay`) and endless `GenStuff.DefaultStuffFor ... Sequence contains no elements`
are just "Odyssey is no longer loaded", and the Harmony line reads exactly like the build-skew or
missing-`--mod` error the harness docs warn about. It is neither, and no flag you pass can fix it.
Check `grep -c "S_API FAIL" <run>/Player.log` first; zero means Steam was fine and the problem is
genuinely yours.

**The harness refuses a held lock rather than queueing.** With another agent working this repo set,
`run_test.sh` exits immediately with "another run_test.sh holds /tmp/rwth-run-1000.lock". Piped
through `tail`, that exits 0 — so a run that never happened can read as a run that passed. Poll for
the lock before launching, and confirm a pass by reading the report, not the exit code.

---

## 1. Per-bill linkage — relax the link rules, mark the exceptions

**The idea.** Today every refusal in `BillGroupOps.CanLink` refuses the *link*. Instead,
link the benches and decide per *bill* which members may work it. A bill that only some
members can make stays in the shared list, marked with a broken-chain icon, and only the
capable benches ever pick it up. The player linked these benches deliberately, so "you get
what you selected, and the list tells you which orders are special" beats a flat refusal.

This is a better shape than the same-recipe-set rule it replaces, because the gate lands on
the bill rather than on the group. It is **not** a loosening of a constant — see below for
which rules it can and cannot cover.

### Which of the five refusals this actually covers

`WorkGiver_DoBill.StartOrResumeBillJob` decides everything on the normal path from `giver` —
the bench the pawn walked to — and only the unfinished-thing path routes through
`bill.billStack.billGiver`, which is the anchor. That split is what determines the answer:

| `CanLink` refusal | Becomes per-bill? | Why |
|---|---|---|
| Mismatched recipe sets | **Yes** | Ingredient search, job target and work stats are all `giver`-relative on the normal path, so a plain `Bill_Production` owned by the anchor and worked at another member is already correct. Only the *selection* needs gating. |
| Unshareable (UFT) bill | **No** | `FinishUftJob` resolves the unfinished item through `bill.billStack.billGiver` (WorkGiver_DoBill.cs:175,180), so a UFT bill in a shared stack is broken *however* it was selected. Pinning it changes who starts it, not where its unfinished item is looked for. Would need the bill to keep its own stack, and `ITab_Bills` reads one `billStack` object, so there is nowhere to put it. Stays a hard refusal. |
| Over `BillStack.MaxCount` (15) | **No** | A vanilla cap on the stack itself, not a property of any one bill. |
| Non-groupable bench class | **No** | About whether anchoring is safe at all, not about sharing. |
| Different maps | **No** | The anchor must be a spawned bench on the same map. |

So the feature is really "**mismatched recipe sets become per-bill**", and the broken-chain
marker is the UI for it. Worth saying plainly in `DESIGN.md`, because "an edge case for each
rule" is the natural expectation and only one rule can have one.

### The mechanism, which is cheaper than it looks

Both hooks already exist and both already only ever turn a yes into a no:

1. `Patch_WorkGiver_DoBill_JobOnThing` already brackets the whole scan with a
   prefix/finalizer pair and already knows the bench. Record the bench being scanned in a
   static there. The finalizer is what makes that safe — it must clear on the exception path
   or every later `ShouldDoNow` call reads a stale bench.
2. `Patch_Bill_Production_ShouldDoNow` already postfixes the exact method the selection loop
   calls per bill. Add: if a bench is currently being scanned and its
   `def.AllRecipes` does not contain `__instance.recipe`, return false.

Note `JobOnThing` early-returns on `BillStack.AnyShouldDoNow`, which is inside the prefix
window, so the fast path and the loop agree without extra work.

Outside that window the context is null and `ShouldDoNow` answers exactly as vanilla, which
is what keeps the bills tab's colouring honest — a pinned bill should read normal on its own
bench's tab and be distinguished by the icon, not by being drawn as un-startable everywhere.

The pure half is a set-membership test over recipe defNames, so it belongs in `Source/Core/`
next to `RecipeSetComparison` and is unit-testable without a game.

### What has to change beyond the gate

- `CanLink` drops the same-recipe-set refusal and gains an *overlap* requirement — at least
  one shared plain recipe, or the group is pointless.
- `RecipeSetComparison` stops being the link rule and becomes the "is this bill universal"
  test. Keep it; it is what decides whether a bill gets the icon.
- Round robin rotates the shared list at job start and would now rotate past bills the
  current bench cannot make. Check that rotation still advances sensibly when a member can
  work only a subset — the current-bench skip must not count as a turn.
- The overshoot guard and in-flight tracking are unaffected: both key off the bill.

### UI (deferred by the requester, but it is the whole point)

A broken-chain icon next to any bill in the list that is not workable at every member of the
group. Vanilla has no such icon; `TexCommand.RearmTrap` is already standing in as a
placeholder elsewhere in this mod (see loose ends), so this wants a real texture. Hover text
should name the benches that *can* do it, since "why is this one marked" is the only question
the icon raises.

### Verification this needs before it ships

The negative-test suite in the next item, and — for the first time in this mod — a real craft
loop scenario. This is the change that makes "a pawn walks to the right bench" a claim about
correctness rather than about plumbing: the whole point is that a cook must *not* path to the
bench that cannot cook.

## 2. Extend the live scenarios

Existing scenarios in `Tests/Scenarios/`: `eligibility_gate`, `link_smoke`,
`round_robin_rotation`, `overshoot_guard`, `shared_save_integrity` — that glob is a valid
one-load suite. The two-load `roundtrip/` pair is deliberately not in it (different fixture);
run it with `Tests/run_roundtrip.sh`. Steps and probes live in `Source/Probes/`.

The reload round-trip pattern is reusable: the harness has no mid-scenario reload step and
does not need one — write a save in one run, name it as the next run's `saveFile`. See
`Tests/run_roundtrip.sh`.

Two harness facts that shape everything below:

- `saveFile` resolves to `<harness>/Fixtures/<name>`, copied to `Saves/autostart.rws` at
  boot. **All scenarios in one run must share a `saveFile`**, so anything needing a
  different starting world is a separate run.
- The fixture (`minimal_colony.rws`) is a real permadeath colony in a bad way — one able
  colonist, starving, a medical emergency. Anything needing several simultaneous workers
  must spawn its own pawns (`SpawnPawn`), and `WbgSimulateBillStart` already falls back to
  reusing a colonist rather than failing.

### 2a. Anchor handover

Needs a new step (`WbgDestroyBench`) — the harness has no destroy/despawn step. Destroy the
anchor and assert: the group survives, the bills survive, a new anchor is elected, and the
survivors' stacks still point at one object. This is the single most consequential
untested path: `HandOffAnchorIfNeeded` failing means blowing up one bench cancels every
craft in the group and deletes the orders.

Also worth covering with the same step: minify/reinstall (redirect withdrawn on despawn,
reinstalled on spawn), and a group falling to one member (dissolves, survivor keeps bills).

### 2b. Negative tests — every refusal path

`BillGroupOps.CanLink` has five refusal branches and none is exercised. Two newer refusals
belong in the same suite: `Patch_BillStack_AddBill` rejecting an unfinished-thing bill added
to a grouped bench (unit-tested predicate, never seen in play), and `BillGroupOps.Link`'s
rollback, which was written against a throw we cannot reproduce on demand and has never run.
A step that links a bench whose comp is rigged to throw would exercise it. Each needs a probe
exposing the refusal reason (add `wbg_last_refusal_code`, an int, set by a
`WbgTryLink` step that expects failure):

- mismatched recipe sets (link a stove to a tailoring bench)
- combined bills over `BillStack.MaxCount` (15)
- a bench holding an unshareable bill (a UFT recipe)
- a non-groupable bench class
- benches on different maps

These are cheap, and they are what would have caught the eligibility change shifting the
refusal set in a way nobody intended. They are also the prerequisite for item 1, which
rewrites two of the five branches — without a passing negative suite there is no baseline to
say what the rewrite changed.

### 2c. The real craft loop

Currently the behavioural scenarios hold a `Wait` job rather than `DoBill`, so "pawns walk
to the right bench and make the right number of things" is unverified. To do it properly:
`SpawnPawn` several colonists, place a powered stove pair plus ingredients, then
`FastForward` and count products. Expect this to be flaky against the shared fixture —
budget time for a leaner purpose-built fixture, and see the harness's own `Fixtures/README.md`.

The specific claim worth proving here is the one round robin exists for: with three bills
and enough workers, products come out roughly 1/1/1 rather than 3/0/0.

### 2d. Ingredient-mute isolation

`IngredientMuteIsolation` is implemented and completely untested. A probe reading the
remembered per-(bill, bench) tick would let a scenario show that one bench failing an
ingredient search does not mute the bill at the other.

### 2e. Nice Bill Tab compatibility — DONE, and it was a bug hunt after all

Resolved in `Source/Compat/NiceBillTabCompat.cs`; the reasoning is in DESIGN.md under "Nice Bill
Tab: what a replacement tab actually costs". Left here because the framing below was wrong in a way
worth remembering.

This item called the risk cosmetic — "nothing here writes state" — and picked option 3 of the three
listed, which was right. But two of the four things that broke were **correctness bugs**, and the
reason this item could not see them is that it reasoned about *our* patches drawing into *their*
panel and never asked what their panel does instead of calling ours:

- `HandleBillDrop` reorders with a bare `Bills.Remove`/`Bills.Insert`, so `BillStack.Reorder`
  never fires and the round-robin snapshot went stale — the player's arrangement was discarded on
  switching back to in-order. Fixed by comparing against a remembered order
  (`Core.OrderDivergence`) rather than by patching their handler, so any mod that mutates the list
  directly is covered.
- `TabBillsDrawer.InsertBill` pastes with a bare `Bills.Insert`, so the unfinished-thing gate on
  `AddBill` was bypassed and a gun bill could enter a shared stack.

The lesson generalises past this mod: **"which of our patches might draw in the wrong place" is a
much smaller question than "which of our chokepoints does this mod route around".** The next
tab-replacing mod gets audited with the second question.

One trap worth keeping, found while verifying the gizmo icon: **`--mod-overlay` installs assemblies
and nothing else.** A texture added in a worktree is not in the overlay, so the game loads it from
the main checkout, does not find it, and draws `BadTex` — a magenta X that looks exactly like a
wrong ContentFinder path. Install the whole versioned folder instead:
`--install <worktree>/1.6:<main-checkout>/1.6`.

### 2f. Conflicting mods, generally

`About.xml` declares seven `loadAfter` entries. Only the baseline load has ever run. At
minimum, one run with Hauler's Dream and Nice Bill Tab active, asserting the existing
probes still pass — those two are the ones that rewrite the surfaces we depend on.

---

## 3. Smaller loose ends

- ~~`BillGroupGizmos.OrderingCommand` uses a placeholder icon.~~ Done: it ships a cycle glyph at
  `1.6/Textures/UI/Commands/WBG_Ordering.png`, this mod's only non-vanilla texture. Two vanilla
  icons were tried on screen first and both failed in ways only a capture shows — `SwapOutfits`
  renders as a pawn's head, `ReorderDown` scales into a wedge that crowds the label.
- The unlink gizmo acts on the whole selection; confirm that reads correctly when benches
  from two different groups are selected at once.
- **Profile at colony scale.** `Tests/Scenarios/hot_path_profile.json` now measures an unpaused
  window (six linked stoves, six colonists, ingredients on the floor) and the answer is that our
  per-call costs are sub-microsecond and the total is 0.002% of a 60 fps budget. What it does
  *not* establish is behaviour at scale: the interesting number is calls per frame, which was
  0.3, and it scales with pawns x benches. A 200-pawn colony with twenty benches is the run that
  would actually stress `Patch_WorkGiver_DoBill_JobOnThing`, and it needs a fixture this one
  cannot provide.
- No `Preview.png` in `About/`, and no `PublishedFileId.txt` (not published).

---

## 4. Ordering — what issue #8 still has open

Issue #8 proposed four ordering features and made the point that three of them are one
mechanism: "do this next", "at least N of each first" and "balance by shortfall" all sort the
shared list by an urgency key and differ only in the key. **§1 "do this next" is now built**
(see `DESIGN.md`, *"Do this next" promotes, it does not force*). The three that remain are §2
batched round robin, §3 stock-aware ordering, and the group-level "one each first" toggle that
rides on §3.

Kept as a numbered section here rather than left in the issue because §1 settled several
questions the issue listed as open, and the next agent should not re-open them:

- **Sticky, not one-shot.** Decided, shipped, and documented in `DESIGN.md` with the reasoning.
- **Exactly one marked order per group.** One nullable load ID on `CompBillGroup`, anchor-only,
  carried across an anchor handover by `AdoptGroupState`.
- **No new `OrderingMode` value.** The marker is mode-agnostic, so the enum was not touched and
  the save format did not move. §3's `Balance` will still need one appended — never reordered.
- **The marker is a list mutation, not a comparator.** It moves the bill to index 0 on the
  click. That is why §1 needed no re-sort in `Patch_WorkGiver_DoBill_JobOnThing` and cost
  nothing on the scan path, and it is the thing §3 cannot copy: shortfall moves with stock,
  which no job-start event tracks. The issue's timing trap still stands in full.

What §3 inherits, and what it changes:

- `BillOrdering.TryPlanRotateToTail` already takes a "this bill is marked" flag and refuses.
  When the urgency comparator lands, the marker becomes its `priorityTier` 0 exactly as the
  issue sketched, and that flag is where it plugs in.
- `NextOrder.Resolve` is the single choke point for "is the marker still real". The completion
  rule it consults, `BillOrdering.IsNextOrderSpent`, clears a marker when a *counted* order runs
  out of count. **"Do forever" staying marked forever is intended and settled — do not "fix" it.**
  A forever order has no completion to wait for, and staying put is what vanilla does with an
  order the player moved to the top of the list. Only **"do until you have X"** is deferred,
  because it needs `CountProducts` and that is far too expensive once per visible row per frame.
  **§3 makes that one cheap** — it has to cache per-bill product counts anyway, so finishing the
  rule there is a few lines on top of the cache and should happen in the same change.
- The canonical-order snapshot widening the issue asks for has a second caller now:
  `RoundRobin.SetOrdering` re-promotes the marked order after reprojecting the authored order,
  and any new list-mutating mode must do the same or the marked row goes red in the middle of
  the list.
- The row's button column is laid out at `xMax - 126` with the badge beneath it at `y + 25`,
  which is the slot the issue's layout table reserved for §1. §2's `(2x)` batch label still has
  its own slot at `xMax - 100, y + 25`, under the chain.

One thing §1 left unphotographed, worth folding into whatever scenario work comes next: a marked
order that is *also* being worked, where the red outline, the green active-bill edge and
vanilla's pink "would not start now" all land on one row. Three colour signals on one line is
exactly the kind of thing that has to be looked at rather than reasoned about, and the
`do_this_next` captures only show a marked order sitting idle.

**Read the layout note in `DESIGN.md` before drawing anything new on a bill row.** §2's proposed
`(2x)` batch label at `(xMax - 100, y + 25)` is on top of `Bill_Production.DoConfigInterface`'s
`WidgetRow`, which runs `LeftThenUp` from `(xMax, y + 29)` across the whole second line. The
issue's layout table was derived from the base `Bill.DoConfigInterface`, which every production
bill overrides. §1 drew its badge there first and had to move it up onto the top line.
