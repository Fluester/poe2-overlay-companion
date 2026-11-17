using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace Poe2OverlayCompanion.Services;

public sealed class AppUpdateInfo
{
    public bool Ok { get; init; }
    public bool HasUpdate { get; init; }
    public string CurrentVersion { get; init; } = "1.1.1";
    public string LatestTag { get; init; } = "";
    public string ReleaseUrl { get; init; } = "https://github.com/Fluester/poe2-overlay-companion/releases/latest";
    public string AssetName { get; init; } = "";
    public string DownloadUrl { get; init; } = "";
    public string Notes { get; init; } = "";
    public string Error { get; init; } = "";
}

/// <summary>
/// Перевіряє нові релізи на GitHub (Fluester/poe2-overlay-companion) та безпечно оновлює встановлену або портативну версію.
/// </summary>
public static class AppUpdateService
{
    public const string LatestReleaseApiUrl = "https://api.github.com/repos/Fluester/poe2-overlay-companion/releases/latest";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };

    static AppUpdateService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd($"Poe2OverlayCompanion/{GetCurrentVersionString()}");
        Http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public static Version GetCurrentVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v == null ? new Version(1, 1, 1) : new Version(v.Major, v.Minor, Math.Max(0, v.Build));
    }

    public static string GetCurrentVersionString()
    {
        var v = GetCurrentVersion();
        return $"{v.Major}.{v.Minor}.{v.Build}";
    }

    public static bool IsNewerVersion(string remoteTag, Version currentVersion)
    {
        string clean = (remoteTag ?? "").Trim().TrimStart('v', 'V');
        int dash = clean.IndexOf('-');
        if (dash >= 0) clean = clean[..dash];
        if (!Version.TryParse(clean, out var remote))
        {
            if (int.TryParse(clean, out int maj)) remote = new Version(maj, 0, 0);
            else return false;
        }
        var normRemote = new Version(remote.Major, Math.Max(0, remote.Minor), Math.Max(0, remote.Build));
        var normCurrent = new Version(currentVersion.Major, Math.Max(0, currentVersion.Minor), Math.Max(0, currentVersion.Build));
        return normRemote > normCurrent;
    }

    public static AppUpdateInfo ParseReleaseJson(string json, Version currentVersion, bool isPortable)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string tag = root.TryGetProperty("tag_name", out var tg) ? tg.GetString() ?? "" : "";
        string htmlUrl = root.TryGetProperty("html_url", out var hu) ? hu.GetString() ?? "" : "https://github.com/Fluester/poe2-overlay-companion/releases/latest";
        string body = root.TryGetProperty("body", out var bd) ? bd.GetString() ?? "" : "";

        string setupName = "", setupUrl = "";
        string portableName = "", portableUrl = "";
        string exeName = "", exeUrl = "";

        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in assets.EnumerateArray())
            {
                string name = a.TryGetProperty("name", out var nEl) ? nEl.GetString() ?? "" : "";
                string dl = a.TryGetProperty("browser_download_url", out var uEl) ? uEl.GetString() ?? "" : "";
                if (!IsAllowedDownloadUrl(dl)) continue;

                if (name.Contains("Setup", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    setupName = name;
                    setupUrl = dl;
                }
                else if (name.Contains("Portable", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    portableName = name;
                    portableUrl = dl;
                }
                else if (name.Equals("Poe2OverlayCompanion.exe", StringComparison.OrdinalIgnoreCase))
                {
                    exeName = name;
                    exeUrl = dl;
                }
            }
        }

        string chosenName = isPortable
            ? (!string.IsNullOrEmpty(portableUrl) ? portableName : exeName)
            : (!string.IsNullOrEmpty(setupUrl) ? setupName : exeName);
        string chosenUrl = isPortable
            ? (!string.IsNullOrEmpty(portableUrl) ? portableUrl : exeUrl)
            : (!string.IsNullOrEmpty(setupUrl) ? setupUrl : exeUrl);

        bool hasUpdate = !string.IsNullOrWhiteSpace(tag) && IsNewerVersion(tag, currentVersion);
        return new AppUpdateInfo
        {
            Ok = !string.IsNullOrWhiteSpace(tag),
            HasUpdate = hasUpdate,
            CurrentVersion = $"{currentVersion.Major}.{currentVersion.Minor}.{currentVersion.Build}",
            LatestTag = tag,
            ReleaseUrl = htmlUrl,
            AssetName = chosenName,
            DownloadUrl = chosenUrl,
            Notes = body.Trim()
        };
    }

    public static async Task<AppUpdateInfo> CheckForUpdateAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await Http.GetAsync(LatestReleaseApiUrl, ct);
            if (!resp.IsSuccessStatusCode)
            {
                return new AppUpdateInfo
                {
                    Ok = false,
                    CurrentVersion = GetCurrentVersionString(),
                    Error = $"GitHub HTTP {(int)resp.StatusCode}"
                };
            }
            string json = await resp.Content.ReadAsStringAsync(ct);
            return ParseReleaseJson(json, GetCurrentVersion(), AppPaths.IsPortableMode);
        }
        catch (Exception ex)
        {
            return new AppUpdateInfo
            {
                Ok = false,
                CurrentVersion = GetCurrentVersionString(),
                Error = ex.Message
            };
        }
    }

    public static async Task<(bool Ok, string Error)> DownloadAndLaunchUpdateAsync(AppUpdateInfo info, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(info.DownloadUrl) || !IsAllowedDownloadUrl(info.DownloadUrl))
            return (false, "Invalid release asset URL.");

        string? currentExe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExe) || !File.Exists(currentExe))
            return (false, "Executable path not found.");

        string stageDir = Path.Combine(Path.GetTempPath(), "Poe2OverlayUpdate_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(stageDir);

        string safeFile = Path.GetFileName(info.AssetName);
        if (string.IsNullOrWhiteSpace(safeFile)) safeFile = "update_pkg.bin";
        string dlPath = Path.Combine(stageDir, safeFile);

        using (var resp = await Http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            if (!resp.IsSuccessStatusCode)
                return (false, $"Download HTTP {(int)resp.StatusCode}");
            await using var fs = File.Create(dlPath);
            await resp.Content.CopyToAsync(fs, ct);
        }

        byte[] header = new byte[2];
        await using (var chk = File.OpenRead(dlPath))
        {
            int read = await chk.ReadAsync(header.AsMemory(0, 2), ct);
            if (read < 2) return (false, "Downloaded file is empty.");
        }

        if (dlPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            if (header[0] != 'P' || header[1] != 'K')
                return (false, "Invalid ZIP signature.");

            string extractedExe = Path.Combine(stageDir, "Poe2OverlayCompanion.exe");
            using (var zip = ZipFile.OpenRead(dlPath))
            {
                var entry = zip.Entries.FirstOrDefault(e => e.Name.Equals("Poe2OverlayCompanion.exe", StringComparison.OrdinalIgnoreCase));
                if (entry == null) return (false, "Poe2OverlayCompanion.exe not found inside portable archive.");
                entry.ExtractToFile(extractedExe, overwrite: true);
            }
            LaunchExeSwapperScript(currentExe, extractedExe, Environment.ProcessId);
            return (true, "");
        }

        if (header[0] != 'M' || header[1] != 'Z')
            return (false, "Invalid EXE signature.");

        if (safeFile.Contains("Setup", StringComparison.OrdinalIgnoreCase))
        {
            string psScript = Path.Combine(stageDir, "run_setup.ps1");
            await File.WriteAllTextAsync(psScript,
                $"Wait-Process -Id {Environment.ProcessId} -Timeout 12 -ErrorAction SilentlyContinue\n" +
                $"Start-Process -FilePath '{dlPath.Replace("'", "''")}' -ArgumentList '/SILENT','/SUPPRESSMSGBOXES'\n",
                ct);
            Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{psScript}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
            return (true, "");
        }

        LaunchExeSwapperScript(currentExe, dlPath, Environment.ProcessId);
        return (true, "");
    }

    private static void LaunchExeSwapperScript(string targetExe, string newExe, int waitPid)
    {
        string psScript = Path.Combine(Path.GetDirectoryName(newExe)!, "apply_update.ps1");
        File.WriteAllText(psScript,
            $"Wait-Process -Id {waitPid} -Timeout 12 -ErrorAction SilentlyContinue\n" +
            $"Start-Sleep -Milliseconds 350\n" +
            $"Move-Item -Path '{newExe.Replace("'", "''")}' -Destination '{targetExe.Replace("'", "''")}' -Force\n" +
            $"Start-Process -FilePath '{targetExe.Replace("'", "''")}'\n");
        Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{psScript}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        });
    }

    private static bool IsAllowedDownloadUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) &&
        u.Scheme == Uri.UriSchemeHttps &&
        ((u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
          u.AbsolutePath.StartsWith("/Fluester/poe2-overlay-companion/", StringComparison.OrdinalIgnoreCase)) ||
         u.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase));
}
