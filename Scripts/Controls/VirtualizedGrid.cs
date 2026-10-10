using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DoomArchitect.Controls;

/// <summary>
/// A virtualized, uniform-cell-size grid: items in a conceptually
/// unbounded list get a real Godot cell node
/// (<typeparamref name="TCell"/>) only while actually visible (plus a
/// small buffer), recycled as the user scrolls - never one permanent
/// node per item.
///
/// Godot has no first-party control that does this. The built-ins that
/// scale to thousands of entries (<see cref="ItemList"/>, <see cref="Tree"/>)
/// do so precisely by never giving an item its own child node at all,
/// which rules out a real per-item overlay control; any built-in
/// <see cref="Container"/> that *does* support arbitrary per-item child
/// scenes lays out every one of them permanently, which doesn't scale to
/// a large dataset. Extracted out of <c>TextureBrowserDialog</c>'s own
/// gallery - the first, inline version of exactly this - once it became
/// clear a second consumer (any future large-collection-by-thumbnail
/// picker) would need the identical mechanism.
///
/// Deliberately not itself a <see cref="Control"/>/<see cref="Node"/>:
/// Godot's own node/property/signal system doesn't support open generic
/// types, so a generic *controller* that drives an existing
/// <see cref="ScrollContainer"/>/<see cref="Control"/> pair (rather than
/// trying to BE one) is what lets this stay a real C# generic class at
/// all - a host still owns the actual scene nodes and calls
/// <see cref="Update"/> from its own <c>_Process</c>.
///
/// Only works for a uniform, fixed <c>cellSize</c> - variable-size items
/// would need a materially different design (real per-item layout,
/// which is exactly the thing a built-in <see cref="Container"/>
/// provides for free and this class deliberately gives up in exchange
/// for virtualization).
/// </summary>
public sealed class VirtualizedGrid<TItem, TCell> where TCell : Control, IVirtualizedGridCell<TItem>
{
    private readonly ScrollContainer _scroll;
    private readonly Control _content;
    private readonly PackedScene _cellScene;
    private readonly Vector2 _cellSize;
    private readonly Vector2 _gap;
    private readonly int _visibleRowBuffer;

    /// <summary>Every cell instance ever created, active or not - reused indefinitely rather than freed, so scrolling never re-triggers <see cref="PackedScene.Instantiate"/>.</summary>
    private readonly List<TCell> _pool = new();

    /// <summary>Which pooled cell (if any) currently represents each visible item index.</summary>
    private readonly Dictionary<int, TCell> _activeByIndex = new();

    private readonly List<TItem> _items = new();
    private int _columns = 1;
    private int _selectedIndex = -1;

    /// <summary>Fires when a cell is clicked (including programmatically via <see cref="SelectIndex"/>).</summary>
    public event Action<int, TItem> ItemSelected;

    /// <summary>Fires on a cell double-click.</summary>
    public event Action<int, TItem> ItemActivated;

    public int SelectedIndex => _selectedIndex;
    public int Count => _items.Count;

    /// <summary>The items currently backed by a real, pooled cell - a host's own hook for lazily-resolved content (e.g. an icon that finishes decoding well after an item first scrolled into view): check which of these still need resolving, then call <see cref="UpdateItem"/> once each is ready. Deliberately not in scope for this generic grid itself - "is an item's content fully loaded" is inherently domain-specific.</summary>
    public IEnumerable<(int Index, TItem Item)> ActiveItems => _activeByIndex.Keys.Select(i => (i, _items[i]));

    public VirtualizedGrid(ScrollContainer scroll, Control content, PackedScene cellScene, Vector2 cellSize, Vector2 gap, int visibleRowBuffer = 2)
    {
        _scroll = scroll;
        _content = content;
        _cellScene = cellScene;
        _cellSize = cellSize;
        _gap = gap;
        _visibleRowBuffer = visibleRowBuffer;
    }

    /// <summary>Replaces the full dataset - releases every pooled cell back to the pool, clears selection, scrolls to the top, and repopulates whatever's now visible.</summary>
    public void SetItems(IReadOnlyList<TItem> items)
    {
        _items.Clear();
        _items.AddRange(items);
        _selectedIndex = -1;

        foreach (var cell in _activeByIndex.Values) cell.Visible = false;
        _activeByIndex.Clear();

        RecomputeContentSize();
        _scroll.ScrollVertical = 0;
        UpdateVisibleCells();
    }

    /// <summary>
    /// Updates a single already-known item in place - for a host's own
    /// lazily-resolved content, without the full reset <see cref="SetItems"/>
    /// would otherwise cause (pool release, selection loss, scroll-to-
    /// top). A no-op on the pool itself if <paramref name="index"/> has
    /// no currently active cell - the new value is still recorded, so an
    /// inactive cell picks it up normally whenever it next scrolls into
    /// view.
    /// </summary>
    public void UpdateItem(int index, TItem item)
    {
        if (index < 0 || index >= _items.Count) return;

        _items[index] = item;
        if (_activeByIndex.TryGetValue(index, out var cell)) cell.SetContent(item);
    }

    /// <summary>Call every frame from the host's own <c>_Process</c> - recomputes columns/content size (so a resize re-flows for free) and the set of pooled cells for whatever's now scrolled into view.</summary>
    public void Update()
    {
        RecomputeContentSize();
        UpdateVisibleCells();
    }

    public void SelectIndex(int index)
    {
        if (_activeByIndex.TryGetValue(_selectedIndex, out var previousCell)) previousCell.Selected = false;

        _selectedIndex = index;

        if (index >= 0 && index < _items.Count) ItemSelected?.Invoke(index, _items[index]);
        if (_activeByIndex.TryGetValue(_selectedIndex, out var newCell)) newCell.Selected = true;
    }

    /// <summary>
    /// Scrolls so <paramref name="index"/>'s row sits at the top of the
    /// viewport, then immediately materializes a cell there - unlike
    /// Godot's own "ensure visible" helpers, which need a real child
    /// control already positioned, this index may have no pooled cell at
    /// all yet.
    /// </summary>
    public void ScrollToIndex(int index)
    {
        if (index < 0 || index >= _items.Count) return;

        _scroll.ScrollVertical = (int)((index / _columns) * RowPitch);
        UpdateVisibleCells();
    }

    private float ColumnPitch => _cellSize.X + _gap.X;
    private float RowPitch => _cellSize.Y + _gap.Y;

    private void RecomputeContentSize()
    {
        var availableWidth = Math.Max(_cellSize.X, _scroll.Size.X);
        _columns = Math.Max(1, (int)((availableWidth + _gap.X) / ColumnPitch));

        var rows = _items.Count == 0 ? 0 : (int)Math.Ceiling(_items.Count / (float)_columns);
        var contentSize = new Vector2(_columns * ColumnPitch - _gap.X, Math.Max(0, rows * RowPitch - _gap.Y));
        _content.CustomMinimumSize = contentSize;
        _content.Size = contentSize;
    }

    /// <summary>
    /// Recomputes which item indices are currently in (or near) the
    /// viewport, releases pooled cells that fell out of that range back
    /// to the pool, and assigns/positions a pooled cell for every index
    /// newly in range - bounded by how many cells actually fit on
    /// screen, never by <see cref="Count"/>.
    /// </summary>
    private void UpdateVisibleCells()
    {
        if (_items.Count == 0) return;

        var viewportHeight = _scroll.Size.Y;
        var firstRow = Math.Max(0, (int)(_scroll.ScrollVertical / RowPitch) - _visibleRowBuffer);
        var lastRow = (int)((_scroll.ScrollVertical + viewportHeight) / RowPitch) + _visibleRowBuffer;

        var firstIndex = firstRow * _columns;
        var lastIndex = Math.Min(_items.Count - 1, ((lastRow + 1) * _columns) - 1);

        foreach (var staleIndex in _activeByIndex.Keys.Where(i => i < firstIndex || i > lastIndex).ToList())
        {
            _activeByIndex[staleIndex].Visible = false;
            _activeByIndex.Remove(staleIndex);
        }

        for (var index = firstIndex; index <= lastIndex; index++)
        {
            if (!_activeByIndex.TryGetValue(index, out var cell))
            {
                cell = RentCell();
                cell.LogicalIndex = index;
                cell.Visible = true;
                cell.SetContent(_items[index]);
                cell.Selected = index == _selectedIndex;

                _activeByIndex[index] = cell;
            }

            // Refreshed every call, not just on first assignment - a
            // resize mid-session can change _columns, which changes
            // where an already-active index belongs without it ever
            // leaving the active set.
            cell.Position = new Vector2((index % _columns) * ColumnPitch, (index / _columns) * RowPitch);
        }
    }

    /// <summary>An invisible, currently-unassigned pool entry if one exists, otherwise a freshly instantiated cell (its events wired exactly once, for life - see <see cref="IVirtualizedGridCell{TItem}.LogicalIndex"/>).</summary>
    private TCell RentCell()
    {
        var free = _pool.FirstOrDefault(c => !c.Visible);
        if (free != null) return free;

        var cell = _cellScene.Instantiate<TCell>();
        _content.AddChild(cell);
        cell.Pressed += () => SelectIndex(cell.LogicalIndex);
        cell.Activated += () => ItemActivated?.Invoke(cell.LogicalIndex, _items[cell.LogicalIndex]);
        _pool.Add(cell);
        return cell;
    }
}
