using WorkbenchGroups.Core;

namespace WorkbenchGroups.Tests;

[TestFixture]
public class SelectedBillRuleTests
{
    [TestCase(0, false)]
    [TestCase(0, true)]
    [TestCase(-1, false)]
    public void Nothing_selected_means_nothing_to_do(int count, bool marked)
    {
        Assert.That(SelectedBillRule.Decide(count, marked), Is.EqualTo(SelectedBillAction.NoSelection));
    }

    [TestCase(2, false)]
    [TestCase(2, true)]
    [TestCase(15, false)]
    public void Several_selected_is_refused_rather_than_guessed(int count, bool marked)
    {
        // Marking is one per group; "the first of your selection" is a bill the player may not
        // associate with the click at all.
        Assert.That(SelectedBillRule.Decide(count, marked), Is.EqualTo(SelectedBillAction.MultipleSelected));
    }

    [Test]
    public void One_unmarked_bill_is_marked()
    {
        Assert.That(SelectedBillRule.Decide(1, selectedIsMarked: false), Is.EqualTo(SelectedBillAction.Mark));
    }

    [Test]
    public void One_marked_bill_is_unmarked_like_vanillas_toggle()
    {
        Assert.That(SelectedBillRule.Decide(1, selectedIsMarked: true), Is.EqualTo(SelectedBillAction.Unmark));
    }

    [TestCase(SelectedBillAction.NoSelection, false)]
    [TestCase(SelectedBillAction.MultipleSelected, false)]
    [TestCase(SelectedBillAction.Mark, true)]
    [TestCase(SelectedBillAction.Unmark, true)]
    public void Only_a_single_selection_is_actionable(SelectedBillAction action, bool expected)
    {
        Assert.That(SelectedBillRule.IsActionable(action), Is.EqualTo(expected));
    }
}
