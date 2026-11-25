using T3.Core.Operator.Slots;
using Xunit;

namespace Core.Tests.Operator;

public class DirtyFlagVersionTests
{
    /// <summary>
    /// A connected input takes over the version of whatever it is connected to. If a newly connected source happens
    /// to carry the version the consumer already cached, the consumer must still pick up the new value.
    /// </summary>
    [Fact]
    public void ReconnectingToSourceWithMatchingVersion_UpdatesConsumer()
    {
        var oldSource = CreateSource(1);
        var newSource = CreateSource(2);

        var compositionInput = new InputSlot<int>();
        var childInput = new InputSlot<int>();
        childInput.AddConnection(compositionInput);
        compositionInput.AddConnection(oldSource);

        Assert.Equal(1, EvaluateFrame(childInput));

        compositionInput.RemoveConnection();
        compositionInput.AddConnection(newSource);

        Assert.Equal(2, EvaluateFrame(childInput));
    }

    [Fact]
    public void NewSlotsStartWithDistinctVersions()
    {
        Assert.NotEqual(new DirtyFlag().SourceVersion, new DirtyFlag().SourceVersion);
    }

    private static InputSlot<int> CreateSource(int value)
    {
        var slot = new InputSlot<int>();
        slot.UpdateAction = _ => slot.Value = value;
        return slot;
    }

    private static int EvaluateFrame(Slot<int> slot)
    {
        DirtyFlag.GlobalInvalidationTick++;
        slot.InvalidateGraph();
        return slot.GetValue(null!);
    }
}
