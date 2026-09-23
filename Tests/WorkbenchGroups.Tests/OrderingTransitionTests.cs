using WorkbenchGroups.Core;

namespace WorkbenchGroups.Tests;

[TestFixture]
public class OrderingTransitionTests
{
    [TestCase(OrderingMode.InOrder, false)]
    [TestCase(OrderingMode.RoundRobin, true)]
    public void Only_in_order_leaves_the_list_alone(OrderingMode mode, bool mutating)
    {
        Assert.That(OrderingTransition.IsListMutating(mode), Is.EqualTo(mutating));
    }

    [Test]
    public void A_mode_this_build_does_not_know_is_treated_as_rearranging()
    {
        // A save from a newer version, loaded after a downgrade. Treating it as rearranging keeps
        // whatever snapshot it carries, so switching to "in order" can still put the list back.
        Assert.That(OrderingTransition.IsListMutating((OrderingMode)99), Is.True);
    }

    [TestCase(OrderingMode.InOrder, OrderingMode.RoundRobin, SnapshotAction.Snapshot)]
    [TestCase(OrderingMode.RoundRobin, OrderingMode.InOrder, SnapshotAction.Restore)]
    [TestCase(OrderingMode.InOrder, OrderingMode.InOrder, SnapshotAction.None)]
    [TestCase(OrderingMode.RoundRobin, OrderingMode.RoundRobin, SnapshotAction.None)]
    public void Switching_modes_snapshots_on_the_way_in_and_restores_on_the_way_out(
        OrderingMode from, OrderingMode to, SnapshotAction expected)
    {
        Assert.That(OrderingTransition.Plan(from, to), Is.EqualTo(expected));
    }

    [Test]
    public void Moving_between_two_rearranging_modes_keeps_the_first_snapshot()
    {
        // The list at that moment is the first mode's output. Snapshotting it would bake a
        // rotation in as if the player had authored it and lose the real order for good.
        Assert.That(OrderingTransition.Plan(OrderingMode.RoundRobin, (OrderingMode)99),
            Is.EqualTo(SnapshotAction.None));
        Assert.That(OrderingTransition.Plan((OrderingMode)99, OrderingMode.RoundRobin),
            Is.EqualTo(SnapshotAction.None));
    }
}
