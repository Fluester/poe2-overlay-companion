using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Poe2OverlayCompanion.Localization;
using Poe2OverlayCompanion.Models;

namespace Poe2OverlayCompanion.Services;

public sealed class OverlayConfig
{
    public AppLanguage Language { get; set; } = AppLanguage.Uk;
    public AiProviderKind AiProvider { get; set; } = AiProviderKind.Gemini;

    public string GeminiApiKey { get; set; } = "";
    public string PreferredModel { get; set; } = "gemini-3.5-flash-lite";

    public string OpenAiBaseUrl { get; set; } = "https://api.openai.com/v1/chat/completions";
    public string OpenAiApiKey { get; set; } = "";
    public string OpenAiModel { get; set; } = "gpt-4o-mini";

    public bool AutoScanEnabled { get; set; } = false;
    public int AutoScanMinutes { get; set; } = 30;
    public bool AutoScanOnlySafeZone { get; set; } = true;
    public bool SoundAlertsEnabled { get; set; } = true;
    public bool HideWhenGameClosed { get; set; } = false;
    public string TradeLeague { get; set; } = "Standard";

    // Універсальний кастомний таймер бафу / кулдауну
    public bool BuffTimerEnabled { get; set; } = false;
    public string BuffTimerKey { get; set; } = "X";
    public string BuffTimerLabel { get; set; } = "Buff";
    public int BuffTimerSeconds { get; set; } = 12;

    public string ActiveProfileId { get; set; } = "";
    public string LastPob2Code { get; set; } = "";
    public string CustomClientTxtPath { get; set; } = "";

    public bool AutoUpdateMonitorEnabled { get; set; } = true;
    public int UpdateCheckIntervalHours { get; set; } = 60;
    public DateTime LastUpdateCheckUtc { get; set; } = DateTime.MinValue;
    public string LastUpdateSummary { get; set; } = "";

    // Компактний розмір у грі, авторівень, поточне спорядження та підключений PoE 2 акаунт
    public string OverlaySizePreset { get; set; } = "M";
    public double WindowWidth { get; set; } = 560;
    public double WindowHeight { get; set; } = 430;
    public bool FocusOverlayOnCtrlC { get; set; } = false;
    public int LastKnownLevel { get; set; } = 1;
    public string PoeAccountName { get; set; } = "";
    public string PoeCharacterId { get; set; } = "";
    public string PoeCharacterName { get; set; } = "";
    public string PoeCharacterClass { get; set; } = "";
    public int PoeCharacterLevel { get; set; } = 0;
    public long PoeCharacterExperience { get; set; } = 0;
    public string PoeCharacterStatsSummary { get; set; } = "";
    public DateTime PoeLastSyncUtc { get; set; } = DateTime.MinValue;
    public Dictionary<string, string> WornGearBySlot { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> PoeCharacterSkills { get; set; } = [];

    // Trade Helper, Command Wheel (F8) та Stash Net-Worth трекер
    public bool TradeHelperEnabled { get; set; } = true;
    public string CustomCmd1 { get; set; } = "/hideout";
    public string CustomCmd2 { get; set; } = "/remaining";
    public double LastStashNetWorthDiv { get; set; } = 0;
    public double SessionStartNetWorthDiv { get; set; } = 0;
    public DateTime LastStashScanUtc { get; set; } = DateTime.MinValue;
    public List<string> LastStashTopLines { get; set; } = [];
    public HashSet<string> CompletedCampaignRewards { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, MapModSafety> CustomWaystoneModTags { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class GeminiAdvisorService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(40) };
    private static readonly string[] GeminiFallbacks =
    [
        "gemini-3.5-flash-lite",
        "gemini-flash-lite-latest",
        "gemini-3.5-flash",
        "gemini-3.6-flash"
    ];

    public OverlayConfig Config { get; private set; }
    private readonly string _configPath;
    private string _lastAutoFrameHash = "";

    public GeminiAdvisorService()
    {
        string appDataDir = AppPaths.GetAppDataDirectory();
        _configPath = Path.Combine(appDataDir, "overlay_config.json");

        MigrateLegacyConfigIfNeeded();
        Config = LoadConfig();
        Loc.Current = Config.Language;
    }

    private void MigrateLegacyConfigIfNeeded()
    {
        try
        {
            if (File.Exists(_configPath)) return;
            string legacyPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Poe2MonkOverlay",
                "overlay_config.json"
            );
            if (File.Exists(legacyPath))
            {
                var json = File.ReadAllText(legacyPath, Encoding.UTF8);
                var oldCfg = JsonSerializer.Deserialize<OverlayConfig>(json);
                if (oldCfg != null)
                {
                    oldCfg.ActiveProfileId = ""; // Не прив'язуємось до старого вбудованого пресету
                    File.WriteAllText(_configPath, JsonSerializer.Serialize(oldCfg, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
                }
            }
        }
        catch { }
    }

    private OverlayConfig LoadConfig()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath, Encoding.UTF8);
                return JsonSerializer.Deserialize<OverlayConfig>(json) ?? new OverlayConfig();
            }
        }
        catch { }
        return new OverlayConfig();
    }

    public void SaveConfig()
    {
        var json = JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_configPath, json, Encoding.UTF8);
    }

    public bool HasApiKey => Config.AiProvider == AiProviderKind.Gemini
        ? !string.IsNullOrWhiteSpace(Config.GeminiApiKey)
        : !string.IsNullOrWhiteSpace(Config.OpenAiBaseUrl);

    public string MaskedApiKey
    {
        get
        {
            var raw = Config.AiProvider == AiProviderKind.Gemini ? Config.GeminiApiKey : Config.OpenAiApiKey;
            var k = (raw ?? "").Trim();
            if (k.Length == 0) return Config.AiProvider == AiProviderKind.OpenAiCompatible ? "Local/NoKey" : Loc.T("Не задано", "Not set", "Не задан");
            if (k.Length <= 8) return "***";
            return $"{k[..4]}...{k[^4..]}";
        }
    }

    public bool IsDuplicateAutoFrame(byte[] jpegBytes)
    {
        string hash = Convert.ToHexString(SHA256.HashData(jpegBytes));
        if (hash == _lastAutoFrameHash) return true;
        _lastAutoFrameHash = hash;
        return false;
    }

    public async Task<(bool ok, string text, string modelUsed)> AskAsync(
        string userPrompt,
        BuildProfile? activeProfile,
        byte[]? jpegBytes = null,
        CancellationToken ct = default,
        int characterLevel = 1,
        int stageIndex = 0,
        IReadOnlyDictionary<string, string>? wornGear = null)
    {
        if (!HasApiKey)
            return (false, Loc.T("API-ключ не задано у вкладці AI-радника.", "API key is not set in AI Advisor.", "API-ключ не задан во вкладке AI-советника."), "");

        string systemPrompt = BuildSystemPrompt(activeProfile, characterLevel, stageIndex, wornGear);
        return Config.AiProvider == AiProviderKind.OpenAiCompatible
            ? await AskOpenAiCompatibleAsync(systemPrompt, userPrompt, jpegBytes, ct)
            : await AskGeminiAsync(systemPrompt, userPrompt, jpegBytes, ct);
    }

    private static string BuildSystemPrompt(
        BuildProfile? profile,
        int characterLevel,
        int stageIndex,
        IReadOnlyDictionary<string, string>? wornGear)
    {
        string langRule = Loc.AiLanguageInstruction();
        if (profile == null)
        {
            return $"""
{langRule}
You are an expert Path of Exile 2 (PoE 2) companion assistant. Character Level: {characterLevel}.
No specific build profile is loaded yet. Analyze the user's question or screenshot accurately using PoE 2 mechanics and give concise, bullet-point tips appropriate for level {characterLevel}. Keep original English game terms in brackets if helpful.
""";
        }

        BuildStageEntry? stage = profile.Stages.Count > 0
            ? profile.Stages[Math.Clamp(stageIndex, 0, profile.Stages.Count - 1)]
            : null;
        string stageWeapon = BuildProfileManager.ResolveEffectiveStageWeaponClass(profile, stageIndex);
        string stageSkills = stage is { SkillSignatures.Count: > 0 }
            ? string.Join(", ", stage.SkillSignatures.Take(6))
            : string.Join(", ", profile.TargetSkills.Take(8));
        string stageGear = stage is { GearSignatures.Count: > 0 }
            ? string.Join(" | ", stage.GearSignatures.Take(6).Select(g => g.Split('\n')[0]))
            : "none listed";
        string wornSummary = wornGear is { Count: > 0 }
            ? string.Join(" | ", wornGear.Take(6).Select(kv => $"{kv.Key}={kv.Value.Split('\n')[0].Trim()}"))
            : "not synced yet";

        return $"""
{langRule}
You are an expert Path of Exile 2 (PoE 2) build assistant.
ACTIVE BUILD PROFILE: {profile.Name} (Author: {profile.Author})
- CURRENT CHARACTER LEVEL: {characterLevel}
- ACTIVE PROGRESSION STAGE: {(stage != null ? $"{stage.Name} (Lv {stage.MinLevel}-{stage.MaxLevel})" : "Default")}
- WEAPON FOR THIS ACTIVE STAGE: {(string.IsNullOrEmpty(stageWeapon) ? "Flexible / Unarmed" : stageWeapon)} (Endgame profile weapon: {profile.PreferredWeaponClass})
- IMPORTANT: Many builds switch weapons across leveling stages (e.g. Spears/Unarmed in early acts before switching to Quarterstaves later). NEVER warn about weapon mismatch if the user's weapon matches this active stage or worn slot!
- Class / Ascendancy: {profile.ClassName} / {profile.Ascendancy}
- CI (Chaos Inoculation): {profile.IsCi} | Defense Focus: {profile.DefenseFocus} | Damage Style: {profile.DamageStyle}
- Active Stage Skills: {stageSkills}
- Active Stage Gear Targets: {stageGear}
- User Currently Worn Gear: {wornSummary}
- Notes: {profile.SummaryNotes}

Give concise, practical advice strictly calibrated to Character Level {characterLevel} and the ACTIVE stage. Never recommend ilvl 80+ / Tier 15 endgame bases if the character is still leveling. Keep exact English PoE 2 item/skill/passive names.
""";
    }

    private async Task<(bool ok, string text, string modelUsed)> AskGeminiAsync(
        string systemPrompt, string userPrompt, byte[]? jpegBytes, CancellationToken ct)
    {
        var modelsToTry = new List<string>();
        if (!string.IsNullOrWhiteSpace(Config.PreferredModel))
            modelsToTry.Add(Config.PreferredModel.Trim());
        foreach (var m in GeminiFallbacks)
            if (!modelsToTry.Contains(m)) modelsToTry.Add(m);

        string lastError = "Unknown error";
        foreach (var model in modelsToTry)
        {
            try
            {
                var parts = new List<object> { new { text = userPrompt } };
                if (jpegBytes is { Length: > 0 })
                {
                    parts.Add(new
                    {
                        inline_data = new { mime_type = "image/jpeg", data = Convert.ToBase64String(jpegBytes) }
                    });
                }

                var body = new
                {
                    system_instruction = new { parts = new[] { new { text = systemPrompt } } },
                    contents = new[] { new { role = "user", parts } },
                    generationConfig = new { temperature = 0.35, maxOutputTokens = 850 }
                };

                var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";
                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.Add("x-goog-api-key", Config.GeminiApiKey.Trim());
                req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

                using var resp = await Http.SendAsync(req, ct);
                string raw = await resp.Content.ReadAsStringAsync(ct);

                if (resp.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests or HttpStatusCode.NotFound)
                {
                    lastError = $"{model}: HTTP {(int)resp.StatusCode}";
                    continue;
                }

                if (!resp.IsSuccessStatusCode)
                    return (false, $"Gemini HTTP {(int)resp.StatusCode}: {ExtractApiError(raw)}", model);

                using var doc = JsonDocument.Parse(raw);
                var candidates = doc.RootElement.GetProperty("candidates");
                if (candidates.GetArrayLength() == 0)
                    return (false, "Empty AI response", model);

                var sb = new StringBuilder();
                foreach (var p in candidates[0].GetProperty("content").GetProperty("parts").EnumerateArray())
                {
                    if (p.TryGetProperty("text", out var t))
                        sb.Append(t.GetString());
                }
                return (true, sb.ToString().Trim(), model);
            }
            catch (TaskCanceledException) { lastError = $"{model}: timeout"; }
            catch (Exception ex) { lastError = $"{model}: {ex.Message}"; }
        }
        return (false, $"All Gemini models failed ({lastError}).", "");
    }

    private async Task<(bool ok, string text, string modelUsed)> AskOpenAiCompatibleAsync(
        string systemPrompt, string userPrompt, byte[]? jpegBytes, CancellationToken ct)
    {
        string endpoint = Config.OpenAiBaseUrl.Trim();
        string model = string.IsNullOrWhiteSpace(Config.OpenAiModel) ? "gpt-4o-mini" : Config.OpenAiModel.Trim();
        try
        {
            object userContent = jpegBytes is { Length: > 0 }
                ? new object[]
                {
                    new { type = "text", text = userPrompt },
                    new { type = "image_url", image_url = new { url = $"data:image/jpeg;base64,{Convert.ToBase64String(jpegBytes)}" } }
                }
                : userPrompt;

            var body = new
            {
                model,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userContent }
                },
                temperature = 0.35,
                max_tokens = 850
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            if (!string.IsNullOrWhiteSpace(Config.OpenAiApiKey))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Config.OpenAiApiKey.Trim());
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            using var resp = await Http.SendAsync(req, ct);
            string raw = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return (false, $"OpenAI API HTTP {(int)resp.StatusCode}: {ExtractApiError(raw)}", model);

            using var doc = JsonDocument.Parse(raw);
            string text = doc.RootElement.GetProperty("choices")[0]
                .GetProperty("message").GetProperty("content").GetString() ?? "";
            return (true, text.Trim(), model);
        }
        catch (Exception ex)
        {
            return (false, $"OpenAI-compatible error: {ex.Message}", model);
        }
    }

    private static string ExtractApiError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err) && err.TryGetProperty("message", out var msg))
                return msg.GetString() ?? json;
        }
        catch { }
        return json.Length > 220 ? json[..220] : json;
    }
}
