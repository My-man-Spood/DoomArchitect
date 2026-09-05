namespace DoomArchitect.Core.Undo;

/// <summary>An invertible edit to a <see cref="Map.MapData"/>.</summary>
public interface ICommand
{
    void Do();

    void Undo();
}
