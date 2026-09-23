# WorkbenchGroups

Repo-wide rules live in `../CLAUDE.md`; this file adds what is specific to this mod.

## Keep Nice Bill Tab in sync

Nice Bill Tab (`Andromeda.NiceBillTab`) replaces the bills tab outright, so **every feature that
draws in, reads from, or changes a bill list must work in both tabs.** Ship both in the same change,
or log the gap in `TODO.md` §2e with the reason. Don't let the vanilla-tab version land alone
without saying so.

Why: their tab doesn't call most of our chokepoints, and when a chokepoint is skipped nothing
reports an error. The feature just isn't there. When the first compatibility pass asked "where
might our UI overlap theirs?", it missed two correctness bugs. Ask instead: **which of our hooks
does their code skip?** The known ones (details in `DESIGN.md` under "Nice Bill Tab: what a
replacement tab actually costs"):

| Our hook | Their route around it | What covers it |
|---|---|---|
| `Bill.DoInterface` (row annotations, accents, marker) | Rows drawn by `DrawBillPreview` | Patches in `Source/Compat/NiceBillTabCompat.cs` |
| `BillStack.Reorder` (authored order, "do next" clearing) | Drag does bare `Bills.Remove`/`Insert` | `Core.OrderDivergence` + the left-pane check |
| `BillStack.AddBill` (unshareable-bill refusal) | Paste via `TabBillsDrawer.InsertBill` | Reflective patch on `InsertBill` |
| `ITab_Bills.FillTab` layout (ordering and "do next" buttons) | Their own two-pane layout | `DrawLeftPart` prefix reserves a strip; "do next" acts on their `Selections` |
| Our own list moves (rotation, promotion) showing up | They draw a cached copy, rebuilt only on their own actions | `OwnBillListMoves` + setting their `shouldRefreshFilter` |

Checklist for a change in this area:

- **Drawing:** route row colours through `Patch_Bill_DoInterface.AccentFor` / `Core.BillAccent`
  so the two tabs can't disagree, and draw the feature in `NiceBillTabCompat` too. One colour, one
  meaning: **red is "do this next" and nothing else**, in both tabs — which is why their
  `NoOneCanDo` stripes are repainted grey (`BillAccentRule.StripeFor`).
- **List changes, and rules about who may add a bill:** assume their tab changes the list directly.
  Never rely only on a `BillStack` method patch. If our own code moves bills without going through
  `BillStack`, record the result as our own (see `NextOrder.PromoteToHead`), or it will be read as
  another mod's reorder.
- **Tests:** if you bind to another of their members, pin it in `NiceBillTabApiTests`. Harmony
  resolves their names at patch time, and a rename fails without an error in game. Extend or add a
  `nicebilltab_*` scenario, and judge it on the screenshot. The probes pass even with the
  compatibility layer removed.
- **Soft dependency:** resolve everything by name at runtime and never reference
  `NiceBillTab.dll`. Their tab can be switched on and off mid-session, so read
  `NiceBillTabCompat.IsDrawingTab()` every frame rather than caching it.
