using System.Numerics;
using DoomArchitect.Core.Map;
using DoomArchitect.Core.Undo;

namespace DoomArchitect.Core.Tests.Undo;

public class UndoStackTests
{
    [Fact]
    public void Execute_PerformsCommandAndMakesItUndoable()
    {
        var map = new MapData();
        var vertex = map.CreateVertex(new Vector2(0, 0));
        var stack = new UndoStack();

        stack.Execute(new MoveVertexCommand(map, vertex, new Vector2(0, 0), new Vector2(10, 10)));

        Assert.Equal(new Vector2(10, 10), vertex.Position);
        Assert.True(stack.CanUndo);
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void Undo_RevertsToThePreviousPosition()
    {
        var map = new MapData();
        var vertex = map.CreateVertex(new Vector2(0, 0));
        var stack = new UndoStack();
        stack.Execute(new MoveVertexCommand(map, vertex, new Vector2(0, 0), new Vector2(10, 10)));

        stack.Undo();

        Assert.Equal(new Vector2(0, 0), vertex.Position);
        Assert.False(stack.CanUndo);
        Assert.True(stack.CanRedo);
    }

    [Fact]
    public void Redo_ReappliesTheUndoneCommand()
    {
        var map = new MapData();
        var vertex = map.CreateVertex(new Vector2(0, 0));
        var stack = new UndoStack();
        stack.Execute(new MoveVertexCommand(map, vertex, new Vector2(0, 0), new Vector2(10, 10)));
        stack.Undo();

        stack.Redo();

        Assert.Equal(new Vector2(10, 10), vertex.Position);
        Assert.True(stack.CanUndo);
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void Execute_AfterUndo_ClearsTheRedoHistory()
    {
        var map = new MapData();
        var vertex = map.CreateVertex(new Vector2(0, 0));
        var stack = new UndoStack();
        stack.Execute(new MoveVertexCommand(map, vertex, new Vector2(0, 0), new Vector2(10, 10)));
        stack.Undo();

        stack.Execute(new MoveVertexCommand(map, vertex, new Vector2(0, 0), new Vector2(5, 5)));

        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void Record_AddsAnAlreadyPerformedCommandWithoutRunningItAgain()
    {
        var map = new MapData();
        var vertex = map.CreateVertex(new Vector2(0, 0));
        map.MoveVertex(vertex, new Vector2(10, 10));
        var stack = new UndoStack();

        stack.Record(new MoveVertexCommand(map, vertex, new Vector2(0, 0), new Vector2(10, 10)));

        Assert.Equal(new Vector2(10, 10), vertex.Position);
        stack.Undo();
        Assert.Equal(new Vector2(0, 0), vertex.Position);
    }

    [Fact]
    public void Undo_WithEmptyHistory_DoesNothing()
    {
        var stack = new UndoStack();

        stack.Undo();

        Assert.False(stack.CanUndo);
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void Record_BeyondMaxHistory_DropsTheOldestEntry()
    {
        var map = new MapData();
        var vertex = map.CreateVertex(new Vector2(0, 0));
        var stack = new UndoStack();

        for (var i = 0; i < UndoStack.MaxHistory + 1; i++)
        {
            stack.Execute(new MoveVertexCommand(map, vertex, new Vector2(i, i), new Vector2(i + 1, i + 1)));
        }

        for (var i = 0; i < UndoStack.MaxHistory; i++)
        {
            stack.Undo();
        }

        // The oldest recorded move (0,0) -> (1,1) should have been
        // dropped, so undoing every remaining entry lands on (1,1), not
        // the original (0,0).
        Assert.Equal(new Vector2(1, 1), vertex.Position);
        Assert.False(stack.CanUndo);
    }

    /// <summary>
    /// A regression case for a real bug found while building
    /// <c>ThingEditDialog</c>: a property dialog's own <c>OnConfirmed</c>
    /// mixes already-applied commands (real-time fields, mutated directly
    /// as the user types) with never-applied ones (OK-only fields, whose
    /// <see cref="SetFieldCommand"/> only ever gets built at Confirm time) in
    /// one <see cref="CommandGroup"/> - calling <see cref="UndoStack.Record"/>
    /// on that group (as <see cref="Record_AddsAnAlreadyPerformedCommandWithoutRunningItAgain"/>
    /// documents is the correct call for an *already-applied* command)
    /// silently leaves every never-applied member unwritten, since
    /// <see cref="UndoStack.Record"/> never calls <see cref="ICommand.Do"/>.
    /// <see cref="UndoStack.Execute"/> is the correct call whenever a group
    /// contains even one such member - re-running an already-applied
    /// command's <c>Do()</c> a second time is harmless (it just re-sets the
    /// same current value).
    /// </summary>
    [Fact]
    public void Execute_WithAnUnappliedSetFieldCommandInAGroup_ActuallyWritesTheField()
    {
        var map = new MapData();
        var thing = map.CreateThing(new Vector2(0, 0), 1);
        var stack = new UndoStack();

        var command = new SetFieldCommand(thing.Fields, "special", new UniValue(UniversalType.Integer, 42L));
        stack.Execute(new CommandGroup(new ICommand[] { command }));

        Assert.Equal(42, thing.Fields.GetInteger("special", 0));

        stack.Undo();
        Assert.Equal(0, thing.Fields.GetInteger("special", 0));
    }

    [Fact]
    public void CommandGroup_Undo_RevertsAllMembersInReverseOrder()
    {
        var map = new MapData();
        var a = map.CreateVertex(new Vector2(0, 0));
        var b = map.CreateVertex(new Vector2(100, 100));
        var group = new CommandGroup(new ICommand[]
        {
            new MoveVertexCommand(map, a, new Vector2(0, 0), new Vector2(1, 1)),
            new MoveVertexCommand(map, b, new Vector2(100, 100), new Vector2(2, 2)),
        });
        var stack = new UndoStack();

        stack.Execute(group);
        stack.Undo();

        Assert.Equal(new Vector2(0, 0), a.Position);
        Assert.Equal(new Vector2(100, 100), b.Position);
    }

    [Fact]
    public void IsDirty_OnAFreshStack_IsFalse()
    {
        var stack = new UndoStack();

        Assert.False(stack.IsDirty);
    }

    [Fact]
    public void IsDirty_AfterExecute_IsTrue()
    {
        var map = new MapData();
        var vertex = map.CreateVertex(new Vector2(0, 0));
        var stack = new UndoStack();

        stack.Execute(new MoveVertexCommand(map, vertex, new Vector2(0, 0), new Vector2(10, 10)));

        Assert.True(stack.IsDirty);
    }

    [Fact]
    public void IsDirty_AfterRecord_IsTrue()
    {
        var map = new MapData();
        var vertex = map.CreateVertex(new Vector2(0, 0));
        map.MoveVertex(vertex, new Vector2(10, 10));
        var stack = new UndoStack();

        stack.Record(new MoveVertexCommand(map, vertex, new Vector2(0, 0), new Vector2(10, 10)));

        Assert.True(stack.IsDirty);
    }

    [Fact]
    public void MarkSaved_ClearsIsDirty()
    {
        var map = new MapData();
        var vertex = map.CreateVertex(new Vector2(0, 0));
        var stack = new UndoStack();
        stack.Execute(new MoveVertexCommand(map, vertex, new Vector2(0, 0), new Vector2(10, 10)));

        stack.MarkSaved();

        Assert.False(stack.IsDirty);
    }

    [Fact]
    public void MarkSaved_ThenFurtherEdit_IsDirtyAgain()
    {
        var map = new MapData();
        var vertex = map.CreateVertex(new Vector2(0, 0));
        var stack = new UndoStack();
        stack.Execute(new MoveVertexCommand(map, vertex, new Vector2(0, 0), new Vector2(10, 10)));
        stack.MarkSaved();

        stack.Execute(new MoveVertexCommand(map, vertex, new Vector2(10, 10), new Vector2(20, 20)));

        Assert.True(stack.IsDirty);
    }

    [Fact]
    public void Undo_BackToExactlyTheSavedVersion_ClearsIsDirtyAgain()
    {
        var map = new MapData();
        var vertex = map.CreateVertex(new Vector2(0, 0));
        var stack = new UndoStack();
        stack.Execute(new MoveVertexCommand(map, vertex, new Vector2(0, 0), new Vector2(10, 10)));
        stack.MarkSaved();
        stack.Execute(new MoveVertexCommand(map, vertex, new Vector2(10, 10), new Vector2(20, 20)));
        Assert.True(stack.IsDirty);

        stack.Undo();

        Assert.False(stack.IsDirty);
    }

    [Fact]
    public void Redo_PastTheSavedVersion_ReDirtiesIt()
    {
        var map = new MapData();
        var vertex = map.CreateVertex(new Vector2(0, 0));
        var stack = new UndoStack();
        stack.Execute(new MoveVertexCommand(map, vertex, new Vector2(0, 0), new Vector2(10, 10)));
        stack.MarkSaved();
        stack.Execute(new MoveVertexCommand(map, vertex, new Vector2(10, 10), new Vector2(20, 20)));
        stack.Undo();
        Assert.False(stack.IsDirty);

        stack.Redo();

        Assert.True(stack.IsDirty);
    }
}
