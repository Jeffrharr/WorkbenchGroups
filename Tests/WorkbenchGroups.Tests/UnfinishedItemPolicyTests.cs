using WorkbenchGroups.Core;

namespace WorkbenchGroups.Tests;

/// <summary>Resume-bench choice and rotation timing for unfinished-item orders (issue #11).</summary>
[TestFixture]
public class UnfinishedItemPolicyTests
{
    [TestCase(true, true, true, ExpectedResult = ResumeBench.Parked)]
    [TestCase(true, true, false, ExpectedResult = ResumeBench.Parked)]
    [TestCase(true, false, true, ExpectedResult = ResumeBench.Scanned)]
    [TestCase(true, false, false, ExpectedResult = ResumeBench.Owner)]
    [TestCase(false, false, true, ExpectedResult = ResumeBench.Scanned)]
    [TestCase(false, false, false, ExpectedResult = ResumeBench.Owner)]
    [TestCase(false, true, true, ExpectedResult = ResumeBench.Scanned)]
    public ResumeBench Chooses_the_resume_bench(bool parked, bool parkedUsable, bool scannedShares)
    {
        // Row by row: a usable parked bench always wins, even over a free scanned one; a busy
        // parked bench yields to the scanned one; nothing eligible falls back to vanilla. The
        // last row is a nonsensical input (usable but not parked) and must not invent a parked
        // bench.
        return UnfinishedItemPolicy.Choose(parked, parkedUsable, scannedShares);
    }

    [Test]
    public void A_moved_job_stays_put_on_the_next_resume()
    {
        // No ping-pong: parked at A, A busy, so the job moves to B. The item is now parked at B,
        // which is free, so the next resume stays at B even if A was scanned first.
        Assert.That(UnfinishedItemPolicy.Choose(true, false, true), Is.EqualTo(ResumeBench.Scanned));
        Assert.That(UnfinishedItemPolicy.Choose(true, true, true), Is.EqualTo(ResumeBench.Parked));
    }

    [TestCase(RotationMoment.JobStart, false, ExpectedResult = true)]
    [TestCase(RotationMoment.UnitCompleted, false, ExpectedResult = false)]
    [TestCase(RotationMoment.JobStart, true, ExpectedResult = false)]
    [TestCase(RotationMoment.UnitCompleted, true, ExpectedResult = true)]
    public bool Rotates_exactly_once_per_unit(RotationMoment moment, bool leavesUnfinishedItem)
    {
        // Each kind of order rotates at exactly one of the two moments, so no unit is counted
        // twice and none is skipped.
        return UnfinishedItemPolicy.RotatesAt(moment, leavesUnfinishedItem);
    }
}
