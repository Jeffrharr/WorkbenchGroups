using RimWorldTestHarness.Mod.Probes;
using Verse;

namespace WorkbenchGroups.Probes
{
    /// <summary>
    /// Registers this mod's probes with the harness. Scenario steps are found by reflection over
    /// loaded assemblies and need no registration call; probes are still explicit.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class ProbeRegistration
    {
        static ProbeRegistration()
        {
            ProbeRegistry.Register(new GroupSizeProbe());
            ProbeRegistry.Register(new SharedBillCountProbe());
            ProbeRegistry.Register(new HeadBillSlotProbe());
            ProbeRegistry.Register(new FirstBillShouldDoNowProbe());
            ProbeRegistry.Register(new OrderingModeProbe());
            ProbeRegistry.Register(new DuplicateSaveIdProbe());
        }
    }
}
