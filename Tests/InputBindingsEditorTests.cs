using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;

namespace TaikoDiveLauncher.Tests;

[TestClass]
public sealed class InputBindingsEditorTests
{
    [TestMethod]
    public void AssignKeyboardAppendsToChosenSlotForEitherPlayer()
    {
        foreach (int player in new[] { 1, 2 })
        {
            for (int slot = 0; slot < 4; slot++)
            {
                InputBindings bindings = new();
                PlayerInputBindings selected = player == 1 ? bindings.Player1 : bindings.Player2;
                PlayerInputBindings other = player == 1 ? bindings.Player2 : bindings.Player1;
                int[][] original = Snapshot(selected);
                int[][] otherOriginal = Snapshot(other);

                Assert.AreEqual(KeyboardAssignmentResult.Changed,
                    InputBindingsEditor.AssignKeyboard(bindings, player, 99, slot));

                for (int index = 0; index < 4; index++)
                {
                    CollectionAssert.AreEqual(index == slot ? [.. original[index], 99] : original[index], selected.GetKeys(index));
                }
                AssertKeys(otherOriginal, other);
            }
        }
    }

    [TestMethod]
    public void AssignKeyboardToExistingSlotIsNoOpAndPreservesOrder()
    {
        InputBindings bindings = new();
        bindings.Player1.DonLeft = [33, 99, 34];
        int[] original = bindings.Player1.DonLeft;

        Assert.AreEqual(KeyboardAssignmentResult.Unchanged,
            InputBindingsEditor.AssignKeyboard(bindings, 1, 99, 1));

        Assert.AreSame(original, bindings.Player1.DonLeft);
        CollectionAssert.AreEqual(new[] { 33, 99, 34 }, bindings.Player1.DonLeft);
    }

    [TestMethod]
    public void AssignKeyboardMovesKeyWithoutRemovingOtherBindings()
    {
        InputBindings bindings = new();
        ControllerInputBinding[] controllers = [new() { InputIndex = 2 }];
        bindings.Player1.DonLeftControllers = controllers;

        Assert.AreEqual(KeyboardAssignmentResult.Changed,
            InputBindingsEditor.AssignKeyboard(bindings, 1, 33, 2));

        CollectionAssert.AreEqual(new[] { 34 }, bindings.Player1.DonLeft);
        CollectionAssert.AreEqual(new[] { 35, 36, 33 }, bindings.Player1.DonRight);
        Assert.AreSame(controllers, bindings.Player1.DonLeftControllers);
    }

    [TestMethod]
    public void AssignKeyboardRejectsOtherPlayerConflictsWithoutAnyMutation()
    {
        foreach (int player in new[] { 1, 2 })
        {
            InputBindings bindings = new();
            bindings.Player1.KaLeft = [99, 99, 32];
            bindings.Player2.DonRight = [46, 99];
            int[][] firstOriginal = Snapshot(bindings.Player1);
            int[][] secondOriginal = Snapshot(bindings.Player2);

            Assert.AreEqual(KeyboardAssignmentResult.Conflict,
                InputBindingsEditor.AssignKeyboard(bindings, player, 99, 0));

            AssertKeys(firstOriginal, bindings.Player1);
            AssertKeys(secondOriginal, bindings.Player2);
        }
    }

    [TestMethod]
    public void ClearKeyboardRemovesAllSelectedPlayerOccurrencesOnly()
    {
        InputBindings bindings = new();
        bindings.Player1.KaLeft = [99, 32, 99];
        bindings.Player1.DonLeft = [33, 99, 34];
        bindings.Player2.KaRight = [99, 47];
        ControllerInputBinding[][] controllers = Enumerable.Range(0, 4)
            .Select(index => new[] { new ControllerInputBinding { InputIndex = index } }).ToArray();
        for (int slot = 0; slot < 4; slot++)
        {
            bindings.Player1.SetControllers(slot, controllers[slot]);
        }
        int[][] otherOriginal = Snapshot(bindings.Player2);

        Assert.AreEqual(KeyboardAssignmentResult.Changed,
            InputBindingsEditor.AssignKeyboard(bindings, 1, 99, null));

        AssertKeys([[32], [33, 34], [35, 36], [37]], bindings.Player1);
        AssertKeys(otherOriginal, bindings.Player2);
        for (int slot = 0; slot < 4; slot++)
        {
            Assert.AreSame(controllers[slot], bindings.Player1.GetControllers(slot));
        }
        Assert.AreEqual(KeyboardAssignmentResult.Unchanged,
            InputBindingsEditor.AssignKeyboard(bindings, 1, 99, null));
    }

    [TestMethod]
    public void AssignKeyboardNormalizesSelectedKeyDuplicatesAcrossAllSlots()
    {
        InputBindings bindings = new();
        bindings.Player1.KaLeft = [99, 32, 99];
        bindings.Player1.DonLeft = [33, 99, 34, 99];
        bindings.Player1.DonRight = [99, 35, 36];
        bindings.Player1.KaRight = [37, 37, 99];

        Assert.AreEqual(KeyboardAssignmentResult.Changed,
            InputBindingsEditor.AssignKeyboard(bindings, 1, 99, 1));

        AssertKeys([[32], [33, 99, 34], [35, 36], [37, 37]], bindings.Player1);
    }

    [TestMethod]
    public void AssignKeyboardRejectsInvalidTargetsWithoutMutation()
    {
        InputBindings bindings = new();
        int[][] original = Snapshot(bindings.Player1);
        Assert.Throws<ArgumentOutOfRangeException>(() => InputBindingsEditor.AssignKeyboard(bindings, 0, 99, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => InputBindingsEditor.AssignKeyboard(bindings, 3, 99, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => InputBindingsEditor.AssignKeyboard(bindings, 1, 99, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => InputBindingsEditor.AssignKeyboard(bindings, 1, 99, 4));
        AssertKeys(original, bindings.Player1);
    }

    private static int[][] Snapshot(PlayerInputBindings player) =>
        Enumerable.Range(0, 4).Select(slot => player.GetKeys(slot).ToArray()).ToArray();

    private static void AssertKeys(int[][] expected, PlayerInputBindings actual)
    {
        for (int slot = 0; slot < 4; slot++)
        {
            CollectionAssert.AreEqual(expected[slot], actual.GetKeys(slot));
        }
    }
}
