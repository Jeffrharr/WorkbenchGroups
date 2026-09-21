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
}
