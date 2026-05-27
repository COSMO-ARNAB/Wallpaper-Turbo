using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using WallpaperTurbo.UI.Services;

namespace WallpaperTurbo.UI.Controls;

/// <summary>
/// A true virtualizing wrap panel that ONLY materializes containers for visible items.
///
/// Unlike standard WrapPanel which eagerly renders all children (breaking scalability
/// at 20+ wallpapers), this panel extends VirtualizingPanel and implements IScrollInfo
/// so WPF's container recycling infrastructure actually works.
///
/// Uniform item size (ItemWidth x ItemHeight) enables O(1) visible range calculation:
///   itemsPerRow = floor(viewportW / ItemWidth)
///   firstRow = floor(scrollY / ItemHeight)
///   lastRow  = ceil((scrollY + viewportH) / ItemHeight)
///
/// Set ItemWidth=228 and ItemHeight=158 to match card Width=220 + Margin=8.
/// </summary>
public class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
{
    // ─────────────────────────────────────────────────────
    // Dependency Properties (uniform item size)
    // ─────────────────────────────────────────────────────

    public static readonly DependencyProperty ItemWidthProperty = DependencyProperty.Register(
        nameof(ItemWidth), typeof(double), typeof(VirtualizingWrapPanel),
        new FrameworkPropertyMetadata(228.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ItemHeightProperty = DependencyProperty.Register(
        nameof(ItemHeight), typeof(double), typeof(VirtualizingWrapPanel),
        new FrameworkPropertyMetadata(158.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double ItemWidth  { get => (double)GetValue(ItemWidthProperty);  set => SetValue(ItemWidthProperty,  value); }
    public double ItemHeight { get => (double)GetValue(ItemHeightProperty); set => SetValue(ItemHeightProperty, value); }

    // ─────────────────────────────────────────────────────
    // IScrollInfo state
    // ─────────────────────────────────────────────────────

    private ScrollViewer? _scrollOwner;
    private bool _canHorizontallyScroll;
    private bool _canVerticallyScroll;
    private Size _extent   = Size.Empty;
    private Size _viewport = Size.Empty;
    private Point _offset  = new(0, 0);

    // Layout cache (updated each MeasureOverride)
    private int _itemsPerRow        = 1;
    private int _totalRows          = 0;
    private int _firstRealizedIndex = 0;
    private int _lastRealizedIndex  = -1;

    // ─────────────────────────────────────────────────────
    // IScrollInfo
    // ─────────────────────────────────────────────────────

    public ScrollViewer? ScrollOwner            { get => _scrollOwner; set => _scrollOwner = value; }
    public bool CanHorizontallyScroll           { get => _canHorizontallyScroll; set => _canHorizontallyScroll = value; }
    public bool CanVerticallyScroll             { get => _canVerticallyScroll;   set => _canVerticallyScroll   = value; }
    public double ExtentWidth                   => _extent.Width;
    public double ExtentHeight                  => _extent.Height;
    public double ViewportWidth                 => _viewport.Width;
    public double ViewportHeight                => _viewport.Height;
    public double HorizontalOffset              => _offset.X;
    public double VerticalOffset                => _offset.Y;

    public void LineUp()           => SetVerticalOffset(VerticalOffset - 48);
    public void LineDown()         => SetVerticalOffset(VerticalOffset + 48);
    public void LineLeft()         { }
    public void LineRight()        { }
    public void PageUp()           => SetVerticalOffset(VerticalOffset - _viewport.Height);
    public void PageDown()         => SetVerticalOffset(VerticalOffset + _viewport.Height);
    public void PageLeft()         { }
    public void PageRight()        { }
    public void MouseWheelUp()     => SetVerticalOffset(VerticalOffset - SystemParameters.WheelScrollLines * 48);
    public void MouseWheelDown()   => SetVerticalOffset(VerticalOffset + SystemParameters.WheelScrollLines * 48);
    public void MouseWheelLeft()   { }
    public void MouseWheelRight()  { }
    public void SetHorizontalOffset(double offset) { }

    public void SetVerticalOffset(double offset)
    {
        double maxOffset = Math.Max(0, _extent.Height - _viewport.Height);
        double clamped   = Math.Max(0, Math.Min(offset, maxOffset));
        if (Math.Abs(clamped - _offset.Y) < 0.5) return;
        _offset = new Point(0, clamped);
        _scrollOwner?.InvalidateScrollInfo();
        InvalidateMeasure();
    }

    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            if (InternalChildren[i] == visual || (InternalChildren[i] is Visual v && v.IsAncestorOf(visual)))
            {
                int idx = _firstRealizedIndex + i;
                int row = idx / Math.Max(1, _itemsPerRow);
                double top    = row * ItemHeight;
                double bottom = top + ItemHeight;
                if (top < _offset.Y)                         SetVerticalOffset(top);
                else if (bottom > _offset.Y + _viewport.Height) SetVerticalOffset(bottom - _viewport.Height);
                break;
            }
        }
        return rectangle;
    }

    // ─────────────────────────────────────────────────────
    // ItemsChanged: collection add/remove/reset
    // ─────────────────────────────────────────────────────

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        base.OnItemsChanged(sender, args);
        InvalidateMeasure();
    }

    // ─────────────────────────────────────────────────────
    // Helpers: get owner ItemsControl and item count
    // ─────────────────────────────────────────────────────

    private ItemsControl? GetOwner() => ItemsControl.GetItemsOwner(this);

    private int GetItemCount()
    {
        var owner = GetOwner();
        return owner?.Items.Count ?? 0;
    }

    // ─────────────────────────────────────────────────────
    // MeasureOverride: core virtualization
    // ─────────────────────────────────────────────────────

    protected override Size MeasureOverride(Size availableSize)
    {
        int itemCount = GetItemCount();

        double iw = ItemWidth;
        double ih = ItemHeight;

        // Guard infinite / zero width (can happen during initial layout pass)
        double viewW = double.IsInfinity(availableSize.Width)  ? 900.0 : Math.Max(iw, availableSize.Width);
        double viewH = double.IsInfinity(availableSize.Height) ? 600.0 : availableSize.Height;

        _itemsPerRow = Math.Max(1, (int)Math.Floor(viewW / iw));
        _totalRows   = itemCount == 0 ? 0 : (itemCount + _itemsPerRow - 1) / _itemsPerRow;

        double totalHeight = _totalRows * ih;
        var newExtent   = new Size(viewW, totalHeight);
        var newViewport = new Size(viewW, viewH);

        bool changed = newExtent != _extent || newViewport != _viewport;
        _extent  = newExtent;
        _viewport = newViewport;
        if (changed) _scrollOwner?.InvalidateScrollInfo();

        // Clamp scroll after extent change
        double maxOff = Math.Max(0, _extent.Height - _viewport.Height);
        if (_offset.Y > maxOff)
        {
            _offset = new Point(0, maxOff);
            _scrollOwner?.InvalidateScrollInfo();
        }

        if (itemCount == 0)
        {
            RecycleAllContainers();
            return new Size(viewW, viewH);
        }

        // Visible row range with 1-row buffer for smooth scrolling
        int firstRow = Math.Max(0, (int)Math.Floor(_offset.Y / ih) - 1);
        int lastRow  = Math.Min(_totalRows - 1, (int)Math.Ceiling((_offset.Y + viewH) / ih));

        int firstIdx = firstRow * _itemsPerRow;
        int lastIdx  = Math.Min(itemCount - 1, (lastRow + 1) * _itemsPerRow - 1);

        if (DebugFlags.SafeDebugMode && !DebugFlags.EnableVirtualization)
        {
            firstIdx = 0;
            lastIdx = itemCount - 1;
        }

        _firstRealizedIndex = firstIdx;
        _lastRealizedIndex  = lastIdx;

        Debug.WriteLine($"[VWP] Visible [{firstIdx}..{lastIdx}] of {itemCount} ({_itemsPerRow}/row, scroll={_offset.Y:F0})");

        var generator = (IRecyclingItemContainerGenerator)ItemContainerGenerator;

        // Recycle out-of-range containers first (makes them available for reuse)
        RecycleOutOfRangeContainers(firstIdx, lastIdx);

        // Generate containers for visible range
        var startPos = ItemContainerGenerator.GeneratorPositionFromIndex(firstIdx);
        int childIndex = (startPos.Offset == 0) ? startPos.Index : startPos.Index + 1;

        using (generator.StartAt(startPos, GeneratorDirection.Forward, true))
        {
            for (int i = firstIdx; i <= lastIdx; i++, childIndex++)
            {
                var child = (UIElement)generator.GenerateNext(out bool isNew);
                if (isNew)
                {
                    if (childIndex >= InternalChildren.Count)
                        AddInternalChild(child);
                    else
                        InsertInternalChild(childIndex, child);
                    generator.PrepareItemContainer(child);
                }
                child.Measure(new Size(iw, ih));
            }
        }

        // Report active container count to diagnostics
        DiagnosticsService.ResetContainerCount(InternalChildren.Count);

        return new Size(viewW, viewH);
    }

    // ─────────────────────────────────────────────────────
    // ArrangeOverride: position realized containers
    // ─────────────────────────────────────────────────────

    protected override Size ArrangeOverride(Size finalSize)
    {
        int itemCount = GetItemCount();
        double iw = ItemWidth;
        double ih = ItemHeight;

        for (int ci = 0; ci < InternalChildren.Count; ci++)
        {
            var child  = InternalChildren[ci];
            var genPos = new GeneratorPosition(ci, 0);
            int index  = ItemContainerGenerator.IndexFromGeneratorPosition(genPos);

            if (index < 0 || index >= itemCount)
            {
                child.Arrange(new Rect(0, -10000, iw, ih)); // hide orphans off-screen
                continue;
            }

            int row = index / _itemsPerRow;
            int col = index % _itemsPerRow;
            child.Arrange(new Rect(col * iw, row * ih - _offset.Y, iw, ih));
        }

        return finalSize;
    }

    // ─────────────────────────────────────────────────────
    // BringIndexIntoView: scroll to a specific item index
    // ─────────────────────────────────────────────────────

    protected override void BringIndexIntoView(int index)
    {
        if (index < 0 || index >= GetItemCount()) return;
        int row = index / Math.Max(1, _itemsPerRow);
        SetVerticalOffset(row * ItemHeight);
    }

    // ─────────────────────────────────────────────────────
    // Container recycling helpers
    // ─────────────────────────────────────────────────────

    private void RecycleOutOfRangeContainers(int firstIdx, int lastIdx)
    {
        var generator = (IRecyclingItemContainerGenerator)ItemContainerGenerator;

        // Cache zone: keep 2 extra rows above/below the visible range pre-loaded
        // to avoid reload flicker during fast scrolling. Items beyond that get their
        // bitmap evicted to free VRAM on large libraries.
        int evictBuffer = _itemsPerRow * 2; // 2 rows of bitmaps kept beyond cache
        int evictBefore = firstIdx - evictBuffer;
        int evictAfter  = lastIdx  + evictBuffer;

        for (int ci = InternalChildren.Count - 1; ci >= 0; ci--)
        {
            var pos   = new GeneratorPosition(ci, 0);
            int index = ItemContainerGenerator.IndexFromGeneratorPosition(pos);

            if (index < firstIdx || index > lastIdx)
            {
                // Evict bitmap for items far outside cache zone to free VRAM
                if (index < evictBefore || index > evictAfter)
                {
                    var fe = InternalChildren[ci] as System.Windows.FrameworkElement;
                    if (fe?.DataContext is WallpaperEntry wp)
                    {
                        wp.EvictThumbnail();
                    }
                }
                generator.Recycle(pos, 1);
                RemoveInternalChildRange(ci, 1);
            }
        }
    }

    private void RecycleAllContainers()
    {
        if (InternalChildren.Count == 0) return;
        var generator = (IRecyclingItemContainerGenerator)ItemContainerGenerator;
        generator.Recycle(new GeneratorPosition(0, 0), InternalChildren.Count);
        RemoveInternalChildRange(0, InternalChildren.Count);
    }
}
