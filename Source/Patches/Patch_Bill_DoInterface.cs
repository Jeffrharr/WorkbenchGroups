using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using WorkbenchGroups.Core;

namespace WorkbenchGroups.Patches
{
    /// <summary>
    /// Annotates each row of the bill list: a chain saying whether the order is shared, and a
    /// highlight saying whether anyone is working it right now.
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

            DrawActiveMarker(__instance, __result);

            // The index is looked up once and handed down. Map.GetComponent walks the map's
            // component list, and this postfix runs per visible row per frame, so asking twice a
            // row was paying for that scan twice for no reason.
            Building_WorkTable anchor = __instance?.billStack?.billGiver as Building_WorkTable;
            BillGroupIndex index = anchor != null && anchor.Spawned
                ? BillGroupIndex.For(anchor.Map)
                : null;

            if (index != null)
            {
                DrawLinkChain(__instance, __result, anchor, index);
            }
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
            Bill bill, Rect __result, Building_WorkTable anchor, BillGroupIndex index)
        {
            int groupSize = index.GroupSize(anchor);
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
