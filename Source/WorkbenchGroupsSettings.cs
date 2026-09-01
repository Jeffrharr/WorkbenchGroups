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

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref preventOvershoot, "preventOvershoot", defaultValue: true);
            Scribe_Values.Look(ref isolateIngredientMute, "isolateIngredientMute", defaultValue: true);
        }
    }
}
