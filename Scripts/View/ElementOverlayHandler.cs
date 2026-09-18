using System;
using System.Collections.Generic;
using System.Linq;
using DoomArchitect.Core.Map;
using DoomArchitect.Core.Undo;
using Godot;
using MapVector2 = System.Numerics.Vector2;

/// <summary>
/// The left-click-select/marquee + right-click-drag input state machine
/// every one of the 2D view's four edit modes follows identically - UDB's
/// own real per-mode button split (pressing on an unselected element
/// replaces the selection with just that one before dragging it, pressing
/// on an already-selected element drags the entire current selection
/// together; left-click only ever selects, right-click-drag is the only
/// thing that moves geometry). Extracted here as one generic engine during
/// <see cref="MapOverlay"/>'s "growing god-object" cleanup, once reading
/// all four of its original <c>Handle*Input</c> methods side by side
/// confirmed they really were the identical skeleton with only the
/// following genuinely varying by mode - each supplied as a constructor
/// delegate rather than a virtual method, since there's no reason for a
/// per-mode subclass when a handful of delegates already captures every
/// real difference:
/// <list type="bullet">
/// <item>which element type is hit-tested/selected (<typeparamref name="TSelectable"/> -
/// Vertex/Linedef/Sector/Thing) vs. which element type actually has a
/// position and gets dragged (<typeparamref name="TDraggable"/> - always
/// Vertex for Linedef/Sector mode, since a linedef/sector has no position
/// of its own; the same type as <typeparamref name="TSelectable"/> for
/// Vertex/Thing mode, which drag themselves directly)</item>
/// <item>the actual <see cref="MapData"/> select/marquee-select/clear
/// calls (different method names, and Linedef/Sector's own real
/// "touching" marquee option Vertex/Thing don't have)</item>
/// <item>how a drag is actually applied and turned into an undo command
/// (<c>MapData.MoveVertex</c>/<c>MoveVertexCommand</c> for every
/// vertex-backed mode, <c>MapData.MoveThing</c>/<c>MoveThingCommand</c>
/// for Thing)</item>
/// <item>whether a double-click even exists for this mode at all (Vertex
/// mode has none - UDB has no vertex property dialog to open) and, when
/// it does, which <see cref="MapOverlay"/> event it fires</item>
/// </list>
/// Every other line of logic - the exact case-by-case shape of
/// <see cref="HandleInput"/> below - is a direct, unmodified port of what
/// was independently duplicated four times before this extraction.
/// </summary>
public sealed class ElementOverlayHandler<TSelectable, TDraggable>
	where TSelectable : class
	where TDraggable : class
{
	private readonly MapOverlayCamera _camera;
	private readonly MarqueeSelector _marquee;
	private readonly Func<UndoStack> _getUndoStack;
	private readonly Func<MapVector2, MapVector2> _snap;

	private readonly Func<Vector2, TSelectable> _findNear;
	private readonly Func<TSelectable, bool> _isSelected;
	private readonly Action<TSelectable> _selectOnly;
	private readonly Action<TSelectable> _toggleSelect;
	private readonly Action _clearSelected;
	private readonly Func<IEnumerable<TDraggable>> _getSelectedDraggables;
	private readonly Func<TDraggable, MapVector2> _getPosition;
	private readonly Action<TDraggable, MapVector2> _setPosition;
	private readonly Func<TDraggable, MapVector2, MapVector2, ICommand> _makeMoveCommand;
	private readonly Action<MapVector2, MapVector2, MarqueeSelectionMode> _marqueeSelect;
	private readonly Action<TSelectable> _onDoubleClick;
	private readonly Action<Vector2> _onEmptyRightClick;

	private MapVector2 _dragOrigin;
	private Dictionary<TDraggable, MapVector2> _dragStart;

	public TSelectable Hovered { get; private set; }

	public ElementOverlayHandler(
		MapOverlayCamera camera, MarqueeSelector marquee, Func<UndoStack> getUndoStack, Func<MapVector2, MapVector2> snap,
		Func<Vector2, TSelectable> findNear, Func<TSelectable, bool> isSelected,
		Action<TSelectable> selectOnly, Action<TSelectable> toggleSelect, Action clearSelected,
		Func<IEnumerable<TDraggable>> getSelectedDraggables, Func<TDraggable, MapVector2> getPosition,
		Action<TDraggable, MapVector2> setPosition, Func<TDraggable, MapVector2, MapVector2, ICommand> makeMoveCommand,
		Action<MapVector2, MapVector2, MarqueeSelectionMode> marqueeSelect, Action<TSelectable> onDoubleClick,
		Action<Vector2> onEmptyRightClick = null)
	{
		_camera = camera;
		_marquee = marquee;
		_getUndoStack = getUndoStack;
		_snap = snap;
		_findNear = findNear;
		_isSelected = isSelected;
		_selectOnly = selectOnly;
		_toggleSelect = toggleSelect;
		_clearSelected = clearSelected;
		_getSelectedDraggables = getSelectedDraggables;
		_getPosition = getPosition;
		_setPosition = setPosition;
		_makeMoveCommand = makeMoveCommand;
		_marqueeSelect = marqueeSelect;
		_onDoubleClick = onDoubleClick;
		_onEmptyRightClick = onEmptyRightClick;
	}

	public void HandleInput(InputEvent @event)
	{
		switch (@event)
		{
			case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true, DoubleClick: true } doubleClick when _onDoubleClick != null:
				var doubleClickTarget = _findNear(doubleClick.Position);
				if (doubleClickTarget != null)
				{
					if (!_isSelected(doubleClickTarget)) _selectOnly(doubleClickTarget);
					_onDoubleClick(doubleClickTarget);
				}

				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press:
				_marquee.BeginOrClick(press.Position);
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
				if (_marquee.IsSelecting)
				{
					_marquee.End((min, max) => _marqueeSelect(min, max, MarqueeSelector.GetSelectionMode()));
				}
				else if (Hovered != null) _toggleSelect(Hovered);
				else _clearSelected();

				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } press:
				var target = _findNear(press.Position);
				Hovered = target;
				if (target != null)
				{
					if (!_isSelected(target)) _selectOnly(target);
					_dragOrigin = _snap(_camera.Unproject(press.Position));
					_dragStart = _getSelectedDraggables().ToDictionary(d => d, _getPosition);
				}
				else if (_onEmptyRightClick != null && !_marquee.IsSelecting)
				{
					// UDB's own real "AutoDrawOnEdit": right-clicking empty
					// space (nothing under the cursor to select/edit) starts
					// Draw mode instead, with the first point already placed
					// right here - not while a marquee drag is in progress,
					// matching UDB's own identical guard.
					_onEmptyRightClick(press.Position);
				}

				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: false }:
				if (_dragStart != null)
				{
					if (_dragStart.Any(kvp => _getPosition(kvp.Key) != kvp.Value))
					{
						var commands = _dragStart
							.Select(kvp => _makeMoveCommand(kvp.Key, kvp.Value, _getPosition(kvp.Key)))
							.ToList();
						_getUndoStack().Record(new CommandGroup(commands));
					}

					_dragStart = null;
				}

				break;
			case InputEventMouseMotion motion when _dragStart != null:
				var delta = _snap(_camera.Unproject(motion.Position)) - _dragOrigin;
				foreach (var (draggable, startPosition) in _dragStart)
				{
					_setPosition(draggable, startPosition + delta);
				}

				break;
			case InputEventMouseMotion motion when motion.ButtonMask.HasFlag(MouseButtonMask.Left):
				if (_marquee.Update(motion.Position)) break;
				Hovered = _findNear(motion.Position);
				break;
			case InputEventMouseMotion motion:
				Hovered = _findNear(motion.Position);
				break;
		}
	}
}
