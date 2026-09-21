using WorkbenchGroups.Core;

namespace WorkbenchGroups.Tests;

[TestFixture]
public class OrderDivergenceTests
{
    [Test]
    public void An_untouched_list_has_not_diverged()
    {
        Assert.That(
            OrderDivergence.Diverged(
                expected: new[] { "a", "b", "c" },
                current: new[] { "a", "b", "c" }),
            Is.False);
    }

    [Test]
    public void A_dragged_bill_is_a_divergence()
    {
        // Nice Bill Tab's drag-and-drop, which never calls BillStack.Reorder: the player has
        // moved "c" to the top and nothing told the snapshot about it.
        Assert.That(
            OrderDivergence.Diverged(
                expected: new[] { "a", "b", "c" },
                current: new[] { "c", "a", "b" }),
            Is.True);
    }

    [Test]
    public void A_bill_appended_at_the_tail_is_not_a_divergence()
    {
        // Vanilla AddBill. The existing three have not moved relative to each other.
        Assert.That(
            OrderDivergence.Diverged(
                expected: new[] { "a", "b", "c" },
                current: new[] { "a", "b", "c", "new" }),
            Is.False);
    }

    [Test]
    public void A_bill_pasted_into_the_middle_is_not_a_divergence()
    {
        // Nice Bill Tab's "paste bill here" inserts at an arbitrary index. That shifts every
        // later bill's position without changing any bill's position *relative to another*,
        // which is the thing the canonical snapshot actually records.
        Assert.That(
            OrderDivergence.Diverged(
                expected: new[] { "a", "b", "c" },
                current: new[] { "a", "new", "b", "c" }),
            Is.False);
    }

    [Test]
    public void A_deleted_bill_is_not_a_divergence()
    {
        Assert.That(
            OrderDivergence.Diverged(
                expected: new[] { "a", "b", "c" },
                current: new[] { "a", "c" }),
            Is.False);
    }

    [Test]
    public void A_drag_combined_with_an_add_and_a_delete_is_still_a_divergence()
    {
        // The case that makes projecting onto the common set worth the code: "b" is gone and
        // "new" has arrived, but "c" has also genuinely moved above "a". A cheaper check that
        // compared lengths or positions would be swamped by the churn and miss the drag.
        Assert.That(
            OrderDivergence.Diverged(
                expected: new[] { "a", "b", "c" },
                current: new[] { "c", "new", "a" }),
            Is.True);
    }

    [Test]
    public void A_wholly_replaced_list_has_not_diverged()
    {
        // No bill in common, so there is no relative order to have changed. Reporting a
        // divergence here would be harmless — it resnapshots to the live list, which is what
        // the restore would do anyway — but "nothing I remember moved" is the honest answer.
        Assert.That(
            OrderDivergence.Diverged(
                expected: new[] { "a", "b" },
                current: new[] { "x", "y" }),
            Is.False);
    }

    [TestCase(null, null)]
    [TestCase(new string[0], null)]
    [TestCase(null, new string[0])]
    public void Empty_and_null_inputs_are_not_a_divergence(string[]? expected, string[]? current)
    {
        // Null is reachable: the snapshot is a scribed list, and a save written before this
        // field existed loads it as null.
        Assert.That(OrderDivergence.Diverged(expected!, current!), Is.False);
    }

    [Test]
    public void Divergence_is_symmetric()
    {
        // Not relied on anywhere, but a projection bug that dropped one side's filtering would
        // show up as asymmetry long before it showed up as a wrong bill order in a save.
        string[] one = { "a", "b", "c" };
        string[] other = { "b", "a", "c" };

        Assert.That(
            OrderDivergence.Diverged(one, other),
            Is.EqualTo(OrderDivergence.Diverged(other, one)));
    }
}
