using Mono.Cecil;

namespace WorkbenchGroups.Tests;

/// <summary>
/// Verifies the Nice Bill Tab members <c>Compat.NiceBillTabCompat</c> patches still exist, and
/// still have the argument names it binds by.
///
/// Worth pinning even though it is someone else's assembly, because every failure here is silent
/// in the game. Harmony binds injected parameters *by name*, so a rename turns a working postfix
/// into an exception at patch time; a method rename turns it into a null lookup and a warning
/// nobody reads. Both present in play as "the chain icons stopped appearing", which is
/// indistinguishable from this mod being switched off — the exact trap
/// <c>Patch_Bill_DoInterface.LastDrawnFrame</c> exists to catch for *other* mods.
///
/// Ignored rather than failed when the mod is not installed: it is a soft dependency, and a
/// developer without it should not have a red suite.
/// </summary>
[TestFixture]
[Category("RequiresNiceBillTab")]
public class NiceBillTabApiTests
{
    private const string FallbackDllPath =
        "/home/deck/.local/share/Steam/steamapps/workshop/content/294100/3520130671/1.6/Assemblies/NiceBillTab.dll";

    private static string DllPath =>
        Environment.GetEnvironmentVariable("NICEBILLTAB_ASSEMBLY") ?? FallbackDllPath;

    private ModuleDefinition _module = null!;

    [OneTimeSetUp]
    public void LoadAssembly()
    {
        if (!File.Exists(DllPath))
            Assert.Ignore($"NiceBillTab.dll not found at {DllPath} — set NICEBILLTAB_ASSEMBLY to run these tests.");
        _module = ModuleDefinition.ReadModule(DllPath);
    }

    [OneTimeTearDown]
    public void Dispose() => _module?.Dispose();

    private TypeDefinition? Drawer => _module.Types.SingleOrDefault(t => t.FullName == "NiceBillTab.TabBillsDrawer");

    private MethodDefinition? MethodOf(string name) =>
        Drawer?.Methods.SingleOrDefault(m => m.Name == name);

    [Test]
    public void The_drawer_type_still_exists()
    {
        Assert.That(Drawer, Is.Not.Null, "NiceBillTab.TabBillsDrawer no longer exists");
    }

    [Test]
    public void Settings_EnabledMod_is_still_a_public_static_bool()
    {
        // Read every frame to decide which of the two ordering controls to show. If this stops
        // being a bool field, IsDrawingTab silently returns false and the ordering control
        // disappears under their tab while the vanilla-position button draws over their search box.
        var field = _module.Types
            .SingleOrDefault(t => t.FullName == "NiceBillTab.Settings")?.Fields
            .SingleOrDefault(f => f.Name == "EnabledMod");

        Assert.That(field, Is.Not.Null, "NiceBillTab.Settings.EnabledMod no longer exists");
        Assert.That(field!.IsStatic, Is.True);
        Assert.That(field.IsPublic, Is.True);
        Assert.That(field.FieldType.FullName, Is.EqualTo("System.Boolean"));
    }

    [Test]
    public void InsertBill_still_takes_the_arguments_the_paste_gate_binds()
    {
        // Their clipboard paste route, which never reaches BillStack.AddBill. Our prefix re-applies
        // the unfinished-thing gate here; lose it and a gun bill can enter a shared stack and
        // strand its UnfinishedThing on the anchor bench.
        var method = MethodOf("InsertBill");

        Assert.That(method, Is.Not.Null, "TabBillsDrawer.InsertBill no longer exists");
        Assert.That(method!.Parameters.Select(p => p.Name),
            Does.Contain("SelTable").And.Contain("bill"),
            "InsertBill's parameter names changed; the Harmony prefix binds by name");
        Assert.That(
            method.Parameters.Single(p => p.Name == "SelTable").ParameterType.FullName,
            Is.EqualTo("RimWorld.Building_WorkTable"));
    }

    [Test]
    public void DrawBillPreview_still_takes_the_arguments_the_annotations_bind()
    {
        // Their per-row drawer, which replaces Bill.DoInterface. The rect is the row as actually
        // drawn — they contract the parameter in the body before drawing, and a Harmony postfix
        // reads the argument slot, so the annotations land on the row rather than near it.
        var method = MethodOf("DrawBillPreview");

        Assert.That(method, Is.Not.Null, "TabBillsDrawer.DrawBillPreview no longer exists");
        Assert.That(method!.Parameters.Select(p => p.Name),
            Does.Contain("recipePreviewRect").And.Contain("bill").And.Contain("drawButtons"),
            "DrawBillPreview's parameter names changed; the Harmony postfix binds by name");
    }

    [Test]
    public void BillStatus_NoOneCanDo_is_still_the_fourth_member()
    {
        // The compat layer reads their status as a bare integer, because naming their enum in a
        // signature would need the hard assembly reference it exists to avoid. That makes the
        // ordinal load-bearing: insert a status above NoOneCanDo and our "leave their red alone"
        // rule silently starts protecting a different state, which shows up as the wrong colour
        // on a row rather than as any kind of error.
        // Nested inside TabBillsDrawer, hence GetTypes() rather than Types — the latter is
        // top-level only, and looking there returns null for a type that is present and fine.
        var status = _module.GetTypes()
            .SingleOrDefault(t => t.FullName == "NiceBillTab.TabBillsDrawer/BillStatus");

        Assert.That(status, Is.Not.Null, "NiceBillTab.BillStatus no longer exists");

        var noOneCanDo = status!.Fields.SingleOrDefault(f => f.Name == "NoOneCanDo");

        Assert.That(noOneCanDo, Is.Not.Null, "BillStatus.NoOneCanDo no longer exists");
        Assert.That(
            noOneCanDo!.Constant,
            Is.EqualTo(4),
            "BillStatus.NoOneCanDo moved; update NiceBillTabCompat.NoOneCanDoStatus");
    }

    [Test]
    public void The_drag_handler_still_bypasses_BillStack_Reorder()
    {
        // Not a patch target — the whole point of Core.OrderDivergence is that this needs no
        // cooperation from them. It is asserted because the *reason* that indirection exists is
        // this bypass: if a future version starts calling Reorder, the eager path in
        // Patch_BillStack_Reorder covers it and the lazy check becomes belt-and-braces rather
        // than the only thing standing between a drag and a discarded arrangement.
        var method = MethodOf("HandleBillDrop");

        Assert.That(method, Is.Not.Null, "TabBillsDrawer.HandleBillDrop no longer exists");

        var callsReorder = method!.Body.Instructions
            .Any(i => i.Operand is MethodReference m && m.Name == "Reorder");

        Assert.That(callsReorder, Is.False,
            "Nice Bill Tab now calls BillStack.Reorder — revisit whether the lazy divergence "
            + "check in Core.OrderDivergence is still load-bearing for this mod.");
    }

    [Test]
    public void Selections_is_still_a_public_static_list_of_RecipeSelection()
    {
        // The "do this next" button above their list acts on their selection. If this field is
        // renamed or stops being static, the lookup returns null and the button shows as having
        // nothing selected, forever, with no error.
        var field = Drawer?.Fields.SingleOrDefault(f => f.Name == "Selections");
        Assert.That(field, Is.Not.Null, "TabBillsDrawer.Selections no longer exists");
        Assert.That(field!.IsStatic && field.IsPublic, Is.True, "Selections is no longer public static");
        Assert.That(field.FieldType.FullName, Does.Contain("NiceBillTab.RecipeSelection"));
    }

    [Test]
    public void RecipeSelection_SelectedBill_is_still_a_Bill_field()
    {
        var field = _module.Types.SingleOrDefault(t => t.FullName == "NiceBillTab.RecipeSelection")?
            .Fields.SingleOrDefault(f => f.Name == "SelectedBill");
        Assert.That(field, Is.Not.Null, "RecipeSelection.SelectedBill no longer exists");
        Assert.That(field!.FieldType.FullName, Is.EqualTo("RimWorld.Bill"));
    }

    [Test]
    public void SelectBill_still_takes_a_bill_and_an_add_flag()
    {
        // Only the scenario step calls this (to select a bill the way a click would), but a
        // scenario that silently selects nothing would pass its "nothing selected" checks.
        var method = MethodOf("SelectBill");
        Assert.That(method, Is.Not.Null, "TabBillsDrawer.SelectBill no longer exists");
        Assert.That(method!.Parameters.Select(p => p.ParameterType.FullName),
            Is.EqualTo(new[] { "RimWorld.Bill", "System.Boolean" }));
    }

    [Test]
    public void ShouldRefreshFilter_is_still_a_public_static_bool()
    {
        // Their bill list is drawn from a cached copy that only this flag rebuilds. We set it after
        // our own rotations and promotions; lose it and those moves go unseen in an open tab.
        var field = Drawer?.Fields.SingleOrDefault(f => f.Name == "shouldRefreshFilter");
        Assert.That(field, Is.Not.Null, "TabBillsDrawer.shouldRefreshFilter no longer exists");
        Assert.That(field!.IsStatic && field.IsPublic, Is.True);
        Assert.That(field.FieldType.FullName, Is.EqualTo("System.Boolean"));
    }
}
