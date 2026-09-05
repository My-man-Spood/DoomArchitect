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
}
