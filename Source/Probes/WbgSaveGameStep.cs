using System.Collections.Generic;
using System.Linq;
using RimWorldTestHarness.Mod;
using RimWorldTestHarness.Mod.Steps;
using RimWorldTestHarness.Shared;
using RimWorldTestHarness.Shared.Steps;
using Verse;

namespace WorkbenchGroups.Probes
{
    /// <summary>
    /// Saves the game, so the save-time field swap can be checked against the failure it exists
    /// to prevent.
    ///
    /// Without the swap, every member of a group deep-saves the same bills, and RimWorld's own
    /// <c>DebugLoadIDsSavingErrorsChecker</c> warns about the duplicate as it happens. That warning
    /// is a far more direct signal than anything observable after a reload, and it is available
    /// without one — which matters, because the harness has no way to reload mid-scenario.
    ///
    /// Records how many such warnings the save produced, for <see cref="DuplicateSaveIdProbe"/>.
    /// </summary>
    public sealed class WbgSaveGameStep : IStepSpec, IStepAction
    {
        public const string TypeName = "WbgSaveGame";

        /// <summary>Warnings logged during the last save that named a duplicated load ID.</summary>
        public static int DuplicateLoadIdWarnings { get; private set; } = -1;

        public string Type => TypeName;

        public ScenarioResidue Residue => ScenarioResidue.None;

        public bool LiveCallable => false;

        public bool TryValidate(IReadOnlyDictionary<string, string> args, out string error)
        {
            error = null;
            return true;
        }

        public StepOutcome Execute(IReadOnlyDictionary<string, string> args, StepContext ctx)
        {
            StepHelpers.MarkLogBaseline();

            string name = args.TryGetValue("name", out string raw) && !string.IsNullOrWhiteSpace(raw)
                ? raw
                : "wbg_roundtrip";

            GameDataSaveLoader.SaveGame(name);

            DuplicateLoadIdWarnings = StepHelpers.MessagesSinceBaseline()
                .Count(m => m.text != null && m.text.Contains("already here"));

            return new StepOutcome();
        }
    }
}
