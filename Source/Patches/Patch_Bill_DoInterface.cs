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
        /// One 22px column further left again, for the "do this next" button and its badge.
        ///
        /// The column is clear of everything vanilla draws. <c>Bill.DoInterface</c> keeps the
        /// reorder arrows in the left 24px and the delete/copy/suspend trio in the right 76px,
        /// and the nearest control below is <c>DoConfigInterface</c>'s info-card button at
        /// roughly <c>(xMax - 32, y + 37)</c>. The only thing this competes with is an
        /// over-long bill label, which the chain icon at <see cref="RightInset"/> already
        /// competes with — so it is not a new problem.
        /// </summary>
        private const float NextOrderInset = 126f;

        /// <summary>Short enough to sit under the button without reaching the status line.</summary>
        private const float BadgeHeight = 16f;

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
            LastDrawnFrame = Time.frameCount;

            // The index and the group size are looked up once and handed down. Map.GetComponent
            // walks the map's component list, and this postfix runs per visible row per frame, so
            // asking twice a row was paying for that scan twice for no reason.
            Building_WorkTable anchor = __instance?.billStack?.billGiver as Building_WorkTable;
            BillGroupIndex index = anchor != null && anchor.Spawned
                ? BillGroupIndex.For(anchor.Map)
                : null;
            int groupSize = index != null ? index.GroupSize(anchor) : 0;

            // Drawn first so the green "being worked now" edge lands on top of the red wash. A
            // marked order is routinely also the order someone is currently working, and the two
            // are answers to different questions — "what did I ask for next" and "what is
            // happening now" — so neither is allowed to hide the other.
            if (groupSize > 1)
            {
                DrawNextOrder(__instance, __result, anchor.GetComp<CompBillGroup>());
            }

            DrawActiveMarker(__instance, __result);

            if (index != null)
            {
                DrawLinkChain(__instance, __result, anchor, index, groupSize);
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
        /// Worth recording, because it is easy to misremember — vanilla does not stamp a single
        /// letter anywhere in the bill list. <c>Bill.DoInterface</c> writes the whole word
        /// SUSPENDED in Medium across a 140x40 plate at the row's centre. A second plate that
        /// size would sit exactly on top of it, and a suspended order can perfectly well also be
        /// the marked one, so what is mirrored here is the idiom — grey plate, centred caps — at
        /// badge scale in this mod's own column. The letter rather than a word is simply what
        /// fits in 22 pixels; the button's tooltip carries the meaning.
        /// </summary>
        private static void DrawNextOrderBadge(Rect row)
        {
            Rect badge = new Rect(row.xMax - NextOrderInset, row.y + 25f, IconSize, BadgeHeight);

            GUI.DrawTexture(badge, TexUI.GrayTextBG);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = NextOrderAccent;
            Widgets.Label(badge, "WBG_NextOrderBadge".Translate());

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
        private static void DrawActiveMarker(Bill bill, Rect row)
        {
            int workers = InFlightTracker.InFlight(bill);
            if (workers <= 0)
            {
                return;
            }

            // A wash plus a hard left edge, rather than a filled box: the postfix draws after
            // vanilla has already written the label and the buttons, so anything opaque would
            // cover them.
            Widgets.DrawBoxSolid(row, ActiveWash);
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

        private static void DrawLinkChain(
            Bill bill, Rect __result, Building_WorkTable anchor, BillGroupIndex index, int groupSize)
        {
            BillLinkState state = BillLinkage.StateFor(
                groupSize > 1,
                index.AllMembersCanMake(anchor, bill.recipe));

            if (state == BillLinkState.NotApplicable)
            {
                return;
            }

            Rect icon = new Rect(
                __result.xMax - RightInset,
                __result.y + 3f,
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
