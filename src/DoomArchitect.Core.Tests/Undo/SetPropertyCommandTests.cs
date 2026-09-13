using DoomArchitect.Core.Undo;

namespace DoomArchitect.Core.Tests.Undo;

public class SetPropertyCommandTests
{
    private sealed class Widget
    {
        public double Value { get; set; }
        public bool Changed { get; set; }
    }

    [Fact]
    public void Do_SetsTheNewValueAndInvokesOnChanged()
    {
        var widget = new Widget { Value = 1 };
        var command = new SetPropertyCommand<Widget, double>(widget, (w, v) => w.Value = v, oldValue: 1, newValue: 9, onChanged: w => w.Changed = true);

        command.Do();

        Assert.Equal(9, widget.Value);
        Assert.True(widget.Changed);
    }

    [Fact]
    public void Undo_RestoresTheSuppliedOldValueAndInvokesOnChangedAgain()
    {
        var widget = new Widget { Value = 1 };
        var changedCount = 0;
        var command = new SetPropertyCommand<Widget, double>(widget, (w, v) => w.Value = v, oldValue: 1, newValue: 9, onChanged: _ => changedCount++);
        command.Do();

        command.Undo();

        Assert.Equal(1, widget.Value);
        Assert.Equal(2, changedCount);
    }

    [Fact]
    public void Do_WithNoOnChanged_DoesNotThrow()
    {
        var widget = new Widget { Value = 1 };
        var command = new SetPropertyCommand<Widget, double>(widget, (w, v) => w.Value = v, oldValue: 1, newValue: 9);

        command.Do();

        Assert.Equal(9, widget.Value);
    }
}
