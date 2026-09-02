using WorkbenchGroups.Core;

namespace WorkbenchGroups.Tests;

[TestFixture]
public class BillLinkageTests
{
    [Test]
    public void An_ungrouped_bench_gets_no_icon()
    {
        // Not "unlinked". A broken chain on every bill of every workbench in a colony that has
        // never used this mod would be noise standing in for information.
        Assert.That(
            BillLinkage.StateFor(benchIsGrouped: false, workableEverywhere: true),
            Is.EqualTo(BillLinkState.NotApplicable));
    }

    [Test]
    public void An_ungrouped_bench_gets_no_icon_even_for_an_odd_recipe()
    {
        Assert.That(
            BillLinkage.StateFor(benchIsGrouped: false, workableEverywhere: false),
            Is.EqualTo(BillLinkState.NotApplicable));
    }

    [Test]
    public void A_bill_every_member_can_work_is_shared()
    {
        Assert.That(
            BillLinkage.StateFor(benchIsGrouped: true, workableEverywhere: true),
            Is.EqualTo(BillLinkState.Shared));
    }

    [Test]
    public void A_bill_only_some_members_can_work_is_pinned()
    {
        // Unreachable until per-bill linkage lands, since linking currently requires identical
        // recipe sets. Asserted now so the icon is already right when that changes.
        Assert.That(
            BillLinkage.StateFor(benchIsGrouped: true, workableEverywhere: false),
            Is.EqualTo(BillLinkState.Pinned));
    }
}
