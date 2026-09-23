using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using WorkbenchGroups.Core;
using WorkbenchGroups.Patches;

namespace WorkbenchGroups.Compat
{
    /// <summary>
    /// Makes this mod work with Nice Bill Tab, which replaces the bills tab wholesale.
    ///
    /// Nice Bill Tab prefixes <c>ITab_Bills.FillTab</c> and returns false, then draws a two-pane
    /// tab of its own. Nothing about the shared bill list itself is disturbed by that — it reads
    /// the <c>billStack</c> field, so the field swap this mod is built on is invisible to it, and
    /// its "is anyone working this" test keys off <c>pawn.CurJob.bill</c>, which is already
    /// group-correct. What breaks is everything that assumed *vanilla's* tab was doing the
    /// drawing, and one rule that assumed vanilla's <c>AddBill</c> was doing the adding.
    ///
    /// All of it is reached by name at runtime. This mod must not reference NiceBillTab.dll:
    /// that would make a soft dependency a hard one, and a player without the mod would get a
    /// type load failure instead of a mod that simply does not need this file.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class NiceBillTabCompat
    {
        private const string DrawerType = "NiceBillTab.TabBillsDrawer";
        private const string SettingsType = "NiceBillTab.Settings";

        /// <summary>
        /// Nice Bill Tab's own on/off switch, read live rather than cached.
        ///
        /// The tab has a checkbox in its top-right corner that turns the replacement off and hands
        /// the tab back to vanilla, mid-session, with no event and no reload. So "is Nice Bill Tab
        /// installed" is the wrong question for anything layout-related; the question is whether it
        /// is drawing *this frame*, and both answers have to work in one session.
        /// </summary>
        private static FieldInfo enabledModField;

        /// <summary>
        /// <c>TabBillsDrawer.shouldRefreshFilter</c>: their "rebuild the cached bill list" flag.
        /// See <see cref="OwnBillListMoves"/> for why we set it.
        /// </summary>
        private static FieldInfo refreshFilterField;

        /// <summary>The <see cref="OwnBillListMoves.Version"/> their cached list last reflected.</summary>
        private static int seenMovesVersion = -1;

        /// <summary><c>TabBillsDrawer.Selections</c>: their static list of selected rows.</summary>
        private static FieldInfo selectionsField;

        /// <summary><c>RecipeSelection.SelectedBill</c>: the bill a selected row stands for, if any.</summary>
        private static FieldInfo selectedBillField;

        /// <summary>Whether the mod is loaded at all. Everything here is a no-op when false.</summary>
        public static bool IsPresent { get; private set; }

        static NiceBillTabCompat()
        {
            Type drawer = AccessTools.TypeByName(DrawerType);
            Type settings = AccessTools.TypeByName(SettingsType);

            IsPresent = drawer != null && settings != null;
            if (!IsPresent)
            {
                return;
            }

            enabledModField = AccessTools.Field(settings, "EnabledMod");

            // Their selection, for the "do this next" button above their list. Resolved here
            // rather than per frame; a miss leaves the button showing "nothing selected", which
            // is wrong but harmless, and NiceBillTabApiTests pins both names so it is caught
            // before a release rather than in play.
            selectionsField = AccessTools.Field(drawer, "Selections");
            refreshFilterField = AccessTools.Field(drawer, "shouldRefreshFilter");
            selectedBillField = AccessTools.Field(
                AccessTools.TypeByName("NiceBillTab.RecipeSelection"), "SelectedBill");

            // Harmony binds injected parameters by name, so these patches depend on argument names
            // in someone else's assembly — a rename between their releases throws here rather than
            // returning null, and an exception in a StaticConstructorOnStartup would take this
            // mod's own patches down with it. Degrade to a warning: a missing chain icon is worth
            // far less than a working mod, and the failure says which half is missing.
            try
            {
                Harmony harmony = new Harmony("joof.workbenchgroups.nicebilltab");
                PatchInsertBill(harmony, drawer);
                PatchBillRow(harmony, drawer);
                PatchLeftPane(harmony, drawer);
                PatchRowStripes(harmony, drawer);
            }
            catch (Exception e)
            {
                Log.Error(
                    "[Workbench Groups] Failed to patch Nice Bill Tab; its bill tab will not show "
                    + "shared-order annotations, and orders that leave an unfinished item behind "
                    + "can be pasted into a linked group through its paste button. Group "
                    + "behaviour is otherwise unaffected. " + e);
            }
        }

        /// <summary>
        /// Whether Nice Bill Tab is drawing the bills tab right now, as opposed to merely being
        /// installed. False when it is absent, and false while its in-tab toggle is off.
        /// </summary>
        public static bool IsDrawingTab()
        {
            if (!IsPresent || enabledModField == null)
            {
                return false;
            }

            return enabledModField.GetValue(null) is bool enabled && enabled;
        }

        /// <summary>
        /// Closes the hole Nice Bill Tab's clipboard paste opens in the unshareable-bill gate.
        ///
        /// <c>Patch_BillStack_AddBill</c> keeps bills that leave an unfinished item behind — guns,
        /// armour, sculptures — out of a group's shared list, because such a bill's
        /// <c>UnfinishedThing</c> resolves through <c>billStack.billGiver</c> and would strand on
        /// whichever bench happens to be the anchor. It does that as a prefix on <c>AddBill</c>,
        /// on the stated grounds that every route into a bill list passes through it.
        ///
        /// Nice Bill Tab's <c>InsertBill</c> does not. It assigns <c>bill.billStack</c> and calls
        /// <c>Bills.Insert</c> directly, so a player could paste an assault rifle bill straight
        /// into a linked machining table's list — the exact case the mod documents as refused.
        /// Its *other* paste route, <c>InsertBillBizarre</c>, pops the tail, calls <c>AddBill</c>
        /// and pushes back, so that one stays gated by the existing prefix and is left alone.
        /// </summary>
        private static void PatchInsertBill(Harmony harmony, Type drawer)
        {
            MethodInfo target = AccessTools.Method(drawer, "InsertBill");
            if (target == null)
            {
                Log.Warning(
                    "[Workbench Groups] Nice Bill Tab is present but TabBillsDrawer.InsertBill was "
                    + "not found, so its paste route cannot be gated. An order that leaves an "
                    + "unfinished item behind could be pasted into a linked group, where it would "
                    + "strand on the anchor bench.");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(
                typeof(NiceBillTabCompat), nameof(InsertBillPrefix)));
        }

        /// <summary>
        /// Parameter names match Nice Bill Tab's own, because Harmony binds by name.
        /// </summary>
        private static bool InsertBillPrefix(Building_WorkTable SelTable, Bill bill)
        {
            return Patch_BillStack_AddBill.AllowInto(SelTable?.billStack, bill);
        }

        /// <summary>
        /// Puts this mod's per-row annotations back on a tab that never calls
        /// <c>Bill.DoInterface</c>.
        ///
        /// <c>Patch_Bill_DoInterface</c> draws the shared/pinned chain and the in-progress marker
        /// by postfixing vanilla's row drawer. Nice Bill Tab draws rows itself, via
        /// <c>DrawBillPreview</c>, and calls <c>DoInterface</c> nowhere — so with it active those
        /// annotations vanish silently and the tab looks exactly like the mod is switched off.
        /// This is the only reason that class bothers to record the frame it last drew on.
        /// </summary>
        private static void PatchBillRow(Harmony harmony, Type drawer)
        {
            MethodInfo target = AccessTools.Method(drawer, "DrawBillPreview");
            if (target == null)
            {
                Log.Warning(
                    "[Workbench Groups] Nice Bill Tab is present but TabBillsDrawer.DrawBillPreview "
                    + "was not found, so shared-order chain icons will not appear on its bill rows. "
                    + "Group behaviour is unaffected.");
                return;
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(typeof(NiceBillTabCompat), nameof(DrawBillPreviewPrefix)),
                postfix: new HarmonyMethod(typeof(NiceBillTabCompat), nameof(DrawBillPreviewPostfix)));
        }

        /// <summary>
        /// Recolours the diagonal stripes behind a bill row to say what is happening to it.
        ///
        /// Nice Bill Tab already tints a row by its own status, but those statuses are about the
        /// bill in isolation — pending, paused, finished. In a group the useful distinction is a
        /// different one, and it is the distinction this whole mod creates: several benches are
        /// working one list, so "is this being made, and is it being made *here*" is the question
        /// the row cannot otherwise answer.
        ///
        /// <see cref="Patch_Bill_DoInterface.ActiveAccent"/> for a bill being worked at this
        /// bench, <see cref="Patch_Bill_DoInterface.NextUpAccent"/> for the one that would start
        /// next, and their own colour for everything else — so the tint is only overridden where
        /// there is something extra to say.
        ///
        /// The mechanism is narrow on purpose. Their <c>DrawStatusedBillBackground</c> picks a
        /// colour and immediately draws with it, so there is no argument to intercept; but it is
        /// the only caller of <c>DrawTilingTextureHorizontalBottom</c>, which reads
        /// <c>GUI.color</c>. Setting the intended colour while a row we care about is being drawn
        /// therefore changes the stripes and nothing else — no transpiler, and their status logic
        /// runs untouched.
        /// </summary>
        private static void PatchRowStripes(Harmony harmony, Type drawer)
        {
            MethodInfo target = AccessTools.Method(drawer, "DrawTilingTextureHorizontalBottom");
            if (target == null)
            {
                Log.Warning(
                    "[Workbench Groups] Nice Bill Tab is present but "
                    + "TabBillsDrawer.DrawTilingTextureHorizontalBottom was not found, so its bill "
                    + "rows will keep their own background tint. Group behaviour is unaffected.");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(
                typeof(NiceBillTabCompat), nameof(StripePrefix)));
        }

        /// <summary>
        /// Colour to paint the current row's stripes, or null to leave Nice Bill Tab's own.
        /// Set for the duration of one row's draw.
        /// </summary>
        private static Color? stripeOverride;

        /// <summary>
        /// Nice Bill Tab's <c>BillStatus.NoOneCanDo</c>, the one they draw red, as a bare ordinal.
        ///
        /// Their status is their own enum, and naming it in a signature here would need the hard
        /// assembly reference this class exists to avoid — so the argument arrives through
        /// Harmony's <c>__args</c> and is compared as an integer. Pinned by a Cecil test, because
        /// an ordinal into someone else's enum is exactly the thing that silently becomes a
        /// different member when they insert one above it.
        /// </summary>
        private const int NoOneCanDoStatus = 4;

        private const int StatusArgIndex = 3;

        private static void DrawBillPreviewPrefix(Bill bill, bool drawButtons, object[] __args)
        {
            stripeOverride = drawButtons ? StripeColorFor(bill, IsBlocked(__args)) : null;
        }

        /// <summary>
        /// Whether their row status is "nobody can do this work". That state still outranks every
        /// colour of ours — a row claiming "starting next" in blue while nobody can start it is
        /// worse than no colour — but it is repainted grey rather than left red, because red means
        /// "do this next" in both tabs. <see cref="BillAccentRule.StripeFor"/> has the reasoning,
        /// including why it is their colour that yields (the state never fires in their 1.6).
        /// </summary>
        private static bool IsBlocked(object[] args)
        {
            if (args == null || args.Length <= StatusArgIndex || args[StatusArgIndex] == null)
            {
                return false;
            }

            return Convert.ToInt32(args[StatusArgIndex]) == NoOneCanDoStatus;
        }

        /// <summary>
        /// Keeps their alpha and replaces only the hue. The alpha is how they distinguish a
        /// pending row from a finished one, and it is not ours to overrule — we are answering a
        /// different question on the same pixels.
        /// </summary>
        private static void StripePrefix()
        {
            if (stripeOverride == null)
            {
                return;
            }

            // Their alpha is kept as a floor rather than a value: it is how they tell a pending
            // row from a finished one, and that is not ours to erase. The override's own alpha can
            // only raise it — the marker's red is lifted to the strength their own red had, so the
            // priority row reads at least as loudly as the row red used to mark.
            Color tint = stripeOverride.Value;
            GUI.color = new Color(tint.r, tint.g, tint.b, Mathf.Max(GUI.color.a, tint.a));
        }

        /// <summary>
        /// Maps the shared classification onto a stripe colour. The decision itself lives in
        /// <see cref="Patch_Bill_DoInterface.AccentFor"/>, so this tab and vanilla's cannot reach
        /// different conclusions about the same bill.
        /// </summary>
        private static Color? StripeColorFor(Bill bill, bool blocked)
        {
            BillAccent accent = Patch_Bill_DoInterface.AccentFor(bill, blocked);
            bool marked = NextOrder.IsNextOrder(NextOrder.AnchorCompOf(bill?.billStack), bill);

            switch (BillAccentRule.StripeFor(accent, marked))
            {
                case RowWash.Blocked:
                    return BlockedStripe;
                case RowWash.NextOrder:
                    Color red = Patch_Bill_DoInterface.NextOrderAccent;
                    return new Color(red.r, red.g, red.b, TheirRedAlpha);
                case RowWash.Accent:
                    Color accentColour = Patch_Bill_DoInterface.ColourOf(accent);
                    return new Color(accentColour.r, accentColour.g, accentColour.b, 0f);
                default:
                    return null;
            }
        }

        /// <summary>
        /// Their "nobody can do this" state, repainted from red to a dark neutral grey, because red
        /// now means "do this next" in both tabs. Grey rather than another hue: it is the absence
        /// of any work happening, and it must not read as a fourth kind of progress next to green,
        /// blue and red. See <see cref="BillAccentRule.StripeFor"/> for why theirs is the state
        /// that yields.
        /// </summary>
        private static readonly Color BlockedStripe = new Color(0.55f, 0.55f, 0.58f, 0f);

        /// <summary>
        /// The alpha Nice Bill Tab drew its own red stripes at (<c>fromHEX(0xFF0000, 0.4f)</c>).
        /// Their pending rows are only 0.2, which made the marker's red read as a tint rather than
        /// as the row's colour.
        /// </summary>
        private const float TheirRedAlpha = 0.4f;

        /// <summary>
        /// <paramref name="recipePreviewRect"/> is the row as Nice Bill Tab actually drew it:
        /// the method contracts its own parameter and sets the final height before drawing, and a
        /// Harmony postfix reads the argument slot, so this sees the adjusted value rather than
        /// what the caller passed. Positioning from it means the annotations follow their row.
        ///
        /// <paramref name="drawButtons"/> is false for the floating ghost drawn under the cursor
        /// during a drag. Annotating that too would double every icon while dragging.
        ///
        /// The <c>BillStatus</c> parameter is deliberately not declared: it is Nice Bill Tab's own
        /// enum, and naming it in a signature here would need the hard assembly reference this
        /// whole class exists to avoid. Harmony injects only the parameters actually asked for.
        /// </summary>
        private static void DrawBillPreviewPostfix(
            Rect recipePreviewRect, Bill bill, bool drawButtons, object[] __args)
        {
            // Cleared unconditionally and first: this is the only thing that scopes the stripe
            // colour to one row, and leaving it set would tint whatever their tab drew next.
            stripeOverride = null;

            if (!drawButtons || bill == null)
            {
                return;
            }

            // Their status goes in too. Without it the stripes were left red but our green edge bar
            // was still drawn on the same row, which the first live capture of this state showed:
            // "red supersedes" held for their pixels and not for ours.
            Patch_Bill_DoInterface.DrawRowAnnotations(
                bill, recipePreviewRect, compact: true, blocked: IsBlocked(__args));
        }

        /// <summary>
        /// Reserves a strip at the top of Nice Bill Tab's left pane for the ordering control, by
        /// pushing their own content down.
        ///
        /// The alternative was to find a gap in their layout and draw into it, and there is not
        /// one: their top strip holds a search field, and its right-hand end moves by 110 pixels
        /// depending on whether a recipe is selected. Anything placed by guesswork lands on the
        /// search box for some states and not others, which is exactly what the first attempt did.
        ///
        /// Taking the space instead of borrowing it makes the position ours and therefore stable.
        /// <c>rect.yMin</c> moves the top edge down *and* shortens the rect by the same amount, so
        /// their pane keeps its bottom edge and its scroll view shrinks to match rather than
        /// overflowing the tab. The cost is 30px of list height, which is under 5% of their
        /// default tab, and the control ends up where the original design wanted it — next to the
        /// list it acts on.
        /// </summary>
        private static void PatchLeftPane(Harmony harmony, Type drawer)
        {
            MethodInfo target = AccessTools.Method(drawer, "DrawLeftPart");
            if (target == null)
            {
                Log.Warning(
                    "[Workbench Groups] Nice Bill Tab is present but TabBillsDrawer.DrawLeftPart "
                    + "was not found, so a linked group's ordering control will not appear on its "
                    + "tab. The mode is still shown on the bench's inspect line.");
                return;
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(typeof(NiceBillTabCompat), nameof(DrawLeftPartPrefix)),
                postfix: new HarmonyMethod(typeof(NiceBillTabCompat), nameof(DrawLeftPartPostfix)));
        }

        /// <summary>Strip reserved by the prefix, in the same GUI space the postfix draws in.</summary>
        private static Rect reservedStrip;

        /// <summary>Where the "do this next" button goes this frame, or zero for nowhere.</summary>
        private static Rect doNextStrip;

        private const float ButtonGap = 6f;

        private const float DoNextButtonWidth = 120f;

        private const float DoNextMinWidth = 70f;

        private const float StripHeight = 30f;

        private const float ButtonHeight = 26f;

        /// <summary>Enough for "Order: Round robin", the longer of the two English labels.</summary>
        private const float ButtonWidth = 185f;

        /// <summary>
        /// Room kept clear on the right for Nice Bill Tab's own enable checkbox and close button,
        /// which it draws from the tab's total size at y=0 and so are unaffected by pushing the
        /// pane down. Without this inset the ordering button slides underneath them.
        /// </summary>
        private const float TopRightControlsWidth = 60f;

        private const float IconSize = 16f;

        /// <summary>
        /// This mod's only non-vanilla texture. Two vanilla icons were tried and looked at on
        /// screen first: <c>UI/Commands/SwapOutfits</c> renders as a pawn's head and reads as
        /// something about colonists, and <c>UI/Buttons/ReorderDown</c> is list-row art that
        /// scales into a wedge big enough to crowd its own label. A plain cycle glyph says "in
        /// turn" and says nothing else.
        /// </summary>
        private static readonly Texture2D OrderingTex =
            ContentFinder<Texture2D>.Get("UI/Commands/WBG_Ordering");

        private static void DrawLeftPartPrefix(ref Rect rect, Building_WorkTable SelTable)
        {
            reservedStrip = Rect.zero;

            if (!ShouldOfferOrdering(SelTable))
            {
                return;
            }

            // Their drag-and-drop never calls BillStack.Reorder, so the rule "a drag that puts
            // something above the marked order cancels the mark" gets no event here. Checking
            // once per tab draw is the earliest point after a drop that we get control again,
            // and it costs a cached reference and one comparison against the head of the list.
            // Covers in-order groups too, where the round-robin divergence check never runs.
            NextOrder.ClearIfDisplacedFromHead(AnchorCompOf(SelTable));

            // Before their body runs, so the rebuild lands in this same frame: they draw from a
            // filtered copy of the list that only their own actions refresh, and our rotations and
            // promotions are not their actions.
            if (OwnBillListMoves.Version != seenMovesVersion)
            {
                seenMovesVersion = OwnBillListMoves.Version;
                refreshFilterField?.SetValue(null, true);
            }

            // Left-aligned and only as wide as it needs to be. Spanning the pane made a short
            // label float in the middle of a very wide button, which read as a header bar rather
            // than as something to press. Clamped so a long translation cannot grow it back under
            // their checkbox and close button.
            reservedStrip = new Rect(
                rect.x,
                rect.y,
                Mathf.Min(ButtonWidth, rect.width - TopRightControlsWidth),
                ButtonHeight);

            // The "do this next" button takes the rest of the same strip, up to the same right
            // inset. Zero-width when a narrow tab leaves no room, which skips it rather than
            // drawing a button too narrow to read.
            float doNextX = reservedStrip.xMax + ButtonGap;
            float doNextWidth = Mathf.Min(DoNextButtonWidth, rect.xMax - TopRightControlsWidth - doNextX);
            doNextStrip = doNextWidth >= DoNextMinWidth
                ? new Rect(doNextX, rect.y, doNextWidth, ButtonHeight)
                : Rect.zero;

            rect.yMin += StripHeight;
        }

        /// <summary>
        /// Drawn in the postfix rather than the prefix so it sits above their pane: their first
        /// act is to fill the panel background, which would otherwise paint straight over this.
        /// </summary>
        private static void DrawLeftPartPostfix(Building_WorkTable SelTable)
        {
            if (reservedStrip == Rect.zero)
            {
                return;
            }

            CompBillGroup anchorComp = AnchorCompOf(SelTable);
            OrderingMode current = OrderingMenu.CurrentOf(anchorComp);

            string described = OrderingMenu.Describe(current, anchorComp?.OneEachFirst ?? false);
            if (Widgets.ButtonText(reservedStrip, "WBG_CommandOrdering".Translate(described)))
            {
                Find.WindowStack.Add(new FloatMenu(OrderingMenu.OptionsFor(anchorComp, current)));
            }

            // Over the button rather than beside it: ButtonText centres its label, so an icon in
            // the left inset reads as part of the same control without the label having to be
            // shortened or the strip widened.
            GUI.DrawTexture(
                new Rect(reservedStrip.x + 5f, reservedStrip.y + 5f, IconSize, IconSize),
                OrderingTex);

            // Hover-gated like the vanilla-tab button: this runs every frame the tab is open, and
            // building a paragraph-length translated string for a tooltip nobody asked for is most
            // of what such a draw costs.
            if (Mouse.IsOver(reservedStrip))
            {
                TooltipHandler.TipRegion(reservedStrip, "WBG_CommandOrderingDesc".Translate());
            }

            if (doNextStrip != Rect.zero)
            {
                DrawDoNextButton(SelTable, anchorComp);
            }
        }

        /// <summary>
        /// The "do this next" control for Nice Bill Tab, acting on their selected bill.
        ///
        /// Not on the row, where vanilla's is. Their row has no free spot that stays put: the top
        /// line runs leftwards from their delete button through a variable number of other mods'
        /// buttons (Better Workbenches adds its own), the bottom line is their repeat controls, the
        /// thumbnail is itself their pause button and would take the click first, and right-click
        /// already opens their menu. A button that lands on someone else's button for some mod
        /// lists and not others is worse than one a few pixels further from the row. The strip
        /// above the list is ours, so the position is stable in every configuration.
        ///
        /// Selecting a row is one click in their tab, and it highlights the row, so "select, then
        /// press" says which order is meant without guessing. None or several selected leaves the
        /// button inert, with a tooltip saying why (<see cref="SelectedBillRule"/>).
        /// </summary>
        private static void DrawDoNextButton(Building_WorkTable bench, CompBillGroup anchorComp)
        {
            SelectedBillAction action = ActionFor(bench, anchorComp, out _);
            bool actionable = SelectedBillRule.IsActionable(action);

            string label = action == SelectedBillAction.Unmark
                ? "WBG_NbtDoNextClearLabel".Translate()
                : "WBG_NbtDoNextLabel".Translate();

            // Drawn in the marker's red while the selected order is the marked one, the same
            // cue vanilla's row button gives.
            Color previous = GUI.color;
            if (action == SelectedBillAction.Unmark)
            {
                GUI.color = Patch_Bill_DoInterface.NextOrderAccent;
            }

            if (Widgets.ButtonText(doNextStrip, label, active: actionable) && actionable)
            {
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                PressDoNext(bench);
            }

            GUI.color = previous;

            if (Mouse.IsOver(doNextStrip))
            {
                TooltipHandler.TipRegion(doNextStrip, TooltipFor(action));
            }
        }

        /// <summary>
        /// Presses the button: marks or unmarks the single selected bill through
        /// <see cref="NextOrder.Toggle"/>, exactly as vanilla's row button does. Returns whether
        /// anything happened. Public so a scenario can press it without replaying mouse input.
        /// </summary>
        public static bool PressDoNext(Building_WorkTable bench)
        {
            CompBillGroup anchorComp = bench != null && bench.Spawned ? AnchorCompOf(bench) : null;
            SelectedBillAction action = ActionFor(bench, anchorComp, out Bill selected);
            if (!SelectedBillRule.IsActionable(action))
            {
                return false;
            }

            NextOrder.Toggle(anchorComp, selected);
            return true;
        }

        private static SelectedBillAction ActionFor(
            Building_WorkTable bench, CompBillGroup anchorComp, out Bill single)
        {
            single = null;
            int count = anchorComp == null ? 0 : CountSelectedBills(bench, out single);
            return SelectedBillRule.Decide(count, NextOrder.IsNextOrder(anchorComp, single));
        }

        /// <summary>
        /// Selected rows of theirs that stand for a bill in this bench's list. A selected
        /// *recipe* (from their recipe browser) carries no bill and is not counted; nor is a
        /// stale selection naming a bill from some other list.
        /// </summary>
        private static int CountSelectedBills(Building_WorkTable bench, out Bill single)
        {
            single = null;
            List<Bill> bills = bench?.billStack?.Bills;
            if (bills == null || selectionsField == null || selectedBillField == null
                || !(selectionsField.GetValue(null) is IList selections))
            {
                return 0;
            }

            int count = 0;
            foreach (object selection in selections)
            {
                if (selection != null
                    && selectedBillField.GetValue(selection) is Bill bill
                    && bills.Contains(bill))
                {
                    count++;
                    single = bill;
                }
            }

            return count;
        }

        private static string TooltipFor(SelectedBillAction action)
        {
            switch (action)
            {
                case SelectedBillAction.Mark:
                    return "WBG_BillDoNextTip".Translate();
                case SelectedBillAction.Unmark:
                    return "WBG_BillDoNextClearTip".Translate();
                case SelectedBillAction.MultipleSelected:
                    return "WBG_NbtDoNextMultipleTip".Translate() + "\n\n" + "WBG_BillDoNextTip".Translate();
                default:
                    return "WBG_NbtDoNextNoSelectionTip".Translate() + "\n\n" + "WBG_BillDoNextTip".Translate();
            }
        }

        /// <summary>
        /// Whether this bench is in a group, asked before the strip is reserved so an ungrouped
        /// bench's tab is left exactly as Nice Bill Tab drew it. An unconditional 30px inset
        /// would tax every workbench in the game for a control almost none of them show.
        /// </summary>
        private static bool ShouldOfferOrdering(Building_WorkTable bench)
        {
            if (bench == null || !bench.Spawned)
            {
                return false;
            }

            BillGroupIndex index = BillGroupIndex.For(bench.Map);
            return index != null && index.GroupSize(bench) >= 2;
        }

        private static CompBillGroup AnchorCompOf(Building_WorkTable bench)
        {
            return BillGroupIndex.For(bench.Map)?.AnchorOf(bench)?.GetComp<CompBillGroup>();
        }
    }
}
