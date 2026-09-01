using Verse;

namespace WorkbenchGroups
{
    /// <summary>
    /// Player-facing toggles. Each one exists because the behaviour it guards is a deliberate
    /// deviation from vanilla that some players will want back, not because the feature is
    /// unfinished — a setting is cheaper than a support thread either way.
    /// </summary>
    public class WorkbenchGroupsSettings : ModSettings
    {
        /// <summary>
        /// Reserve a bill's remaining count when a pawn starts rather than when they finish.
        ///
        /// Off, a linked group behaves exactly like vanilla and can overproduce a "make 5" order
        /// when several pawns start it at once. Kept switchable because the guard makes bills
        /// render pink while they are being worked (vanilla colours any bill that would not be
        /// started right now), which reads as a bug to some players.
        /// </summary>
        public bool preventOvershoot = true;

        /// <summary>
        /// Track the "couldn't find ingredients, try again in ~10 seconds" timer per bench rather
        /// than per bill.
        ///
        /// Vanilla stores it on the bill, which was unambiguous when a bill belonged to one
        /// bench. Shared, one badly-placed bench mutes the bill for every other member — including
        /// one standing next to the materials. Off, the vanilla field is left alone.
        /// </summary>
        public bool isolateIngredientMute = true;

        /// <summary>
        /// Bench <c>thingClass</c> names the player wants left ungrouped, separated by commas or
        /// newlines. Matched against both the qualified and the bare name.
        ///
        /// This is the escape hatch for the one thing the eligibility rule cannot see. Benches are
        /// admitted on the strength of their recipes, which is what decides the bill types we have
        /// to handle — but a modded bench class can still cast <c>billStack.billGiver</c> to its
        /// own type inside its own code, and no rule over defs can detect that before it throws.
        /// Naming the class here is a fix the player can apply the same evening, rather than one
        /// they wait a release for.
        ///
        /// Free text rather than a list of picked types because the failure hands them the name:
        /// it is in the stack trace they are already looking at.
        /// </summary>
        public string excludedBenchClasses = "";

        /// <summary>
        /// Selecting one bench in a group selects them all.
        ///
        /// Off by default, and the reason is worth recording because the feature was asked for and
        /// works: **RimWorld shows no ITab for a multi-selection.** Select two stoves and the
        /// inspect pane reads "Electric stove x2" with no tabs at all — so auto-selecting the
        /// group makes the bills tab unreachable by clicking a bench, and the bills tab is the
        /// entire point of this mod. A screenshot caught it; nothing else would have.
        ///
        /// The second consequence, which stands on its own: gizmos act on the whole selection, so
        /// with this on, clicking one bench and pressing Deconstruct deconstructs the group. That
        /// is why vanilla's storage groups do not do this either.
        ///
        /// Left in rather than dropped because the group-at-a-glance reading is genuinely useful
        /// when you are arranging a workshop rather than editing orders, and because the
        /// connecting line and the group outline — which are the informative half — draw either
        /// way, off a single selected bench.
        /// </summary>
        public bool selectWholeGroup = false;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref preventOvershoot, "preventOvershoot", defaultValue: true);
            Scribe_Values.Look(ref isolateIngredientMute, "isolateIngredientMute", defaultValue: true);
            Scribe_Values.Look(ref excludedBenchClasses, "excludedBenchClasses", defaultValue: "");
            Scribe_Values.Look(ref selectWholeGroup, "selectWholeGroup", defaultValue: false);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && excludedBenchClasses == null)
            {
                excludedBenchClasses = "";
            }
        }
    }
}
