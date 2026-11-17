using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Poe2OverlayCompanion.Localization;
using Poe2OverlayCompanion.Models;
using Poe2OverlayCompanion.Services;

namespace Poe2OverlayCompanion;

public partial class MainWindow : Window
{
    // --- Інтерактивне Дерево Пасивок (як у Mobalytics) ---
    private void RefreshPassiveTreeTab(bool resetSteps, bool preserveViewport = false)
    {
        var prof = _profiles.ActiveProfile;
        if (prof == null || (prof.Stages.Count == 0 && _selectedPlannerBuild == null))
        {
            TreeStageTitle.Text = "—";
            TreeCanvas.SetTreeData(null);
            PassivesOrderPanel.Children.Clear();
            PassivesOrderPanel.Children.Add(CreateSimpleRowCard(
                Loc.T("Немає завантаженого дерева пасивок", "No passive tree loaded", "Нет загруженного дерева пассивок"),
                Loc.T("Додай .build файли, Mobalytics або PoB2 білд, щоб побачити 2D карту й порядок прокачки.", "Import .build files, Mobalytics, or a PoB2 build to inspect progression.", "Импортируй .build, Mobalytics или PoB2 билд, чтобы увидеть карту и порядок прокачки.")));
            return;
        }

        var (title, curPassives, prevPassives, activeClass) = ResolveStagePassives(prof);

        TreeStageTitle.Text = ShortStageTitle(title, 18);

        _suppressTreeSliderEvent = true;
        TreeStepSlider.Minimum = 0;
        TreeStepSlider.Maximum = Math.Max(1, curPassives.Count);
        if (resetSteps || TreeStepSlider.Value > TreeStepSlider.Maximum || TreeStepSlider.Value == 0)
            TreeStepSlider.Value = curPassives.Count;
        _suppressTreeSliderEvent = false;

        RenderPassiveDiffAndList(title, curPassives, prevPassives, preserveViewport, activeClass);
    }

    private (string title, List<LocalBuildPassive> cur, List<LocalBuildPassive>? prev, string? activeClass) ResolveStagePassives(BuildProfile? prof)
    {
        List<LocalBuildPassive> curPassives = [];
        List<LocalBuildPassive>? prevPassives = null;
        string title = prof?.Name ?? "";
        string? activeClass = prof == null ? _ai.Config.PoeCharacterClass
            : !string.IsNullOrWhiteSpace(prof.Ascendancy) ? prof.Ascendancy : prof.ClassName;

        if (prof != null && prof.Stages.Count > 0)
        {
            _treeStageIdx = Math.Clamp(_treeStageIdx, 0, prof.Stages.Count - 1);
            var st = prof.Stages[_treeStageIdx];
            title = st.Name;
            curPassives = !string.IsNullOrWhiteSpace(st.FilePath) && File.Exists(st.FilePath)
                ? (BuildPlannerService.TryParseFile(st.FilePath)?.Passives ?? [])
                : st.PassiveIds.Select(ParseStoredPassive).ToList();

            if (ChkComparePrevStage.IsChecked == true && _treeStageIdx > 0)
            {
                var prevSt = prof.Stages[_treeStageIdx - 1];
                prevPassives = !string.IsNullOrWhiteSpace(prevSt.FilePath) && File.Exists(prevSt.FilePath)
                    ? BuildPlannerService.TryParseFile(prevSt.FilePath)?.Passives
                    : prevSt.PassiveIds.Select(ParseStoredPassive).ToList();
            }
        }
        else if (_selectedPlannerBuild != null)
        {
            curPassives = _selectedPlannerBuild.Passives;
            title = _selectedPlannerBuild.Name;
        }
        return (title, curPassives, prevPassives, activeClass);
    }

    private static LocalBuildPassive ParseStoredPassive(string raw)
    {
        int bar = raw.IndexOf('|');
        if (bar > 0 && int.TryParse(raw[(bar + 1)..], out int ws))
            return new LocalBuildPassive(raw[..bar], 1, 100, ws, "");
        return new LocalBuildPassive(raw, 1, 100, 0, "");
    }

    private void RenderPassiveDiffAndList(string title, IReadOnlyList<LocalBuildPassive> curPassives, IReadOnlyList<LocalBuildPassive>? prevPassives, bool preserveViewport, string? activeClassName = null)
    {
        int stepLimit = Math.Clamp((int)TreeStepSlider.Value, 0, curPassives.Count);
        TreeStepLabel.Text = $"{stepLimit} / {curPassives.Count}";

        var diff = PassiveTreeService.AnalyzeBuildPassives(title, curPassives, prevPassives, stepLimit);
        TreeCanvas.SetTreeData(diff, preserveViewport: preserveViewport, activeClassName: activeClassName);

        if (!_treeListOpen) return;

        PassivesOrderPanel.Children.Clear();
        bool onlyKey = ChkOnlyKeyPassives.IsChecked == true;
        string search = (PassiveSearchBox.Text ?? "").Trim().ToLowerInvariant();

        var filtered = diff.Entries.Where(e =>
        {
            if (onlyKey && e.Status != PassiveDiffStatus.Removed)
            {
                if (e.Node == null) return false;
                if (e.Node.Kind is not (PassiveNodeKind.Keystone or PassiveNodeKind.Notable or PassiveNodeKind.JewelSocket or PassiveNodeKind.AscendancyNotable))
                    return false;
            }
            if (!string.IsNullOrEmpty(search))
            {
                string nodeName = (e.Node?.Name ?? e.RawId).ToLowerInvariant();
                string stats = string.Join(" ", e.Node?.Stats ?? []).ToLowerInvariant();
                if (!nodeName.Contains(search) && !stats.Contains(search))
                    return false;
            }
            return true;
        }).ToList();

        if (filtered.Count == 0)
        {
            PassivesOrderPanel.Children.Add(CreateSimpleRowCard(
                Loc.T("Вузлів за фільтром не знайдено", "No matching nodes", "Узлы по фильтру не найдены"),
                Loc.T("Спробуй зняти фільтр Keystones/Notables.", "Try unchecking Keystones/Notables filter.", "Попробуй отключить фильтр Keystones/Notables.")));
            return;
        }

        foreach (var item in filtered)
        {
            string name = item.Node != null && !string.IsNullOrWhiteSpace(item.Node.Name)
                ? item.Node.Name
                : item.RawId;
            string kind = item.Node != null ? PoeTermLocalizer.NodeKindLabel(item.Node.Kind) : "Attribute / Node";
            string statusTag = item.Status switch
            {
                PassiveDiffStatus.Added => item.StepOrder > 0 ? $"#{item.StepOrder} +" : "+",
                PassiveDiffStatus.Removed => Loc.T("Скинути", "Respec", "Сброс"),
                _ => $"#{item.StepOrder}"
            };

            string stats = item.Node != null && item.Node.Stats.Length > 0
                ? string.Join("\n", item.Node.Stats.Select(PoeTermLocalizer.StatLine))
                : item.Note;

            var baseBrush = item.Status switch
            {
                PassiveDiffStatus.Added => UiBrushes.BorderEmerald,
                PassiveDiffStatus.Removed => UiBrushes.BorderCrimson,
                _ => (item.Node?.Kind == PassiveNodeKind.Keystone ? UiBrushes.BorderBrass : UiBrushes.BorderSubtle)
            };

            var card = new Border
            {
                Background = UiBrushes.BgWindow,
                BorderBrush = baseBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 5),
                Cursor = Cursors.Hand
            };

            string focusTarget = item.Node?.NumericId ?? item.RawId;
            card.MouseEnter += (_, _) => card.BorderBrush = UiBrushes.BorderBrass;
            card.MouseLeave += (_, _) => card.BorderBrush = baseBrush;
            card.MouseLeftButtonUp += (_, _) => TreeCanvas.FocusNode(focusTarget, zoomIn: true);

            var stack = new StackPanel();
            var topRow = new DockPanel();
            var badge = new TextBlock
            {
                Text = statusTag,
                Foreground = item.Status switch
                {
                    PassiveDiffStatus.Added => UiBrushes.FgEmerald,
                    PassiveDiffStatus.Removed => UiBrushes.FgCrimson,
                    _ => UiBrushes.FgBrass
                },
                FontWeight = FontWeights.SemiBold,
                FontSize = 10,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            topRow.Children.Add(badge);

            var titleTb = new TextBlock
            {
                Text = $"{name}  ·  {kind}",
                Foreground = UiBrushes.FgPrimary,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            topRow.Children.Add(titleTb);
            stack.Children.Add(topRow);

            if (!string.IsNullOrWhiteSpace(stats))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = stats,
                    Foreground = UiBrushes.FgMuted,
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 3, 0, 0),
                    LineHeight = 14
                });
            }

            card.Child = stack;
            PassivesOrderPanel.Children.Add(card);
        }
    }

    private void TreePrevStage_Click(object sender, RoutedEventArgs e)
    {
        if (_profiles.ActiveProfile is not { Stages.Count: > 0 }) return;
        _treeStageIdx = Math.Max(0, _treeStageIdx - 1);
        RefreshPassiveTreeTab(resetSteps: true, preserveViewport: false);
    }

    private void TreeNextStage_Click(object sender, RoutedEventArgs e)
    {
        if (_profiles.ActiveProfile is not { Stages.Count: > 0 } p) return;
        _treeStageIdx = Math.Min(p.Stages.Count - 1, _treeStageIdx + 1);
        RefreshPassiveTreeTab(resetSteps: true, preserveViewport: false);
    }

    private void TreeOptionChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        RefreshPassiveTreeTab(resetSteps: false, preserveViewport: true);
    }

    private void PassiveSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || PagePassives.Visibility != Visibility.Visible) return;
        RefreshPassiveTreeTab(resetSteps: false, preserveViewport: true);
    }

    private void TreeStepSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressTreeSliderEvent || !IsLoaded || PagePassives.Visibility != Visibility.Visible) return;
        RefreshPassiveTreeTab(resetSteps: false, preserveViewport: true);
    }

    private void TreeStepDec_Click(object sender, RoutedEventArgs e)
    {
        if (TreeStepSlider.Value > TreeStepSlider.Minimum)
            TreeStepSlider.Value = Math.Max(TreeStepSlider.Minimum, TreeStepSlider.Value - 1);
    }

    private void TreeStepInc_Click(object sender, RoutedEventArgs e)
    {
        if (TreeStepSlider.Value < TreeStepSlider.Maximum)
            TreeStepSlider.Value = Math.Min(TreeStepSlider.Maximum, TreeStepSlider.Value + 1);
    }

    private void TreeZoomIn_Click(object sender, RoutedEventArgs e) => TreeCanvas.ZoomBy(1.3);
    private void TreeZoomOut_Click(object sender, RoutedEventArgs e) => TreeCanvas.ZoomBy(1.0 / 1.3);
    private void TreeFit_Click(object sender, RoutedEventArgs e) => TreeCanvas.FitToActive();

    private void ToggleTreeListDrawer_Click(object sender, RoutedEventArgs e)
    {
        _treeListOpen = !_treeListOpen;
        TreeListCol.Width = _treeListOpen ? new GridLength(205) : new GridLength(0);
        TreeListGap.Width = _treeListOpen ? new GridLength(6) : new GridLength(0);
        BtnToggleTreeList.Background = _treeListOpen ? UiBrushes.BgBrass : UiBrushes.BgCard;
        BtnToggleTreeList.Foreground = _treeListOpen ? UiBrushes.FgDark : UiBrushes.FgMuted;
        if (_treeListOpen) RefreshPassiveTreeTab(resetSteps: false, preserveViewport: true);
    }

    private void TreeSetAll_Click(object sender, RoutedEventArgs e) => ApplyTreeWeaponSet(null);
    private void TreeSet1_Click(object sender, RoutedEventArgs e) => ApplyTreeWeaponSet(1);
    private void TreeSet2_Click(object sender, RoutedEventArgs e) => ApplyTreeWeaponSet(2);

    private void ApplyTreeWeaponSet(int? setFilter)
    {
        _treeWeaponSetFilter = setFilter;
        TreeCanvas.SetWeaponSetFilter(setFilter);
        foreach (var (b, val) in new[] { (BtnSetAll, (int?)null), (BtnSet1, 1), (BtnSet2, 2) })
        {
            bool on = val == setFilter;
            b.Background = on ? UiBrushes.BgBrass : UiBrushes.BgCard;
            b.Foreground = on ? UiBrushes.FgDark : UiBrushes.FgMuted;
            b.BorderBrush = on ? UiBrushes.BorderBrass : UiBrushes.BorderSubtle;
        }
    }

    private void PopoutTreeWindow_Click(object sender, RoutedEventArgs e)
    {
        var prof = _profiles.ActiveProfile;
        if (prof == null) return;
        var (title, cur, prev, activeClass) = ResolveStagePassives(prof);
        int stepLimit = Math.Clamp((int)TreeStepSlider.Value, 0, cur.Count);
        var diff = PassiveTreeService.AnalyzeBuildPassives(title, cur, prev, stepLimit);
        var pop = new PassiveTreeWindow(title, diff, _treeWeaponSetFilter, activeClass);
        pop.Show();
    }

    private async void TreeExplainAi_Click(object sender, RoutedEventArgs e)
    {
        var prof = _profiles.ActiveProfile;
        if (prof == null) return;
        SelectPage(NavBtnAi);
        await RunAiQueryAsync(Loc.T("Поясни пріоритет пасивок на поточному етапі цього білду та що брати далі.", "Explain passive tree priority for this build stage and what to allocate next.", "Объясни приоритет пассивок на этом этапе билда и что брать дальше."), includeScreenshot: false);
    }
}
