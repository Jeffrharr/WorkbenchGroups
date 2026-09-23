using System.Collections.Generic;
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

        /// <summary>
        /// Accent for a bill someone is currently working.
        ///
        /// Public because the replacement bills tab tints its row backgrounds with it (see
        /// <see cref="Compat.NiceBillTabCompat"/>). One definition, so "green means someone is
        /// making this" survives a change of tab rather than being two similar greens that drift.
        /// </summary>
        public static readonly Color ActiveAccent = new Color(0.45f, 0.8f, 0.45f, 1f);

        /// <summary>
        /// Accent for the bill that would be started next.
        ///
        /// Blue rather than a paler green on purpose: "being made" and "about to be made" are
        /// different states, and two shades of one hue read as an intensity — more urgent, more
        /// progressed — rather than as a different kind of thing. It is lighter than the green is
        /// saturated because it is the weaker claim of the two; nothing is happening yet.
        /// </summary>
        public static readonly Color NextUpAccent = new Color(0.45f, 0.68f, 0.92f, 1f);

        /// <summary>
        /// Low enough to read as a tint rather than a panel. The row already carries vanilla's
        /// alternating stripe and, for a claimed bill, vanilla's pink "would not start now"
        /// colouring, so anything stronger fights two existing signals.
        /// </summary>
        private const float WashAlpha = 0.13f;

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
        public static readonly Color NextOrderAccent = new Color(0.9f, 0.25f, 0.25f, 1f);

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
        /// <param name="blocked">
        /// The host's own "nobody can do this" verdict, when it has one; see
        /// <see cref="AccentFor"/>.
        /// </param>
        public static void DrawRowAnnotations(Bill bill, Rect row, bool compact, bool blocked = false)
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

            CompBillGroup anchorComp = groupSize > 1 ? anchor.GetComp<CompBillGroup>() : null;
            bool marked = anchorComp != null && NextOrder.IsNextOrder(anchorComp, bill);
            BillAccent accent = AccentFor(bill, blocked);

            // Two channels, drawn in a fixed order. The "do this next" marker answers "what did
            // the player ask for" and the accent answers "what is the colony doing"; a marked
            // order is usually also next up and often also being worked, so neither may hide the
            // other. The marker owns the outline and badge, the accent owns the left edge bar, and
            // the one surface both want — the fill — is settled by BillAccentRule.WashFor. The
            // edge bar is drawn after the outline so "being worked now" sits on top of it.
            DrawWash(row, BillAccentRule.WashFor(accent, marked, compact), accent);

            if (marked)
            {
                DrawNextOrderMark(row, compact);
            }

            DrawAccentEdge(bill, row, accent);

            // The button is vanilla-rows only. Its slot is laid out against vanilla's row, and a
            // compact host has nowhere stable to put one: Nice Bill Tab's top line runs from its
            // delete button leftwards through a variable number of other mods' buttons, its
            // thumbnail is itself their pause button and would eat the click, and a right-click
            // on the row already opens their menu. The *state* is still shown there, so a marker
            // set from vanilla's tab is never invisible in theirs.
            if (anchorComp != null && !compact)
            {
                DrawNextOrderButton(bill, row, anchorComp, marked);
            }

            if (index != null)
            {
                DrawLinkChain(bill, row, anchor, index, groupSize, compact, anchorComp);
            }
        }

        /// <summary>
        /// The control half of "do this next": the button that marks this order, or un-marks it.
        /// The highlight and badge that say it is marked are <see cref="DrawNextOrderMark"/>.
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
        private static void DrawNextOrderButton(
            Bill bill, Rect row, CompBillGroup anchorComp, bool marked)
        {
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
        /// The state half of "do this next": an outline and the PRIORITY badge.
        ///
        /// An outline rather than another left edge bar, because the left edge belongs to the
        /// accent; and nothing opaque except the badge's own plate, because this draws after the
        /// host has already written the label and the buttons.
        /// </summary>
        private static void DrawNextOrderMark(Rect row, bool compact)
        {
            Color previous = GUI.color;
            GUI.color = NextOrderAccent;
            Widgets.DrawBox(row, 2);
            GUI.color = previous;

            if (compact)
            {
                DrawCompactNextOrderIcon(row);
            }
            else
            {
                DrawNextOrderBadge(row);
            }
        }

        /// <summary>
        /// The compact host's badge: the "do this next" arrow, in the marker's red, on the
        /// top-left corner of the product thumbnail.
        ///
        /// Not the PRIORITY word. The first capture put the word across the top of the thumbnail,
        /// and at Tiny it is wider than the thumbnail, so the plate ran on over the start of the
        /// bill label and cut "Cook" down to "ok". There is no wider stable slot on their row, so
        /// the badge became the glyph a player already knows from vanilla's rows — the same arrow
        /// as the button that sets the mark — sized and placed to mirror the chain badge on the
        /// thumbnail's bottom-left corner. The outline still carries the colour.
        /// </summary>
        private static void DrawCompactNextOrderIcon(Rect row)
        {
            float thumbnailTop = row.center.y - (CompactThumbnailSize / 2f);
            Rect icon = new Rect(
                row.x + CompactThumbnailInset,
                thumbnailTop,
                CompactBadgeSize,
                CompactBadgeSize);

            Widgets.DrawBoxSolid(icon, BadgePlate);

            Color previous = GUI.color;
            GUI.color = NextOrderAccent;
            GUI.DrawTexture(icon, TexButton.NextBig);
            GUI.color = previous;

            if (Mouse.IsOver(icon))
            {
                TooltipHandler.TipRegion(icon, "WBG_NextOrderMarkedCompactTip".Translate());
            }
        }

        /// <summary>
        /// The one translucent fill a row gets, if any; <see cref="BillAccentRule.WashFor"/>
        /// decided which.
        /// </summary>
        private static void DrawWash(Rect row, RowWash wash, BillAccent accent)
        {
            if (wash == RowWash.NextOrder)
            {
                Widgets.DrawBoxSolid(row, NextOrderWash);
            }
            else if (wash == RowWash.Accent)
            {
                Color colour = ColourOf(accent);
                Widgets.DrawBoxSolid(row, new Color(colour.r, colour.g, colour.b, WashAlpha));
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
        private static void DrawAccentEdge(Bill bill, Rect row, BillAccent accent)
        {
            if (accent == BillAccent.None || accent == BillAccent.Blocked)
            {
                // Blocked draws nothing of ours in vanilla's tab: vanilla already paints a bill it
                // would not start, and a red bar on top of that is the same sentence twice. The
                // state exists in the enum because the *replacement* tab needs it, where it means
                // "nobody can do this", painted grey over everything else.
                return;
            }

            Color colour = ColourOf(accent);

            // A hard left edge rather than a filled box: this draws after vanilla has already
            // written the label and the buttons, so anything opaque would cover them. The fill,
            // if any, was drawn by DrawWash before the marker's outline.
            Widgets.DrawBoxSolid(new Rect(row.x, row.y, EdgeBarWidth, row.height), colour);

            // Gated on hover, like vanilla's own paste button does in ITab_Bills.FillTab.
            // TipRegion takes the built string, so an ungated call formats a translated string per
            // row per frame for a tooltip almost nobody is looking at — which is most of what this
            // postfix used to cost.
            Rect tip = new Rect(row.x, row.y, EdgeBarWidth * 4f, row.height);
            if (Mouse.IsOver(tip))
            {
                TooltipHandler.TipRegion(tip, TooltipFor(accent, bill));
            }
        }

        /// <summary>
        /// The one place either bills tab decides what a row is saying, so vanilla's tab and a
        /// replacement cannot drift into meaning different things by the same colour.
        ///
        /// The bench is the selected one, not the stack's owner. In a group those differ — the
        /// stack belongs to the anchor — and it is the bench the player is looking at that makes
        /// "here" mean anything.
        /// </summary>
        /// <param name="blocked">
        /// Whether nobody can do this work. Only the replacement tab passes it: it has already
        /// computed the answer to colour its own row, and recomputing a reachability-and-work-
        /// priority test per row per frame to tell vanilla something it already shows would be
        /// paying twice for a worse answer.
        /// </param>
        public static BillAccent AccentFor(Bill bill, bool blocked = false)
        {
            Building_WorkTable bench = Find.Selector?.SingleSelectedThing as Building_WorkTable;
            if (bill == null || bench == null || !bench.Spawned)
            {
                return BillAccent.None;
            }

            bool workedHere = InFlightTracker.IsWorkedAt(bill, bench);

            return BillAccentRule.Classify(
                blocked,
                workedHere,
                workedElsewhere: !workedHere && InFlightTracker.InFlight(bill) > 0,
                isNextUp: bill == NextBillPreview.In(bench.billStack));
        }

        public static Color ColourOf(BillAccent accent)
        {
            return accent == BillAccent.NextUp ? NextUpAccent : ActiveAccent;
        }

        private static string TooltipFor(BillAccent accent, Bill bill)
        {
            return accent == BillAccent.NextUp
                ? "WBG_BillNextUp".Translate()
                : "WBG_BillBeingWorked".Translate(InFlightTracker.InFlight(bill));
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
            Bill bill,
            Rect row,
            Building_WorkTable anchor,
            BillGroupIndex index,
            int groupSize,
            bool compact,
            CompBillGroup anchorComp)
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

            bool batched = anchorComp?.Ordering == OrderingMode.RoundRobin;
            if (batched)
            {
                // In Nice Bill Tab's compact rows the chain sits on their product thumbnail, which
                // is itself their pause button, so a click there must stay theirs. The count is
                // still drawn — a batch set from vanilla's tab is never invisible in theirs — but
                // it is only *set* from vanilla's tab, like the "do next" button.
                DrawBatchControl(bill, icon, anchorComp, interactive: !compact);
            }

            if (Mouse.IsOver(icon))
            {
                string tip = state == BillLinkState.Shared
                    ? "WBG_BillSharedTip".Translate(groupSize)
                    : "WBG_BillPinnedTip".Translate();

                if (batched)
                {
                    tip += "\n\n" + (compact ? "WBG_BillBatchTipReadOnly" : "WBG_BillBatchTip")
                        .Translate(anchorComp.BatchSizeOf(bill));
                }

                TooltipHandler.TipRegion(icon, tip);
            }
        }

        /// <summary>
        /// Batched round robin's per-row control: the chain becomes a button that sets how many of
        /// this order to make before rotating, and a count badge on its corner shows the size.
        ///
        /// <b>Why here and not under the chain.</b> The design note put a <c>(2x)</c> label at
        /// <c>(xMax - 100, y + 25)</c>, on the second line. That line is not free:
        /// <c>Bill_Production.DoConfigInterface</c> runs a <c>WidgetRow</c> from
        /// <c>(xMax, y + 29)</c> leftwards with "Details...", the repeat-mode button and the +/-
        /// controls, on every bill this mod can hold — the same finding that moved the PRIORITY
        /// badge up. The top line is spoken for too: reorder arrows on the left, the
        /// delete/copy/suspend trio on the right, and the badge, the "do next" arrow and the chain
        /// between them, the badge variable in width.
        ///
        /// So the count rides on the chain itself, as a corner number — the convention every
        /// player already reads on a stack of items. It inherits the chain's visibility rule for
        /// free, which is right: a batch only means anything on a shared order. And making the
        /// chain the button needs no new rect at all, where any separate control would have had
        /// to take one from the bill label.
        ///
        /// Drawn only in round robin, the one mode a batch affects. The badge is left off at a
        /// batch of one so a group that has never used the feature looks exactly as it did.
        /// </summary>
        private static void DrawBatchControl(Bill bill, Rect icon, CompBillGroup anchorComp, bool interactive)
        {
            int size = anchorComp.BatchSizeOf(bill);
            if (size > 1)
            {
                DrawBatchBadge(icon, size);
            }

            if (!interactive)
            {
                return;
            }

            Widgets.DrawHighlightIfMouseover(icon);
            if (Widgets.ButtonInvisible(icon))
            {
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                Find.WindowStack.Add(new FloatMenu(BatchOptions(bill, anchorComp, size)));
            }
        }

        /// <summary>The sizes offered. Small steps where they matter, then coarse ones.</summary>
        private static readonly int[] BatchChoices = { 1, 2, 3, 5, 10, BillOrdering.MaxBatchSize };

        private static List<FloatMenuOption> BatchOptions(Bill bill, CompBillGroup anchorComp, int current)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>(BatchChoices.Length);
            foreach (int choice in BatchChoices)
            {
                int chosen = choice;
                string label = choice == 1
                    ? "WBG_BatchOptionOne".Translate()
                    : "WBG_BatchOption".Translate(choice);

                if (choice == current)
                {
                    label = "WBG_OrderingCurrent".Translate(label);
                }

                options.Add(new FloatMenuOption(label, delegate
                {
                    anchorComp.SetBatchSize(bill, chosen);
                }));
            }

            return options;
        }

        /// <summary>
        /// Opaque behind the number for the same reason as the PRIORITY badge: the chain texture is
        /// busy, and a bare Tiny digit over it came out as noise.
        /// </summary>
        private static readonly Color BatchPlate = new Color(0.08f, 0.08f, 0.08f, 0.9f);

        /// <summary>Tall enough for a Tiny digit's glyph, not its line height.</summary>
        private const float BatchPlateHeight = 12f;

        /// <summary>
        /// The count, right-aligned to the chain's right edge and bottom-aligned to one pixel below
        /// it. Right, not further: the suspend button starts at <c>xMax - 76</c>, two pixels past
        /// the chain. Down, not further: <c>Bill_Production</c>'s widget row starts at
        /// <c>y + 29</c>, and this ends at <c>y + 26</c>. It grows upwards over the chain, which
        /// is what a stack count on an item icon does too.
        /// </summary>
        private static void DrawBatchBadge(Rect icon, int size)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;

            Text.Font = GameFont.Tiny;
            string text = "WBG_BatchBadge".Translate(size);
            // The plate and the label are two rects on purpose. The first capture drew the label
            // into a 13px box and Tiny clipped it to the bottom half of "3x"; the second sized the
            // box to Tiny's full line height, which is legible but covers nearly the whole chain,
            // because a line height is mostly empty space above and below the glyphs. So the plate
            // hugs the glyphs and the label gets its full line height, centred on the plate.
            Vector2 textSize = Text.CalcSize(text);
            float width = textSize.x + 2f;

            Rect plate = new Rect(icon.xMax - width, icon.yMax + 1f - BatchPlateHeight, width, BatchPlateHeight);
            Widgets.DrawBoxSolid(plate, BatchPlate);

            Rect label = new Rect(plate.x, plate.center.y - textSize.y / 2f, plate.width, textSize.y);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(label, text);

            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
        }

    }
}
