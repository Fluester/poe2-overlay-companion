using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Poe2OverlayCompanion.Localization;
using Poe2OverlayCompanion.Models;
using Poe2OverlayCompanion.Services;

namespace Poe2OverlayCompanion.Controls;

/// <summary>
/// Mobalytics / PoE 2-стилізований 2D-переглядач дерева пасивок:
/// - орбітальні дуги з офіційної математики PoE 2 (0_5/tree.json)
/// - одноразово запечена текстура фону (RenderTargetBitmap) для 120+ FPS без жодних лагів
/// - векторна чіткість видимого в'юпорту при близькому зумі
/// - центральний класовий медальйон і золотий світловий шлях прокачки
/// - O(1) просторова сітка ховеру + BFS пошук найкоротшого шляху ("+N очків").
/// </summary>
public sealed class PassiveTreeView : FrameworkElement
{
    private const double CellSize = 420.0;
    private const double BgWorldExtent = 17600.0;
    private const int BgTextureSize = 2560; // ~25 MB GPU-текстура лише коли відкрите дерево
    private const double VectorLodThreshold = 0.135;

    private readonly VisualCollection _visuals;
    private readonly DrawingVisual _bgVisual = new();
    private readonly DrawingVisual _lodVisual = new();
    private readonly DrawingVisual _fgVisual = new();
    private readonly DrawingVisual _previewVisual = new();
    private readonly DrawingVisual _labelVisual = new();

    private BitmapSource? _bakedBgBitmap;
    private PassiveTreeIndex? _index;
    private double[] _orbitRadii = [0, 82, 162, 335, 493, 662, 846, 251, 1080, 1322];
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
    private string? _activeClassName;

    private string? _focusedNodeId;
    private string? _hoveredNodeId;
    private int _cachedHoverPathCost;
    private Point _lastTooltipPos = new(-999, -999);
    private int? _weaponSetFilter;
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

    // --- Палітра PoE 2 (чорний космос, приглушені орбіти, яскравий золотий шлях) ---
    private static readonly SolidColorBrush BgPitchBlack = FreezeBrush(0x02, 0x03, 0x05);
    private static readonly Brush CenterNebulaBrush = CreateRadialBrush(
        Color.FromArgb(140, 0x22, 0x3C, 0x6A),
        Color.FromArgb(45, 0x10, 0x1B, 0x32),
        Color.FromArgb(0, 0x02, 0x03, 0x05));

    private static readonly Pen BgConstellationPen = FreezePen(Color.FromArgb(46, 0x22, 0x2D, 0x42), 9);
    private static readonly Pen BgEdgePen = FreezePen(0x21, 0x28, 0x36, 15);
    private static readonly SolidColorBrush BgSocketFill = FreezeBrush(0x07, 0x09, 0x0D);
    private static readonly Pen BgSmallSlatePen = FreezePen(0x2B, 0x35, 0x48, 9);
    private static readonly Pen BgSmallStrPen = FreezePen(0x6C, 0x2B, 0x2E, 11);
    private static readonly Pen BgSmallDexPen = FreezePen(0x24, 0x60, 0x43, 11);
    private static readonly Pen BgSmallIntPen = FreezePen(0x2A, 0x4E, 0x78, 11);
    private static readonly Pen BgNotablePen = FreezePen(0x78, 0x61, 0x38, 13);
    private static readonly Pen BgKeystonePen = FreezePen(0xA6, 0x85, 0x46, 16);
    private static readonly Pen BgJewelPen = FreezePen(0x32, 0x70, 0x86, 14);
    private static readonly Pen BgAscPen = FreezePen(0x52, 0x46, 0x78, 12);

    private static readonly Pen CenterHubOuterPen = FreezePen(0x5C, 0x4B, 0x2B, 24);
    private static readonly Pen CenterHubInnerPen = FreezePen(0xB8, 0x95, 0x56, 14);
    private static readonly Pen CenterHubSpokePen = FreezePen(0x24, 0x30, 0x48, 14);

    // Активні гілки (золото з ореолом + плюси смарагд + респек червоний + набори зброї)
    private static readonly Pen KeepOuterHaloPen = FreezePen(Color.FromArgb(75, 0xD8, 0x9A, 0x32), 104);
    private static readonly Pen KeepMidGlowPen = FreezePen(Color.FromArgb(155, 0xEB, 0xB8, 0x54), 48);
    private static readonly Pen KeepCorePen = FreezePen(0xFF, 0xEB, 0xA8, 22);

    private static readonly Pen AddedGlowPen = FreezePen(Color.FromArgb(95, 0x3A, 0xC8, 0x82), 78);
    private static readonly Pen AddedCorePen = FreezePen(0x58, 0xE6, 0x9E, 26);
    private static readonly Pen RemovedCorePen = FreezePen(0xEA, 0x61, 0x61, 24);

    private static readonly Pen WeaponSet1EdgePen = FreezePen(Color.FromArgb(200, 0xFF, 0x7A, 0x48), 24);
    private static readonly Pen WeaponSet2EdgePen = FreezePen(Color.FromArgb(200, 0x4C, 0xDC, 0xFF), 24);

    private static readonly Pen PreviewGlowPen = FreezePen(Color.FromArgb(90, 0x38, 0x84, 0xD6), 76);
    private static readonly Pen PreviewPathPen = FreezePen(0x78, 0xBC, 0xFF, 28);

    private static readonly SolidColorBrush KeepNodeBrush = FreezeBrush(0xF5, 0xD2, 0x7A);
    private static readonly SolidColorBrush AddedNodeBrush = FreezeBrush(0x4E, 0xCF, 0x8F);
    private static readonly SolidColorBrush RemovedNodeBrush = FreezeBrush(0xEA, 0x61, 0x61);
    private static readonly SolidColorBrush PreviewNodeBrush = FreezeBrush(0x69, 0xAC, 0xEC);

    private static readonly Brush KeepNodeAura = CreateRadialBrush(Color.FromArgb(170, 0xF7, 0xCA, 0x66), Color.FromArgb(65, 0xC8, 0x88, 0x22), Color.FromArgb(0, 0, 0, 0));
    private static readonly Brush AddedNodeAura = CreateRadialBrush(Color.FromArgb(170, 0x52, 0xE0, 0x98), Color.FromArgb(55, 0x26, 0x94, 0x5A), Color.FromArgb(0, 0, 0, 0));

    private static readonly Pen NodeOutlinePen = FreezePen(0x09, 0x0B, 0x10, 14);
    private static readonly Pen KeystoneRingPen = FreezePen(0xFF, 0xE6, 0xA3, 22);
    private static readonly Pen AscendancyRingPen = FreezePen(0xB4, 0x8E, 0xFF, 20);
    private static readonly Pen WeaponSet1RingPen = FreezePen(0xFF, 0x7A, 0x48, 22);
    private static readonly Pen WeaponSet2RingPen = FreezePen(0x4C, 0xDC, 0xFF, 22);
    private static readonly Pen FocusOuterPen = FreezePen(0x69, 0xAC, 0xEC, 34);
    private static readonly Pen FocusInnerPen = FreezePen(0xFF, 0xFF, 0xFF, 18);

    private static readonly Typeface LabelTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly SolidColorBrush LabelTextBrush = FreezeBrush(0xFA, 0xEC, 0xC6);
    private static readonly SolidColorBrush LabelBgBrush = FreezeBrush(Color.FromArgb(195, 0x06, 0x08, 0x0C));
    private static readonly Pen LabelBgPen = FreezePen(0x9C, 0x7D, 0x43, 10);

    public PassiveTreeView()
    {
        ClipToBounds = true;
        Focusable = false;
        _visuals = new VisualCollection(this) { _bgVisual, _lodVisual, _fgVisual, _previewVisual, _labelVisual };

        _ttTitle = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 12, Foreground = UiBrushes.FgBrass, TextWrapping = TextWrapping.Wrap };
        _ttMeta = new TextBlock { FontSize = 10.5, Margin = new Thickness(0, 2, 0, 4), TextWrapping = TextWrapping.Wrap };
        _ttStats = new TextBlock { FontSize = 11, Foreground = UiBrushes.FgPrimary, TextWrapping = TextWrapping.Wrap, LineHeight = 16 };

        var ttStack = new StackPanel { MaxWidth = 310 };
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
                Background = FreezeBrush(0x0B, 0x0D, 0x12),
                BorderBrush = UiBrushes.BorderBrass,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 7, 10, 7),
                Child = ttStack
            }
        };

        SizeChanged += (_, _) =>
        {
            if (!_hasFittedOnce && ActualWidth > 30 && ActualHeight > 30)
            {
                _hasFittedOnce = true;
                FitToActive();
            }
        };
    }

    protected override int VisualChildrenCount => _visuals.Count;
    protected override Visual GetVisualChild(int index) => _visuals[index];

    public void ReleaseCachedTexture()
    {
        if (!_bgBuilt) return;
        _bgBuilt = false;
        _bakedBgBitmap = null;
        using var dc = _bgVisual.RenderOpen();
        using var ldc = _lodVisual.RenderOpen();
    }

    public void SetWeaponSetFilter(int? weaponSet)
    {
        _weaponSetFilter = weaponSet;
        RedrawForeground();
        RedrawLabels();
    }

    public void SetTreeData(PassiveDiffResult? diff, bool preserveViewport = false, string? focusedNodeId = null, string? activeClassName = null)
    {
        _index = PassiveTreeService.Index;
        if (_index.OrbitRadii.Length > 0)
            _orbitRadii = _index.OrbitRadii;
        if (_spatialGrid == null || _nodesArr.Length != _index.AllNodes.Count)
            BuildSpatialGrid();
        if (!_bgBuilt)
            BuildBakedBackground();

        _currentDiff = diff;
        if (!string.IsNullOrWhiteSpace(activeClassName))
            _activeClassName = activeClassName;

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
            RedrawVectorLod();
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
        _offsetX = ActualWidth / 2.0 - node.X * _scale;
        _offsetY = ActualHeight / 2.0 - node.Y * _scale;
        ApplyTransform();
        RedrawForeground();
        RedrawVectorLod();
        RedrawLabels();
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
        RedrawVectorLod();
        MaybeRedrawLabelsOnZoom();
    }

    public void FitToActive()
    {
        if (_index == null || ActualWidth < 20 || ActualHeight < 20) return;

        var pts = _stepByNumId.Values
            .Select(v => v.Node!)
            .Where(n => _weaponSetFilter == null || !_stepByNumId.TryGetValue(n.NumericId, out var st) || st.WeaponSet == 0 || st.WeaponSet == _weaponSetFilter.Value)
            .ToList();

        var mainPts = pts.Where(n => n.Kind is not (PassiveNodeKind.AscendancySmall or PassiveNodeKind.AscendancyNotable)).ToList();
        if (mainPts.Count >= 3) pts = mainPts;

        double minX, maxX, minY, maxY;
        if (pts.Count > 0)
        {
            minX = Math.Min(0, pts.Min(n => n.X)) - 1400;
            maxX = Math.Max(0, pts.Max(n => n.X)) + 1400;
            minY = Math.Min(0, pts.Min(n => n.Y)) - 1400;
            maxY = Math.Max(0, pts.Max(n => n.Y)) + 1400;
        }
        else
        {
            minX = -10500;
            maxX = 10500;
            minY = -10500;
            maxY = 10500;
        }

        double spanX = Math.Max(2200, maxX - minX);
        double spanY = Math.Max(2200, maxY - minY);
        _scale = Math.Clamp(Math.Min((ActualWidth - 20) / spanX, (ActualHeight - 20) / spanY), 0.015, 0.22);

        double midX = (minX + maxX) / 2.0;
        double midY = (minY + maxY) / 2.0;
        _offsetX = ActualWidth / 2.0 - midX * _scale;
        _offsetY = ActualHeight / 2.0 - midY * _scale;
        ApplyTransform();
        RedrawForeground();
        RedrawVectorLod();
        RedrawLabels();
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

    private void BuildBakedBackground()
    {
        _bgBuilt = true;
        if (_index == null || _nodesArr.Length == 0) return;

        var vectorBg = new DrawingVisual();
        double scaleToTex = BgTextureSize / (BgWorldExtent * 2.0);
        var xf = new Matrix();
        xf.Translate(BgWorldExtent, BgWorldExtent);
        xf.Scale(scaleToTex, scaleToTex);
        vectorBg.Transform = new MatrixTransform(xf);

        using (var dc = vectorBg.RenderOpen())
        {
            // 1. Глибокий чорний фон + м'яке центральне сяйво сузір'я
            dc.DrawRectangle(BgPitchBlack, null, new Rect(-BgWorldExtent, -BgWorldExtent, BgWorldExtent * 2, BgWorldExtent * 2));
            dc.DrawEllipse(CenterNebulaBrush, null, new Point(0, 0), 3600, 3600);

            // 2. Тонкі фонові кола орбіт навколо ключових кластерів (як у PoE 2 / Mobalytics)
            var seenGroups = new HashSet<(int gid, int orb)>();
            foreach (var n in _nodesArr)
            {
                if (n.Connections.Length == 0 || n.GroupId == 0 || n.Orbit is 0 or >= 7 || n.Orbit >= _orbitRadii.Length) continue;
                if (seenGroups.Add((n.GroupId, n.Orbit)) && string.IsNullOrEmpty(n.AscendancyName))
                {
                    double orad = _orbitRadii[n.Orbit];
                    if (orad > 90 && orad < 680)
                        dc.DrawEllipse(null, BgConstellationPen, new Point(n.GroupX, n.GroupY), orad, orad);
                }
            }

            // 3. Всі дугові та прямі з'єднання фону (ігноруємо між-Ascendancy стрибки!)
            var edgeGeo = new StreamGeometry();
            using (var ctx = edgeGeo.Open())
            {
                var seenEdges = new HashSet<long>();
                for (int i = 0; i < _nodesArr.Length; i++)
                {
                    var n = _nodesArr[i];
                    for (int k = 0; k < n.Connections.Length; k++)
                    {
                        if (!_numToIdx.TryGetValue(n.Connections[k], out int j) || i >= j) continue;
                        long key = ((long)i << 20) | (uint)j;
                        if (!seenEdges.Add(key)) continue;

                        var t = _nodesArr[j];
                        if (!ShouldRenderEdge(n, t)) continue;
                        sbyte corb = k < n.ConnectionOrbits.Length ? n.ConnectionOrbits[k] : (sbyte)0;
                        AppendEdge(ctx, n, t, corb);
                    }
                }
            }
            edgeGeo.Freeze();
            dc.DrawGeometry(null, BgEdgePen, edgeGeo);

            // 4. Сокети нод за типом і атрибутом (темний центр + кольоровий обідок)
            var smallGeo = new StreamGeometry();
            var strGeo = new StreamGeometry();
            var dexGeo = new StreamGeometry();
            var intGeo = new StreamGeometry();
            var notableGeo = new StreamGeometry();
            var keystoneGeo = new StreamGeometry();
            var jewelGeo = new StreamGeometry();
            var ascGeo = new StreamGeometry();

            using (var cSmall = smallGeo.Open())
            using (var cStr = strGeo.Open())
            using (var cDex = dexGeo.Open())
            using (var cInt = intGeo.Open())
            using (var cNot = notableGeo.Open())
            using (var cKey = keystoneGeo.Open())
            using (var cJew = jewelGeo.Open())
            using (var cAsc = ascGeo.Open())
            {
                foreach (var n in _nodesArr)
                {
                    if (n.Connections.Length == 0 && n.Kind != PassiveNodeKind.ClassStart)
                        continue; // Сховуємо відв'язані Delirium-anoint / Voices-слоти з чистого фону

                    switch (n.Kind)
                    {
                        case PassiveNodeKind.Keystone:
                            AppendPolygon(cKey, n.X, n.Y, 78, 4, rotated: false);
                            break;
                        case PassiveNodeKind.Notable:
                            AppendCircle(cNot, n.X, n.Y, 50);
                            break;
                        case PassiveNodeKind.AscendancyNotable:
                            AppendCircle(cAsc, n.X, n.Y, 52);
                            break;
                        case PassiveNodeKind.JewelSocket:
                            AppendPolygon(cJew, n.X, n.Y, 54, 4, rotated: true);
                            break;
                        case PassiveNodeKind.ClassStart:
                            AppendCircle(cKey, n.X, n.Y, 96);
                            break;
                        case PassiveNodeKind.AscendancySmall:
                            AppendCircle(cAsc, n.X, n.Y, 28);
                            break;
                        default:
                            var target = ClassifyAttributeSocket(n, cSmall, cStr, cDex, cInt);
                            AppendCircle(target, n.X, n.Y, 25);
                            break;
                    }
                }
            }
            smallGeo.Freeze(); strGeo.Freeze(); dexGeo.Freeze(); intGeo.Freeze();
            notableGeo.Freeze(); keystoneGeo.Freeze(); jewelGeo.Freeze(); ascGeo.Freeze();

            dc.DrawGeometry(BgSocketFill, BgSmallSlatePen, smallGeo);
            dc.DrawGeometry(BgSocketFill, BgSmallStrPen, strGeo);
            dc.DrawGeometry(BgSocketFill, BgSmallDexPen, dexGeo);
            dc.DrawGeometry(BgSocketFill, BgSmallIntPen, intGeo);
            dc.DrawGeometry(BgSocketFill, BgNotablePen, notableGeo);
            dc.DrawGeometry(BgSocketFill, BgAscPen, ascGeo);
            dc.DrawGeometry(BgSocketFill, BgJewelPen, jewelGeo);
            dc.DrawGeometry(BgSocketFill, BgKeystonePen, keystoneGeo);

            // 5. Центральний медальйон (як на PoE 2 екрані)
            DrawCentralHub(dc);
        }

        var rtb = new RenderTargetBitmap(BgTextureSize, BgTextureSize, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(vectorBg);
        rtb.Freeze();
        _bakedBgBitmap = rtb;

        RenderOptions.SetBitmapScalingMode(_bgVisual, BitmapScalingMode.HighQuality);
        using var bgDc = _bgVisual.RenderOpen();
        bgDc.DrawImage(_bakedBgBitmap, new Rect(-BgWorldExtent, -BgWorldExtent, BgWorldExtent * 2, BgWorldExtent * 2));
    }

    private void DrawCentralHub(DrawingContext dc)
    {
        dc.DrawEllipse(CenterNebulaBrush, CenterHubOuterPen, new Point(0, 0), 1040, 1040);
        dc.DrawEllipse(null, CenterHubInnerPen, new Point(0, 0), 960, 960);
        dc.DrawEllipse(null, BgConstellationPen, new Point(0, 0), 1470, 1470);
        foreach (var n in _nodesArr)
        {
            if (n.Kind != PassiveNodeKind.ClassStart) continue;
            double d = Math.Sqrt(n.X * n.X + n.Y * n.Y);
            if (d > 1050)
                dc.DrawLine(CenterHubSpokePen, new Point(n.X / d * 1040.0, n.Y / d * 1040.0), new Point(n.X, n.Y));
        }
    }

    private void RedrawVectorLod()
    {
        using var dc = _lodVisual.RenderOpen();
        if (_scale < VectorLodThreshold || _index == null || _spatialGrid == null || ActualWidth < 20 || ActualHeight < 20)
            return;

        // Коли користувач зумить дуже близько — домальовуємо чіткі векторні контури видимих нод (100-200 штук, < 0.3 мс)
        double minX = (0 - _offsetX) / _scale - 160;
        double maxX = (ActualWidth - _offsetX) / _scale + 160;
        double minY = (0 - _offsetY) / _scale - 160;
        double maxY = (ActualHeight - _offsetY) / _scale + 160;

        int gx0 = (int)Math.Floor(minX / CellSize), gx1 = (int)Math.Floor(maxX / CellSize);
        int gy0 = (int)Math.Floor(minY / CellSize), gy1 = (int)Math.Floor(maxY / CellSize);

        var notableGeo = new StreamGeometry();
        var smallGeo = new StreamGeometry();
        int count = 0;

        using (var cNot = notableGeo.Open())
        using (var cSml = smallGeo.Open())
        {
            for (int gx = gx0; gx <= gx1 && count < 450; gx++)
            {
                for (int gy = gy0; gy <= gy1 && count < 450; gy++)
                {
                    if (!_spatialGrid.TryGetValue((gx, gy), out var bucket)) continue;
                    foreach (var n in bucket)
                    {
                        if (n.Connections.Length == 0 && n.Kind != PassiveNodeKind.ClassStart) continue;
                        if (n.X < minX || n.X > maxX || n.Y < minY || n.Y > maxY) continue;
                        if (_stepByNumId.ContainsKey(n.NumericId)) continue;
                        count++;
                        if (n.Kind == PassiveNodeKind.Keystone)
                            AppendPolygon(cNot, n.X, n.Y, 78, 4, rotated: false);
                        else if (n.Kind is PassiveNodeKind.Notable or PassiveNodeKind.AscendancyNotable)
                            AppendCircle(cNot, n.X, n.Y, 50);
                        else
                            AppendCircle(cSml, n.X, n.Y, 25);
                    }
                }
            }
        }
        notableGeo.Freeze();
        smallGeo.Freeze();
        dc.DrawGeometry(BgSocketFill, BgNotablePen, notableGeo);
        dc.DrawGeometry(BgSocketFill, BgSmallSlatePen, smallGeo);
    }

    private bool PassesWeaponSetFilter(PassiveStepEntry entry)
    {
        if (_weaponSetFilter == null || entry.WeaponSet == 0) return true;
        return entry.WeaponSet == _weaponSetFilter.Value;
    }

    private void RedrawForeground()
    {
        using var dc = _fgVisual.RenderOpen();
        if (_index == null) return;

        // Легкий підпис класу всередині центрального медальйона (як на PoE 2 екрані)
        if (!string.IsNullOrWhiteSpace(_activeClassName))
        {
            var ft = new FormattedText(_activeClassName.ToUpperInvariant(), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, LabelTypeface, 145, KeepNodeBrush, 1.0);
            dc.DrawText(ft, new Point(-ft.Width / 2.0, -ft.Height / 2.0));
        }

        if (_stepByNumId.Count == 0) return;

        var keepEdges = new StreamGeometry();
        var addEdges = new StreamGeometry();
        var remEdges = new StreamGeometry();
        var ws1Edges = new StreamGeometry();
        var ws2Edges = new StreamGeometry();

        PassiveNodeInfo? connectedStart = null;
        foreach (var kv in _stepByNumId.Values)
        {
            if (kv.Node?.Kind == PassiveNodeKind.ClassStart && PassesWeaponSetFilter(kv))
            {
                connectedStart = kv.Node;
                break;
            }
        }

        using (var kCtx = keepEdges.Open())
        using (var aCtx = addEdges.Open())
        using (var rCtx = remEdges.Open())
        using (var w1Ctx = ws1Edges.Open())
        using (var w2Ctx = ws2Edges.Open())
        {
            var hubStart = connectedStart;
            if (hubStart == null)
            {
                foreach (var n in _nodesArr)
                {
                    if (n.Kind != PassiveNodeKind.ClassStart) continue;
                    if (n.Connections.Any(cid => _stepByNumId.ContainsKey(cid)))
                    {
                        hubStart = n;
                        break;
                    }
                }
            }
            if (hubStart != null)
            {
                double hd = Math.Sqrt(hubStart.X * hubStart.X + hubStart.Y * hubStart.Y);
                if (hd > 1050)
                {
                    kCtx.BeginFigure(new Point(hubStart.X / hd * 1040.0, hubStart.Y / hd * 1040.0), false, false);
                    kCtx.LineTo(new Point(hubStart.X, hubStart.Y), true, false);
                }
            }

            var seen = new HashSet<long>();
            foreach (var kv in _stepByNumId)
            {
                if (!PassesWeaponSetFilter(kv.Value)) continue;
                var n = kv.Value.Node!;
                if (!_numToIdx.TryGetValue(n.NumericId, out int idxA)) continue;

                for (int c = 0; c < n.Connections.Length; c++)
                {
                    string cid = n.Connections[c];
                    if (!_stepByNumId.TryGetValue(cid, out var other) || !PassesWeaponSetFilter(other)) continue;
                    var t = other.Node!;
                    if (!_numToIdx.TryGetValue(t.NumericId, out int idxB)) continue;
                    if (!ShouldRenderEdge(n, t)) continue;

                    int lo = Math.Min(idxA, idxB), hi = Math.Max(idxA, idxB);
                    long key = ((long)lo << 20) | (uint)hi;
                    if (!seen.Add(key)) continue;

                    sbyte corb = c < n.ConnectionOrbits.Length ? n.ConnectionOrbits[c] : (sbyte)0;
                    StreamGeometryContext ctx =
                        (kv.Value.Status == PassiveDiffStatus.Removed || other.Status == PassiveDiffStatus.Removed) ? rCtx :
                        (kv.Value.WeaponSet == 1 || other.WeaponSet == 1) ? w1Ctx :
                        (kv.Value.WeaponSet == 2 || other.WeaponSet == 2) ? w2Ctx :
                        (kv.Value.Status == PassiveDiffStatus.Added || other.Status == PassiveDiffStatus.Added) ? aCtx :
                        kCtx;

                    AppendEdge(ctx, n, t, corb);
                }
            }
        }
        keepEdges.Freeze(); addEdges.Freeze(); remEdges.Freeze(); ws1Edges.Freeze(); ws2Edges.Freeze();

        // 3-шарове золоте світіння + кольорові гілки
        dc.DrawGeometry(null, KeepOuterHaloPen, keepEdges);
        dc.DrawGeometry(null, KeepMidGlowPen, keepEdges);
        dc.DrawGeometry(null, KeepCorePen, keepEdges);

        dc.DrawGeometry(null, AddedGlowPen, addEdges);
        dc.DrawGeometry(null, AddedCorePen, addEdges);
        dc.DrawGeometry(null, RemovedCorePen, remEdges);
        dc.DrawGeometry(null, WeaponSet1EdgePen, ws1Edges);
        dc.DrawGeometry(null, WeaponSet2EdgePen, ws2Edges);

        // Підсвітка активних нод (ореоли + чітка форма)
        foreach (var entry in _stepByNumId.Values)
        {
            if (!PassesWeaponSetFilter(entry)) continue;
            var n = entry.Node!;
            double r = n.Kind switch
            {
                PassiveNodeKind.Keystone or PassiveNodeKind.ClassStart => 92,
                PassiveNodeKind.Notable or PassiveNodeKind.AscendancyNotable => 64,
                PassiveNodeKind.JewelSocket => 62,
                _ => 38
            };
            Brush fill = entry.Status switch
            {
                PassiveDiffStatus.Added => AddedNodeBrush,
                PassiveDiffStatus.Removed => RemovedNodeBrush,
                _ => KeepNodeBrush
            };

            if (entry.Status != PassiveDiffStatus.Removed)
            {
                Brush aura = entry.Status == PassiveDiffStatus.Added ? AddedNodeAura : KeepNodeAura;
                dc.DrawEllipse(aura, null, new Point(n.X, n.Y), r * 2.15, r * 2.15);
            }

            if (n.Kind == PassiveNodeKind.Keystone)
            {
                DrawDiamond(dc, fill, NodeOutlinePen, n.X, n.Y, r * 1.15);
                DrawDiamond(dc, null, KeystoneRingPen, n.X, n.Y, r * 1.48);
            }
            else if (n.Kind == PassiveNodeKind.JewelSocket)
            {
                dc.DrawRoundedRectangle(fill, WeaponSet2RingPen, new Rect(n.X - r, n.Y - r, r * 2, r * 2), 14, 14);
            }
            else
            {
                dc.DrawEllipse(fill, NodeOutlinePen, new Point(n.X, n.Y), r, r);
                if (n.Kind is PassiveNodeKind.AscendancyNotable or PassiveNodeKind.AscendancySmall)
                    dc.DrawEllipse(null, AscendancyRingPen, new Point(n.X, n.Y), r + 22, r + 22);
            }

            if (entry.WeaponSet == 1)
                dc.DrawEllipse(null, WeaponSet1RingPen, new Point(n.X, n.Y), r + 30, r + 30);
            else if (entry.WeaponSet == 2)
                dc.DrawEllipse(null, WeaponSet2RingPen, new Point(n.X, n.Y), r + 30, r + 30);

            if (string.Equals(_focusedNodeId, n.NumericId, StringComparison.OrdinalIgnoreCase))
            {
                dc.DrawEllipse(null, FocusOuterPen, new Point(n.X, n.Y), r + 64, r + 64);
                dc.DrawEllipse(null, FocusInnerPen, new Point(n.X, n.Y), r + 42, r + 42);
            }
        }
    }

    private void MaybeRedrawLabelsOnZoom()
    {
        int tier = _scale >= 0.13 ? 2 : _scale >= 0.06 ? 1 : 0;
        if (tier != _lastLabelTier)
            RedrawLabels();
    }

    private void RedrawLabels()
    {
        _lastLabelTier = _scale >= 0.13 ? 2 : _scale >= 0.06 ? 1 : 0;
        using var dc = _labelVisual.RenderOpen();
        if (_index == null || _stepByNumId.Count == 0) return;

        // Чисті підписи лише для Keystones та вибраної ноди (без громіздких сірих коробок "#1..#99")
        double fontSize = _lastLabelTier switch { 2 => 88, 1 => 140, _ => 210 };
        foreach (var entry in _stepByNumId.Values)
        {
            if (!PassesWeaponSetFilter(entry)) continue;
            var n = entry.Node!;
            bool isFocused = string.Equals(_focusedNodeId, n.NumericId, StringComparison.OrdinalIgnoreCase);
            bool isKeystone = n.Kind == PassiveNodeKind.Keystone;
            bool isCloseNotable = _lastLabelTier >= 2 && n.Kind is PassiveNodeKind.Notable or PassiveNodeKind.AscendancyNotable;

            if (!isFocused && !isKeystone && !isCloseNotable) continue;
            string txt = n.Name;
            if (string.IsNullOrWhiteSpace(txt)) continue;

            var ft = new FormattedText(txt, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelTypeface, fontSize, LabelTextBrush, 1.0);
            double py = n.Y + (isKeystone ? 140 : 92);
            double px = n.X - ft.Width / 2;
            dc.DrawRoundedRectangle(LabelBgBrush, LabelBgPen, new Rect(px - 20, py - 6, ft.Width + 40, ft.Height + 12), 10, 10);
            dc.DrawText(ft, new Point(px, py));
        }
    }

    private int RedrawShortestPathPreview(PassiveNodeInfo? hovered)
    {
        using var dc = _previewVisual.RenderOpen();
        if (_index == null || hovered == null || _stepByNumId.ContainsKey(hovered.NumericId) || _stepByNumId.Count == 0)
            return 0;

        var path = FindShortestPathFromAllocated(hovered.NumericId, maxDepth: 14);
        if (path.Count < 2) return 0;

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            for (int i = 0; i < path.Count - 1; i++)
            {
                var a = path[i];
                var b = path[i + 1];
                if (!ShouldRenderEdge(a, b)) continue;
                sbyte corb = 0;
                for (int c = 0; c < a.Connections.Length; c++)
                {
                    if (string.Equals(a.Connections[c], b.NumericId, StringComparison.OrdinalIgnoreCase) && c < a.ConnectionOrbits.Length)
                    {
                        corb = a.ConnectionOrbits[c];
                        break;
                    }
                }
                AppendEdge(ctx, a, b, corb);
            }
        }
        geo.Freeze();
        dc.DrawGeometry(null, PreviewGlowPen, geo);
        dc.DrawGeometry(null, PreviewPathPen, geo);

        foreach (var node in path.Skip(1))
            dc.DrawEllipse(PreviewNodeBrush, NodeOutlinePen, new Point(node.X, node.Y), 46, 46);

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
        _lodVisual.Transform = mt;
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
        RedrawVectorLod();
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
            RedrawVectorLod();
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
        double hitRadius = Math.Max(110.0, 14.0 / _scale);
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

    private static bool ShouldRenderEdge(PassiveNodeInfo a, PassiveNodeInfo b)
    {
        if (!string.Equals(a.AscendancyName, b.AscendancyName, StringComparison.OrdinalIgnoreCase))
            return false;
        double dx = b.X - a.X, dy = b.Y - a.Y;
        return dx * dx + dy * dy <= 3200.0 * 3200.0;
    }

    private void AppendEdge(StreamGeometryContext ctx, PassiveNodeInfo a, PassiveNodeInfo b, sbyte connOrbit)
    {
        ctx.BeginFigure(new Point(a.X, a.Y), false, false);

        int absOrb = Math.Abs(connOrbit);
        if (connOrbit != 0 && absOrb < _orbitRadii.Length)
        {
            double r = _orbitRadii[absOrb];
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (r > 0 && dist > 1 && dist < r * 2.0)
            {
                double perp = Math.Sqrt(Math.Max(0, r * r - dist * dist * 0.25)) * (connOrbit > 0 ? 1.0 : -1.0);
                double cx = a.X + dx * 0.5 + perp * (dy / dist);
                double cy = a.Y + dy * 0.5 - perp * (dx / dist);
                double da = NormalizeAngleDelta(Math.Atan2(b.Y - cy, b.X - cx) - Math.Atan2(a.Y - cy, a.X - cx));
                ctx.ArcTo(new Point(b.X, b.Y), new Size(r, r), 0, false,
                    da > 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true, false);
                return;
            }
        }
        else if (connOrbit == 0 && a.GroupId > 0 && a.GroupId == b.GroupId && a.Orbit > 0 && a.Orbit == b.Orbit && a.Orbit < _orbitRadii.Length)
        {
            double r = _orbitRadii[a.Orbit];
            double dist = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            if (r > 0 && dist > 1 && dist < r * 2.0)
            {
                double da = NormalizeAngleDelta(Math.Atan2(b.Y - b.GroupY, b.X - b.GroupX) - Math.Atan2(a.Y - a.GroupY, a.X - a.GroupX));
                ctx.ArcTo(new Point(b.X, b.Y), new Size(r, r), 0, false,
                    da > 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true, false);
                return;
            }
        }

        ctx.LineTo(new Point(b.X, b.Y), true, false);
    }

    private static double NormalizeAngleDelta(double da)
    {
        while (da > Math.PI) da -= Math.PI * 2.0;
        while (da < -Math.PI) da += Math.PI * 2.0;
        return da;
    }

    private static StreamGeometryContext ClassifyAttributeSocket(
        PassiveNodeInfo n,
        StreamGeometryContext def,
        StreamGeometryContext str,
        StreamGeometryContext dex,
        StreamGeometryContext intl)
    {
        if (n.Stats.Length == 1)
        {
            string s = n.Stats[0];
            if (s.Contains("Strength", StringComparison.OrdinalIgnoreCase) && !s.Contains("Dexterity")) return str;
            if (s.Contains("Dexterity", StringComparison.OrdinalIgnoreCase) && !s.Contains("Intelligence")) return dex;
            if (s.Contains("Intelligence", StringComparison.OrdinalIgnoreCase) && !s.Contains("Strength")) return intl;
        }
        return def;
    }

    private static void DrawDiamond(DrawingContext dc, Brush? fill, Pen? pen, double cx, double cy, double r)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
            AppendPolygon(ctx, cx, cy, r, 4, rotated: false);
        g.Freeze();
        dc.DrawGeometry(fill, pen, g);
    }

    private static void AppendCircle(StreamGeometryContext ctx, double cx, double cy, double r) =>
        AppendPolygon(ctx, cx, cy, r, 8, rotated: false);

    private static void AppendPolygon(StreamGeometryContext ctx, double cx, double cy, double r, int sides, bool rotated)
    {
        double start = rotated ? Math.PI / sides : 0;
        for (int i = 0; i < sides; i++)
        {
            double a = start + i * (Math.PI * 2.0 / sides);
            var pt = new Point(cx + Math.Cos(a) * r, cy + Math.Sin(a) * r);
            if (i == 0) ctx.BeginFigure(pt, true, true);
            else ctx.LineTo(pt, true, false);
        }
    }

    private static SolidColorBrush FreezeBrush(byte r, byte g, byte b)
    {
        var br = new SolidColorBrush(Color.FromRgb(r, g, b));
        br.Freeze();
        return br;
    }

    private static SolidColorBrush FreezeBrush(Color c)
    {
        var br = new SolidColorBrush(c);
        br.Freeze();
        return br;
    }

    private static Pen FreezePen(byte r, byte g, byte b, double thickness) =>
        FreezePen(Color.FromRgb(r, g, b), thickness);

    private static Pen FreezePen(Color c, double thickness)
    {
        var p = new Pen(FreezeBrush(c), thickness) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        p.Freeze();
        return p;
    }

    private static RadialGradientBrush CreateRadialBrush(Color inner, Color mid, Color outer)
    {
        var g = new RadialGradientBrush
        {
            GradientStops =
            [
                new GradientStop(inner, 0.0),
                new GradientStop(mid, 0.55),
                new GradientStop(outer, 1.0)
            ]
        };
        g.Freeze();
        return g;
    }
}
