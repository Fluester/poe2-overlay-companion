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
    // --- Візуальний Огляд Білду + Гемопедія + Де взяти шмотки ---
    private void ShowOverviewMainSubtab_Click(object sender, RoutedEventArgs e) => SetOverviewSubtab(0);
    private void ShowOverviewGemsSubtab_Click(object sender, RoutedEventArgs e) => SetOverviewSubtab(1);
    private void ShowOverviewGearSubtab_Click(object sender, RoutedEventArgs e) => SetOverviewSubtab(2);

    private void SetOverviewSubtab(int idx)
    {
        _overviewSubtab = idx;
        OverviewMainSubPanel.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
        OverviewGemsPanel.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
        OverviewGearPanel.Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;

        void StyleSub(Button b, bool on)
        {
            b.Background = on ? UiBrushes.BgBrass : UiBrushes.BgCard;
            b.BorderBrush = on ? UiBrushes.BorderBrass : UiBrushes.BorderSubtle;
            b.Foreground = on ? UiBrushes.FgDark : UiBrushes.FgMuted;
            b.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
        }
        StyleSub(BtnSubOverviewMain, idx == 0);
        StyleSub(BtnSubOverviewGems, idx == 1);
        StyleSub(BtnSubOverviewGear, idx == 2);

        if (idx == 1) RefreshGemsSubtab();
        else if (idx == 2) RefreshGearObtainSubtab();
    }

    private void GemFilterChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded && _overviewSubtab == 1) RefreshGemsSubtab();
    }

    private void SetGemFilterBuild_Click(object sender, RoutedEventArgs e) => SetGemFilterMode("build");
    private void SetGemFilterAll_Click(object sender, RoutedEventArgs e) => SetGemFilterMode("all");
    private void SetGemFilterActive_Click(object sender, RoutedEventArgs e) => SetGemFilterMode("active");
    private void SetGemFilterSupport_Click(object sender, RoutedEventArgs e) => SetGemFilterMode("support");
    private void SetGemFilterSpirit_Click(object sender, RoutedEventArgs e) => SetGemFilterMode("spirit");

    private void SetGemFilterMode(string mode)
    {
        _gemFilterMode = mode;
        foreach (var (b, m) in new[] { (BtnGemFilterBuild, "build"), (BtnGemFilterAll, "all"), (BtnGemFilterActive, "active"), (BtnGemFilterSupport, "support"), (BtnGemFilterSpirit, "spirit") })
        {
            bool on = m == mode;
            b.Background = on ? UiBrushes.BgBrass : UiBrushes.BgCard;
            b.Foreground = on ? UiBrushes.FgDark : UiBrushes.FgMuted;
            b.BorderBrush = on ? UiBrushes.BorderBrass : UiBrushes.BorderSubtle;
        }
        RefreshGemsSubtab();
    }

    private void RefreshOverview()
    {
        OverviewCardsPanel.Children.Clear();
        OverviewInspectorCard.Visibility = Visibility.Collapsed;
        var prof = _profiles.ActiveProfile;
        bool hasAccount = !string.IsNullOrWhiteSpace(_ai.Config.PoeCharacterName) ||
                          _ai.Config.WornGearBySlot.Count > 0 ||
                          _ai.Config.PoeCharacterSkills.Count > 0;
        int curLvl = EffectiveLevel;

        if (prof == null && !hasAccount)
        {
            HeaderBuildBadge.Text = Loc.NoBuildSelected;
            OverviewBuildTitle.Text = Loc.NoBuildSelected;
            OverviewBuildMeta.Text = Loc.T("Імпортуй з Mobalytics, .build файли, PoB2 або URL", "Import from Mobalytics, .build files, PoB2, or URL", "Импортируй с Mobalytics, .build файлы, PoB2 или URL");
            OverviewStageNav.Visibility = Visibility.Collapsed;
            OverviewEmptyCard.Visibility = Visibility.Visible;
            OverviewContentWrap.Visibility = Visibility.Collapsed;
            if (_overviewSubtab == 1) RefreshGemsSubtab();
            else if (_overviewSubtab == 2) RefreshGearObtainSubtab();
            return;
        }

        OverviewEmptyCard.Visibility = Visibility.Collapsed;
        OverviewContentWrap.Visibility = Visibility.Visible;

        if (prof == null)
        {
            HeaderBuildBadge.Text = Loc.NoBuildSelected;
            OverviewBuildTitle.Text = !string.IsNullOrWhiteSpace(_ai.Config.PoeCharacterName)
                ? $"{_ai.Config.PoeCharacterName} · {Loc.LevelShort}{curLvl}"
                : Loc.T("Твій персонаж (без білду)", "Your Character (No Build)", "Твой персонаж (без билда)");
            OverviewBuildMeta.Text = Loc.T(
                $"PoE 2 Акаунт (відображається окремо від білду) · {(string.IsNullOrWhiteSpace(_ai.Config.PoeCharacterClass) ? "PoE 2" : _ai.Config.PoeCharacterClass)} · {_ai.Config.TradeLeague}",
                $"PoE 2 Account (separate from builds) · {(string.IsNullOrWhiteSpace(_ai.Config.PoeCharacterClass) ? "PoE 2" : _ai.Config.PoeCharacterClass)} · {_ai.Config.TradeLeague}",
                $"PoE 2 Аккаунт (отдельно от билда) · {(string.IsNullOrWhiteSpace(_ai.Config.PoeCharacterClass) ? "PoE 2" : _ai.Config.PoeCharacterClass)} · {_ai.Config.TradeLeague}");
            OverviewStageNav.Visibility = Visibility.Collapsed;

            // Компактна панель додавання окремого білд-гайда (щоб не плутати акаунт із білдом)
            var ctaStack = new StackPanel();
            ctaStack.Children.Add(new TextBlock
            {
                Text = Loc.T("Білд-гайд ще не додано (використовуються дані твого акаунта)", "No build guide imported yet (showing live account character)", "Билд-гайд ещё не добавлен (показан персонаж аккаунта)"),
                Foreground = UiBrushes.FgBrass,
                FontWeight = FontWeights.SemiBold,
                FontSize = 11
            });
            ctaStack.Children.Add(new TextBlock
            {
                Text = Loc.T("Імпортуй сторонній білд із Mobalytics, .build або PoB2 — тоді всі предмети й пасивки будуть порівнюватись І з твоїм екіпом, І з білд-гайдом.", "Import a build guide from Mobalytics, .build, or PoB2 to unlock passive tree progression and side-by-side build comparisons.", "Импортируй билд-гайд с Mobalytics, .build или PoB2 для дерева пассивок и двойного сравнения."),
                Foreground = UiBrushes.FgMuted,
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 6)
            });
            var ctaBtns = new WrapPanel();
            var btnMob = new Button { Content = "+ Mobalytics", Style = (Style)FindResource("BrassButton"), FontSize = 9.5, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 6, 0) };
            btnMob.Click += OpenMobalyticsImport_Click;
            var btnImp = new Button { Content = Loc.T("+ Імпорт білду (.build / PoB2)", "+ Import Build (.build / PoB2)", "+ Импорт билда"), Style = (Style)FindResource("FlatButton"), FontSize = 9.5, Padding = new Thickness(8, 3, 8, 3) };
            btnImp.Click += GoToBuildsTab_Click;
            ctaBtns.Children.Add(btnMob);
            ctaBtns.Children.Add(btnImp);
            ctaStack.Children.Add(ctaBtns);
            OverviewCardsPanel.Children.Add(new Border
            {
                Background = UiBrushes.BgCard,
                BorderBrush = UiBrushes.BorderBrass,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 8),
                Child = ctaStack
            });

            if (!string.IsNullOrWhiteSpace(_ai.Config.PoeCharacterName))
            {
                string accSummary = FormatAccountStatsSummary();
                OverviewCardsPanel.Children.Add(CreateSimpleRowCard(
                    $"{Loc.T("PoE 2 Акаунт", "PoE 2 Account", "PoE 2 Аккаунт")}: {_ai.Config.PoeCharacterName} · {Loc.LevelShort} {_ai.Config.PoeCharacterLevel} {_ai.Config.PoeCharacterClass} ({_ai.Config.TradeLeague})",
                    !string.IsNullOrWhiteSpace(accSummary)
                        ? accSummary
                        : $"{_ai.Config.WornGearBySlot.Count} {Loc.T("слотів", "slots", "слотов")}"));
            }

            if (_ai.Config.PoeCharacterSkills.Count > 0)
            {
                AddSectionHeader(OverviewCardsPanel, $"{Loc.T("Скіли персонажа", "Synced Character Skills", "Скиллы персонажа")} ({_ai.Config.PoeCharacterSkills.Count})", topMargin: 8);
                foreach (var sig in _ai.Config.PoeCharacterSkills)
                {
                    var arrowSplit = sig.Split("->", 2);
                    string mainSkill = arrowSplit[0].Trim();
                    var sups = arrowSplit.Length == 2
                        ? arrowSplit[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
                        : [];
                    OverviewCardsPanel.Children.Add(CreateSkillLinkCard(mainSkill, sups, curLvl));
                }
            }

            if (_ai.Config.WornGearBySlot.Count > 0)
            {
                AddSectionHeader(OverviewCardsPanel, $"{Loc.T("Вдягнуті предмети акаунта", "Equipped Account Gear", "Надетая экипировка")} ({_ai.Config.WornGearBySlot.Count})", topMargin: 10);
                foreach (var kv in _ai.Config.WornGearBySlot)
                {
                    string title = kv.Value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                        .SkipWhile(l => l.StartsWith("Item Class:", StringComparison.OrdinalIgnoreCase) || l.StartsWith("Rarity:", StringComparison.OrdinalIgnoreCase) || l.StartsWith("--------"))
                        .FirstOrDefault()?.Trim() ?? kv.Key;
                    OverviewCardsPanel.Children.Add(CreateGearTargetCard(kv.Key, PoeTermLocalizer.SlotName(kv.Key), title, kv.Value));
                }
            }

            string inferredWep = PoeAccountService.InferCharacterWeaponClass(_ai.Config.WornGearBySlot);
            string stageAdvice = PoeAccountService.BuildStageAdvice(
                curLvl,
                _ai.Config.PoeCharacterClass,
                _ai.Config.TradeLeague,
                inferredWep,
                _ai.Config.PoeCharacterSkills,
                _ai.Config.WornGearBySlot);
            AddSectionHeader(OverviewCardsPanel, Loc.T("Аналіз та поради по етапу", "Stage Advice & Diagnostics", "Советы по этапу"), topMargin: 10);
            OverviewCardsPanel.Children.Add(CreateSimpleRowCard(Loc.T("Поради під твій поточний рівень і екіп", "Advice for your current level & gear", "Советы под твой уровень и экип"), stageAdvice));
        }
        else
        {
            string shortBadge = prof.Name.Length > 24 ? prof.Name[..23] + "…" : prof.Name;
            HeaderBuildBadge.Text = shortBadge;
            OverviewBuildTitle.Text = prof.Name;

            string asc = string.IsNullOrWhiteSpace(prof.Ascendancy) ? prof.ClassName : prof.Ascendancy;
            string def = PoeTermLocalizer.DefenseLabel(prof.DefenseFocus);
            string wep = PoeTermLocalizer.WeaponClassLabel(prof.PreferredWeaponClass);
            string ciTag = prof.IsCi ? " · CI" : "";
            OverviewBuildMeta.Text = $"{PoeTermLocalizer.BuildSourceLabel(prof.SourceKind)} · {(string.IsNullOrEmpty(asc) ? "PoE 2" : asc)} · {def} · {wep}{ciTag}";

            BuildStageEntry? activeStage = null;
            if (prof.Stages.Count > 0)
            {
                _overviewStageIdx = Math.Clamp(_overviewStageIdx, 0, prof.Stages.Count - 1);
                activeStage = prof.Stages[_overviewStageIdx];
                OverviewStageNav.Visibility = prof.Stages.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
                OverviewStageLabel.Text = ShortStageTitle(activeStage.Name, 22);
            }
            else
            {
                OverviewStageNav.Visibility = Visibility.Collapsed;
            }

            // Отримуємо живі скіли та шмотки для поточного етапу
            LocalBuildPlan? livePlan = null;
            if (activeStage != null && !string.IsNullOrWhiteSpace(activeStage.FilePath) && File.Exists(activeStage.FilePath))
                livePlan = BuildPlannerService.TryParseFile(activeStage.FilePath);

            if (curLvl > 0 && !string.IsNullOrWhiteSpace(_ai.Config.PoeCharacterName))
            {
                string accSummary = FormatAccountStatsSummary();
                OverviewCardsPanel.Children.Add(CreateSimpleRowCard(
                    $"{Loc.T("PoE 2 Акаунт", "PoE 2 Account", "PoE 2 Аккаунт")}: {_ai.Config.PoeCharacterName} · {Loc.LevelShort} {_ai.Config.PoeCharacterLevel} {_ai.Config.PoeCharacterClass} ({_ai.Config.TradeLeague})",
                    !string.IsNullOrWhiteSpace(accSummary)
                        ? accSummary
                        : $"{_ai.Config.WornGearBySlot.Count} {Loc.T("слотів", "slots", "слотов")}"));
            }

            if (livePlan != null)
            {
                var activeSkills = livePlan.Skills.Where(s => curLvl >= s.MinLevel && curLvl <= s.MaxLevel).ToList();
                if (activeSkills.Count == 0) activeSkills = livePlan.Skills;

                AddSectionHeader(OverviewCardsPanel, $"{Loc.LblActiveSkills} ({activeSkills.Count})");
                foreach (var sk in activeSkills)
                    OverviewCardsPanel.Children.Add(CreateSkillLinkCard(sk.SkillName, sk.Supports, sk.MinLevel));

                var upcomingSkills = livePlan.Skills.Where(s => s.MinLevel > curLvl).OrderBy(s => s.MinLevel).Take(4).ToList();
                if (upcomingSkills.Count > 0)
                {
                    AddSectionHeader(OverviewCardsPanel, Loc.LblUnlocksNext, topMargin: 10);
                    foreach (var sk in upcomingSkills)
                        OverviewCardsPanel.Children.Add(CreateSimpleRowCard($"{Loc.LevelShort} {sk.MinLevel}: {sk.SkillName}", string.Join(", ", sk.Supports)));
                }

                AddSectionHeader(OverviewCardsPanel, $"{Loc.LblGearTargets} ({livePlan.Gear.Count})", topMargin: 10);
                foreach (var g in livePlan.Gear)
                    OverviewCardsPanel.Children.Add(CreateGearTargetCard(g.RawSlotId, PoeTermLocalizer.SlotName(g.RawSlotId), PoeTermLocalizer.TranslateGearDescription(g.Text), g.Text));
            }
            else if (prof.SourceKind == BuildSourceKind.Pob2Import && !string.IsNullOrWhiteSpace(prof.Pob2Code))
            {
                _cachedActivePob ??= Pob2Parser.Parse(prof.Pob2Code);
                if (_cachedActivePob.Success)
                {
                    AddSectionHeader(OverviewCardsPanel, $"{Loc.LblActiveSkills} ({_cachedActivePob.SkillGroups.Count})");
                    foreach (var g in _cachedActivePob.SkillGroups)
                        OverviewCardsPanel.Children.Add(CreateSkillLinkCard(g.MainSkill, g.Supports, 1));

                    AddSectionHeader(OverviewCardsPanel, Loc.LblGearTargets, topMargin: 10);
                    foreach (var itemLine in _cachedActivePob.EquippedItems)
                    {
                        var parts = itemLine.Split(':', 2);
                        string rawSlot = parts.Length == 2 ? parts[0].Trim() : "Item";
                        string slot = parts.Length == 2 ? PoeTermLocalizer.SlotName(rawSlot) : "Item";
                        string desc = parts.Length == 2 ? parts[1].Trim() : itemLine;
                        string fullRaw = _cachedActivePob.RawEquippedBySlot.TryGetValue(rawSlot, out var rawItem) ? rawItem : desc;
                        OverviewCardsPanel.Children.Add(CreateGearTargetCard(rawSlot, slot, desc, fullRaw));
                    }

                    if (_cachedActivePob.Recommendations.Count > 0)
                    {
                        AddSectionHeader(OverviewCardsPanel, Loc.T("Примітки по PoB2", "PoB2 Notes", "Заметки PoB2"), topMargin: 10);
                        foreach (var r in _cachedActivePob.Recommendations)
                            OverviewCardsPanel.Children.Add(CreateSimpleRowCard(r, ""));
                    }
                }
            }
            else if (activeStage != null && (activeStage.SkillSignatures.Count > 0 || activeStage.GearSignatures.Count > 0))
            {
                if (activeStage.SkillSignatures.Count > 0)
                {
                    AddSectionHeader(OverviewCardsPanel, $"{Loc.LblActiveSkills} ({activeStage.SkillSignatures.Count})");
                    foreach (var sig in activeStage.SkillSignatures)
                    {
                        var arrowSplit = sig.Split("->", 2);
                        string mainSkill = arrowSplit[0].Trim();
                        var sups = arrowSplit.Length == 2
                            ? arrowSplit[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
                            : [];
                        OverviewCardsPanel.Children.Add(CreateSkillLinkCard(mainSkill, sups, activeStage.MinLevel));
                    }
                }

                if (activeStage.GearSignatures.Count > 0)
                {
                    AddSectionHeader(OverviewCardsPanel, $"{Loc.LblGearTargets} ({activeStage.GearSignatures.Count})", topMargin: 10);
                    foreach (var gh in activeStage.GearSignatures)
                    {
                        var parts = gh.Split(':', 2);
                        string rawSlot = parts.Length == 2 ? parts[0].Trim() : "Item";
                        string desc = parts.Length == 2 ? parts[1].Trim() : gh;
                        string fullRaw = activeStage.GearDetailsBySlot.TryGetValue(rawSlot, out var rawItem)
                            ? rawItem
                            : _ai.Config.WornGearBySlot.TryGetValue(rawSlot, out var wornRaw) ? wornRaw : desc;
                        OverviewCardsPanel.Children.Add(CreateGearTargetCard(rawSlot, PoeTermLocalizer.SlotName(rawSlot), PoeTermLocalizer.TranslateGearDescription(desc), fullRaw));
                    }
                }

                string stageNote = !string.IsNullOrWhiteSpace(activeStage.Notes) ? activeStage.Notes : prof.SummaryNotes;
                if (string.IsNullOrWhiteSpace(stageNote))
                {
                    stageNote = PoeAccountService.BuildStageAdvice(
                        curLvl,
                        prof.ClassName,
                        _ai.Config.TradeLeague,
                        prof.PreferredWeaponClass,
                        activeStage.SkillSignatures,
                        _ai.Config.WornGearBySlot.Count > 0 ? _ai.Config.WornGearBySlot : activeStage.GearDetailsBySlot);
                }
                if (!string.IsNullOrWhiteSpace(stageNote))
                {
                    AddSectionHeader(OverviewCardsPanel, Loc.T("Опис / Нотатки варіанта", "Variant Notes", "Заметки варианта"), topMargin: 10);
                    OverviewCardsPanel.Children.Add(CreateSimpleRowCard(Loc.T("Поради по етапу", "Stage Guide Notes", "Советы по этапу"), stageNote));
                }
            }
            else
            {
                if (prof.TargetSkills.Count > 0)
                {
                    AddSectionHeader(OverviewCardsPanel, Loc.LblActiveSkills);
                    foreach (var sk in prof.TargetSkills)
                        OverviewCardsPanel.Children.Add(CreateSkillLinkCard(sk, [], 1));
                }
                if (prof.TargetGearHints.Count > 0)
                {
                    AddSectionHeader(OverviewCardsPanel, Loc.LblGearTargets, topMargin: 10);
                    foreach (var gh in prof.TargetGearHints)
                    {
                        var parts = gh.Split(':', 2);
                        string rawSlot = parts.Length == 2 ? parts[0].Trim() : "Item";
                        string desc = parts.Length == 2 ? parts[1].Trim() : gh;
                        OverviewCardsPanel.Children.Add(CreateGearTargetCard(rawSlot, PoeTermLocalizer.SlotName(rawSlot), PoeTermLocalizer.StatLine(desc), desc));
                    }
                }
                if (!string.IsNullOrWhiteSpace(prof.SummaryNotes))
                {
                    AddSectionHeader(OverviewCardsPanel, Loc.T("Опис / Нотатки", "Notes", "Заметки"), topMargin: 10);
                    OverviewCardsPanel.Children.Add(CreateSimpleRowCard(prof.SummaryNotes, ""));
                }
            }
        }

        // Mobalytics-подібний чекліст постійних нагород кампанії PoE 2 (+Spirit, +Пасивки, +Резисти)
        AddSectionHeader(OverviewCardsPanel, Loc.T("Чекліст нагород кампанії (+Spirit / +Пасивки / +Резисти)", "Campaign Permanent Rewards", "Чеклист наград кампании"), topMargin: 12);
        var checklistWrap = new StackPanel();
        foreach (var ms in CampaignGuideDb.PermanentMilestones)
        {
            var chk = new CheckBox
            {
                Content = Loc.T(ms.TitleUk, ms.TitleEn, ms.TitleRu),
                IsChecked = _ai.Config.CompletedCampaignRewards.Contains(ms.Id),
                Style = (Style)FindResource("DarkCheck"),
                FontSize = 10.5,
                Margin = new Thickness(0, 1.5, 0, 2.5)
            };
            string milestoneId = ms.Id;
            chk.Checked += (_, _) => { _ai.Config.CompletedCampaignRewards.Add(milestoneId); _ai.SaveConfig(); };
            chk.Unchecked += (_, _) => { _ai.Config.CompletedCampaignRewards.Remove(milestoneId); _ai.SaveConfig(); };
            checklistWrap.Children.Add(chk);
        }
        OverviewCardsPanel.Children.Add(new Border
        {
            Background = UiBrushes.BgCard,
            BorderBrush = UiBrushes.BorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(9, 7, 9, 7),
            Child = checklistWrap
        });

        if (_overviewSubtab == 1) RefreshGemsSubtab();
        else if (_overviewSubtab == 2) RefreshGearObtainSubtab();
    }

    private void RefreshGemsSubtab()
    {
        GemsCatalogPanel.Children.Clear();
        int curLvl = EffectiveLevel;
        string q = (GemSearchBox.Text ?? "").Trim();

        if (_gemFilterMode == "build")
        {
            var buildGemNames = CollectActiveStageGemPairs();
            if (buildGemNames.Count == 0)
            {
                GemsCatalogPanel.Children.Add(CreateSimpleRowCard(
                    Loc.T("У активному білді поки немає каменів", "No gems in active build yet", "В активном билде пока нет камней"),
                    Loc.T("Перемкнися на «Каталог PoE 2» вище або імпортуй білд (.build / PoB2 / Mobalytics).", "Switch to 'PoE 2 Catalog' above or import a build.", "Переключись на «Каталог PoE 2» выше или импортируй билд.")));
                return;
            }

            foreach (var (rawGem, minStageLvl) in buildGemNames)
            {
                var info = Poe2GemsDb.ResolveGem(rawGem, minStageLvl);
                if (!string.IsNullOrEmpty(q) &&
                    !info.Name.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                    !info.Tags.Contains(q, StringComparison.OrdinalIgnoreCase))
                    continue;
                GemsCatalogPanel.Children.Add(CreateGemDetailCard(info, curLvl));
            }
            return;
        }

        GemCategory? cat = _gemFilterMode switch
        {
            "active" => GemCategory.ActiveSkill,
            "support" => GemCategory.Support,
            "spirit" => GemCategory.SpiritBuff,
            _ => null
        };
        var items = Poe2GemsDb.GetAllCatalogEntries(q, cat);
        if (_gemFilterMode == "spirit")
            items = items.Concat(Poe2GemsDb.GetAllCatalogEntries(q, GemCategory.MetaTrigger)).DistinctBy(x => x.Name).OrderBy(x => x.MinUnlockCharacterLevel).ToList();

        foreach (var g in items)
            GemsCatalogPanel.Children.Add(CreateGemDetailCard(g, curLvl));
    }

    private List<(string gemName, int minLvl)> CollectActiveStageGemPairs()
    {
        var list = new List<(string, int)>();
        var prof = _profiles.ActiveProfile;
        if (prof == null)
        {
            foreach (var sig in _ai.Config.PoeCharacterSkills)
            {
                var p = sig.Split("->", 2);
                list.Add((p[0].Trim(), Math.Max(1, _ai.Config.PoeCharacterLevel)));
                if (p.Length == 2)
                    foreach (var sup in p[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        list.Add((sup, Math.Max(1, _ai.Config.PoeCharacterLevel)));
            }
            return list.DistinctBy(x => x.Item1.ToLowerInvariant()).ToList();
        }

        if (prof.Stages.Count > 0 && _overviewStageIdx < prof.Stages.Count)
        {
            var st = prof.Stages[_overviewStageIdx];
            if (!string.IsNullOrWhiteSpace(st.FilePath) && File.Exists(st.FilePath))
            {
                var plan = BuildPlannerService.TryParseFile(st.FilePath);
                if (plan != null)
                {
                    foreach (var s in plan.Skills)
                    {
                        list.Add((s.SkillName, s.MinLevel));
                        foreach (var sup in s.Supports) list.Add((sup, s.MinLevel));
                    }
                    return list.DistinctBy(x => x.Item1.ToLowerInvariant()).ToList();
                }
            }
            foreach (var sig in st.SkillSignatures)
            {
                var p = sig.Split("->", 2);
                list.Add((p[0].Trim(), st.MinLevel));
                if (p.Length == 2)
                    foreach (var sup in p[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        list.Add((sup, st.MinLevel));
            }
        }
        else if (prof.SourceKind == BuildSourceKind.Pob2Import && !string.IsNullOrWhiteSpace(prof.Pob2Code))
        {
            _cachedActivePob ??= Pob2Parser.Parse(prof.Pob2Code);
            if (_cachedActivePob.Success)
            {
                foreach (var g in _cachedActivePob.SkillGroups)
                {
                    list.Add((g.MainSkill, 1));
                    foreach (var sup in g.Supports) list.Add((sup, 1));
                }
            }
        }
        return list.DistinctBy(x => x.Item1.ToLowerInvariant()).ToList();
    }

    private void RefreshGearObtainSubtab()
    {
        GearObtainPanel.Children.Clear();
        int curLvl = EffectiveLevel;

        // Поточні вдягнуті слоти користувача (основні + 2-й набір зброї + фласки/чарми, якщо синхронізовані з акаунта)
        AddSectionHeader(GearObtainPanel, Loc.T($"Твій поточний екіп (Вдягнуто зараз · Рів.{curLvl})", $"Your Currently Equipped Gear (Lvl {curLvl})", $"Твой текущий экип (Ур.{curLvl})"));
        var wornWrap = new StackPanel();
        string[] coreSlots = ["Weapon1", "Offhand1", "Weapon2", "Offhand2", "Helm1", "BodyArmour1", "Gloves1", "Boots1", "Amulet1", "Ring1", "Ring2", "Ring3", "Belt1", "Flask1", "Flask2", "Charm1", "Charm2", "Charm3"];
        foreach (string slotKey in coreSlots)
        {
            bool hasItem = _ai.Config.WornGearBySlot.TryGetValue(slotKey, out string? wornText) && !string.IsNullOrWhiteSpace(wornText);
            bool optionalSlot = slotKey is "Weapon2" or "Offhand2" or "Ring3" or "Flask1" or "Flask2" or "Charm1" or "Charm2" or "Charm3";
            if (!hasItem && optionalSlot) continue;

            string firstLine = hasItem
                ? (wornText!.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                    .SkipWhile(l => l.StartsWith("Item Class:", StringComparison.OrdinalIgnoreCase) || l.StartsWith("Rarity:", StringComparison.OrdinalIgnoreCase) || l.StartsWith("--------"))
                    .FirstOrDefault()?.Trim() ?? slotKey)
                : Loc.T("не задано (Ctrl+C -> Вдягнути / Синхр.)", "empty (Ctrl+C -> Equip / Account Sync)", "пусто");

            var row = new DockPanel { Margin = new Thickness(0, 1.5, 0, 1.5) };
            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            DockPanel.SetDock(btns, Dock.Right);

            string capturedSlot = slotKey;
            if (hasItem)
            {
                var btnInspect = new Button
                {
                    Content = Loc.T("Оглянути", "Inspect", "Осмотр"),
                    Style = (Style)FindResource("FlatButton"),
                    Foreground = UiBrushes.FgEmerald,
                    FontSize = 9,
                    Padding = new Thickness(6, 1, 6, 1),
                    Margin = new Thickness(4, 0, 0, 0)
                };
                btnInspect.Click += (_, _) => InspectEquippedSlotInItemsTab(capturedSlot);
                btns.Children.Add(btnInspect);
            }

            var btnPaste = new Button
            {
                Content = Loc.T("З буфера", "From Clip", "Из буфера"),
                Style = (Style)FindResource("FlatButton"),
                FontSize = 9,
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(4, 0, 0, 0)
            };
            btnPaste.Click += (_, _) =>
            {
                try
                {
                    if (!Clipboard.ContainsText()) return;
                    string clip = Clipboard.GetText();
                    if (!clip.Contains("Item Class:")) return;
                    _ai.Config.WornGearBySlot[capturedSlot] = clip;
                    _ai.SaveConfig();
                    UpdateAccountStatusLabel();
                    RefreshGearObtainSubtab();
                    RefreshWornSlotsStrip();
                    if (_lastItemEval != null) CheckClipboard(force: true);
                }
                catch { }
            };
            btns.Children.Add(btnPaste);

            if (!string.IsNullOrWhiteSpace(wornText))
            {
                var btnClr = new Button
                {
                    Content = "×",
                    Style = (Style)FindResource("FlatButton"),
                    Foreground = UiBrushes.FgCrimson,
                    FontSize = 9,
                    Padding = new Thickness(5, 1, 5, 1),
                    Margin = new Thickness(3, 0, 0, 0)
                };
                btnClr.Click += (_, _) =>
                {
                    _ai.Config.WornGearBySlot.Remove(capturedSlot);
                    _ai.SaveConfig();
                    UpdateAccountStatusLabel();
                    RefreshGearObtainSubtab();
                    RefreshWornSlotsStrip();
                };
                btns.Children.Add(btnClr);
            }

            row.Children.Add(btns);
            var rowTextStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            rowTextStack.Children.Add(new TextBlock
            {
                Text = $"{PoeTermLocalizer.SlotName(slotKey)}: {firstLine}",
                Foreground = !string.IsNullOrWhiteSpace(wornText) ? UiBrushes.FgPrimary : UiBrushes.FgMuted,
                FontSize = 10.5,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            if (hasItem)
            {
                string compact = PoeAccountService.SummarizeItemStatsCompact(wornText!);
                if (!string.IsNullOrWhiteSpace(compact))
                {
                    rowTextStack.Children.Add(new TextBlock
                    {
                        Text = compact,
                        Foreground = UiBrushes.FgSteel,
                        FontSize = 9.3,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });
                }
            }
            row.Children.Add(rowTextStack);
            wornWrap.Children.Add(row);
        }
        GearObtainPanel.Children.Add(new Border
        {
            Background = UiBrushes.BgCard,
            BorderBrush = UiBrushes.BorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(9, 7, 9, 7),
            Margin = new Thickness(0, 0, 0, 10),
            Child = wornWrap
        });

        var prof = _profiles.ActiveProfile;
        var items = new List<(string slotId, string text)>();
        if (prof == null)
        {
            if (_ai.Config.WornGearBySlot.Count > 0)
            {
                items.AddRange(_ai.Config.WornGearBySlot.Select(kv => (kv.Key, kv.Value)));
            }
            else
            {
                GearObtainPanel.Children.Add(CreateSimpleRowCard(
                    Loc.T("Спочатку обери або імпортуй білд", "Select or import a build first", "Сначала выбери или импортируй билд"),
                    Loc.T("Тут покаже кожен слот екіпу з інструкцією, де його вибити, який потрібен ilvl та як оцінити вигоду.", "Shows every gear slot, boss/drop source, ilvl breakpoints, and craft tips.", "Покажет каждый слот экипа, откуда он падает и какой нужен ilvl.")));
                return;
            }
        }
        else if (prof.Stages.Count > 0 && _overviewStageIdx < prof.Stages.Count)
        {
            var st = prof.Stages[_overviewStageIdx];
            if (!string.IsNullOrWhiteSpace(st.FilePath) && File.Exists(st.FilePath))
            {
                var plan = BuildPlannerService.TryParseFile(st.FilePath);
                if (plan != null) items.AddRange(plan.Gear.Select(g => (g.RawSlotId, g.Text)));
            }
            if (items.Count == 0 && st.GearDetailsBySlot.Count > 0)
                items.AddRange(st.GearDetailsBySlot.Select(kv => (kv.Key, kv.Value)));
            if (items.Count == 0)
            {
                foreach (var sig in st.GearSignatures)
                {
                    var p = sig.Split(':', 2);
                    items.Add((p.Length == 2 ? p[0].Trim() : "Item", p.Length == 2 ? p[1].Trim() : sig));
                }
            }
        }
        else if (prof != null && prof.TargetGearHints.Count > 0)
        {
            foreach (var h in prof.TargetGearHints)
            {
                var p = h.Split(':', 2);
                items.Add((p.Length == 2 ? p[0].Trim() : "Item", p.Length == 2 ? p[1].Trim() : h));
            }
        }

        if (items.Count == 0)
        {
            GearObtainPanel.Children.Add(CreateSimpleRowCard(
                Loc.T("Немає записаних слотів шмоток у цьому білді", "No gear entries recorded in this stage", "В этом этапе нет записей по вещам"),
                Loc.T("Імпортуй .build, PoB2 або скористайся Ctrl+C у грі у вкладці «Предмети» для аналізу дропу.", "Import .build/PoB2 or press Ctrl+C in-game on any item inside the Item Check tab.", "Импортируй .build/PoB2 или нажми Ctrl+C в игре во вкладке «Предметы».")));
            return;
        }

        AddSectionHeader(GearObtainPanel, Loc.T($"Де отримати / скрафтити під твій рівень (Рів.{curLvl})", $"Where to Obtain / Craft for Lvl {curLvl}", $"Где выбить / скрафтить (Ур.{curLvl})"));
        foreach (var (slotId, rawText) in items)
        {
            var obt = Poe2KnowledgeDb.ResolveItemObtain(rawText, slotId, prof, curLvl);
            string slotLabel = PoeTermLocalizer.SlotName(slotId);
            string src = Loc.T(obt.ObtainSourceUk, obt.ObtainSourceEn, obt.ObtainSourceRu);
            string tip = Loc.T(obt.StrategyTipUk, obt.StrategyTipEn, obt.StrategyTipRu);
            string firstLine = rawText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .SkipWhile(l => l.StartsWith("Item Class:", StringComparison.OrdinalIgnoreCase) || l.StartsWith("Rarity:", StringComparison.OrdinalIgnoreCase) || l.StartsWith("--------"))
                .FirstOrDefault()?.Trim() ?? obt.ItemTitle;
            string compactStats = PoeAccountService.SummarizeItemStatsCompact(rawText);

            var card = new Border
            {
                Background = UiBrushes.BgCard,
                BorderBrush = UiBrushes.BorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 6)
            };
            var st = new StackPanel();
            var top = new DockPanel();
            var tradeBtn = new Button
            {
                Content = "Trade 2",
                Style = (Style)FindResource("FlatButton"),
                Foreground = UiBrushes.FgSteel,
                FontSize = 9.5,
                Padding = new Thickness(8, 2, 8, 2),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            DockPanel.SetDock(tradeBtn, Dock.Right);
            string searchQuery = firstLine.Length <= 42 ? firstLine : obt.ItemTitle;
            tradeBtn.Click += (_, _) =>
            {
                SaveTradeLeague();
                TradeLinkBuilder.OpenTradeByItemName(_ai.Config.TradeLeague, searchQuery, isUnique: obt.Category.Contains("Unique"));
            };
            top.Children.Add(tradeBtn);
            top.Children.Add(new TextBlock
            {
                Text = $"{slotLabel} — {firstLine}",
                Foreground = UiBrushes.FgBrass,
                FontWeight = FontWeights.SemiBold,
                FontSize = 11.5,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            st.Children.Add(top);
            if (!string.IsNullOrWhiteSpace(compactStats))
            {
                st.Children.Add(new TextBlock
                {
                    Text = compactStats,
                    Foreground = UiBrushes.FgSteel,
                    FontSize = 9.8,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            st.Children.Add(new TextBlock
            {
                Text = $"{obt.Category} · {obt.MinItemOrAreaLevel}",
                Foreground = UiBrushes.FgEmerald,
                FontSize = 10,
                Margin = new Thickness(0, 2, 0, 3)
            });
            st.Children.Add(new TextBlock
            {
                Text = $"{Loc.T("Де отримати:", "Where to get:", "Где получить:")} {src}\n{Loc.T("Порада:", "Tip:", "Совет:")} {tip}",
                Foreground = UiBrushes.FgPrimary,
                FontSize = 10.5,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 15.5
            });
            card.Child = st;
            GearObtainPanel.Children.Add(card);
        }
    }

    private void ShowGemInspector(PoeGemInfo gem)
    {
        int curLvl = EffectiveLevel;
        bool unlocked = curLvl >= gem.MinUnlockCharacterLevel;
        string catLabel = gem.Category switch
        {
            GemCategory.Support => Loc.T("Підтримка", "Support", "Поддержка"),
            GemCategory.SpiritBuff => "Spirit Buff / Herald",
            GemCategory.MetaTrigger => "Meta Trigger",
            _ => Loc.T("Активне вміння", "Active Skill", "Активное умение")
        };
        InspectorTitleText.Text = $"{gem.Name}  ·  {catLabel}  ({gem.Attribute})";

        string statusIcon = unlocked
            ? Loc.T($"✅ Доступний зараз (потр. Рів.{gem.MinUnlockCharacterLevel})", $"✅ Unlocked (req Lvl {gem.MinUnlockCharacterLevel})", $"✅ Доступен сейчас (нужен Ур.{gem.MinUnlockCharacterLevel})")
            : Loc.T($"🔒 З Рів.{gem.MinUnlockCharacterLevel} персонажа", $"🔒 Unlocks at Lvl {gem.MinUnlockCharacterLevel}", $"🔒 С Ур.{gem.MinUnlockCharacterLevel} персонажа");

        string spiritPart = gem.SpiritCost > 0 ? $" · Spirit: {gem.SpiritCost}" : $" · Mana Mult: {gem.ManaMultiplierPct}%";
        string buildLvl = $"Gem Lv {gem.GemLevelInBuild}{(gem.QualityInBuild > 0 ? $" Q{gem.QualityInBuild}%" : "")}";
        InspectorMetaText.Text = $"{buildLvl} · {gem.UncutTier} · {statusIcon}{spiritPart}";
        InspectorMetaText.Foreground = unlocked ? UiBrushes.FgEmerald : UiBrushes.FgAmber;

        string where = Loc.T(gem.ObtainWhereUk, gem.ObtainWhereEn, gem.ObtainWhereRu);
        string desc = Loc.T(gem.DescriptionUk, gem.DescriptionEn, gem.DescriptionRu);
        string attrs = "";
        if (gem.RequiredAttrStr > 0 || gem.RequiredAttrDex > 0 || gem.RequiredAttrInt > 0)
            attrs = $"\n{Loc.T("Атрибути ~:", "Est. Attrs:", "Атрибуты ~:")} Str {gem.RequiredAttrStr} / Dex {gem.RequiredAttrDex} / Int {gem.RequiredAttrInt}";

        InspectorBodyText.Text = $"{desc}\n{Loc.T("Де отримати:", "Where to get:", "Где получить:")} {where}{attrs}";
        _inspectorAiPrompt = $"Explain PoE 2 gem '{gem.Name}' ({gem.UncutTier}, unlocks at char level {gem.MinUnlockCharacterLevel}) for my build: scaling, links, and practical tips.";
        OverviewInspectorCard.Visibility = Visibility.Visible;
    }

    private void ShowGearInspector(string rawSlotId, string slotTitle, string rawText)
    {
        var obt = Poe2KnowledgeDb.ResolveItemObtain(rawText, rawSlotId, _profiles.ActiveProfile, EffectiveLevel);
        InspectorTitleText.Text = $"{slotTitle} — {obt.ItemTitle}";
        InspectorMetaText.Text = $"{obt.Category} · {obt.MinItemOrAreaLevel}";
        InspectorMetaText.Foreground = UiBrushes.FgSteel;

        string src = Loc.T(obt.ObtainSourceUk, obt.ObtainSourceEn, obt.ObtainSourceRu);
        string tip = Loc.T(obt.StrategyTipUk, obt.StrategyTipEn, obt.StrategyTipRu);
        string compactStats = PoeAccountService.SummarizeItemStatsCompact(rawText);
        string modLines = rawText.Contains("--------")
            ? string.Join(" · ", rawText.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .SkipWhile(l => !l.StartsWith("--------"))
                .Where(l => !l.StartsWith("--------") && !l.StartsWith("Item Level:") && !l.StartsWith("Requires"))
                .Take(6)
                .Select(PoeTermLocalizer.StatLine))
            : "";

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(compactStats)) parts.Add($"{Loc.T("Стати:", "Stats:", "Статы:")} {compactStats}");
        if (!string.IsNullOrWhiteSpace(modLines) && modLines != compactStats) parts.Add(modLines);
        parts.Add($"{Loc.T("Де отримати:", "Where to get:", "Где получить:")} {src}");
        parts.Add($"{Loc.T("Порада / цінність:", "Tip / Value:", "Совет / ценность:")} {tip}");

        InspectorBodyText.Text = string.Join("\n", parts);
        _inspectorAiPrompt = $"Explain where to farm/craft '{obt.ItemTitle}' ({rawSlotId}) in PoE 2, key stats to look for, and when it is worth upgrading or selling: {rawText}";
        OverviewInspectorCard.Visibility = Visibility.Visible;
    }

    private void CloseInspector_Click(object sender, RoutedEventArgs e) => OverviewInspectorCard.Visibility = Visibility.Collapsed;

    private async void AskAiAboutInspector_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_inspectorAiPrompt)) return;
        SelectPage(NavBtnAi);
        await RunAiQueryAsync(_inspectorAiPrompt, includeScreenshot: false);
    }

    private Border CreateGemDetailCard(PoeGemInfo gem, int curLvl)
    {
        bool unlocked = curLvl >= gem.MinUnlockCharacterLevel;
        string where = Loc.T(gem.ObtainWhereUk, gem.ObtainWhereEn, gem.ObtainWhereRu);
        string desc = Loc.T(gem.DescriptionUk, gem.DescriptionEn, gem.DescriptionRu);
        string spiritStr = gem.SpiritCost > 0 ? $" · {gem.SpiritCost} Spirit" : "";

        var st = new StackPanel();
        var top = new DockPanel();
        top.Children.Add(new TextBlock
        {
            Text = $"{gem.Name}  ·  Lv {gem.GemLevelInBuild}{(gem.QualityInBuild > 0 ? $" Q{gem.QualityInBuild}%" : "")}  ·  {gem.Attribute}",
            Foreground = UiBrushes.FgBrass,
            FontWeight = FontWeights.SemiBold,
            FontSize = 11.5
        });
        top.Children.Add(new TextBlock
        {
            Text = unlocked
                ? Loc.T($"✅ З Рів.{gem.MinUnlockCharacterLevel}", $"✅ Lvl {gem.MinUnlockCharacterLevel}+", $"✅ С Ур.{gem.MinUnlockCharacterLevel}")
                : Loc.T($"🔒 Рів.{gem.MinUnlockCharacterLevel}", $"🔒 Lvl {gem.MinUnlockCharacterLevel}", $"🔒 Ур.{gem.MinUnlockCharacterLevel}"),
            Foreground = unlocked ? UiBrushes.FgEmerald : UiBrushes.FgAmber,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Right
        });
        st.Children.Add(top);

        st.Children.Add(new TextBlock
        {
            Text = $"{gem.UncutTier}{spiritStr} · [{gem.Tags}]",
            Foreground = UiBrushes.FgSteel,
            FontSize = 10,
            Margin = new Thickness(0, 2, 0, 3)
        });
        st.Children.Add(new TextBlock
        {
            Text = $"{desc}\n{Loc.T("Отримати:", "Obtain:", "Получить:")} {where}",
            Foreground = UiBrushes.FgPrimary,
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 15
        });

        return new Border
        {
            Background = UiBrushes.BgCard,
            BorderBrush = unlocked ? UiBrushes.BorderSubtle : UiBrushes.BorderBrass,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 0, 0, 5),
            Child = st
        };
    }

    private void OverviewPrevStage_Click(object sender, RoutedEventArgs e)
    {
        if (_profiles.ActiveProfile is not { Stages.Count: > 0 }) return;
        _autoSyncLevel = false;
        _overviewStageIdx = Math.Max(0, _overviewStageIdx - 1);
        RefreshOverview();
    }

    private void OverviewNextStage_Click(object sender, RoutedEventArgs e)
    {
        if (_profiles.ActiveProfile is not { Stages.Count: > 0 } p) return;
        _autoSyncLevel = false;
        _overviewStageIdx = Math.Min(p.Stages.Count - 1, _overviewStageIdx + 1);
        RefreshOverview();
    }
}
