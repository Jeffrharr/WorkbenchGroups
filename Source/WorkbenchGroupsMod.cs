using HarmonyLib;
using UnityEngine;
using Verse;

namespace WorkbenchGroups
{
    /// <summary>
    /// Settings owner and window. Deliberately separate from <see cref="WorkbenchGroupsStartup"/>:
    /// the Mod subclass is constructed by RimWorld's mod loader, while patching has to happen from
    /// a static constructor, and tangling the two makes the ordering between them implicit.
    /// </summary>
    public class WorkbenchGroupsMod : Mod
    {
        /// <summary>
        /// Static so the Harmony patch classes, which cannot hold a Mod instance, can read live
        /// settings without a Find lookup on a hot path.
        /// </summary>
        public static WorkbenchGroupsSettings Settings { get; private set; }

        public WorkbenchGroupsMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<WorkbenchGroupsSettings>();
        }

        public override string SettingsCategory()
        {
            return "Workbench Groups";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            listing.CheckboxLabeled(
                "WBG_SettingPreventOvershoot".Translate(),
                ref Settings.preventOvershoot,
                "WBG_SettingPreventOvershootTip".Translate());

            listing.CheckboxLabeled(
                "WBG_SettingIsolateMute".Translate(),
                ref Settings.isolateIngredientMute,
                "WBG_SettingIsolateMuteTip".Translate());

            listing.CheckboxLabeled(
                "WBG_SettingSelectWholeGroup".Translate(),
                ref Settings.selectWholeGroup,
                "WBG_SettingSelectWholeGroupTip".Translate());

            listing.Gap();

            // Takes effect on restart, not immediately: the comp is injected into defs at startup,
            // so a bench that already has one keeps it for this session. Said in the label rather
            // than left for the player to discover, because "I excluded it and nothing changed"
            // is the bug report this setting exists to prevent.
            listing.Label("WBG_SettingExcludedClasses".Translate());
            listing.Label("WBG_SettingExcludedClassesTip".Translate());
            Settings.excludedBenchClasses = listing.TextEntry(Settings.excludedBenchClasses ?? "", 3);

            listing.End();
        }
    }

    /// <summary>
    /// Applies the Harmony patches. Runs after defs are loaded, which is what
    /// <see cref="BillGroupInjector"/> depends on.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class WorkbenchGroupsStartup
    {
        static WorkbenchGroupsStartup()
        {
            new Harmony("joof.workbenchgroups").PatchAll();
        }
    }
}
