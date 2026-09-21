using System;
using DoomArchitect.Core.Map;
using Godot;
using MapVector2 = System.Numerics.Vector2;

/// <summary>
/// The left-button marquee/box-select state machine, shared across all
/// four edit modes (there's only ever one marquee in flight regardless of
/// which mode is active) - split out of <see cref="MapOverlay"/> during
/// its "growing god-object" cleanup. Each of the four
/// <see cref="ElementOverlayHandler{TSelectable,TDraggable}"/> instances
/// drives the exact same shared instance of this class rather than owning
/// one each, so a marquee begun in one mode can't somehow coexist with a
/// second one in another (switching modes mid-drag isn't possible in this
/// project's own input model, but sharing one instance makes that true by
/// construction rather than by convention).
/// </summary>
public sealed class MarqueeSelector
{
	private const float MarqueeStartThresholdPixels = 2f; // matches UDB's own MouseSelectionThreshold default
	private const float MarqueeMinSize = 0.1f; // matches UDB's own selectionvolume guard

	/// <summary>One color per <see cref="MarqueeSelectionMode"/>, matching which combine mode the current marquee drag would apply on release (see <see cref="GetSelectionMode"/>) - this project's own color choices, not a port of UDB's actual theme values.</summary>
	private static readonly Color SelectColor = new(0.9f, 0.9f, 0.9f);
	private static readonly Color AddColor = new(0.3f, 0.9f, 0.3f);
	private static readonly Color SubtractColor = new(0.9f, 0.3f, 0.3f);
	private static readonly Color IntersectColor = new(0.6f, 0.4f, 0.9f);

	private readonly MapOverlayCamera _camera;

	private Vector2 _selectPressScreen;
	private MapVector2 _selectStartMap;
	private MapVector2 _selectEndMap;

	public MarqueeSelector(MapOverlayCamera camera)
	{
		_camera = camera;
	}

	public bool IsSelecting { get; private set; }

	/// <summary>Ported from UDB's real <c>BaseClassicMode.GetMultiSelectionMode</c> - the two modifier roles are real, independently rebindable actions (<c>marquee_subtract_modifier</c>/<c>marquee_add_modifier</c>).</summary>
	public static MarqueeSelectionMode GetSelectionMode()
	{
		var ctrl = Input.IsActionPressed("marquee_subtract_modifier");
		var shift = Input.IsActionPressed("marquee_add_modifier");
		if (ctrl && shift) return MarqueeSelectionMode.Intersect;
		if (ctrl) return MarqueeSelectionMode.Subtract;
		if (shift) return MarqueeSelectionMode.Add;
		return MarqueeSelectionMode.Select;
	}

	/// <summary>Left-button press: record where a marquee would start from, without yet deciding whether this becomes a click or a drag.</summary>
	public void BeginOrClick(Vector2 screenPosition)
	{
		_selectPressScreen = screenPosition;
		_selectStartMap = _camera.Unproject(screenPosition);
		_selectEndMap = _selectStartMap;
		IsSelecting = false;
	}

	/// <summary>
	/// Call on every left-button-held motion event. Crosses into
	/// "selecting" once past <see cref="MarqueeStartThresholdPixels"/> and
	/// keeps the live rectangle updated from then on. Returns whether a
	/// marquee is (now) in progress, so the caller knows not to also
	/// update hover this frame - matches the existing "hover freezes
	/// during a drag" convention already used for right-button move-drags.
	/// </summary>
	public bool Update(Vector2 screenPosition)
	{
		if (!IsSelecting && _selectPressScreen.DistanceTo(screenPosition) > MarqueeStartThresholdPixels)
		{
			IsSelecting = true;
		}

		if (!IsSelecting) return false;

		_selectEndMap = _camera.Unproject(screenPosition);
		return true;
	}

	/// <summary>Left-button release while a marquee was in progress: applies the combine mode over the final rectangle, unless it's too small to have been a real drag (matches UDB's own <c>selectionvolume</c> guard).</summary>
	public void End(Action<MapVector2, MapVector2> applySelection)
	{
		var min = MapVector2.Min(_selectStartMap, _selectEndMap);
		var max = MapVector2.Max(_selectStartMap, _selectEndMap);
		if (max.X - min.X > MarqueeMinSize && max.Y - min.Y > MarqueeMinSize)
		{
			applySelection(min, max);
		}

		IsSelecting = false;
	}

	/// <summary>
	/// The live marquee rectangle while a left-button drag is in progress
	/// - an unfilled outline, matching UDB's own real
	/// <c>ClassicMode.RenderMultiSelection</c> (a border-only rectangle,
	/// not a filled one). Color reflects whichever combine mode would
	/// apply if released right now, so the modifier-key feedback is live.
	/// </summary>
	public void Draw(CanvasItem target)
	{
		if (!IsSelecting) return;

		var color = GetSelectionMode() switch
		{
			MarqueeSelectionMode.Add => AddColor,
			MarqueeSelectionMode.Subtract => SubtractColor,
			MarqueeSelectionMode.Intersect => IntersectColor,
			_ => SelectColor,
		};

		var a = _camera.Project(_selectStartMap);
		var b = _camera.Project(_selectEndMap);
		var min = new Vector2(Mathf.Min(a.X, b.X), Mathf.Min(a.Y, b.Y));
		var max = new Vector2(Mathf.Max(a.X, b.X), Mathf.Max(a.Y, b.Y));
		target.DrawRect(new Rect2(min, max - min), color, false, 2f);
	}
}
