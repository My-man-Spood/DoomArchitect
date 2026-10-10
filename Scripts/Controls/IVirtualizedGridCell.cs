using System;

namespace DoomArchitect.Controls;

/// <summary>
/// What <see cref="VirtualizedGrid{TItem,TCell}"/> needs from a cell
/// scene's root script to drive a uniform-cell-size virtualized grid of
/// them: render whatever item is currently assigned, show/hide a
/// selection highlight, and report clicks/double-clicks back.
///
/// <see cref="LogicalIndex"/> exists because a pooled cell gets
/// reassigned to a different item (and index) as the user scrolls - a
/// host subscribing to <see cref="Pressed"/>/<see cref="Activated"/>
/// should subscribe exactly once per cell instance and read this field
/// at invocation time, never close over an index captured at
/// subscription time, which would go stale the moment the cell is
/// recycled for a different item. <see cref="VirtualizedGrid{TItem,TCell}"/>
/// itself follows this rule for its own internal subscriptions.
/// </summary>
public interface IVirtualizedGridCell<TItem>
{
    /// <summary>Which item in the host grid's own list this (possibly recycled) cell currently represents - set by the grid every time it (re)assigns this instance.</summary>
    int LogicalIndex { get; set; }

    bool Selected { get; set; }

    /// <summary>A single left click anywhere in the cell.</summary>
    event Action Pressed;

    /// <summary>A double-click (or the equivalent activation) anywhere in the cell.</summary>
    event Action Activated;

    /// <summary>Renders the given item - called once when this cell is newly assigned to it, and again any time a host updates that same item in place (<see cref="VirtualizedGrid{TItem,TCell}.UpdateItem"/>) while it's still active.</summary>
    void SetContent(TItem item);
}
