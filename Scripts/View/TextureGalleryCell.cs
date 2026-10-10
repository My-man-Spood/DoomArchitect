using System;
using DoomArchitect.Controls;
using DoomArchitect.Rendering;
using Godot;

/// <summary>The one texture name + its already-decoded icon (null until resolved) - <see cref="TextureBrowserDialog"/>'s own item type for its <see cref="VirtualizedGrid{TItem,TCell}"/>.</summary>
public readonly record struct TextureGalleryItem(string Name, Texture2D Icon);

/// <summary>
/// One cell in <see cref="TextureBrowserDialog"/>'s gallery: a thumbnail
/// with a real corner-overlay size badge - the exact same node layout and
/// styling as <see cref="TexturePreviewEdit"/>'s own corner label (minus
/// the editable name field; this is a picker cell, not a property editor)
/// - plus a plain name caption below.
///
/// Replaces an earlier attempt that baked the size text directly into
/// each icon's own pixels to work around <see cref="ItemList"/> having no
/// per-item overlay slot at all - that looked wrong (the badge's apparent
/// size varied with the source texture's own native resolution, and the
/// text got clipped outright on thin textures like door tracks) and was
/// scrapped outright rather than patched.
///
/// Implements <see cref="IVirtualizedGridCell{TItem}"/> so
/// <see cref="TextureBrowserDialog"/> can drive its gallery through the
/// shared, reusable <see cref="VirtualizedGrid{TItem,TCell}"/> - only
/// however many of these fit the visible scroll viewport (plus a short
/// buffer) ever exist at once, recycled as the user scrolls, rather than
/// one permanent instance per texture name: a real resource set can hold
/// thousands of names, and a plain container laying out that many real
/// child controls at once (most never even scrolled into view) was the
/// actual cause of a real, reported performance regression from the
/// first version of this gallery, before virtualization existed at all.
/// </summary>
public partial class TextureGalleryCell : PanelContainer, IVirtualizedGridCell<TextureGalleryItem>
{
    private static readonly StyleBoxFlat NormalStyle = new() { BgColor = new Color(0, 0, 0, 0) };

    private static readonly StyleBoxFlat SelectedStyle = new()
    {
        BgColor = new Color(0.3f, 0.5f, 0.8f, 0.35f),
        BorderColor = new Color(0.45f, 0.65f, 1f, 1f),
        BorderWidthLeft = 2,
        BorderWidthTop = 2,
        BorderWidthRight = 2,
        BorderWidthBottom = 2,
        CornerRadiusTopLeft = 3,
        CornerRadiusTopRight = 3,
        CornerRadiusBottomRight = 3,
        CornerRadiusBottomLeft = 3,
    };

    private TextureRect _preview;
    private PanelContainer _sizeBox;
    private Label _sizeLabel;
    private Label _nameLabel;
    private bool _selected;

    /// <summary>
    /// Which entry in the host's own displayed list this (recycled, pooled)
    /// cell currently represents - set by the host every time it reassigns
    /// a pooled instance to a different index. <see cref="Pressed"/>/
    /// <see cref="Activated"/> are each subscribed exactly once per cell
    /// instance (at creation, never re-subscribed on reuse) specifically
    /// so a host's handler can read this field at invocation time instead
    /// of closing over an index that would go stale the moment the cell
    /// is recycled for a different entry.
    /// </summary>
    public int LogicalIndex { get; set; } = -1;

    public bool Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            AddThemeStyleboxOverride("panel", value ? SelectedStyle : NormalStyle);
        }
    }

    /// <summary>A single left click anywhere in the cell.</summary>
    public event Action Pressed;

    /// <summary>A double-click (or the equivalent activation) anywhere in the cell.</summary>
    public event Action Activated;

    public override void _Ready()
    {
        _preview = GetNode<TextureRect>("Margin/Content/Preview");
        _sizeBox = GetNode<PanelContainer>("Margin/Content/Preview/SizeBox");
        _sizeLabel = GetNode<Label>("Margin/Content/Preview/SizeBox/SizeLabel");
        _nameLabel = GetNode<Label>("Margin/Content/NameLabel");

        AddThemeStyleboxOverride("panel", NormalStyle);
    }

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true, DoubleClick: true }:
                Activated?.Invoke();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }:
                Pressed?.Invoke();
                break;
        }
    }

    /// <summary>
    /// Renders the given already-decoded texture (or a shared placeholder
    /// for null, matching every other texture preview in this project)
    /// and updates the corner size label from that texture's own real
    /// pixel dimensions - never baked into the image, never guessed.
    /// Resizes <see cref="_sizeBox"/> to its own real computed minimum
    /// size every time, same as <see cref="TexturePreviewEdit.SetPreviewTexture"/> -
    /// it has no parent <see cref="Container"/> of its own (its parent,
    /// <see cref="_preview"/>, is a plain <see cref="TextureRect"/>) to
    /// auto-size it to its own content, and the shown text's length
    /// varies ("8x8" vs. "256x256").
    /// </summary>
    public void SetContent(string name, Texture2D icon)
    {
        _nameLabel.Text = name;
        _preview.Texture = icon ?? PlaceholderIcon.Instance;
        _sizeBox.Visible = icon != null;

        if (icon == null) return;

        _sizeLabel.Text = $"{icon.GetWidth()}x{icon.GetHeight()}";
        _sizeBox.Size = _sizeBox.GetCombinedMinimumSize();
    }

    /// <summary><see cref="IVirtualizedGridCell{TItem}"/>'s own entry point - delegates straight to <see cref="SetContent(string,Texture2D)"/>, kept as its own public two-argument overload since that's the natural call for anything constructing/testing a cell directly rather than through a <see cref="VirtualizedGrid{TItem,TCell}"/>.</summary>
    public void SetContent(TextureGalleryItem item) => SetContent(item.Name, item.Icon);
}
