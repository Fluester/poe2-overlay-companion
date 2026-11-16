using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using Poe2OverlayCompanion.Localization;
using Poe2OverlayCompanion.Models;

namespace Poe2OverlayCompanion.Services;

public sealed class UpdateCheckReport
{
    public bool CheckedOnline { get; init; }
    public bool HasChanges { get; init; }
    public string Headline { get; init; } = "";
    public string Details { get; init; } = "";
}

public static class BuildUpdateMonitorService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(14) };

    static BuildUpdateMonitorService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Poe2OverlayCompanion/2.0");
    }

    public static bool IsCheckDue(OverlayConfig cfg)
    {
        if (!cfg.AutoUpdateMonitorEnabled) return false;
        if (cfg.LastUpdateCheckUtc == DateTime.MinValue) return true;
        return (DateTime.UtcNow - cfg.LastUpdateCheckUtc).TotalHours >= Math.Max(24, cfg.UpdateCheckIntervalHours);
    }

    public static async Task<UpdateCheckReport> CheckAllBuildsAsync(
        OverlayConfig cfg,
        BuildProfileManager profileMgr,
        int? currentPlayerLevel,
        CancellationToken ct = default)
    {
        if (profileMgr.Profiles.Count == 0)
        {
            return new UpdateCheckReport
            {
                Headline = Loc.T("Немає доданих білдів для моніторингу.", "No builds added to monitor.", "Нет добавленных билдов для мониторинга."),
                Details = Loc.T("Додай білд через .build, PoB2/pobb.in або посилання на гайд.", "Import a build via .build, PoB2/pobb.in, or guide URL first.", "Сначала добавь билд через .build, PoB2/pobb.in или ссылку на гайд.")
            };
        }

        var lines = new List<string>();
        bool anyOnline = false;
        int totalChanges = 0;

        foreach (var profile in profileMgr.Profiles)
        {
            var profileDiffs = new List<string>();

            // 1. Локальні .build файли цього білду (Skills, Supports, Gear, Passives!)
            CheckLocalBuildFilesDiff(profile, currentPlayerLevel, profileDiffs);

            // 2. Онлайн PoB2 URL (pobb.in / pastebin)
            if (!string.IsNullOrWhiteSpace(profile.PobSourceUrl))
            {
                bool ok = await CheckOnlinePobDiffAsync(profile, profileDiffs, ct);
                if (ok) anyOnline = true;
            }

            // 3. Онлайн URL гайду (якщо не заблоковано антиботом / Cloudflare)
            if (!string.IsNullOrWhiteSpace(profile.GuideUrl))
            {
                bool ok = await CheckGuideUrlAsync(profile, profileDiffs, ct);
                if (ok) anyOnline = true;
            }

            if (profileDiffs.Count > 0)
            {
                totalChanges += profileDiffs.Count;
                profile.Changelog.Insert(0, new BuildChangelogEntry
                {
                    CheckedUtc = DateTime.UtcNow,
                    Source = PoeTermLocalizer.BuildSourceLabel(profile.SourceKind),
                    Headline = Loc.T($"Виявлено змін: {profileDiffs.Count}", $"Changes detected: {profileDiffs.Count}", $"Обнаружено изменений: {profileDiffs.Count}"),
                    DiffLines = [.. profileDiffs]
                });
                if (profile.Changelog.Count > 25) profile.Changelog.RemoveRange(25, profile.Changelog.Count - 25);

                lines.Add($"[{profile.Name}]");
                foreach (var d in profileDiffs) lines.Add($"  • {d}");
                lines.Add("");
            }
            else
            {
                lines.Add($"[{profile.Name}] — {Loc.T("Без змін", "Up to date", "Без изменений")}");
            }
        }

        cfg.LastUpdateCheckUtc = DateTime.UtcNow;
        string headline = totalChanges > 0
            ? Loc.T($"Оновлення білдів: знайдено змін ({totalChanges})", $"Build Updates: {totalChanges} change(s) detected", $"Обновления билдов: найдено изменений ({totalChanges})")
            : Loc.T("Усі додані білди актуальні", "All added builds are up to date", "Все добавленные билды актуальны");
        cfg.LastUpdateSummary = headline;
        profileMgr.SaveProfiles();

        return new UpdateCheckReport
        {
            CheckedOnline = anyOnline,
            HasChanges = totalChanges > 0,
            Headline = headline,
            Details = string.Join("\n", lines).Trim()
        };
    }

    private static void CheckLocalBuildFilesDiff(BuildProfile profile, int? playerLevel, List<string> diffs)
    {
        if (profile.Stages.Count == 0 && profile.LinkedBuildFiles.Count == 0) return;

        foreach (var stage in profile.Stages)
        {
            if (string.IsNullOrWhiteSpace(stage.FilePath) || !File.Exists(stage.FilePath)) continue;
            var fresh = BuildPlannerService.TryParseFile(stage.FilePath);
            if (fresh == null) continue;

            if (!string.IsNullOrEmpty(stage.LastFileHash) && stage.LastFileHash == fresh.ContentSha256)
                continue;

            if (string.IsNullOrEmpty(stage.LastFileHash))
            {
                stage.LastFileHash = fresh.ContentSha256;
                continue;
            }

            // Глибоке порівняння: Пасивки, Скіли, Екіп!
            var oldPassives = new HashSet<string>(stage.PassiveIds, StringComparer.OrdinalIgnoreCase);
            var newPassives = new HashSet<string>(fresh.Passives.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);
            var addedNodes = newPassives.Except(oldPassives).Take(6).Select(id => PassiveTreeService.Index.Resolve(id)?.Name ?? id).ToList();
            var removedNodes = oldPassives.Except(newPassives).Take(6).Select(id => PassiveTreeService.Index.Resolve(id)?.Name ?? id).ToList();

            if (addedNodes.Count > 0 || removedNodes.Count > 0)
                diffs.Add(Loc.T($"[{stage.Name}] Пасивки: +{addedNodes.Count} ({string.Join(", ", addedNodes)}) / -{removedNodes.Count} ({string.Join(", ", removedNodes)})", $"[{stage.Name}] Passives: +{addedNodes.Count} ({string.Join(", ", addedNodes)}) / -{removedNodes.Count} ({string.Join(", ", removedNodes)})", $"[{stage.Name}] Пассивки: +{addedNodes.Count} ({string.Join(", ", addedNodes)}) / -{removedNodes.Count} ({string.Join(", ", removedNodes)})"));

            var oldSkills = new HashSet<string>(stage.SkillSignatures);
            var newSkills = fresh.Skills.Select(s => $"{s.SkillName} -> {string.Join(", ", s.Supports)}").ToList();
            foreach (var s in newSkills.Where(x => !oldSkills.Contains(x)).Take(4))
                diffs.Add(Loc.T($"[{stage.Name}] Скіл/лінкі +: {s}", $"[{stage.Name}] Skill/Links +: {s}", $"[{stage.Name}] Скилл/линки +: {s}"));

            var oldGear = new HashSet<string>(stage.GearSignatures);
            var newGear = fresh.Gear.Select(g => $"{g.RawSlotId}: {g.Text}").ToList();
            foreach (var g in newGear.Where(x => !oldGear.Contains(x)).Take(4))
                diffs.Add(Loc.T($"[{stage.Name}] Екіп оновлено: {g.Split('\n')[0]}", $"[{stage.Name}] Gear updated: {g.Split('\n')[0]}", $"[{stage.Name}] Экип обновлён: {g.Split('\n')[0]}"));

            if (diffs.Count == 0)
                diffs.Add(Loc.T($"[{stage.Name}] Файл .build оновлено (SHA {fresh.ContentSha256})", $"[{stage.Name}] .build file updated (SHA {fresh.ContentSha256})", $"[{stage.Name}] Файл .build обновлён (SHA {fresh.ContentSha256})"));

            stage.LastFileHash = fresh.ContentSha256;
            stage.PassiveIds = fresh.Passives.Select(p => p.Id).ToList();
            stage.SkillSignatures = newSkills;
            stage.GearSignatures = newGear;
        }

        // Якщо гравець уже виріс по рівню і має лише стартовий етап
        if (playerLevel is int lvl && profile.Stages.Count == 1 && lvl > profile.Stages[0].MaxLevel + 5)
        {
            diffs.Add(Loc.T($"Твій рівень ({lvl}) переріс єдиний завантажений етап ({profile.Stages[0].MinLevel}–{profile.Stages[0].MaxLevel}). Додай наступний .build / PoB2 етап.", $"Your level ({lvl}) exceeds the only stage ({profile.Stages[0].MinLevel}-{profile.Stages[0].MaxLevel}). Import next .build / PoB2 stage.", $"Твой уровень ({lvl}) перерос единственный этап ({profile.Stages[0].MinLevel}–{profile.Stages[0].MaxLevel}). Добавь следующий .build / PoB2 этап."));
        }
    }

    private static async Task<bool> CheckOnlinePobDiffAsync(BuildProfile profile, List<string> diffs, CancellationToken ct)
    {
        var pob = await Pob2Parser.ResolveAndParseAsync(profile.PobSourceUrl, ct);
        if (!pob.Success)
        {
            diffs.Add(Loc.T($"PoB URL помилка: {pob.Error}", $"PoB URL error: {pob.Error}", $"Ошибка PoB URL: {pob.Error}"));
            return false;
        }

        string nextHash = BuildProfileManager.ComputeHash(pob.RawContextSummary);
        if (!string.IsNullOrEmpty(profile.LastPobSnapshotHash) && profile.LastPobSnapshotHash != nextHash)
        {
            diffs.Add(Loc.T($"Онлайн PoB оновився ({pob.SourceUrl}): DPS {pob.TotalDps:F0}, ES {pob.EnergyShield:F0}, Life {pob.Life:F0}, {pob.SkillNames.Count} умінь.", $"Online PoB updated ({pob.SourceUrl}): DPS {pob.TotalDps:F0}, ES {pob.EnergyShield:F0}, Life {pob.Life:F0}.", $"Онлайн PoB обновился ({pob.SourceUrl}): DPS {pob.TotalDps:F0}, ES {pob.EnergyShield:F0}, Life {pob.Life:F0}."));
            if (!string.IsNullOrWhiteSpace(pob.RawCode)) profile.Pob2Code = pob.RawCode;
            profile.TargetSkills = pob.SkillNames.Take(12).ToList();
            profile.TargetGearHints = pob.EquippedItems.Take(12).ToList();
            if (pob.TreeSpecs.Count > 0)
            {
                profile.Stages = pob.TreeSpecs.Select((ts, i) => new BuildStageEntry
                {
                    Name = ts.Title,
                    MinLevel = i == pob.TreeSpecs.Count - 1 ? Math.Max(1, pob.Level) : 1,
                    MaxLevel = 100,
                    PassiveIds = [.. ts.NodeIds]
                }).ToList();
            }
        }
        profile.LastPobSnapshotHash = nextHash;
        return true;
    }

    private static async Task<bool> CheckGuideUrlAsync(BuildProfile profile, List<string> diffs, CancellationToken ct)
    {
        try
        {
            using var resp = await Http.GetAsync(profile.GuideUrl, ct);
            if (resp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                // Чесно повідомляємо про захист сайту без спроб обійти антибот/капчу
                diffs.Add(profile.SourceKind == BuildSourceKind.MobalyticsImport
                    ? Loc.T("Mobalytics використовує Cloudflare — онови через кнопку «Mobalytics (Браузер)» або Diff з буфера (Ctrl+A → Ctrl+C).", "Mobalytics uses Cloudflare — click 'Mobalytics (Browser)' or Clipboard Diff (Ctrl+A -> Ctrl+C) to sync updates.", "Mobalytics использует Cloudflare — обнови через «Mobalytics (Браузер)» или Diff из буфера.")
                    : Loc.T("Сторінка гайду захищена Cloudflare — відкрий у браузері й встав оновлений текст (Ctrl+A → Ctrl+C) для diff або онови .build.", "Guide URL uses Cloudflare protection — open in browser and copy page text (Ctrl+A -> Ctrl+C) for diff or drop updated .build.", "Страница гайда защищена Cloudflare — открой в браузере и скопируй текст (Ctrl+A -> Ctrl+C) для diff или обнови .build."));
                return false;
            }
            if (!resp.IsSuccessStatusCode) return false;

            string html = await resp.Content.ReadAsStringAsync(ct);
            string clean = BuildProfileManager.ExtractCleanTextFromHtml(html);
            string newHash = BuildProfileManager.ComputeHash(clean);

            var updMatch = Regex.Match(html, @"(?:updated|modified|patch)[^0-9]{0,20}(202\d-\d\d-\d\d|\d+\.\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            string marker = updMatch.Success ? updMatch.Groups[1].Value : "";

            if (!string.IsNullOrEmpty(profile.LastGuideSnapshotHash) && profile.LastGuideSnapshotHash != newHash)
            {
                var lineDiffs = ComputeLineDiff(profile.LastGuideSnapshotText, clean, maxLines: 6);
                string markerInfo = !string.IsNullOrEmpty(marker) ? $" ({marker})" : "";
                diffs.Add(Loc.T($"Гайд оновився{markerInfo}: {string.Join(" | ", lineDiffs)}", $"Guide updated{markerInfo}: {string.Join(" | ", lineDiffs)}", $"Гайд обновился{markerInfo}: {string.Join(" | ", lineDiffs)}"));
            }

            profile.LastGuideSnapshotHash = newHash;
            profile.LastGuideSnapshotText = clean;
            if (!string.IsNullOrEmpty(marker)) profile.LastOnlineUpdatedMarker = marker;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static BuildChangelogEntry DiffClipboardGuideText(BuildProfile profile, string pastedText)
    {
        string normalized = string.Join("\n",
            pastedText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(l => l.Length >= 3)
                .Take(350));

        string newHash = BuildProfileManager.ComputeHash(normalized);
        List<string> lines;
        string headline;

        if (string.IsNullOrEmpty(profile.LastGuideSnapshotText))
        {
            lines = [Loc.T($"Збережено базовий знімок гайду ({normalized.Length} симв.). Наступного разу покаже точні зміни.", $"Saved baseline guide snapshot ({normalized.Length} chars). Future pastes will show exact diff.", $"Сохранён базовый снимок гайда ({normalized.Length} симв.). В следующий раз покажет точные изменения.")];
            headline = Loc.T("Знімок гайду збережено", "Guide snapshot saved", "Снимок гайда сохранён");
        }
        else if (profile.LastGuideSnapshotHash == newHash)
        {
            lines = [Loc.T("Змін порівняно з попереднім знімком не знайдено.", "No changes since previous snapshot.", "Изменений с предыдущего снимка не найдено.")];
            headline = Loc.T("Без змін", "No changes", "Без изменений");
        }
        else
        {
            lines = ComputeLineDiff(profile.LastGuideSnapshotText, normalized, maxLines: 20);
            headline = Loc.T($"Знайдено змін у гайді ({lines.Count})", $"Guide changes detected ({lines.Count})", $"Найдено изменений в гайде ({lines.Count})");
        }

        profile.LastGuideSnapshotHash = newHash;
        profile.LastGuideSnapshotText = normalized;
        var entry = new BuildChangelogEntry
        {
            CheckedUtc = DateTime.UtcNow,
            Source = "Clipboard Diff",
            Headline = headline,
            DiffLines = lines
        };
        profile.Changelog.Insert(0, entry);
        return entry;
    }

    private static List<string> ComputeLineDiff(string oldText, string newText, int maxLines)
    {
        var oldSet = new HashSet<string>(oldText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var newSet = new HashSet<string>(newText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var result = new List<string>();
        foreach (var line in newSet.Where(l => !oldSet.Contains(l)).Take(maxLines / 2))
            result.Add($"+ {(line.Length > 95 ? line[..94] + "…" : line)}");
        foreach (var line in oldSet.Where(l => !newSet.Contains(l)).Take(maxLines / 2))
            result.Add($"- {(line.Length > 95 ? line[..94] + "…" : line)}");

        if (result.Count == 0)
            result.Add(Loc.T("Оновився порядок або форматування секцій.", "Section ordering or formatting changed.", "Обновился порядок или форматирование секций."));
        return result;
    }
}
