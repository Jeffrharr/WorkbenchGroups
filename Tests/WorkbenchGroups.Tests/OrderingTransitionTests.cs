using WorkbenchGroups.Core;

namespace WorkbenchGroups.Tests;

[TestFixture]
public class OrderingTransitionTests
{
    [TestCase(OrderingMode.InOrder, false, false)]
    [TestCase(OrderingMode.RoundRobin, false, true)]
    [TestCase(OrderingMode.Balance, false, true)]
    [TestCase(OrderingMode.InOrder, true, true)]
    [TestCase(OrderingMode.RoundRobin, true, true)]
    [TestCase(OrderingMode.Balance, true, true)]
    public void Only_in_order_without_floors_leaves_the_list_alone(
        OrderingMode mode, bool oneEachFirst, bool mutating)
    {
        Assert.That(OrderingTransition.IsListMutating(mode, oneEachFirst), Is.EqualTo(mutating));
    }

    [Test]
    public void A_mode_this_build_does_not_know_is_treated_as_rearranging()
    {
        // A save from a newer version, loaded after a downgrade. Treating it as rearranging keeps
        // whatever snapshot it carries, so switching to "in order" can still put the list back.
        Assert.That(OrderingTransition.IsListMutating((OrderingMode)99, false), Is.True);
    }

    [Test]
    public void Balance_is_appended_so_saved_values_keep_their_meaning()
    {
        // OrderingMode is saved by value. Reordering it would silently turn every saved
        // round-robin group into something else on load.
        Assert.That((int)OrderingMode.InOrder, Is.EqualTo(0));
        Assert.That((int)OrderingMode.RoundRobin, Is.EqualTo(1));
        Assert.That((int)OrderingMode.Balance, Is.EqualTo(2));
    }

    [TestCase(false, true, SnapshotAction.Snapshot)]
    [TestCase(true, false, SnapshotAction.Restore)]
    [TestCase(false, false, SnapshotAction.None)]
    [TestCase(true, true, SnapshotAction.None)]
    public void Snapshot_on_the_way_in_restore_on_the_way_out(
        bool wasMutating, bool willMutate, SnapshotAction expected)
    {
        Assert.That(OrderingTransition.Plan(wasMutating, willMutate), Is.EqualTo(expected));
    }

    [TestCase(OrderingMode.InOrder, false, OrderingMode.Balance, false, SnapshotAction.Snapshot)]
    [TestCase(OrderingMode.Balance, false, OrderingMode.InOrder, false, SnapshotAction.Restore)]
    [TestCase(OrderingMode.RoundRobin, false, OrderingMode.Balance, false, SnapshotAction.None)]
    [TestCase(OrderingMode.Balance, false, OrderingMode.RoundRobin, false, SnapshotAction.None)]
    [TestCase(OrderingMode.InOrder, false, OrderingMode.InOrder, true, SnapshotAction.Snapshot)]
    [TestCase(OrderingMode.InOrder, true, OrderingMode.InOrder, false, SnapshotAction.Restore)]
    [TestCase(OrderingMode.RoundRobin, true, OrderingMode.RoundRobin, false, SnapshotAction.None)]
    [TestCase(OrderingMode.InOrder, true, OrderingMode.Balance, true, SnapshotAction.None)]
    public void Mode_switches_and_the_floor_toggle_are_the_same_kind_of_switch(
        OrderingMode from, bool fromFloors, OrderingMode to, bool toFloors, SnapshotAction expected)
    {
        // Moving between two rearranging states keeps the first snapshot: the list at that moment
        // is the first state's output, and snapshotting it would bake a rotation or a sort in as
        // if the player had authored it.
        Assert.That(
            OrderingTransition.Plan(
                OrderingTransition.IsListMutating(from, fromFloors),
                OrderingTransition.IsListMutating(to, toFloors)),
            Is.EqualTo(expected));
    }
}
