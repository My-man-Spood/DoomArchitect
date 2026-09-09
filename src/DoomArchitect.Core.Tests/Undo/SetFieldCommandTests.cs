using DoomArchitect.Core.Map;
using DoomArchitect.Core.Undo;

namespace DoomArchitect.Core.Tests.Undo;

public class SetFieldCommandTests
{
    [Fact]
    public void Do_OnAnAbsentKey_AddsIt()
    {
        var fields = new UniFields();
        var command = new SetFieldCommand(fields, "special", new UniValue(UniversalType.Integer, 9L));

        command.Do();

        Assert.Equal(9L, fields["special"].Value);
    }

    [Fact]
    public void Undo_OnAnAbsentKey_RemovesItAgain()
    {
        var fields = new UniFields();
        var command = new SetFieldCommand(fields, "special", new UniValue(UniversalType.Integer, 9L));
        command.Do();

        command.Undo();

        Assert.False(fields.ContainsKey("special"));
    }

    [Fact]
    public void Do_OnAnExistingKey_OverwritesIt()
    {
        var fields = new UniFields { ["special"] = new UniValue(UniversalType.Integer, 1L) };
        var command = new SetFieldCommand(fields, "special", new UniValue(UniversalType.Integer, 9L));

        command.Do();

        Assert.Equal(9L, fields["special"].Value);
    }

    [Fact]
    public void Undo_OnAnExistingKey_RestoresTheOriginalValue()
    {
        var fields = new UniFields { ["special"] = new UniValue(UniversalType.Integer, 1L) };
        var command = new SetFieldCommand(fields, "special", new UniValue(UniversalType.Integer, 9L));
        command.Do();

        command.Undo();

        Assert.Equal(1L, fields["special"].Value);
    }

    [Fact]
    public void Construction_CapturesThePreExistingValueAtConstructionTime_NotAtDoTime()
    {
        var fields = new UniFields { ["special"] = new UniValue(UniversalType.Integer, 1L) };
        var command = new SetFieldCommand(fields, "special", new UniValue(UniversalType.Integer, 9L));

        // Mutate the field out from under the already-constructed command.
        fields["special"] = new UniValue(UniversalType.Integer, 5L);
        command.Do();
        command.Undo();

        Assert.Equal(1L, fields["special"].Value);
    }

    [Fact]
    public void CommandGroup_Undo_RevertsBothFieldsInOneStep()
    {
        var fieldsA = new UniFields();
        var fieldsB = new UniFields();
        var group = new CommandGroup(new ICommand[]
        {
            new SetFieldCommand(fieldsA, "special", new UniValue(UniversalType.Integer, 9L)),
            new SetFieldCommand(fieldsB, "id", new UniValue(UniversalType.Integer, 3L)),
        });
        var stack = new UndoStack();

        stack.Execute(group);
        stack.Undo();

        Assert.False(fieldsA.ContainsKey("special"));
        Assert.False(fieldsB.ContainsKey("id"));
    }
}
