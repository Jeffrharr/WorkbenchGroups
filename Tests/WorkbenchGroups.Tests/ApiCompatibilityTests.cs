using Mono.Cecil;

namespace WorkbenchGroups.Tests;

/// <summary>
/// Verifies that the RimWorld API surface Workbench Groups depends on still exists.
/// Run these after every RimWorld update. Failures mean the mod needs updating.
///
/// These prove members still exist; they say nothing about whether our logic is right. The
/// behaviour tests over Source/Core do that. Both matter, for different failures.
/// </summary>
[TestFixture]
[Category("RequiresGameDll")]
public class ApiCompatibilityTests
{
    private const string FallbackDllPath =
        "/home/deck/.local/share/Steam/steamapps/common/RimWorld/RimWorldLinux_Data/Managed/Assembly-CSharp.dll";

    private static string DllPath =>
        Environment.GetEnvironmentVariable("RIMWORLD_ASSEMBLY") ?? FallbackDllPath;

    private ModuleDefinition _module = null!;

    [OneTimeSetUp]
    public void LoadAssembly()
    {
        if (!File.Exists(DllPath))
            Assert.Ignore($"Assembly-CSharp.dll not found at {DllPath} — set RIMWORLD_ASSEMBLY to run these tests.");
        _module = ModuleDefinition.ReadModule(DllPath);
    }

    [OneTimeTearDown]
    public void Dispose() => _module?.Dispose();

    // --- Building_WorkTable (CompBillGroup, Patch_Building_WorkTable_ExposeData) ---

    [Test]
    public void BuildingWorkTable_billStack_IsStillAPublicField()
    {
        // The whole shared-list design rests on this being a field we can reassign. If it ever
        // becomes a property, the field swap silently stops working: the tab keeps showing each
        // bench's own bills while the work giver reads something else.
        var field = GetType("RimWorld.Building_WorkTable")?.Fields
            .SingleOrDefault(f => f.Name == "billStack");

        Assert.That(field, Is.Not.Null, "Building_WorkTable.billStack no longer exists");
        Assert.That(field!.IsPublic, Is.True, "Building_WorkTable.billStack is no longer public");
        Assert.That(field.FieldType.FullName, Is.EqualTo("RimWorld.BillStack"));
    }

    [Test]
    public void BuildingWorkTable_ExposeData_Exists()
    {
        Assert.That(MethodOf("RimWorld.Building_WorkTable", "ExposeData", 0), Is.Not.Null,
            "Building_WorkTable.ExposeData no longer exists — the save-time swap has no hook");
    }

    // --- BillStack (RoundRobin, Patch_BillStack_Delete, BillGroupOps) ---

    [Test]
    public void BillStack_Bills_ExposesTheLiveList()
    {
        // Round-robin rotation mutates this list directly rather than calling Reorder, which
        // corrupts the stack when given a bill that has already been deleted.
        var property = GetType("RimWorld.BillStack")?.Properties
            .SingleOrDefault(p => p.Name == "Bills");

        Assert.That(property, Is.Not.Null, "BillStack.Bills no longer exists");
        Assert.That(property!.PropertyType.FullName, Is.EqualTo("System.Collections.Generic.List`1<RimWorld.Bill>"));
    }

    [Test]
    public void BillStack_MaxCount_StillExists()
    {
        Assert.That(GetType("RimWorld.BillStack")?.Fields.SingleOrDefault(f => f.Name == "MaxCount"),
            Is.Not.Null, "BillStack.MaxCount no longer exists — the link-time cap check is unanchored");
    }

    [Test]
    public void BillStack_AddBill_And_Delete_Exist()
    {
        Assert.That(MethodOf("RimWorld.BillStack", "AddBill", 1), Is.Not.Null,
            "BillStack.AddBill no longer exists");
        Assert.That(MethodOf("RimWorld.BillStack", "Delete", 1), Is.Not.Null,
            "BillStack.Delete no longer exists");
    }

    [Test]
    public void BillStack_billGiver_IsAssignable()
    {
        // Handing a group's list to a new anchor re-points this field. If it becomes read-only,
        // anchor migration cannot work and destroying a bench takes the group's bills with it.
        var field = GetType("RimWorld.BillStack")?.Fields.SingleOrDefault(f => f.Name == "billGiver");

        Assert.That(field, Is.Not.Null, "BillStack.billGiver no longer exists");
        Assert.That(field!.IsInitOnly, Is.False, "BillStack.billGiver is now read-only");
    }

    // --- Bill / Bill_Production (overshoot guard, mute isolation) ---

    [Test]
    public void BillProduction_ShouldDoNow_Exists()
    {
        Assert.That(MethodOf("RimWorld.Bill_Production", "ShouldDoNow", 0), Is.Not.Null,
            "Bill_Production.ShouldDoNow no longer exists — the overshoot guard has no hook");
    }

    [TestCase("repeatMode")]
    [TestCase("repeatCount")]
    [TestCase("targetCount")]
    [TestCase("paused")]
    public void BillProduction_CountingFields_StillExist(string fieldName)
    {
        Assert.That(GetType("RimWorld.Bill_Production")?.Fields.SingleOrDefault(f => f.Name == fieldName),
            Is.Not.Null, $"Bill_Production.{fieldName} no longer exists");
    }

    [Test]
    public void Bill_suspended_And_IngredientSearchTimer_StillExist()
    {
        var type = GetType("RimWorld.Bill");
        Assert.That(type?.Fields.SingleOrDefault(f => f.Name == "suspended"), Is.Not.Null,
            "Bill.suspended no longer exists");
        Assert.That(type?.Fields.SingleOrDefault(f => f.Name == "nextTickToSearchForIngredients"), Is.Not.Null,
            "Bill.nextTickToSearchForIngredients no longer exists — per-bench mute isolation has nothing to isolate");
    }

    [Test]
    public void Bill_GetUniqueLoadID_Exists()
    {
        // The remembered authored order is keyed by this string.
        Assert.That(MethodOf("RimWorld.Bill", "GetUniqueLoadID", 0), Is.Not.Null,
            "Bill.GetUniqueLoadID no longer exists");
    }

    [Test]
    public void RecipeDef_WorkerCounter_And_CountProducts_Exist()
    {
        Assert.That(GetType("Verse.RecipeDef")?.Properties.SingleOrDefault(p => p.Name == "WorkerCounter"),
            Is.Not.Null, "RecipeDef.WorkerCounter no longer exists");
        Assert.That(MethodOf("Verse.RecipeWorkerCounter", "CountProducts", 1), Is.Not.Null,
            "RecipeWorkerCounter.CountProducts no longer exists");
    }

    // --- Job plumbing (in-flight tracking, round-robin trigger) ---

    [Test]
    public void Job_bill_StillExists()
    {
        // Every count and rotation keys off this field rather than the job's def, so that bill
        // work run under another mod's JobDef is still seen.
        Assert.That(GetType("Verse.AI.Job")?.Fields.SingleOrDefault(f => f.Name == "bill"),
            Is.Not.Null, "Job.bill no longer exists");
    }

    [Test]
    public void PawnJobTracker_StartJob_Exists()
    {
        Assert.That(GetType("Verse.AI.Pawn_JobTracker")?.Methods.SingleOrDefault(m => m.Name == "StartJob"),
            Is.Not.Null, "Pawn_JobTracker.StartJob no longer exists");
    }

    [Test]
    public void PawnJobTracker_CleanupCurrentJob_StillExistsAndIsTheFunnel()
    {
        // Private, so it is resolved by name at runtime — a rename would fail silently at patch
        // time and leak bill reservations until no bill could be started again.
        Assert.That(GetType("Verse.AI.Pawn_JobTracker")?.Methods.SingleOrDefault(m => m.Name == "CleanupCurrentJob"),
            Is.Not.Null, "Pawn_JobTracker.CleanupCurrentJob no longer exists");
    }

    [Test]
    public void PawnJobTracker_curJob_IsReadableFromAPrefix()
    {
        var field = GetType("Verse.AI.Pawn_JobTracker")?.Fields.SingleOrDefault(f => f.Name == "curJob");

        Assert.That(field, Is.Not.Null, "Pawn_JobTracker.curJob no longer exists");
        Assert.That(field!.IsPublic, Is.True, "Pawn_JobTracker.curJob is no longer public");
    }

    // --- WorkGiver_DoBill (mute isolation) ---

    [Test]
    public void WorkGiverDoBill_JobOnThing_KeepsItsParameterNames()
    {
        // Harmony binds injected parameters by name, so a rename here means our prefix silently
        // never receives the bench and the isolation quietly stops happening.
        var method = GetType("RimWorld.WorkGiver_DoBill")?.Methods
            .SingleOrDefault(m => m.Name == "JobOnThing" && m.Parameters.Count == 3);

        Assert.That(method, Is.Not.Null, "WorkGiver_DoBill.JobOnThing(3 args) no longer exists");
        Assert.That(method!.Parameters[1].Name, Is.EqualTo("thing"),
            "WorkGiver_DoBill.JobOnThing's second parameter was renamed");
    }

    // --- Types we subclass or hard-exclude ---

    [Test]
    public void SelfRunningWorkTables_StillDeriveFromBuildingWorkTable()
    {
        // These cast their bills' owner back to their own type and would throw every frame if a
        // group ever anchored on one. Their recipes already exclude them, so this is the safety
        // net: if it stops deriving from Building_WorkTable the assignability check silently stops
        // covering anything.
        Assert.That(GetType("RimWorld.Building_WorkTableAutonomous")?.BaseType?.FullName,
            Is.EqualTo("RimWorld.Building_WorkTable"),
            "Building_WorkTableAutonomous no longer derives from Building_WorkTable — revisit the exclusion");
    }

    [TestCase("UsesUnfinishedThing")]
    [TestCase("formingTicks")]
    [TestCase("gestationCycles")]
    [TestCase("mechResurrection")]
    public void RecipeDef_StillCarriesTheMembersTheGateReads(string memberName)
    {
        // BenchEligibility no longer names bench classes: it admits a bench when every one of its
        // recipes would make a plain Bill_Production, which BillUtility.MakeNewBill decides from
        // exactly these four fields. Rename or remove one and RecipeGate silently starts admitting
        // a bill type we cannot share.
        var type = GetType("Verse.RecipeDef");

        Assert.That(type, Is.Not.Null, "Verse.RecipeDef no longer exists");
        Assert.That(
            type!.Fields.Any(f => f.Name == memberName)
                || type.Properties.Any(p => p.Name == memberName),
            Is.True,
            $"RecipeDef.{memberName} no longer exists — re-derive the gate from BillUtility.MakeNewBill");
    }

    [Test]
    public void MakeNewBill_StillBranchesOnFourThingsAndNothingElse()
    {
        // The gate is only as correct as this branch list is current. Counting the Bill types
        // MakeNewBill can construct catches a fifth case being added — which would be a new bill
        // subclass slipping into shared stacks with no test failing anywhere else.
        var method = GetType("RimWorld.BillUtility")?.Methods
            .FirstOrDefault(m => m.Name == "MakeNewBill");

        Assert.That(method, Is.Not.Null, "BillUtility.MakeNewBill no longer exists");

        var constructed = method!.Body.Instructions
            .Where(i => i.OpCode.Code == Mono.Cecil.Cil.Code.Newobj)
            .Select(i => ((MethodReference)i.Operand).DeclaringType.FullName)
            .Distinct()
            .ToList();

        Assert.That(constructed, Is.EquivalentTo(new[]
        {
            "RimWorld.Bill_ProductionWithUft",
            "RimWorld.Bill_ResurrectMech",
            "RimWorld.Bill_ProductionMech",
            "RimWorld.Bill_Autonomous",
            "RimWorld.Bill_Production",
        }), "MakeNewBill constructs a different set of bills — RecipeGate's rule needs re-deriving");
    }

    [Test]
    public void BillProductionWithUft_StillExists()
    {
        Assert.That(GetType("RimWorld.Bill_ProductionWithUft"), Is.Not.Null,
            "Bill_ProductionWithUft no longer exists — revisit the unshareable-bill rule");
    }

    [TestCase("PostMapInit")]
    [TestCase("PreSwapMap")]
    [TestCase("PostDeSpawn")]
    [TestCase("PostDestroy")]
    [TestCase("PostSpawnSetup")]
    [TestCase("PostExposeData")]
    public void ThingComp_LifecycleHooks_WeOverride_StillExist(string methodName)
    {
        // A renamed base method turns our override into an ordinary method that is never called —
        // it still compiles, so nothing else catches this.
        Assert.That(GetType("Verse.ThingComp")?.Methods.SingleOrDefault(m => m.Name == methodName),
            Is.Not.Null, $"ThingComp.{methodName} no longer exists");
    }

    private TypeDefinition? GetType(string fullName) =>
        _module.GetType(fullName);

    private MethodDefinition? MethodOf(string typeName, string methodName, int parameterCount) =>
        GetType(typeName)?.Methods
            .SingleOrDefault(m => m.Name == methodName && m.Parameters.Count == parameterCount);
}
