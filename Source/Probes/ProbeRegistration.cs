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
            ProbeRegistry.Register(new SharedStackIdentityProbe());
            ProbeRegistry.Register(new MemberReportedModeProbe());
            ProbeRegistry.Register(new SelectedCountProbe());
            ProbeRegistry.Register(new ActiveBillSlotProbe());
            ProbeRegistry.Register(new DuplicateSaveIdProbe());
            ProbeRegistry.Register(new NextOrderSlotProbe());
            ProbeRegistry.Register(new MarkedBillShouldDoNowProbe());
            ProbeRegistry.Register(new AccentSlotProbe("wbg_worked_here_slot", Core.BillAccent.WorkedHere));
            ProbeRegistry.Register(new AccentSlotProbe("wbg_worked_elsewhere_slot", Core.BillAccent.WorkedElsewhere));
            ProbeRegistry.Register(new AccentSlotProbe("wbg_next_up_slot", Core.BillAccent.NextUp));
            ProbeRegistry.Register(new BillOrderProbe());
            ProbeRegistry.Register(new BenchesPoweredProbe());
            ProbeRegistry.Register(new BillsInFlightProbe());
        }
    }
}
