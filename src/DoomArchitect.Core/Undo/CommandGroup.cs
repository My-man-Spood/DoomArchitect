namespace DoomArchitect.Core.Undo;

/// <summary>
/// Several commands that undo/redo as one step - e.g. every vertex a
/// sector drag touched. <see cref="Undo"/> runs the children in reverse
/// order, since a later command may depend on an earlier one having
/// already run.
/// </summary>
public sealed class CommandGroup : ICommand
{
    private readonly IReadOnlyList<ICommand> commands;

    public CommandGroup(IReadOnlyList<ICommand> commands)
    {
        this.commands = commands;
    }

    public void Do()
    {
        foreach (var command in commands)
        {
            command.Do();
        }
    }

    public void Undo()
    {
        for (var i = commands.Count - 1; i >= 0; i--)
        {
            commands[i].Undo();
        }
    }
}
