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
    // --- Предмети (Ctrl+C) + Порівняння вдягнутого екіпу в оверлеї + Trade2 ---
    private void CheckClipboardNow_Click(object sender, RoutedEventArgs e)
    {
        _overrideCompareSlotKey = null;
        CheckClipboard(force: true);
    }

    private void CheckClipboard(bool force)
    {
        if (!force && ChkAutoClipboard.IsChecked != true) return;

        string text;
        try { if (!Clipboard.ContainsText()) return; text = Clipboard.GetText(); }
        catch { return; }

        if (!string.IsNullOrEmpty(_ignoreClipboardText) && text == _ignoreClipboardText)
        {
            _ignoreClipboardText = "";
            return;
        }
        if (!force && text == _lastClipboard) return;
        _lastClipboard = text;
        if (!force) _overrideCompareSlotKey = null;

        EvaluateAndDisplayItemText(text, force, isExternalDrop: true, fetchLivePrice: true);
    }

    private void InspectEquippedSlotInItemsTab(string slotKey)
    {
        if (!_ai.Config.WornGearBySlot.TryGetValue(slotKey, out string? wornRaw) || string.IsNullOrWhiteSpace(wornRaw))
            return;

        SelectPage(NavBtnItems);
        ShowItemsEvalSubtab_Click(this, new RoutedEventArgs());
        _overrideCompareSlotKey = slotKey;
        EvaluateAndDisplayItemText(wornRaw, force: true, isExternalDrop: false, fetchLivePrice: false);
    }

    private void InspectEquippedSlotOrRawInItemsTab(string rawSlotId, string rawText)
    {
        SelectPage(NavBtnItems);
        ShowItemsEvalSubtab_Click(this, new RoutedEventArgs());
        if (_ai.Config.WornGearBySlot.TryGetValue(rawSlotId, out string? wornRaw) &&
            !string.IsNullOrWhiteSpace(wornRaw) &&
            wornRaw.Trim() == rawText.Trim())
        {
            _overrideCompareSlotKey = rawSlotId;
            EvaluateAndDisplayItemText(wornRaw, force: true, isExternalDrop: false, fetchLivePrice: false);
            return;
        }
        _overrideCompareSlotKey = _ai.Config.WornGearBySlot.ContainsKey(rawSlotId) ? rawSlotId : null;
        EvaluateAndDisplayItemText(rawText, force: true, isExternalDrop: true, fetchLivePrice: false);
    }

    private void ToggleCompareWithLastExternal_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastExternalItemText)) return;
        EvaluateAndDisplayItemText(_lastExternalItemText, force: true, isExternalDrop: true, fetchLivePrice: false);
    }

    private void RefreshWornSlotsStrip()
    {
        if (WornSlotsStripPanel == null || WornGearQuickBox == null) return;
        WornSlotsStripPanel.Children.Clear();
        if (_ai.Config.WornGearBySlot.Count == 0)
        {
            WornGearQuickBox.Visibility = Visibility.Collapsed;
            return;
        }

        string[] orderedSlots = ["Weapon1", "Offhand1", "Weapon2", "Offhand2", "Helm1", "BodyArmour1", "Gloves1", "Boots1", "Amulet1", "Ring1", "Ring2", "Ring3", "Belt1", "Flask1", "Flask2", "Charm1", "Charm2", "Charm3"];
        foreach (string slot in orderedSlots.Concat(_ai.Config.WornGearBySlot.Keys).Distinct())
        {
            if (!_ai.Config.WornGearBySlot.TryGetValue(slot, out string? raw) || string.IsNullOrWhiteSpace(raw))
                continue;

            string title = raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .SkipWhile(l => l.StartsWith("Item Class:", StringComparison.OrdinalIgnoreCase) || l.StartsWith("Rarity:", StringComparison.OrdinalIgnoreCase) || l.StartsWith("--------"))
                .FirstOrDefault()?.Trim() ?? slot;
            if (title.Length > 22) title = title[..21] + "…";
            string statsTip = PoeAccountService.SummarizeItemStatsCompact(raw);
            bool selected = string.Equals(_overrideCompareSlotKey, slot, StringComparison.OrdinalIgnoreCase) ||
                            (_lastItemEval != null && _lastItemEval.RawCopiedText.Trim() == raw.Trim());

            string capturedSlot = slot;
            var btn = new Button
            {
                Content = $"{PoeTermLocalizer.SlotName(slot)}: {title}",
                ToolTip = string.IsNullOrWhiteSpace(statsTip) ? title : $"{PoeTermLocalizer.SlotName(slot)} — {title}\n{statsTip}",
                Style = (Style)FindResource("FlatButton"),
                Background = selected ? UiBrushes.BgEmeraldDim : UiBrushes.BgCard,
                BorderBrush = selected ? UiBrushes.BorderEmerald : UiBrushes.BorderSubtle,
                Foreground = selected ? UiBrushes.FgEmerald : UiBrushes.FgPrimary,
                FontSize = 9.3,
                Padding = new Thickness(7, 2.5, 7, 2.5),
                Margin = new Thickness(0, 0, 4, 4)
            };
            btn.Click += (_, _) =>
            {
                if (!string.IsNullOrWhiteSpace(_lastExternalItemText) &&
                    _lastItemEval != null &&
                    !_ai.Config.WornGearBySlot.Values.Any(w => w.Trim() == _lastItemEval.RawCopiedText.Trim()))
                {
                    // Якщо вже скопійований інший предмет і клікаємо на слот — порівнюємо скопійований саме з цим слотом!
                    _overrideCompareSlotKey = capturedSlot;
                    EvaluateAndDisplayItemText(_lastExternalItemText, force: true, isExternalDrop: true, fetchLivePrice: false);
                }
                else
                {
                    InspectEquippedSlotInItemsTab(capturedSlot);
                }
            };
            WornSlotsStripPanel.Children.Add(btn);
        }
        WornGearQuickBox.Visibility = WornSlotsStripPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderSideBySideCompare(ItemEvaluation ev, bool inspectingWornItself)
    {
        if (SideBySideCompareBox == null) return;
        bool hasWornRef = !string.IsNullOrWhiteSpace(ev.MatchedWornText);
        bool sameAsWorn = hasWornRef && ev.MatchedWornText.Trim() == ev.RawCopiedText.Trim();

        if (!hasWornRef || sameAsWorn || inspectingWornItself)
        {
            if (inspectingWornItself && !string.IsNullOrWhiteSpace(_lastExternalItemText) &&
                _lastExternalItemText.Trim() != ev.RawCopiedText.Trim())
            {
                CompareNewTitle.Text = Loc.T("Оглядаєш вдягнутий слот", "Inspecting Equipped Slot", "Осмотр надетого слота") + $": {ev.ItemName}";
                CompareNewBody.Text = PoeAccountService.SummarizeItemStatsCompact(ev.RawCopiedText)
                    is { Length: > 0 } cs ? cs : ev.Summary;
                CompareWornTitle.Text = Loc.T("Останній Ctrl+C / Буфер", "Last Ctrl+C Item", "Последний Ctrl+C");
                CompareWornBody.Text = PoeAccountService.SummarizeItemStatsCompact(_lastExternalItemText)
                    is { Length: > 0 } es ? es : _lastExternalItemText.Split('\n').FirstOrDefault()?.Trim() ?? "";
                BtnToggleCompareAgainstLast.Visibility = Visibility.Visible;
                SideBySideCompareBox.Visibility = Visibility.Visible;
                return;
            }
            BtnToggleCompareAgainstLast.Visibility = Visibility.Collapsed;
            SideBySideCompareBox.Visibility = Visibility.Collapsed;
            return;
        }

        BtnToggleCompareAgainstLast.Visibility = Visibility.Collapsed;
        string slotLabel = PoeTermLocalizer.SlotName(_overrideCompareSlotKey ?? ev.MatchedBuildSlot);
        string wornTitle = ev.MatchedWornText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .SkipWhile(l => l.StartsWith("Item Class:", StringComparison.OrdinalIgnoreCase) || l.StartsWith("Rarity:", StringComparison.OrdinalIgnoreCase) || l.StartsWith("--------"))
            .FirstOrDefault()?.Trim() ?? slotLabel;

        static string ExtractCompareLines(string raw)
        {
            var mods = raw.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .SkipWhile(l => !l.StartsWith("--------"))
                .Where(l => !l.StartsWith("--------") && !l.StartsWith("Item Level:") && !l.StartsWith("Requires") && !l.StartsWith("Sockets:"))
                .Take(7)
                .Select(PoeTermLocalizer.StatLine)
                .ToList();
            string stats = PoeAccountService.SummarizeItemStatsCompact(raw);
            if (!string.IsNullOrWhiteSpace(stats) && mods.Count > 0)
                return $"{stats}\n{string.Join("\n", mods.Select(m => $"• {m}"))}";
            return mods.Count > 0 ? string.Join("\n", mods.Select(m => $"• {m}")) : stats;
        }

        CompareNewTitle.Text = $"{ev.UpgradeVerdict}\n{ev.ItemName}";
        CompareNewBody.Text = ExtractCompareLines(ev.RawCopiedText);
        CompareWornTitle.Text = $"{Loc.T("На тобі зараз", "Equipped now", "Надето сейчас")} ({slotLabel})\n{wornTitle}";
        CompareWornBody.Text = ExtractCompareLines(ev.MatchedWornText);
        SideBySideCompareBox.Visibility = Visibility.Visible;
    }

    private void EvaluateAndDisplayItemText(string text, bool force, bool isExternalDrop, bool fetchLivePrice)
    {
        IReadOnlyDictionary<string, string> compareMap = _ai.Config.WornGearBySlot;
        if (!string.IsNullOrWhiteSpace(_overrideCompareSlotKey) &&
            _ai.Config.WornGearBySlot.TryGetValue(_overrideCompareSlotKey, out string? forcedWorn))
        {
            var probe = ItemEvaluator.Evaluate(text, _profiles.ActiveProfile, _overviewStageIdx, null, EffectiveLevel, _ai.Config.CustomWaystoneModTags);
            string slotToMap = !string.IsNullOrWhiteSpace(probe.ItemClass) ? ItemEvaluator.MapItemClassToSlotKey(probe.ItemClass) : _overrideCompareSlotKey;
            compareMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [slotToMap] = forcedWorn,
                [_overrideCompareSlotKey] = forcedWorn
            };
        }

        var ev = ItemEvaluator.Evaluate(text, _profiles.ActiveProfile, _overviewStageIdx, compareMap, EffectiveLevel, _ai.Config.CustomWaystoneModTags);
        if (!ev.IsPoeItem)
        {
            if (force)
            {
                ItemVerdictText.Text = Loc.T("У буфері не знайдено предмета PoE 2", "Clipboard does not contain a PoE 2 item", "В буфере не найден предмет PoE 2");
                ItemVerdictText.Foreground = UiBrushes.FgMuted;
                ItemUpgradeBadge.Visibility = Visibility.Collapsed;
                ItemPriceBadge.Visibility = Visibility.Collapsed;
                ItemProfitBadge.Visibility = Visibility.Collapsed;
                ItemAffixBadge.Visibility = Visibility.Collapsed;
                SideBySideCompareBox.Visibility = Visibility.Collapsed;
                WaystoneModsBox.Visibility = Visibility.Collapsed;
                ItemModFilterBox.Visibility = Visibility.Collapsed;
                LiveListingsBox.Visibility = Visibility.Collapsed;
                RefreshWornSlotsStrip();
            }
            return;
        }

        if (isExternalDrop) _lastExternalItemText = text;
        bool isWornItself = !isExternalDrop || _ai.Config.WornGearBySlot.Values.Any(v => v.Trim() == text.Trim());

        _lastItemEval = ev;
        BtnOpenWiki.IsEnabled = !string.IsNullOrWhiteSpace(ev.WikiUrl);
        RenderSelectableModCheckboxes(ev);
        RenderWaystoneModsPanel(ev);
        RenderLiveListings(ev);
        RenderSideBySideCompare(ev, inspectingWornItself: !isExternalDrop);
        RefreshWornSlotsStrip();

        Color col = ev.Verdict == ItemVerdict.Danger
            ? GetVerdictColor(ItemVerdict.Danger)
            : isWornItself
                ? Color.FromRgb(0xB5, 0xAE, 0xA4)
                : ev.UpgradeDeltaPct >= 8
                    ? Color.FromRgb(0x3F, 0xB9, 0x50)
                    : ev.UpgradeDeltaPct <= -8
                        ? Color.FromRgb(0xE0, 0x52, 0x52)
                        : GetVerdictColor(ev.Verdict);
        var brush = UiBrushes.ForColor(col);
        string slotDisplay = PoeTermLocalizer.SlotName(_overrideCompareSlotKey ?? ev.MatchedBuildSlot);
        string headlineVerdict = isWornItself
            ? Loc.T($"[ЕКІП] {slotDisplay}", $"[EQUIPPED] {slotDisplay}", $"[НАДЕТО] {slotDisplay}")
            : !string.IsNullOrWhiteSpace(ev.UpgradeVerdict) ? ev.UpgradeVerdict : Loc.Verdict(ev.Verdict);
        ItemVerdictText.Text = $"{headlineVerdict}  ·  {ev.ItemName}";
        ItemVerdictText.Foreground = brush;
        ItemVerdictBorder.BorderBrush = brush;

        if (!isWornItself && !string.IsNullOrWhiteSpace(ev.UpgradeVerdict))
        {
            ItemUpgradeBadgeText.Text = ev.UpgradeVerdict;
            ItemUpgradeBadgeText.Foreground = ev.UpgradeDeltaPct >= 8 ? UiBrushes.FgEmerald : ev.UpgradeDeltaPct <= -8 ? UiBrushes.FgCrimson : UiBrushes.FgSteel;
            ItemUpgradeBadge.Visibility = Visibility.Visible;
        }
        else ItemUpgradeBadge.Visibility = Visibility.Collapsed;

        ItemPriceBadge.Visibility = Visibility.Collapsed;

        if (!string.IsNullOrWhiteSpace(ev.ProfitVerdict))
        {
            ItemProfitBadgeText.Text = ev.ProfitVerdict;
            ItemProfitBadge.Visibility = Visibility.Visible;
        }
        else ItemProfitBadge.Visibility = Visibility.Collapsed;

        if (!string.IsNullOrWhiteSpace(ev.AffixSummary))
        {
            ItemAffixBadgeText.Text = ev.AffixSummary;
            ItemAffixBadge.Visibility = Visibility.Visible;
        }
        else ItemAffixBadge.Visibility = Visibility.Collapsed;

        string details = BuildFormattedItemDetails(ev);
        ItemDetailsText.Text = details;

        bool canSwitchTab = _ai.Config.FocusOverlayOnCtrlC || PageItems.Visibility == Visibility.Visible;
        if (!_collapsed && !_clickThrough && !_miniHudMode && !force && canSwitchTab)
            SelectPage(NavBtnItems);

        if (ChkToastPopup.IsChecked == true && !force)
            _toast.ShowToast($"{headlineVerdict} · {ev.ItemName}", details, col, nearCursor: true, seconds: ev.Verdict == ItemVerdict.Danger ? 7.5 : 5.0);

        if (ChkSoundAlert.IsChecked == true && _ai.Config.SoundAlertsEnabled && !force)
        {
            if (ev.Verdict == ItemVerdict.Danger) SystemSounds.Hand.Play();
            else if (ev.Verdict == ItemVerdict.Great || ev.UpgradeDeltaPct >= 10) SystemSounds.Asterisk.Play();
        }

        // Фоновий живий Trade 2 прайс-чек (не блокує миттєвий тост)
        if (fetchLivePrice) _ = FetchLivePriceAndRefreshUiAsync(ev);
    }

    private static string BuildFormattedItemDetails(ItemEvaluation ev)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(ev.UpgradeComparisonNote)) lines.Add(ev.UpgradeComparisonNote);
        if (!string.IsNullOrWhiteSpace(ev.BuildMatchVerdict) || !string.IsNullOrWhiteSpace(ev.BuildComparisonNote))
        {
            string buildLine = string.Join(" · ", new[] { ev.BuildMatchVerdict, ev.BuildComparisonNote }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (!string.IsNullOrWhiteSpace(buildLine)) lines.Add(buildLine);
        }
        if (!string.IsNullOrWhiteSpace(ev.AffixSummary)) lines.Add(ev.AffixSummary);
        if (!string.IsNullOrWhiteSpace(ev.LivePriceSummary)) lines.Add(ev.LivePriceSummary);
        if (ev.LiveListings.Count > 0)
        {
            string topListings = string.Join(" · ", ev.LiveListings.Take(4).Select(l => $"{l.Amount:0.##} {l.Currency} ({l.AgeText})"));
            lines.Add($"{Loc.T("Лоти:", "Listings:", "Лоты:")} {topListings}");
        }
        if (!string.IsNullOrWhiteSpace(ev.ProfitVerdict)) lines.Add(ev.ProfitVerdict);
        lines.Add(ev.Summary);
        lines.Add("");
        if (ev.Warnings.Count > 0) { lines.Add(Loc.T("ПОПЕРЕДЖЕННЯ:", "WARNINGS:", "ПРЕДУПРЕЖДЕНИЯ:")); lines.AddRange(ev.Warnings.Select(w => $"• {w}")); lines.Add(""); }
        if (ev.Positives.Count > 0) { lines.Add(Loc.T("ПЛЮСИ:", "POSITIVES:", "ПЛЮСЫ:")); lines.AddRange(ev.Positives.Select(p => $"+ {p}")); lines.Add(""); }
        if (ev.Missing.Count > 0) { lines.Add(Loc.T("БАЖАНО ДОДАТИ:", "MISSING / UPGRADE:", "ЖЕЛАТЕЛЬНО ДОБАВИТЬ:")); lines.AddRange(ev.Missing.Select(m => $"• {m}")); lines.Add(""); }
        if (!string.IsNullOrWhiteSpace(ev.ObtainInfo))
        {
            lines.Add(Loc.T("ДЕ ОТРИМАТИ:", "WHERE TO OBTAIN:", "ГДЕ ПОЛУЧИТЬ:"));
            lines.Add(ev.ObtainInfo);
        }
        return string.Join("\n", lines).Trim();
    }

    private async Task FetchLivePriceAndRefreshUiAsync(ItemEvaluation ev)
    {
        string league = string.IsNullOrWhiteSpace(_ai.Config.TradeLeague) ? "Forbidden Rites" : _ai.Config.TradeLeague;
        string price = await PoeTradePriceService.EstimateLivePriceAsync(ev, league);
        if (string.IsNullOrWhiteSpace(price)) return;

        await Dispatcher.InvokeAsync(() =>
        {
            if (_lastItemEval != ev) return;
            ev.LivePriceSummary = price;
            ItemPriceBadgeText.Text = price;
            ItemPriceBadge.Visibility = Visibility.Visible;
            RenderLiveListings(ev);
            string updated = BuildFormattedItemDetails(ev);
            ItemDetailsText.Text = updated;
            _toast.UpdateBodyIfVisible(updated);
        });
    }

    private void RenderWaystoneModsPanel(ItemEvaluation ev)
    {
        WaystoneModsPanel.Children.Clear();
        if (!ev.IsWaystone || ev.WaystoneMods.Count == 0)
        {
            WaystoneModsBox.Visibility = Visibility.Collapsed;
            return;
        }

        foreach (var wm in ev.WaystoneMods)
        {
            var (fg, bg, border, icon) = wm.Safety switch
            {
                MapModSafety.Deadly => (UiBrushes.FgCrimson, UiBrushes.BgCrimsonDim, UiBrushes.BorderCrimson, "✖"),
                MapModSafety.Caution => (UiBrushes.FgAmber, UiBrushes.BgBrassDim, UiBrushes.BorderBrass, "▲"),
                _ => (UiBrushes.FgEmerald, UiBrushes.BgEmeraldDim, UiBrushes.BorderEmerald, "✓")
            };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = $"{icon} {wm.ModText}",
                Foreground = fg,
                FontSize = 10,
                FontWeight = wm.Safety == MapModSafety.Deadly ? FontWeights.SemiBold : FontWeights.Normal,
                TextWrapping = TextWrapping.Wrap
            });
            if (!string.IsNullOrWhiteSpace(wm.Reason))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = wm.Reason,
                    Foreground = UiBrushes.FgMuted,
                    FontSize = 9,
                    Margin = new Thickness(12, 1, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                });
            }

            var btn = new Button
            {
                Content = stack,
                Background = bg,
                BorderBrush = border,
                BorderThickness = new Thickness(1),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(7, 4, 7, 4),
                Margin = new Thickness(0, 0, 0, 4),
                Cursor = Cursors.Hand
            };
            btn.Click += (_, _) =>
            {
                WaystoneModChecker.CycleCustomTag(_ai.Config.CustomWaystoneModTags, wm.PatternKey, wm.Safety);
                _ai.SaveConfig();
                CheckClipboard(force: true);
            };
            WaystoneModsPanel.Children.Add(btn);
        }
        WaystoneModsBox.Visibility = Visibility.Visible;
    }

    private void RenderLiveListings(ItemEvaluation ev)
    {
        if (ev.LiveListings.Count == 0)
        {
            LiveListingsBox.Visibility = Visibility.Collapsed;
            return;
        }
        LiveListingsText.Text = string.Join("   ·   ",
            ev.LiveListings.Take(6).Select(l => $"{l.Amount:0.##} {l.Currency} ({l.AgeText})"));
        LiveListingsBox.Visibility = Visibility.Visible;
    }

    private void RenderSelectableModCheckboxes(ItemEvaluation ev)
    {
        ItemModChecksPanel.Children.Clear();
        _modFilterCheckboxes.Clear();
        if (ev.SelectableMods.Count == 0)
        {
            ItemModFilterBox.Visibility = Visibility.Collapsed;
            return;
        }

        foreach (var mod in ev.SelectableMods)
        {
            var row = new DockPanel { Margin = new Thickness(0, 1, 0, 2), LastChildFill = true };
            var minBox = new TextBox
            {
                Width = 42,
                Height = 20,
                Text = mod.MinSuggested > 0 ? mod.MinSuggested.ToString() : "",
                Background = UiBrushes.BgInput,
                Foreground = UiBrushes.FgPrimary,
                BorderBrush = UiBrushes.BorderSubtle,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4, 1, 4, 1),
                FontSize = 9.5,
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
                ToolTip = Loc.T("Мін. значення (min) для Trade 2", "Minimum stat threshold for Trade 2", "Мин. порог (min) для Trade 2")
            };
            DockPanel.SetDock(minBox, Dock.Right);
            row.Children.Add(minBox);

            var chk = new CheckBox
            {
                Content = mod.DisplayText,
                IsChecked = mod.IsDefaultChecked,
                Style = (Style)FindResource("DarkCheck"),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            };
            row.Children.Add(chk);
            ItemModChecksPanel.Children.Add(row);
            _modFilterCheckboxes.Add((chk, minBox, mod));
        }
        ItemModFilterBox.Visibility = _itemsExtraExpanded ? Visibility.Visible : Visibility.Collapsed;
        UpdateItemsExtraButtonUi();
    }

    private void ToggleItemsExtra_Click(object sender, RoutedEventArgs e)
    {
        _itemsExtraExpanded = !_itemsExtraExpanded;
        TradeShortcutsBox.Visibility = _itemsExtraExpanded ? Visibility.Visible : Visibility.Collapsed;
        ItemModFilterBox.Visibility = (_itemsExtraExpanded && _modFilterCheckboxes.Count > 0) ? Visibility.Visible : Visibility.Collapsed;
        UpdateItemsExtraButtonUi();
    }

    private void UpdateItemsExtraButtonUi()
    {
        if (BtnToggleItemsExtra == null) return;
        string arrow = _itemsExtraExpanded ? "▴" : "▾";
        string cnt = _modFilterCheckboxes.Count > 0 ? $" ({_modFilterCheckboxes.Count})" : "";
        BtnToggleItemsExtra.Content = Loc.T($"Trade / Моди{cnt} {arrow}", $"Trade / Mods{cnt} {arrow}", $"Trade / Моды{cnt} {arrow}");
        BtnToggleItemsExtra.Background = _itemsExtraExpanded ? UiBrushes.BgBrass : UiBrushes.BgCard;
        BtnToggleItemsExtra.Foreground = _itemsExtraExpanded ? UiBrushes.FgDark : UiBrushes.FgPrimary;
        BtnToggleItemsExtra.BorderBrush = _itemsExtraExpanded ? UiBrushes.BorderBrass : UiBrushes.BorderSubtle;
    }

    private List<ItemModFilterOption> GetCheckedMods()
    {
        var selected = new List<ItemModFilterOption>();
        foreach (var (box, minBox, mod) in _modFilterCheckboxes)
        {
            if (box.IsChecked != true) continue;
            string rawMin = minBox.Text.Trim();
            if (int.TryParse(rawMin, out int parsedInt) && parsedInt >= 0)
                mod.MinSuggested = parsedInt;
            else if (double.TryParse(rawMin.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedDouble) && parsedDouble >= 0)
                mod.MinSuggested = (int)Math.Floor(parsedDouble);
            else if (string.IsNullOrEmpty(rawMin))
                mod.MinSuggested = 0;
            selected.Add(mod);
        }
        return selected;
    }

    private async void RecheckPriceByMods_Click(object sender, RoutedEventArgs e)
    {
        if (_lastItemEval == null || !_lastItemEval.IsPoeItem) return;
        BtnRecheckPriceByMods.IsEnabled = false;
        try
        {
            var mods = GetCheckedMods();
            string league = string.IsNullOrWhiteSpace(_ai.Config.TradeLeague) ? "Standard" : _ai.Config.TradeLeague;
            string res = await PoeTradePriceService.EstimateCustomPriceAsync(_lastItemEval, league, mods);
            if (!string.IsNullOrWhiteSpace(res))
            {
                _lastItemEval.LivePriceSummary = res;
                ItemPriceBadgeText.Text = res;
                ItemPriceBadge.Visibility = Visibility.Visible;
                RenderLiveListings(_lastItemEval);
                ItemDetailsText.Text = BuildFormattedItemDetails(_lastItemEval);
            }
        }
        finally
        {
            BtnRecheckPriceByMods.IsEnabled = true;
        }
    }

    private void OpenTradeByMods_Click(object sender, RoutedEventArgs e)
    {
        if (_lastItemEval == null || !_lastItemEval.IsPoeItem) return;
        SaveTradeLeague();
        string q = PoeTradePriceService.BuildCustomTradeQueryJson(_lastItemEval, GetCheckedMods());
        TradeLinkBuilder.OpenTradeQuery(_ai.Config.TradeLeague, q);
    }

    private void OpenWikiLastItem_Click(object sender, RoutedEventArgs e)
    {
        string url = !string.IsNullOrWhiteSpace(_lastItemEval?.WikiUrl)
            ? _lastItemEval.WikiUrl
            : "https://poe2db.tw/us/";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void OpenTradeHelper_Click(object sender, RoutedEventArgs e)
    {
        if (_tradeHelper.IsVisible) _tradeHelper.Hide();
        else
        {
            _tradeHelper.RefreshUi();
            _tradeHelper.Show();
            Win32OverlayHelper.EnsureTopmostNoActivate(_tradeHelper);
        }
    }

    private void OpenCommandWheelNearCursor()
    {
        string c1 = CustomCmd1Box?.Text ?? _ai.Config.CustomCmd1;
        string c2 = CustomCmd2Box?.Text ?? _ai.Config.CustomCmd2;
        _commandWheel.ToggleNearCursor(_watcher.LastWhisperPlayer, c1, c2);
    }

    private void ExecutePoeChatMacro(string command)
    {
        bool sentToGame = Win32OverlayHelper.SendPoeChatCommand(command, s => _ignoreClipboardText = s);
        if (!sentToGame)
        {
            _toast.ShowToast(
                Loc.T("Команда скопійована", "Command Copied", "Команда скопирована"),
                Loc.T($"PoE 2 не знайдено — встав у чат гри (Enter → Ctrl+V):\n{command}", $"PoE 2 window not active — paste into chat:\n{command}", $"Окно PoE 2 не активно — вставь в чат:\n{command}"),
                Color.FromRgb(0xB5, 0xAE, 0xA4), nearCursor: false, seconds: 3.5);
        }
    }

    private void EquipCopiedItemToSlot_Click(object sender, RoutedEventArgs e)
    {
        if (_lastItemEval == null || !_lastItemEval.IsPoeItem || string.IsNullOrWhiteSpace(_lastItemEval.RawCopiedText))
            return;

        string slot = string.IsNullOrWhiteSpace(_lastItemEval.MatchedBuildSlot)
            ? ItemEvaluator.MapItemClassToSlotKey(_lastItemEval.ItemClass)
            : _lastItemEval.MatchedBuildSlot;
        if (slot == "Ring1" && _ai.Config.WornGearBySlot.ContainsKey("Ring1") && !_ai.Config.WornGearBySlot.ContainsKey("Ring2"))
            slot = "Ring2";
        _ai.Config.WornGearBySlot[slot] = _lastItemEval.RawCopiedText;
        _ai.SaveConfig();
        UpdateAccountStatusLabel();
        if (_overviewSubtab == 2) RefreshGearObtainSubtab();
        CheckClipboard(force: true);
        _toast.ShowToast(
            PoeTermLocalizer.SlotName(slot),
            Loc.T($"Збережено як твою поточну річ для порівнянь: {_lastItemEval.ItemName}", $"Saved as worn baseline: {_lastItemEval.ItemName}", $"Сохранено как твоя вещь: {_lastItemEval.ItemName}"),
            Color.FromRgb(0x3F, 0xB9, 0x50), nearCursor: false, seconds: 3.5);
    }

    private void CopyVendorRegex_Click(object sender, RoutedEventArgs e)
    {
        string rx = RegexGeneratorService.BuildVendorRegex(_profiles.ActiveProfile);
        SetClipboardSafe(rx);
        _toast.ShowToast(Loc.T("Regex Торгівля", "Vendor Regex", "Regex Торговец"), rx, Color.FromRgb(0xC8, 0x9B, 0x3C), nearCursor: false, seconds: 3.5);
    }

    private void CopyWaystoneRegex_Click(object sender, RoutedEventArgs e)
    {
        string rx = RegexGeneratorService.BuildSafeWaystoneRegex(_profiles.ActiveProfile);
        SetClipboardSafe(rx);
        _toast.ShowToast(Loc.T("Regex Мап", "Safe Waystone Regex", "Regex Карт"), rx, Color.FromRgb(0x3F, 0xB9, 0x50), nearCursor: false, seconds: 3.5);
    }

    private void SetClipboardSafe(string text)
    {
        try
        {
            _ignoreClipboardText = text;
            Clipboard.SetText(text);
        }
        catch { }
    }

    private void RefreshDynamicTradeButtons()
    {
        TradeButtonsPanel.Children.Clear();
        foreach (var sc in TradeLinkBuilder.BuildDynamicShortcuts(_profiles.ActiveProfile))
        {
            var shortcut = sc;
            var btn = new Button
            {
                Content = shortcut.Label,
                Style = (Style)FindResource("FlatButton"),
                Foreground = UiBrushes.FgBrass,
                FontSize = 10,
                Padding = new Thickness(9, 3, 9, 3),
                Margin = new Thickness(0, 0, 5, 5)
            };
            btn.Click += (_, _) =>
            {
                SaveTradeLeague();
                TradeLinkBuilder.OpenTradeQuery(_ai.Config.TradeLeague, shortcut.QueryJson);
            };
            TradeButtonsPanel.Children.Add(btn);
        }
    }

    private void TradeLastItem_Click(object sender, RoutedEventArgs e)
    {
        if (_lastItemEval == null || !_lastItemEval.IsPoeItem) return;
        SaveTradeLeague();
        string url = TradeLinkBuilder.BuildForCopiedItem(_ai.Config.TradeLeague, _lastItemEval);
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void SaveTradeLeague()
    {
        if (!string.IsNullOrWhiteSpace(TradeLeagueBox.Text))
            ApplySelectedLeague(TradeLeagueBox.Text.Trim());
    }

    private static Color GetVerdictColor(ItemVerdict v) => v switch
    {
        ItemVerdict.Great => Color.FromRgb(0x3F, 0xB9, 0x50),
        ItemVerdict.Good => Color.FromRgb(0x3F, 0xB9, 0x50),
        ItemVerdict.Ok => Color.FromRgb(0xC8, 0x9B, 0x3C),
        ItemVerdict.Warn => Color.FromRgb(0xD2, 0x99, 0x22),
        ItemVerdict.Danger => Color.FromRgb(0xE0, 0x52, 0x52),
        _ => Color.FromRgb(0x9C, 0x95, 0x8B)
    };


    // --- Інструменти, Налаштування, Фільтр, Таймери ---
    private void InstallFilter_Click(object sender, RoutedEventArgs e) => InstallFilterCore();

    private void InstallFilterCore()
    {
        var dlg = new OpenFileDialog
        {
            Title = Loc.T("Обери свій .filter файл для PoE 2", "Select custom .filter file for PoE 2", "Выбери свой .filter файл для PoE 2"),
            Filter = "PoE 2 Loot Filter (*.filter)|*.filter|All (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            InstallAndPreviewFilter(dlg.FileName);
            SelectPage(NavBtnItems);
            ShowLootFilterSubtab();
        }
        Win32OverlayHelper.ApplyOverlayStyles(this, noActivate: !_allowTyping, clickThrough: _clickThrough);
    }

    private void InstallAndPreviewFilter(string sourcePath)
    {
        try
        {
            var (ok, dst, content) = LootFilterService.InstallExistingFilter(sourcePath);
            if (!ok) return;
            FilterNameBox.Text = Path.GetFileNameWithoutExtension(dst);
            FilterEditorBox.Text = content;
            var val = LootFilterService.ValidateFilter(content);
            string warnSuffix = val.Warnings.Count > 0 ? $" · ⚠ {string.Join("; ", val.Warnings)}" : " · OK";
            _filterStatusCustom = true;
            FilterStatusText.Text = Loc.T($"Встановлено: {Path.GetFileName(dst)} ({val.BlockCount} блоків{warnSuffix})", $"Installed: {Path.GetFileName(dst)} ({val.BlockCount} blocks{warnSuffix})", $"Установлен: {Path.GetFileName(dst)} ({val.BlockCount} блоков{warnSuffix})");
            FilterStatusText.Foreground = val.Warnings.Count > 0 ? UiBrushes.FgAmber : UiBrushes.FgEmerald;
            InstallFilterBtn.Content = Loc.T("✓ Встановлено!", "✓ Installed!", "✓ Установлено!");
            RefreshInstalledFiltersLabel();
        }
        catch
        {
            _filterStatusCustom = true;
            FilterStatusText.Text = Loc.T("Помилка завантаження .filter", "Error loading .filter", "Ошибка загрузки .filter");
            FilterStatusText.Foreground = UiBrushes.FgCrimson;
        }
    }

    private void ShowItemsEvalSubtab_Click(object sender, RoutedEventArgs e)
    {
        ItemsEvalPanel.Visibility = Visibility.Visible;
        LootFilterPanel.Visibility = Visibility.Collapsed;
        BtnSubItemsEval.Background = UiBrushes.BgBrass;
        BtnSubItemsEval.BorderBrush = UiBrushes.BgBrass;
        BtnSubItemsEval.Foreground = UiBrushes.FgDark;
        BtnSubLootFilter.Background = UiBrushes.BgCard;
        BtnSubLootFilter.BorderBrush = UiBrushes.BorderSubtle;
        BtnSubLootFilter.Foreground = UiBrushes.FgMuted;
    }

    private void ShowLootFilterSubtab_Click(object sender, RoutedEventArgs e) => ShowLootFilterSubtab();

    private void ShowLootFilterSubtab()
    {
        ItemsEvalPanel.Visibility = Visibility.Collapsed;
        LootFilterPanel.Visibility = Visibility.Visible;
        BtnSubLootFilter.Background = UiBrushes.BgBrass;
        BtnSubLootFilter.BorderBrush = UiBrushes.BgBrass;
        BtnSubLootFilter.Foreground = UiBrushes.FgDark;
        BtnSubItemsEval.Background = UiBrushes.BgCard;
        BtnSubItemsEval.BorderBrush = UiBrushes.BorderSubtle;
        BtnSubItemsEval.Foreground = UiBrushes.FgMuted;
        RefreshInstalledFiltersLabel();
        if (string.IsNullOrWhiteSpace(FilterEditorBox.Text) && _profiles.ActiveProfile != null)
            FilterNameBox.Text = string.Concat(_profiles.ActiveProfile.Name.Split([' ', '-', '/', '\\'], StringSplitOptions.RemoveEmptyEntries).Take(3));
    }

    private void RefreshInstalledFiltersLabel()
    {
        var files = LootFilterService.ListInstalledFilters().Select(Path.GetFileName).ToList();
        LblFilterInstalled.Text = files.Count == 0
            ? Loc.T("Фільтри в PoE 2: немає жодного .filter", "PoE 2 Filters: none installed", "Фильтры в PoE 2: нет ни одного .filter")
            : $"{Loc.T("В PoE 2:", "In PoE 2:", "В PoE 2:")} {string.Join(", ", files.Take(4))}{(files.Count > 4 ? $" (+{files.Count - 4})" : "")}";
    }

    private void SetFilterStrict1_Click(object sender, RoutedEventArgs e) => SetFilterStrictness(1);
    private void SetFilterStrict2_Click(object sender, RoutedEventArgs e) => SetFilterStrictness(2);
    private void SetFilterStrict3_Click(object sender, RoutedEventArgs e) => SetFilterStrictness(3);
    private void SetFilterStrict4_Click(object sender, RoutedEventArgs e) => SetFilterStrictness(4);

    private void SetFilterStrictness(int level)
    {
        _filterStrictness = level;
        var btns = new[] { (1, BtnStrict1), (2, BtnStrict2), (3, BtnStrict3), (4, BtnStrict4) };
        foreach (var (lvl, b) in btns)
        {
            bool on = lvl == level;
            b.Background = on ? UiBrushes.BgBrass : UiBrushes.BgCard;
            b.BorderBrush = on ? UiBrushes.BgBrass : UiBrushes.BorderSubtle;
            b.Foreground = on ? UiBrushes.FgDark : UiBrushes.FgMuted;
        }
    }

    private async void GenerateFilterWithAi_Click(object sender, RoutedEventArgs e)
    {
        if (_filterGenBusy) return;
        _filterGenBusy = true;
        _filterStatusCustom = true;
        BtnAiGenerateFilter.Content = "AI …";
        FilterStatusText.Text = Loc.T("Генерую PoE 2 лутофільтр під активний білд...", "Generating PoE 2 loot filter for active build...", "Генерирую PoE 2 лутфильтр под активный билд...");
        FilterStatusText.Foreground = UiBrushes.FgBrass;

        try
        {
            var (ok, text, modelOrErr) = await LootFilterService.GenerateWithAiAsync(_ai, _profiles.ActiveProfile, _filterStrictness, FilterPromptBox.Text);
            if (!ok)
            {
                FilterStatusText.Text = modelOrErr;
                FilterStatusText.Foreground = UiBrushes.FgCrimson;
                return;
            }
            FilterEditorBox.Text = text;
            var val = LootFilterService.ValidateFilter(text);
            string warn = val.Warnings.Count > 0 ? $" · ⚠ {string.Join("; ", val.Warnings)}" : "";
            FilterStatusText.Text = Loc.T($"Згенеровано ({modelOrErr}) · {val.BlockCount} блоків{warn}. Натисни «Зберегти в PoE 2»!", $"Generated ({modelOrErr}) · {val.BlockCount} blocks{warn}. Click 'Save to PoE 2'!", $"Сгенерировано ({modelOrErr}) · {val.BlockCount} блоков{warn}. Нажми «Сохранить в PoE 2»!");
            FilterStatusText.Foreground = val.IsValid && val.Warnings.Count == 0 ? UiBrushes.FgEmerald : UiBrushes.FgAmber;
        }
        finally
        {
            _filterGenBusy = false;
            BtnAiGenerateFilter.Content = Loc.T("AI-генерувати", "AI Generate", "AI-генерировать");
        }
    }

    private void SaveFilterEditorToGame_Click(object sender, RoutedEventArgs e)
    {
        string text = FilterEditorBox.Text;
        _filterStatusCustom = true;
        var val = LootFilterService.ValidateFilter(text);
        if (!val.IsValid)
        {
            FilterStatusText.Text = string.Join(" ", val.Warnings);
            FilterStatusText.Foreground = UiBrushes.FgCrimson;
            return;
        }
        try
        {
            string savedPath = LootFilterService.SaveCustomFilter(FilterNameBox.Text, text);
            string warn = val.Warnings.Count > 0 ? $" · ⚠ {string.Join("; ", val.Warnings)}" : "";
            FilterStatusText.Text = Loc.T($"Збережено в PoE 2: {Path.GetFileName(savedPath)} ({val.BlockCount} блоків{warn})", $"Saved to PoE 2: {Path.GetFileName(savedPath)} ({val.BlockCount} blocks{warn})", $"Сохранено в PoE 2: {Path.GetFileName(savedPath)} ({val.BlockCount} блоков{warn})");
            FilterStatusText.Foreground = UiBrushes.FgEmerald;
            RefreshInstalledFiltersLabel();
        }
        catch (Exception ex)
        {
            FilterStatusText.Text = ex.Message;
            FilterStatusText.Foreground = UiBrushes.FgCrimson;
        }
    }
}
