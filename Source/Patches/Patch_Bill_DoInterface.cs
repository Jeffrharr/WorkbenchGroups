using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using WorkbenchGroups.Core;

namespace WorkbenchGroups.Patches
{
    /// <summary>
    /// Annotates each row of the bill list: a chain saying whether the order is shared, a
    /// highlight saying whether anyone is working it right now, and the "do this next" button
    /// with the red highlight and badge that say an order has been marked.
    ///
    /// Drawn in a postfix because <c>Bill.DoInterface</c> ends its own <c>Widgets.BeginGroup</c>
    /// before returning, and returns the row's rect in absolute coordinates — so by the time this
    /// runs the coordinate space is the one the returned rect is expressed in, and no offset
    /// arithmetic is needed.
    ///
    /// The textures are vanilla's own storage-link chains, the same pair this mod's link and
    /// unlink gizmos use. Reusing them is deliberate: a player who has linked storage already
    /// knows what a chain and a broken chain mean here.
    /// </summary>
    [HarmonyPatch(typeof(Bill), nameof(Bill.DoInterface))]
    public static class Patch_Bill_DoInterface
    {
        private static readonly Texture2D SharedTex =
            ContentFinder<Texture2D>.Get("UI/Commands/LinkStorageSettings");

        private static readonly Texture2D PinnedTex =
            ContentFinder<Texture2D>.Get("UI/Commands/UnlinkStorageSettings");

        /// <summary>Muted rather than white so the chain reads as an annotation, not a button.</summary>
        private static readonly Color SharedColor = new Color(0.6f, 0.85f, 0.6f, 1f);

        /// <summary>Amber, because "only some benches" is a caveat rather than an error.</summary>
        private static readonly Color PinnedColor = new Color(0.9f, 0.7f, 0.35f, 0.9f);

        private const float IconSize = 22f;

        /// <summary>Accent for a bill someone is currently working.</summary>
        private static readonly Color ActiveAccent = new Color(0.45f, 0.8f, 0.45f, 1f);

        /// <summary>
        /// Low enough to read as a tint rather than a panel. The row already carries vanilla's
        /// alternating stripe and, for a claimed bill, vanilla's pink "would not start now"
        /// colouring, so anything stronger fights two existing signals.
        /// </summary>
        private static readonly Color ActiveWash = new Color(0.45f, 0.8f, 0.45f, 0.13f);

        private const float EdgeBarWidth = 3f;

        /// <summary>
        /// Left of the delete/copy/suspend trio, which occupy the row's top-right 76 pixels.
        /// </summary>
        private const float RightInset = 100f;

        /// <summary>
        /// One 22px column further left again, for the "do this next" button.
        ///
        /// <c>Bill.DoInterface</c> keeps the reorder arrows in the left 24px and the
        /// delete/copy/suspend trio in the right 76px, so the top line is free from here
        /// leftwards. The only thing this competes with is an over-long bill label — vanilla
        /// clips the label at <c>xMax - 40</c> and lets it run under its own buttons — which the
        /// chain icon at <see cref="RightInset"/> already competes with, so it is not a new
        /// problem.
        /// </summary>
        private const float NextOrderInset = 126f;

        /// <summary>
        /// The badge, one more 22px column left of the button.
        ///
        /// It started out *under* the button at <c>y + 25</c>, which is what the design note
        /// proposed and what a reading of <c>Bill.DoConfigInterface</c> supports: that base method
        /// draws only an info-card button at roughly <c>(xMax - 32, y + 37)</c>, leaving the rest
        /// of the second line empty. **`Bill_Production` overrides it and draws something else
        /// entirely** — a <c>WidgetRow</c> anchored at <c>(baseRect.xMax, baseRect.y + 29)</c>
        /// running <c>LeftThenUp</c> with "Details...", the repeat-mode button and the +/-
        /// controls. That sweeps the whole second line from the right edge leftwards, and every
        /// bill this mod can hold is a <c>Bill_Production</c>, so the "empty" slot is occupied on
        /// every row there is. The first capture showed the badge sitting half on top of the
        /// "Do X times" button.
        ///
        /// So the badge moved up onto the top line, which is genuinely free. Anything else that
        /// wants a second-line slot on these rows — the batched round-robin counter in issue #8
        /// §2 proposes <c>(xMax - 100, y + 25)</c> — needs to know this before it is drawn, not
        /// after.
        ///
        /// The badge is right-aligned to end just left of the button rather than pinned to a
        /// fixed left edge, and its width is measured from the word. A fixed box was fine for one
        /// letter; "PRIORITY" is eight, and a localisation is free to be longer still, so a
        /// hardcoded width would clip in whichever language nobody here reads.
        ///
        /// Flush against the button rather than gapped away from it. A 4px gap left a sliver of
        /// bill label showing between the plate and the arrow on a long label — one stray letter
        /// fragment, which reads as a rendering fault rather than as spacing.
        /// </summary>
        private const float BadgeRightEdge = NextOrderInset;

        /// <summary>Breathing room either side of the word, inside its plate.</summary>
        private const float BadgePadding = 5f;

        /// <summary>
        /// Tall enough for a Tiny caps word, short enough to stay inside the row's top line —
        /// which ends at <c>y + 29</c>, where <c>Bill_Production</c>'s own widget row begins.
        /// </summary>
        private const float BadgeHeight = 17f;

        /// <summary>
        /// Red, because the player asked for red and because nothing else in the bill list is.
        /// Vanilla's palette here runs to white, grey, the pink of "would not start now" and this
        /// mod's own green; a saturated red is the one accent that cannot be mistaken for any of
        /// them at a glance.
        /// </summary>
        private static readonly Color NextOrderAccent = new Color(0.9f, 0.25f, 0.25f, 1f);

        /// <summary>
        /// Kept as faint as the active-bill wash, and for the same reason: a marked order is very
        /// often also the one being worked, and two washes at full strength on one row would
        /// muddy both into a colour that means neither.
        /// </summary>
        private static readonly Color NextOrderWash = new Color(0.9f, 0.25f, 0.25f, 0.13f);

        /// <summary>
        /// Opaque fill behind the badge word, and the whole reason the badge is readable.
        ///
        /// The first capture of the spelled-out word had only <c>TexUI.GrayTextBG</c> behind it —
        /// a soft vignette that hides nothing — so "PRIORITY" printed straight over the tail of
        /// the bill label and the two smeared into something unreadable. Covering is not optional
        /// here: vanilla clips bill labels at <c>xMax - 40</c> and lets them run under its own
        /// buttons, so there is no x on this row at which a word can avoid the label. The only
        /// choice is whether it covers the label cleanly or fights it.
        ///
        /// Vanilla answers the same way a few lines below, where the SUSPENDED plate covers the
        /// label outright rather than dodging it. So this covers too: opaque, and dark enough
        /// that the red word on top carries. Kept slightly warm so it reads as part of the red
        /// highlight rather than as a hole punched in the row.
        /// </summary>
        private static readonly Color BadgePlate = new Color(0.14f, 0.09f, 0.09f, 1f);

        /// <summary>
        /// Nice Bill Tab's product thumbnail: 50 square, inset 4 from the row, centred vertically.
        /// Restated here rather than read from them for the same reason this mod restates
        /// vanilla's own rects — their layout is hardcoded in a method body with nothing to
        /// anchor to.
        /// </summary>
        private const float CompactThumbnailSize = 50f;

        private const float CompactThumbnailInset = 4f;

        private const float CompactBadgeSize = 16f;

        /// <summary>
        /// Frame on which this postfix last ran, read by <see cref="Patch_ITab_Bills_FillTab"/>.
        ///
        /// Our row annotations only appear if something actually calls <c>Bill.DoInterface</c>.
        /// A mod that draws its own rows instead would take the chain icons and the active-bill
        /// highlight away with no error anywhere — the tab would simply look like the plain
        /// vanilla one, which is indistinguishable from the mod being off. Recording the frame
        /// lets the tab patch notice and say so once.
        /// </summary>
        public static int LastDrawnFrame { get; private set; } = -1;

        public static void Postfix(Bill __instance, Rect __result)
        {
            DrawRowAnnotations(__instance, __result, compact: false);
        }

        /// <summary>
        /// Draws this mod's annotations onto one bill row, wherever that row was drawn and by
        /// whoever drew it.
        ///
        /// Split out of the postfix so <see cref="Compat.NiceBillTabCompat"/> can call it for a
        /// tab that never invokes <c>Bill.DoInterface</c>. One implementation rather than a
        /// parallel one for the replacement tab is what stops the two drifting into saying
        /// different things about the same group.
        /// </summary>
        /// <param name="compact">
        /// True for a host whose rows are taller and already carry their own status colouring, in
        /// which case these annotations shrink to what that host does not already say.
        /// </param>
        public static void DrawRowAnnotations(Bill bill, Rect row, bool compact)
        {
            LastDrawnFrame = Time.frameCount;

            // The index and the group size are looked up once and handed down. Map.GetComponent
            // walks the map's component list, and this runs per visible row per frame, so asking
            // twice a row was paying for that scan twice for no reason.
            Building_WorkTable anchor = bill?.billStack?.billGiver as Building_WorkTable;
            BillGroupIndex index = anchor != null && anchor.Spawned
                ? BillGroupIndex.For(anchor.Map)
                : null;
            int groupSize = index != null ? index.GroupSize(anchor) : 0;

            // Drawn first so the green "being worked now" edge lands on top of the red wash. A
            // marked order is routinely also the order someone is currently working, and the two
            // are answers to different questions — "what did I ask for next" and "what is
            // happening now" — so neither is allowed to hide the other.
            //
            // Vanilla rows only, for now: its button and badge are laid out against vanilla's row,
            // and a compact host's right-hand side is ingredient icons of varying count.
            if (groupSize > 1 && !compact)
            {
                DrawNextOrder(bill, row, anchor.GetComp<CompBillGroup>());
            }

            DrawActiveMarker(bill, row, compact);

            if (index != null)
            {
                DrawLinkChain(bill, row, anchor, index, groupSize, compact);
            }
        }

        /// <summary>
        /// The per-row half of "do this next": a button that marks this order, and the highlight
        /// and badge that say it is marked.
        ///
        /// Group-only, like the chain icon and the ordering control. On a bench working alone
        /// vanilla's own reorder arrows already put an order first, so a second control that did
        /// the same thing would be clutter claiming to be a feature.
        ///
        /// Clicking mutates the list while <c>BillStack.DoListing</c> is part-way through its
        /// index loop over that same list, which sounds worse than it is. The move is a
        /// remove-and-insert, so the count never changes and the loop cannot run off the end —
        /// vanilla's own delete button, three pixels to the right, genuinely does shrink it. And
        /// the click is reported during the mouse-up event pass, which paints nothing; the list
        /// is already in its new order by the repaint that follows.
        /// </summary>
        private static void DrawNextOrder(Bill bill, Rect row, CompBillGroup anchorComp)
        {
            bool marked = NextOrder.IsNextOrder(anchorComp, bill);

            if (marked)
            {
                // An outline plus a wash rather than another left edge bar: the left edge is
                // spoken for by the active-bill accent, and this postfix draws after vanilla has
                // already written the label and the buttons, so anything opaque would cover them.
                Widgets.DrawBoxSolid(row, NextOrderWash);

                Color previous = GUI.color;
                GUI.color = NextOrderAccent;
                Widgets.DrawBox(row, 2);
                GUI.color = previous;

                DrawNextOrderBadge(row);
            }

            Rect button = new Rect(row.xMax - NextOrderInset, row.y + 3f, IconSize, IconSize);

            // Vanilla's plain right arrow, the same texture its storage chains and "next" controls
            // use. Reusing it is the same bet the chain icons make: an existing icon a player has
            // already learned beats a bespoke one they have not.
            if (Widgets.ButtonImage(
                    button, TexButton.NextBig, marked ? NextOrderAccent : GenUI.SubtleMouseoverColor))
            {
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                NextOrder.Toggle(anchorComp, bill);
            }

            // Hover-gated, like every other tooltip in this patch: TipRegion takes the built
            // string, so an ungated call formats a paragraph-length translated string per row per
            // frame for a tooltip almost nobody is looking at.
            if (Mouse.IsOver(button))
            {
                TooltipHandler.TipRegion(
                    button,
                    marked ? "WBG_BillDoNextClearTip".Translate() : "WBG_BillDoNextTip".Translate());
            }
        }

        /// <summary>
        /// The row badge, built the way vanilla builds its suspended overlay: a caps label
        /// centred on <c>TexUI.GrayTextBG</c>.
        ///
        /// Vanilla has two things that could be called "how a suspended bill is marked", and they
        /// are worth separating because the design note conflated them. The letter "S" on the row
        /// is <c>TexButton.Suspend</c> — a *button* glyph in the right-hand strip, not a state
        /// indicator; it looks identical whether the bill is suspended or not. The actual state
        /// indicator is the word SUSPENDED, written in Medium across a 140x40 plate at the row's
        /// centre. A second plate that size would land straight on top of it, and a suspended
        /// order can perfectly well also be the marked one.
        ///
        /// So this takes the state indicator's construction — <c>TexUI.GrayTextBG</c> plate,
        /// centred caps label — and shrinks it into the button strip where the letter the request
        /// was actually pointing at lives. <c>GameFont.Small</c>, not Tiny: the first capture had
        /// this in Tiny inside a 16px box and the glyph came out as a handful of scattered red
        /// pixels that read as dirt rather than as a letter.
        /// </summary>
        private static void DrawNextOrderBadge(Rect row)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            // Tiny, and measured before the rect is built. Small fits one letter and runs a whole
            // word straight through the chain icon; Tiny is also what vanilla uses for the status
            // line on these same rows, so the badge is set in a size the bill list already uses.
            Text.Font = GameFont.Tiny;
            string word = "WBG_NextOrderBadge".Translate();
            float width = Text.CalcSize(word).x + BadgePadding * 2f;

            Rect badge = new Rect(
                row.xMax - BadgeRightEdge - width,
                row.y + 4f,
                width,
                BadgeHeight);

            // Opaque first, then vanilla's plate texture over it. GrayTextBG alone is a soft
            // vignette and hides nothing, which is what made the first spelled-out capture
            // illegible against a long bill label.
            Widgets.DrawBoxSolid(badge, BadgePlate);
            GUI.DrawTexture(badge, TexUI.GrayTextBG);

            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = NextOrderAccent;
            Widgets.Label(badge, word);

            // Restored to whatever was there rather than to vanilla's defaults. This runs inside
            // someone else's draw call, and a postfix that resets global GUI state to its own
            // idea of normal is how one mod's icon silently changes another mod's font.
            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
        }

        /// <summary>
        /// Marks the order a pawn is actually working.
        ///
        /// Vanilla never needed this: it works the list top-down, so the order being worked is
        /// the one at the top. Round robin breaks that — the bill rotates to the bottom the
        /// moment someone starts it, which is exactly what makes vanilla's selection produce
        /// round-robin behaviour, and exactly why the top of the list stops answering "what is
        /// happening now".
        ///
        /// It also disambiguates a rough edge this mod already had. The overshoot guard makes a
        /// fully-claimed bill report "would not start now", and vanilla paints any such bill pink
        /// — which reads as "blocked" when it means "already being handled". A green edge on the
        /// same row is the difference between those two readings.
        ///
        /// Drawn for ungrouped benches too. The tracker counts every bill a pawn commits to, not
        /// just grouped ones, so there is no reason to withhold an indicator vanilla lacks
        /// entirely.
        /// </summary>
        private static void DrawActiveMarker(Bill bill, Rect row, bool compact)
        {
            int workers = InFlightTracker.InFlight(bill);
            if (workers <= 0)
            {
                return;
            }

            // A wash plus a hard left edge, rather than a filled box: the postfix draws after
            // vanilla has already written the label and the buttons, so anything opaque would
            // cover them.
            //
            // The wash is dropped for a compact host because Nice Bill Tab already tints a row's
            // whole background by status, and laying a second translucent green over that reads as
            // a rendering fault rather than as information. The edge bar still earns its place:
            // their tint marks one bill (the first their 30-tick scan finds), ours marks every
            // bill a pawn has actually committed to, which in a group is routinely several.
            if (!compact)
            {
                Widgets.DrawBoxSolid(row, ActiveWash);
            }

            Widgets.DrawBoxSolid(new Rect(row.x, row.y, EdgeBarWidth, row.height), ActiveAccent);

            // Gated on hover, like vanilla's own paste button does in ITab_Bills.FillTab.
            // TipRegion takes the built string, so an ungated call formats a translated string per
            // row per frame for a tooltip almost nobody is looking at — which is most of what this
            // postfix used to cost.
            Rect tip = new Rect(row.x, row.y, EdgeBarWidth * 4f, row.height);
            if (Mouse.IsOver(tip))
            {
                TooltipHandler.TipRegion(tip, "WBG_BillBeingWorked".Translate(workers));
            }
        }

        /// <summary>
        /// A badge on the bottom-left corner of the row's product thumbnail.
        ///
        /// The right-hand inset used for vanilla rows is not available here: Nice Bill Tab fills
        /// a row's right side with ingredient icons whose count varies by recipe, so a fixed
        /// offset from <c>xMax</c> lands on them for some bills and not others. The thumbnail is
        /// the one element of their row that is always present, always the same size, and always
        /// in the same place, which makes its corner the only stable anchor on offer.
        /// </summary>
        private static Rect CompactChainRect(Rect row)
        {
            float thumbnailBottom = row.center.y + (CompactThumbnailSize / 2f);

            return new Rect(
                row.x + CompactThumbnailInset,
                thumbnailBottom - CompactBadgeSize,
                CompactBadgeSize,
                CompactBadgeSize);
        }

        private static void DrawLinkChain(
            Bill bill, Rect row, Building_WorkTable anchor, BillGroupIndex index, int groupSize, bool compact)
        {
            BillLinkState state = BillLinkage.StateFor(
                groupSize > 1,
                index.AllMembersCanMake(anchor, bill.recipe));

            if (state == BillLinkState.NotApplicable)
            {
                return;
            }

            Rect icon = compact ? CompactChainRect(row) : new Rect(
                row.xMax - RightInset,
                row.y + 3f,
                IconSize,
                IconSize);

            Color previous = GUI.color;
            GUI.color = state == BillLinkState.Shared ? SharedColor : PinnedColor;
            GUI.DrawTexture(icon, state == BillLinkState.Shared ? SharedTex : PinnedTex);
            GUI.color = previous;

            if (Mouse.IsOver(icon))
            {
                TooltipHandler.TipRegion(icon, state == BillLinkState.Shared
                    ? "WBG_BillSharedTip".Translate(groupSize)
                    : "WBG_BillPinnedTip".Translate());
            }
        }

    }
}
