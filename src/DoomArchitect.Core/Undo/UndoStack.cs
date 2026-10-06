namespace DoomArchitect.Core.Undo;

/// <summary>
/// A plain two-stack undo/redo history. <see cref="MaxHistory"/> matches
/// UDB's <c>UndoManager.MAX_UNDO_LEVELS</c> - same cap, on an entirely
/// different (and far smaller) underlying mechanism: this isn't a port
/// of UDB's binary snapshot/diffing system.
/// </summary>
public sealed class UndoStack
{
    public const int MaxHistory = 2000;

    /// <summary>
    /// Each entry is stamped with the document version reached by applying
    /// it - not a per-operation counter, but a permanent id for that exact
    /// point in history, so <see cref="Undo"/>/<see cref="Redo"/> can
    /// restore the exact version number a position had before, rather than
    /// just moving further away from it. That's what lets undoing back to
    /// precisely the last-saved point read as clean again, not merely
    /// "closer to clean."
    /// </summary>
    private readonly List<(ICommand Command, long Version)> undoStack = new();
    private readonly List<(ICommand Command, long Version)> redoStack = new();

    private long nextVersion;
    private long version;
    private long savedVersion;

    public bool CanUndo => undoStack.Count > 0;

    public bool CanRedo => redoStack.Count > 0;

    /// <summary>
    /// Whether the document has changed since the last <see cref="MarkSaved"/>.
    /// Version-based rather than a plain dirty bool: undoing back to
    /// exactly the last-saved point reads as clean again, and redoing past
    /// that point re-dirties it.
    /// </summary>
    public bool IsDirty => version != savedVersion;

    /// <summary>
    /// Raised whenever <see cref="IsDirty"/>'s result might have changed -
    /// after <see cref="Record"/>, <see cref="Undo"/>, <see cref="Redo"/>,
    /// and <see cref="MarkSaved"/>. A UI layer (e.g. a tab's own dirty
    /// indicator) subscribes to this instead of polling every frame.
    /// </summary>
    public event Action? Changed;

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
        nextVersion++;
        undoStack.Add((command, nextVersion));
        if (undoStack.Count > MaxHistory) undoStack.RemoveAt(0);
        redoStack.Clear();
        version = nextVersion;
        Changed?.Invoke();
    }

    public void Undo()
    {
        if (!CanUndo) return;

        var (command, _) = undoStack[^1];
        undoStack.RemoveAt(undoStack.Count - 1);
        command.Undo();
        redoStack.Add((command, version));
        version = undoStack.Count > 0 ? undoStack[^1].Version : 0;
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (!CanRedo) return;

        var (command, redoneVersion) = redoStack[^1];
        redoStack.RemoveAt(redoStack.Count - 1);
        command.Do();
        undoStack.Add((command, redoneVersion));
        version = redoneVersion;
        Changed?.Invoke();
    }

    /// <summary>Snapshots the current version as "saved" - <see cref="IsDirty"/> reads false until the next edit.</summary>
    public void MarkSaved()
    {
        savedVersion = version;
        Changed?.Invoke();
    }
}
