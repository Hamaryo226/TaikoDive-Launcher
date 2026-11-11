using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;

namespace TaikoDiveLauncher.Tests;

[TestClass]
public sealed class ControllerAssignmentsTests
{
    private static ControllerInputBinding Input(int button, int ordinal = 0) => new()
    {
        VendorId = 123, ProductId = 456, DeviceOrdinal = ordinal, InputIndex = button, DeviceName = "Test controller",
    };

    [TestMethod]
    public void ReassignMovesOnlyThisInputAndRetainsOtherDeviceAndKeyboard()
    {
        InputBindings bindings = new();
        var target = Input(0);
        var other = Input(1);
        var secondDevice = Input(0, 1);
        bindings.Player1.DonLeftControllers = [target, other, secondDevice];
        int[] keys = bindings.Player1.DonLeft;
        Assert.AreEqual(ControllerAssignmentResult.Changed, InputBindingsEditor.AssignController(bindings, 1, target, 2));
        CollectionAssert.AreEqual(new[] { other, secondDevice }, bindings.Player1.DonLeftControllers);
        CollectionAssert.AreEqual(new[] { target }, bindings.Player1.DonRightControllers);
        Assert.AreSame(keys, bindings.Player1.DonLeft);
    }

    [TestMethod]
    public void ConflictingPlayerCannotBeOverwritten()
    {
        InputBindings bindings = new();
        var target = Input(0);
        bindings.Player2.KaRightControllers = [target];
        Assert.AreEqual(ControllerAssignmentResult.Conflict, InputBindingsEditor.AssignController(bindings, 1, Input(0), 1));
        CollectionAssert.AreEqual(new[] { target }, bindings.Player2.KaRightControllers);
        Assert.IsEmpty(bindings.Player1.DonLeftControllers);
    }

    [TestMethod]
    public void ClearRemovesOnlySelectedPlayersMatchingInputs()
    {
        InputBindings bindings = new();
        var target = Input(0);
        var other = Input(1);
        bindings.Player1.KaLeftControllers = [target, target, other];
        bindings.Player1.KaRightControllers = [target];
        bindings.Player2.KaLeftControllers = [target];
        Assert.AreEqual(ControllerAssignmentResult.Changed, InputBindingsEditor.AssignController(bindings, 1, target, null));
        CollectionAssert.AreEqual(new[] { other }, bindings.Player1.KaLeftControllers);
        Assert.IsEmpty(bindings.Player1.KaRightControllers);
        CollectionAssert.AreEqual(new[] { target }, bindings.Player2.KaLeftControllers);
    }

    [TestMethod]
    public void SelectingExistingAssignmentIsNoOp()
    {
        InputBindings bindings = new();
        var target = Input(0);
        ControllerInputBinding[] original = [Input(1), target, Input(2)];
        bindings.Player1.DonLeftControllers = original;
        Assert.AreEqual(ControllerAssignmentResult.Unchanged, InputBindingsEditor.AssignController(bindings, 1, Input(0), 1));
        Assert.AreSame(original, bindings.Player1.DonLeftControllers);
    }
}
