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

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref preventOvershoot, "preventOvershoot", defaultValue: true);
            Scribe_Values.Look(ref isolateIngredientMute, "isolateIngredientMute", defaultValue: true);
            Scribe_Values.Look(ref excludedBenchClasses, "excludedBenchClasses", defaultValue: "");

            if (Scribe.mode == LoadSaveMode.PostLoadInit && excludedBenchClasses == null)
            {
                excludedBenchClasses = "";
            }
        }
    }
}
