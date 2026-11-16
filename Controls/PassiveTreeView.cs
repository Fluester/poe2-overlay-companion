using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Poe2OverlayCompanion.Localization;
using Poe2OverlayCompanion.Models;
using Poe2OverlayCompanion.Services;

namespace Poe2OverlayCompanion.Controls;

/// <summary>
/// Mobalytics-подібний 2D-переглядач дерева пасивок PoE 2:
/// - O(1) просторова сітка ховеру (spatial grid)
/// - BFS найкоротший шлях при наведенні ("+N очків") із підсвіткою на дереві
/// - окремий шар міток (ліниве перемальовування тексту)
/// - фільтр Weapon Set 1 / Set 2 та незмінна камера при кроках і пошуку.
/// </summary>
public sealed class PassiveTreeView : FrameworkElement
{
    private const double CellSize = 420.0;

    private readonly VisualCollection _visuals;
    private readonly DrawingVisual _bgVisual = new();
    private readonly DrawingVisual _fgVisual = new();
    private readonly DrawingVisual _previewVisual = new();
    private readonly DrawingVisual _labelVisual = new();

    private PassiveTreeIndex? _index;
    private Dictionary<(int gx, int gy), List<PassiveNodeInfo>>? _spatialGrid;
    private PassiveNodeInfo[] _nodesArr = [];
    private Dictionary<string, int> _numToIdx = new(StringComparer.OrdinalIgnoreCase);
    private int[][] _adj = [];
    private int[] _bfsStampArr = [];
    private int[] _bfsParentArr = [];
    private int[] _bfsDepthArr = [];
    private int[] _bfsQueueArr = [];
    private int _bfsStamp = 1;

    private PassiveDiffResult? _currentDiff;
    private Dictionary<string, PassiveStepEntry> _stepByNumId = new(StringComparer.OrdinalIgnoreCase);

    private string? _focusedNodeId;
    private string? _hoveredNodeId;
    private int _cachedHoverPathCost;
    private Point _lastTooltipPos = new(-999, -999);
    private int? _weaponSetFilter; // null = All, 1 = Set 1, 2 = Set 2
    private bool _hasFittedOnce;
    private double _scale = 0.035;
    private double _offsetX = 220;
    private double _offsetY = 170;
    private int _lastLabelTier = -1;
    private bool _dragging;
    private Point _dragStart;
    private Point _mouseDownStart;
    private bool _bgBuilt;

    public event Action<PassiveNodeInfo, int>? NodeClicked;

    private readonly Popup _tooltipPopup;
    private readonly TextBlock _ttTitle;
    private readonly TextBlock _ttMeta;
    private readonly TextBlock _ttStats;

    private static readonly Pen BgEdgePen = FreezePen(0x2B, 0x35, 0x48, 16);
    private static readonly SolidColorBrush BgNodeBrush = FreezeBrush(0x3B, 0x46, 0x5E);
    private static readonly SolidColorBrush BgNotableBrush = FreezeBrush(0x52, 0x60, 0x7A);

    private static readonly Pen KeepGlowPen = FreezePen(0x58, 0x45, 0x22, 85);
    private static readonly Pen KeepEdgePen = FreezePen(0xE0, 0xBC, 0x6D, 36);
    private static readonly Pen AddedEdgePen = FreezePen(0x4E, 0xCF, 0x8F, 44);
    private static readonly Pen RemovedEdgePen = FreezePen(0xEA, 0x61, 0x61, 30);
    private static readonly Pen PreviewPathPen = FreezePen(0x69, 0xAC, 0xEC, 42);
    private static readonly Pen PreviewGlowPen = FreezePen(0x22, 0x44, 0x6B, 92);

    private static readonly SolidColorBrush KeepNodeBrush = FreezeBrush(0xDF, 0xB8, 0x6C);
    private static readonly SolidColorBrush AddedNodeBrush = FreezeBrush(0x4E, 0xCF, 0x8F);
    private static readonly SolidColorBrush RemovedNodeBrush = FreezeBrush(0xEA, 0x61, 0x61);
    private static readonly SolidColorBrush PreviewNodeBrush = FreezeBrush(0x69, 0xAC, 0xEC);

    private static readonly Pen NodeOutlinePen = FreezePen(0x0B, 0x0D, 0x11, 20);
    private static readonly Pen KeystoneRingPen = FreezePen(0xF5, 0xDC, 0x99, 28);
    private static readonly Pen AscendancyRingPen = FreezePen(0xA6, 0x7E, 0xF5, 24);
    private static readonly Pen WeaponSet1Pen = FreezePen(0x69, 0xAC, 0xEC, 24);
    private static readonly Pen WeaponSet2Pen = FreezePen(0xC4, 0x7A, 0xF5, 24);
    private static readonly Pen FocusOuterPen = FreezePen(0x69, 0xAC, 0xEC, 38);
    private static readonly Pen FocusInnerPen = FreezePen(0xFF, 0xFF, 0xFF, 20);

    private static readonly Typeface LabelTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly SolidColorBrush LabelTextBrush = FreezeBrush(0xF5, 0xF7, 0xFA);
    private static readonly SolidColorBrush LabelBgBrush = FreezeBrush(0x10, 0x14, 0x1E);
    private static readonly Pen LabelBgPen = FreezePen(0xC4, 0x9E, 0x57, 14);

    public PassiveTreeView()
    {
        ClipToBounds = true;
        Focusable = false;
        _visuals = new VisualCollection(this) { _bgVisual, _fgVisual, _previewVisual, _labelVisual };

        _ttTitle = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 12, Foreground = UiBrushes.FgBrass, TextWrapping = TextWrapping.Wrap };
        _ttMeta = new TextBlock { FontSize = 10.5, Margin = new Thickness(0, 2, 0, 4), TextWrapping = TextWrapping.Wrap };
        _ttStats = new TextBlock { FontSize = 11, Foreground = UiBrushes.FgPrimary, TextWrapping = TextWrapping.Wrap, LineHeight = 16 };

        var ttStack = new StackPanel { MaxWidth = 300 };
        ttStack.Children.Add(_ttTitle);
        ttStack.Children.Add(_ttMeta);
        ttStack.Children.Add(_ttStats);

        _tooltipPopup = new Popup
        {
            AllowsTransparency = false,
            IsHitTestVisible = false,
            Placement = PlacementMode.Relative,
            PlacementTarget = this,
            Child = new Border
            {
                Background = FreezeBrush(0x11, 0x13, 0x18),
                BorderBrush = UiBrushes.BorderBrass,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 7, 10, 7),
                Child = ttStack
            }
        };

        SizeChanged += (_, _) =>
        {
            if (!_hasFittedOnce && _currentDiff != null && ActualWidth > 30 && ActualHeight > 30)
            {
                _hasFittedOnce = true;
                FitToActive();
            }
        };
    }

    protected override int VisualChildrenCount => _visuals.Count;
    protected override Visual GetVisualChild(int index) => _visuals[index];

    public void SetWeaponSetFilter(int? weaponSet)
    {
        _weaponSetFilter = weaponSet;
        RedrawForeground();
        RedrawLabels();
    }

    public void SetTreeData(PassiveDiffResult? diff, bool preserveViewport = false, string? focusedNodeId = null)
    {
        _index ??= PassiveTreeService.Index;
        if (_spatialGrid == null) BuildSpatialGrid();
        if (!_bgBuilt) BuildFrozenBackground();

        _currentDiff = diff;
        _stepByNumId.Clear();
        if (diff != null)
        {
            foreach (var entry in diff.Entries)
            {
                if (entry.Node != null && !string.IsNullOrEmpty(entry.Node.NumericId))
                    _stepByNumId[entry.Node.NumericId] = entry;
            }
        }

        if (focusedNodeId != null)
            _focusedNodeId = focusedNodeId;
        else if (diff != null && diff.Entries.Count > 0)
            _focusedNodeId = diff.Entries.LastOrDefault(e => e.Status != PassiveDiffStatus.Removed && e.Node != null)?.Node?.NumericId;

        if (!preserveViewport || !_hasFittedOnce)
        {
            FitToActive();
            if (ActualWidth > 30 && ActualHeight > 30) _hasFittedOnce = true;
        }
        else
        {
            RedrawForeground();
            RedrawLabels();
        }
    }

    public void FocusNode(string idOrNumericId, bool zoomIn = true)
    {
        _index ??= PassiveTreeService.Index;
        var node = _index.Resolve(idOrNumericId);
        if (node == null || ActualWidth < 20 || ActualHeight < 20) return;

        _focusedNodeId = node.NumericId;
        if (zoomIn) _scale = Math.Max(_scale, 0.085);
        RedrawForeground();
        RedrawLabels();
        _offsetX = ActualWidth / 2.0 - node.X * _scale;
        _offsetY = ActualHeight / 2.0 - node.Y * _scale;
        ApplyTransform();
    }

    public void ZoomBy(double factor)
    {
        if (ActualWidth < 20 || ActualHeight < 20) return;
        double cx = ActualWidth / 2.0;
        double cy = ActualHeight / 2.0;
        double nextScale = Math.Clamp(_scale * factor, 0.008, 0.55);
        double worldX = (cx - _offsetX) / _scale;
        double worldY = (cy - _offsetY) / _scale;
        _scale = nextScale;
        _offsetX = cx - worldX * _scale;
        _offsetY = cy - worldY * _scale;
        ApplyTransform();
        MaybeRedrawLabelsOnZoom();
    }

    public void FitToActive()
    {
        if (_index == null || ActualWidth < 20 || ActualHeight < 20) return;

        var pts = _stepByNumId.Values
            .Select(v => v.Node!)
            .Where(n => _weaponSetFilter == null || !_stepByNumId.TryGetValue(n.NumericId, out var st) || st.WeaponSet == 0 || st.WeaponSet == _weaponSetFilter.Value)
            .ToList();

        // Фільтруємо можливі острівці Ascendancy далеко від основного кластеру при першому авто-масштабі
        var mainPts = pts.Where(n => n.Kind is not (PassiveNodeKind.AscendancySmall or PassiveNodeKind.AscendancyNotable)).ToList();
        if (mainPts.Count >= 3) pts = mainPts;

        double minX, maxX, minY, maxY;
        if (pts.Count > 0)
        {
            minX = pts.Min(n => n.X) - 700;
            maxX = pts.Max(n => n.X) + 700;
            minY = pts.Min(n => n.Y) - 700;
            maxY = pts.Max(n => n.Y) + 700;
        }
        else
        {
            minX = -9500;
            maxX = 9500;
            minY = -9500;
            maxY = 9500;
        }

        double spanX = Math.Max(1200, maxX - minX);
        double spanY = Math.Max(1200, maxY - minY);
        _scale = Math.Clamp(Math.Min((ActualWidth - 20) / spanX, (ActualHeight - 20) / spanY), 0.015, 0.22);

        double midX = (minX + maxX) / 2.0;
        double midY = (minY + maxY) / 2.0;
        _offsetX = ActualWidth / 2.0 - midX * _scale;
        _offsetY = ActualHeight / 2.0 - midY * _scale;
        RedrawForeground();
        RedrawLabels();
        ApplyTransform();
    }

    private void BuildSpatialGrid()
    {
        if (_index == null) return;
        int nCount = _index.AllNodes.Count;
        _spatialGrid = new Dictionary<(int, int), List<PassiveNodeInfo>>(nCount);
        _nodesArr = new PassiveNodeInfo[nCount];
        _numToIdx = new Dictionary<string, int>(nCount, StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < nCount; i++)
        {
            var n = _index.AllNodes[i];
            _nodesArr[i] = n;
            if (!string.IsNullOrEmpty(n.NumericId))
                _numToIdx[n.NumericId] = i;

            var cell = ((int)Math.Floor(n.X / CellSize), (int)Math.Floor(n.Y / CellSize));
            if (!_spatialGrid.TryGetValue(cell, out var bucket))
            {
                bucket = [];
                _spatialGrid[cell] = bucket;
            }
            bucket.Add(n);
        }

        _adj = new int[nCount][];
        for (int i = 0; i < nCount; i++)
        {
            var conns = _nodesArr[i].Connections;
            var list = new List<int>(conns.Length);
            foreach (var c in conns)
                if (_numToIdx.TryGetValue(c, out int idx)) list.Add(idx);
            _adj[i] = [.. list];
        }
        _bfsStampArr = new int[nCount];
        _bfsParentArr = new int[nCount];
        _bfsDepthArr = new int[nCount];
        _bfsQueueArr = new int[nCount];
    }

    private void BuildFrozenBackground()
    {
        _bgBuilt = true;
        if (_index == null || _index.AllNodes.Count == 0) return;

        RenderOptions.SetEdgeMode(_bgVisual, EdgeMode.Aliased);
        using var dc = _bgVisual.RenderOpen();
        dc.DrawRectangle(UiBrushes.BgInput, null, new Rect(-40000, -40000, 80000, 80000));

        var edgeGeo = new StreamGeometry();
        using (var ctx = edgeGeo.Open())
        {
            var seenEdges = new HashSet<long>();
            for (int i = 0; i < _nodesArr.Length; i++)
            {
                var n = _nodesArr[i];
                foreach (int j in _adj[i])
                {
                    if (i >= j) continue;
                    long key = ((long)i << 20) | (uint)j;
                    if (!seenEdges.Add(key)) continue;
                    var t = _nodesArr[j];
                    ctx.BeginFigure(new Point(n.X, n.Y), false, false);
                    ctx.LineTo(new Point(t.X, t.Y), true, false);
                }
            }
        }
        edgeGeo.Freeze();
        dc.DrawGeometry(null, BgEdgePen, edgeGeo);

        var smallNodesGeo = new StreamGeometry();
        var bigNodesGeo = new StreamGeometry();
        using (var sCtx = smallNodesGeo.Open())
        using (var bCtx = bigNodesGeo.Open())
        {
            foreach (var n in _nodesArr)
            {
                double r = n.Kind is PassiveNodeKind.Keystone or PassiveNodeKind.ClassStart ? 82 :
                           n.Kind is PassiveNodeKind.Notable or PassiveNodeKind.AscendancyNotable ? 56 : 28;
                var target = n.Kind is PassiveNodeKind.Small or PassiveNodeKind.AscendancySmall ? sCtx : bCtx;
                DrawOctagon(target, n.X, n.Y, r);
            }
        }
        smallNodesGeo.Freeze();
        bigNodesGeo.Freeze();
        dc.DrawGeometry(BgNodeBrush, null, smallNodesGeo);
        dc.DrawGeometry(BgNotableBrush, null, bigNodesGeo);
    }

    private bool PassesWeaponSetFilter(PassiveStepEntry entry)
    {
        if (_weaponSetFilter == null || entry.WeaponSet == 0) return true;
        return entry.WeaponSet == _weaponSetFilter.Value;
    }

    private void RedrawForeground()
    {
        using var dc = _fgVisual.RenderOpen();
        if (_index == null || _stepByNumId.Count == 0) return;

        var keepEdges = new StreamGeometry();
        var addEdges = new StreamGeometry();
        var remEdges = new StreamGeometry();

        using (var kCtx = keepEdges.Open())
        using (var aCtx = addEdges.Open())
        using (var rCtx = remEdges.Open())
        {
            var seen = new HashSet<string>();
            foreach (var kv in _stepByNumId)
            {
                if (!PassesWeaponSetFilter(kv.Value)) continue;
                var n = kv.Value.Node!;
                foreach (var cid in n.Connections)
                {
                    if (!_stepByNumId.TryGetValue(cid, out var other) || !PassesWeaponSetFilter(other)) continue;
                    string key = string.CompareOrdinal(n.NumericId, other.Node!.NumericId) < 0
                        ? $"{n.NumericId}-{other.Node.NumericId}"
                        : $"{other.Node.NumericId}-{n.NumericId}";
                    if (!seen.Add(key)) continue;

                    var ctx = (kv.Value.Status == PassiveDiffStatus.Removed || other.Status == PassiveDiffStatus.Removed)
                        ? rCtx
                        : (kv.Value.Status == PassiveDiffStatus.Added || other.Status == PassiveDiffStatus.Added)
                            ? aCtx
                            : kCtx;

                    ctx.BeginFigure(new Point(n.X, n.Y), false, false);
                    ctx.LineTo(new Point(other.Node.X, other.Node.Y), true, false);
                }
            }
        }
        keepEdges.Freeze();
        addEdges.Freeze();
        remEdges.Freeze();

        dc.DrawGeometry(null, KeepGlowPen, keepEdges);
        dc.DrawGeometry(null, KeepEdgePen, keepEdges);
        dc.DrawGeometry(null, AddedEdgePen, addEdges);
        dc.DrawGeometry(null, RemovedEdgePen, remEdges);

        foreach (var entry in _stepByNumId.Values)
        {
            if (!PassesWeaponSetFilter(entry)) continue;
            var n = entry.Node!;
            double r = n.Kind switch
            {
                PassiveNodeKind.Keystone or PassiveNodeKind.ClassStart => 118,
                PassiveNodeKind.Notable or PassiveNodeKind.AscendancyNotable => 82,
                PassiveNodeKind.JewelSocket => 74,
                _ => 48
            };
            Brush fill = entry.Status switch
            {
                PassiveDiffStatus.Added => AddedNodeBrush,
                PassiveDiffStatus.Removed => RemovedNodeBrush,
                _ => KeepNodeBrush
            };

            if (n.Kind == PassiveNodeKind.Keystone)
            {
                DrawDiamond(dc, fill, NodeOutlinePen, n.X, n.Y, r * 1.18);
                DrawDiamond(dc, null, KeystoneRingPen, n.X, n.Y, r * 1.52);
            }
            else if (n.Kind == PassiveNodeKind.JewelSocket)
            {
                var rect = new Rect(n.X - r, n.Y - r, r * 2, r * 2);
                dc.DrawRoundedRectangle(fill, WeaponSet1Pen, rect, 18, 18);
            }
            else
            {
                dc.DrawEllipse(fill, NodeOutlinePen, new Point(n.X, n.Y), r, r);
                if (n.Kind is PassiveNodeKind.AscendancyNotable or PassiveNodeKind.AscendancySmall)
                    dc.DrawEllipse(null, AscendancyRingPen, new Point(n.X, n.Y), r + 26, r + 26);
            }

            if (entry.WeaponSet == 1)
                dc.DrawEllipse(null, WeaponSet1Pen, new Point(n.X, n.Y), r + 34, r + 34);
            else if (entry.WeaponSet == 2)
                dc.DrawEllipse(null, WeaponSet2Pen, new Point(n.X, n.Y), r + 34, r + 34);

            if (string.Equals(_focusedNodeId, n.NumericId, StringComparison.OrdinalIgnoreCase))
            {
                dc.DrawEllipse(null, FocusOuterPen, new Point(n.X, n.Y), r + 68, r + 68);
                dc.DrawEllipse(null, FocusInnerPen, new Point(n.X, n.Y), r + 45, r + 45);
            }
        }
    }

    private void MaybeRedrawLabelsOnZoom()
    {
        int tier = _scale >= 0.085 ? 2 : _scale >= 0.042 ? 1 : 0;
        if (tier != _lastLabelTier)
            RedrawLabels();
    }

    private void RedrawLabels()
    {
        _lastLabelTier = _scale >= 0.085 ? 2 : _scale >= 0.042 ? 1 : 0;
        using var dc = _labelVisual.RenderOpen();
        if (_index == null || _stepByNumId.Count == 0) return;

        double fontSize = _lastLabelTier switch { 2 => 105, 1 => 170, _ => 250 };
        int drawn = 0;
        foreach (var entry in _stepByNumId.Values)
        {
            if (!PassesWeaponSetFilter(entry)) continue;
            var n = entry.Node!;
            bool isFocused = string.Equals(_focusedNodeId, n.NumericId, StringComparison.OrdinalIgnoreCase);
            bool showKeystoneLabel = n.Kind == PassiveNodeKind.Keystone;
            bool showNotableStep = _lastLabelTier >= 1 && n.Kind is PassiveNodeKind.Notable or PassiveNodeKind.AscendancyNotable or PassiveNodeKind.JewelSocket;
            bool showSmallStep = _lastLabelTier >= 2 && entry.StepOrder > 0 && drawn < 48;

            if (!isFocused && !showKeystoneLabel && !showNotableStep && !showSmallStep)
                continue;

            string txt = showKeystoneLabel || isFocused
                ? (entry.StepOrder > 0 ? $"#{entry.StepOrder} {n.Name}" : n.Name)
                : (_lastLabelTier >= 2 && !string.IsNullOrWhiteSpace(n.Name) && showNotableStep
                    ? $"#{entry.StepOrder} {n.Name}"
                    : $"#{entry.StepOrder}");

            if (string.IsNullOrWhiteSpace(txt)) continue;
            drawn++;

            var ft = new FormattedText(txt, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelTypeface, fontSize, LabelTextBrush, 1.0);
            double offsetY = (n.Kind == PassiveNodeKind.Keystone ? 150 : 100);
            double px = n.X - ft.Width / 2;
            double py = n.Y + offsetY;
            dc.DrawRoundedRectangle(LabelBgBrush, showKeystoneLabel || isFocused ? LabelBgPen : null, new Rect(px - 24, py - 8, ft.Width + 48, ft.Height + 16), 14, 14);
            dc.DrawText(ft, new Point(px, py));
        }
    }

    private int RedrawShortestPathPreview(PassiveNodeInfo? hovered)
    {
        using var dc = _previewVisual.RenderOpen();
        if (_index == null || hovered == null || _stepByNumId.ContainsKey(hovered.NumericId) || _stepByNumId.Count == 0)
            return 0;

        var path = FindShortestPathFromAllocated(hovered.NumericId, maxDepth: 16);
        if (path.Count < 2) return 0;

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(path[0].X, path[0].Y), false, false);
            for (int i = 1; i < path.Count; i++)
                ctx.LineTo(new Point(path[i].X, path[i].Y), true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(null, PreviewGlowPen, geo);
        dc.DrawGeometry(null, PreviewPathPen, geo);

        foreach (var node in path.Skip(1))
            dc.DrawEllipse(PreviewNodeBrush, NodeOutlinePen, new Point(node.X, node.Y), 64, 64);

        return path.Count - 1;
    }

    private List<PassiveNodeInfo> FindShortestPathFromAllocated(string targetNumericId, int maxDepth)
    {
        if (_index == null || _nodesArr.Length == 0 || !_numToIdx.TryGetValue(targetNumericId, out int targetIdx))
            return [];

        int stamp = ++_bfsStamp;
        int head = 0, tail = 0;

        foreach (var allocated in _stepByNumId.Values)
        {
            if (allocated.Status == PassiveDiffStatus.Removed || allocated.Node == null) continue;
            if (!_numToIdx.TryGetValue(allocated.Node.NumericId, out int idx)) continue;
            if (_bfsStampArr[idx] != stamp)
            {
                _bfsStampArr[idx] = stamp;
                _bfsParentArr[idx] = -1;
                _bfsDepthArr[idx] = 0;
                _bfsQueueArr[tail++] = idx;
            }
        }

        while (head < tail)
        {
            int cur = _bfsQueueArr[head++];
            int curD = _bfsDepthArr[cur];
            if (curD >= maxDepth) continue;

            foreach (int next in _adj[cur])
            {
                if (_bfsStampArr[next] == stamp) continue;
                _bfsStampArr[next] = stamp;
                _bfsParentArr[next] = cur;
                _bfsDepthArr[next] = curD + 1;

                if (next == targetIdx)
                {
                    var rev = new List<PassiveNodeInfo>(curD + 2);
                    int walk = next;
                    while (walk >= 0)
                    {
                        rev.Add(_nodesArr[walk]);
                        walk = _bfsParentArr[walk];
                    }
                    rev.Reverse();
                    return rev;
                }
                _bfsQueueArr[tail++] = next;
            }
        }
        return [];
    }

    private void ApplyTransform()
    {
        var m = new Matrix();
        m.Scale(_scale, _scale);
        m.Translate(_offsetX, _offsetY);
        var mt = new MatrixTransform(m);
        mt.Freeze();
        _bgVisual.Transform = mt;
        _fgVisual.Transform = mt;
        _previewVisual.Transform = mt;
        _labelVisual.Transform = mt;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        Point pos = e.GetPosition(this);
        double factor = e.Delta > 0 ? 1.18 : 1.0 / 1.18;
        double nextScale = Math.Clamp(_scale * factor, 0.008, 0.55);
        double worldX = (pos.X - _offsetX) / _scale;
        double worldY = (pos.Y - _offsetY) / _scale;
        _scale = nextScale;
        _offsetX = pos.X - worldX * _scale;
        _offsetY = pos.Y - worldY * _scale;
        ApplyTransform();
        MaybeRedrawLabelsOnZoom();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _dragging = true;
        _dragStart = e.GetPosition(this);
        _mouseDownStart = _dragStart;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        FitToActive();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Point pos = e.GetPosition(this);
        if (_dragging)
        {
            _offsetX += pos.X - _dragStart.X;
            _offsetY += pos.Y - _dragStart.Y;
            _dragStart = pos;
            ApplyTransform();
            _tooltipPopup.IsOpen = false;
            return;
        }

        UpdateHoverTooltip(pos);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragging)
        {
            _dragging = false;
            ReleaseMouseCapture();
            Point upPos = e.GetPosition(this);
            if ((upPos - _mouseDownStart).Length < 5.0)
            {
                var clicked = HitTestClosestNode(upPos);
                if (clicked != null)
                {
                    _focusedNodeId = clicked.NumericId;
                    int pathPts = RedrawShortestPathPreview(clicked);
                    RedrawForeground();
                    RedrawLabels();
                    NodeClicked?.Invoke(clicked, pathPts);
                }
            }
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _tooltipPopup.IsOpen = false;
        _hoveredNodeId = null;
        RedrawShortestPathPreview(null);
    }

    private PassiveNodeInfo? HitTestClosestNode(Point screenPos)
    {
        if (_index == null) return null;
        if (_spatialGrid == null) BuildSpatialGrid();

        double worldX = (screenPos.X - _offsetX) / _scale;
        double worldY = (screenPos.Y - _offsetY) / _scale;
        double hitRadius = Math.Max(130.0, 16.0 / _scale);
        double bestSq = hitRadius * hitRadius;

        int cx = (int)Math.Floor(worldX / CellSize);
        int cy = (int)Math.Floor(worldY / CellSize);
        int span = Math.Max(1, (int)Math.Ceiling(hitRadius / CellSize));

        PassiveNodeInfo? closest = null;
        if (_spatialGrid != null)
        {
            for (int gx = cx - span; gx <= cx + span; gx++)
            {
                for (int gy = cy - span; gy <= cy + span; gy++)
                {
                    if (!_spatialGrid.TryGetValue((gx, gy), out var bucket)) continue;
                    foreach (var n in bucket)
                    {
                        double dx = n.X - worldX;
                        double dy = n.Y - worldY;
                        double dSq = dx * dx + dy * dy;
                        if (dSq < bestSq)
                        {
                            bestSq = dSq;
                            closest = n;
                        }
                    }
                }
            }
        }
        return closest;
    }

    private void UpdateHoverTooltip(Point screenPos)
    {
        var closest = HitTestClosestNode(screenPos);
        if (closest == null)
        {
            if (_tooltipPopup.IsOpen) _tooltipPopup.IsOpen = false;
            if (_hoveredNodeId != null)
            {
                _hoveredNodeId = null;
                _cachedHoverPathCost = 0;
                RedrawShortestPathPreview(null);
            }
            return;
        }

        if (string.Equals(_hoveredNodeId, closest.NumericId, StringComparison.OrdinalIgnoreCase))
        {
            if ((screenPos - _lastTooltipPos).Length >= 10)
            {
                _lastTooltipPos = screenPos;
                _tooltipPopup.HorizontalOffset = screenPos.X + 16;
                _tooltipPopup.VerticalOffset = screenPos.Y + 14;
            }
            return;
        }

        _hoveredNodeId = closest.NumericId;
        _cachedHoverPathCost = RedrawShortestPathPreview(closest);

        _stepByNumId.TryGetValue(closest.NumericId, out var step);
        _ttTitle.Text = string.IsNullOrWhiteSpace(closest.Name) ? closest.StringId : closest.Name;

        string kindText = PoeTermLocalizer.NodeKindLabel(closest.Kind);
        if (!string.IsNullOrWhiteSpace(closest.AscendancyName))
            kindText += $" · {closest.AscendancyName}";

        if (step != null)
        {
            string statusTxt = step.Status switch
            {
                PassiveDiffStatus.Added => Loc.T($"Крок #{step.StepOrder} (взяти зараз)", $"Step #{step.StepOrder} (Allocate)", $"Шаг #{step.StepOrder} (взять сейчас)"),
                PassiveDiffStatus.Removed => Loc.T("Скинути на цьому етапі (Respec)", "Respec / Drop here", "Сбросить на этом этапе (Respec)"),
                _ => Loc.T($"Крок #{step.StepOrder} (вже взято)", $"Step #{step.StepOrder} (Already allocated)", $"Шаг #{step.StepOrder} (уже взято)")
            };
            if (step.WeaponSet > 0) statusTxt += $" · Set {step.WeaponSet}";
            _ttMeta.Text = $"{kindText}\n{statusTxt}";
            _ttMeta.Foreground = step.Status switch
            {
                PassiveDiffStatus.Added => UiBrushes.FgEmerald,
                PassiveDiffStatus.Removed => UiBrushes.FgCrimson,
                _ => UiBrushes.FgBrass
            };
        }
        else
        {
            string costStr = _cachedHoverPathCost > 0
                ? $"\n{Loc.T($"Шлях до ноди: +{_cachedHoverPathCost} очків", $"Shortest path: +{_cachedHoverPathCost} pt(s)", $"Путь до ноды: +{_cachedHoverPathCost} очк.")}"
                : "";
            _ttMeta.Text = $"{kindText}{costStr}";
            _ttMeta.Foreground = _cachedHoverPathCost > 0 ? UiBrushes.FgSteel : UiBrushes.FgMuted;
        }

        _ttStats.Text = closest.Stats.Length > 0
            ? string.Join("\n", closest.Stats.Select(PoeTermLocalizer.StatLine))
            : Loc.T("Без додаткових описів", "No stat text", "Без описания статов");

        _lastTooltipPos = screenPos;
        _tooltipPopup.HorizontalOffset = screenPos.X + 16;
        _tooltipPopup.VerticalOffset = screenPos.Y + 14;
        if (!_tooltipPopup.IsOpen) _tooltipPopup.IsOpen = true;
    }

    private static void DrawDiamond(DrawingContext dc, Brush? fill, Pen? pen, double cx, double cy, double r)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(cx, cy - r), fill != null, true);
            ctx.LineTo(new Point(cx + r, cy), true, false);
            ctx.LineTo(new Point(cx, cy + r), true, false);
            ctx.LineTo(new Point(cx - r, cy), true, false);
        }
        g.Freeze();
        dc.DrawGeometry(fill, pen, g);
    }

    private static void DrawOctagon(StreamGeometryContext ctx, double cx, double cy, double r)
    {
        double k = r * 0.7071;
        ctx.BeginFigure(new Point(cx + r, cy), true, true);
        ctx.LineTo(new Point(cx + k, cy + k), false, false);
        ctx.LineTo(new Point(cx, cy + r), false, false);
        ctx.LineTo(new Point(cx - k, cy + k), false, false);
        ctx.LineTo(new Point(cx - r, cy), false, false);
        ctx.LineTo(new Point(cx - k, cy - k), false, false);
        ctx.LineTo(new Point(cx, cy - r), false, false);
        ctx.LineTo(new Point(cx + k, cy - k), false, false);
    }

    private static SolidColorBrush FreezeBrush(byte r, byte g, byte b)
    {
        var br = new SolidColorBrush(Color.FromRgb(r, g, b));
        br.Freeze();
        return br;
    }

    private static Pen FreezePen(byte r, byte g, byte b, double thickness)
    {
        var p = new Pen(FreezeBrush(r, g, b), thickness);
        p.Freeze();
        return p;
    }
}
