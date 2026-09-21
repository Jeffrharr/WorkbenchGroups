using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
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

            harmony.Patch(target, postfix: new HarmonyMethod(
                typeof(NiceBillTabCompat), nameof(DrawBillPreviewPostfix)));
        }

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
        private static void DrawBillPreviewPostfix(Rect recipePreviewRect, Bill bill, bool drawButtons)
        {
            if (!drawButtons || bill == null)
            {
                return;
            }

            Patch_Bill_DoInterface.DrawRowAnnotations(bill, recipePreviewRect, compact: true);
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

        private const float StripHeight = 30f;

        private const float ButtonHeight = 26f;

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

            reservedStrip = new Rect(
                rect.x,
                rect.y,
                rect.width - TopRightControlsWidth,
                ButtonHeight);

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

            if (Widgets.ButtonText(reservedStrip, "WBG_CommandOrdering".Translate(OrderingMenu.LabelOf(current))))
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
