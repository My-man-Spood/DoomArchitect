namespace DoomArchitect.Core.Undo;

/// <summary>
/// A plain two-stack undo/redo history. <see cref="MaxHistory"/> matches
/// UDB's own <c>UndoManager.MAX_UNDO_LEVELS</c> - same cap, on an entirely
/// different (and far smaller) underlying mechanism; see the type-level
/// remarks on why this isn't a port of UDB's actual binary
/// snapshot/diffing system.
/// </summary>
public sealed class UndoStack
{
    public const int MaxHistory = 2000;

    private readonly List<ICommand> undoStack = new();
    private readonly List<ICommand> redoStack = new();

    public bool CanUndo => undoStack.Count > 0;

    public bool CanRedo => redoStack.Count > 0;

    /// <summary>Performs a command and adds it to the undo history.</summary>
    public void Execute(ICommand command)
    {
        command.Do();
        Record(command);
    }

    /// <summary>
    /// Adds an already-performed command to the undo history without
    /// re-running it - for a live-preview edit (e.g. a drag) that should
    /// only become one undo step once the gesture completes, not once
    /// per intermediate frame.
    /// </summary>
    public void Record(ICommand command)
    {
        undoStack.Add(command);
        if (undoStack.Count > MaxHistory) undoStack.RemoveAt(0);
        redoStack.Clear();
    }

    public void Undo()
    {
        if (!CanUndo) return;

        var command = undoStack[^1];
        undoStack.RemoveAt(undoStack.Count - 1);
        command.Undo();
        redoStack.Add(command);
    }

    public void Redo()
    {
        if (!CanRedo) return;

        var command = redoStack[^1];
        redoStack.RemoveAt(redoStack.Count - 1);
        command.Do();
        undoStack.Add(command);
    }
}
