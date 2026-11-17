using System.Diagnostics;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Poe2OverlayCompanion.Services;

/// <summary>
/// Єдине спільне середовище WebView2 для всього додатка:
/// 1) Запобігає COMException 0x8007139F при відкритті різних WebView2 вікон в одній папці кешу.
/// 2) Вимикає NativeWinOcclusion / throttling та GPU-vsync-конфлікти, що викликали чорний екран і цикл перезавантажень у WPF.
/// </summary>
public static class WebView2Helper
{
    private static readonly object Sync = new();
    private static Task<CoreWebView2Environment>? _sharedEnvTask;

    public static Task<CoreWebView2Environment> GetEnvironmentAsync()
    {
        lock (Sync)
        {
            if (_sharedEnvTask == null || _sharedEnvTask.IsFaulted || _sharedEnvTask.IsCanceled)
                _sharedEnvTask = CreateEnvironmentCoreAsync();
            return _sharedEnvTask;
        }
    }

    private static async Task<CoreWebView2Environment> CreateEnvironmentCoreAsync()
    {
        string userData = AppPaths.GetWebView2CacheDirectory();
        var options = new CoreWebView2EnvironmentOptions
        {
            // Вимикаємо GPU compositing (DirectComposition swapchain у WPF часто дає чорний екран через 1с і цикл)
            // та NativeWinOcclusion/throttling, зберігаючи WebGL/JS-перевірки працездатними.
            AdditionalBrowserArguments =
                "--disable-gpu-compositing " +
                "--disable-features=CalculateNativeWinOcclusion,IntensiveWakeUpThrottling,msWebOOUI " +
                "--disable-backgrounding-occluded-windows " +
                "--disable-renderer-backgrounding " +
                "--disable-gpu-vsync"
        };
        return await CoreWebView2Environment.CreateAsync(userDataFolder: userData, options: options);
    }

    public static async Task<string> ExecuteAsyncScriptAsync(CoreWebView2 core, string asyncScriptExpression, int timeoutMs = 25000)
    {
        string reqId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string msg = e.TryGetWebMessageAsString();
                if (string.IsNullOrWhiteSpace(msg) || !msg.Contains(reqId, StringComparison.Ordinal)) return;
                using var doc = JsonDocument.Parse(msg);
                if (!doc.RootElement.TryGetProperty("__poe2ReqId", out var idEl) || idEl.GetString() != reqId) return;

                if (doc.RootElement.TryGetProperty("ok", out var okEl) && !okEl.GetBoolean())
                {
                    string err = doc.RootElement.TryGetProperty("error", out var errEl) ? errEl.GetString() ?? "JS error" : "JS error";
                    tcs.TrySetResult(JsonSerializer.Serialize(new { ok = false, error = err }));
                    return;
                }
                string data = doc.RootElement.TryGetProperty("data", out var dataEl) ? (dataEl.ValueKind == JsonValueKind.String ? dataEl.GetString() ?? "" : dataEl.GetRawText()) : "";
                tcs.TrySetResult(data);
            }
            catch (Exception ex)
            {
                tcs.TrySetResult(JsonSerializer.Serialize(new { ok = false, error = ex.Message }));
            }
        }

        core.Settings.IsWebMessageEnabled = true;
        core.WebMessageReceived += Handler;
        try
        {
            string cleanExpr = asyncScriptExpression.Trim().TrimEnd(';');
            string safeReqId = JsonSerializer.Serialize(reqId);
            string wrapper = $$"""
            (async () => {
              const __reqId = {{safeReqId}};
              try {
                const __res = await ({{cleanExpr}});
                window.chrome.webview.postMessage(JSON.stringify({
                  __poe2ReqId: __reqId,
                  ok: true,
                  data: typeof __res === 'string' ? __res : JSON.stringify(__res)
                }));
              } catch (e) {
                window.chrome.webview.postMessage(JSON.stringify({
                  __poe2ReqId: __reqId,
                  ok: false,
                  error: String(e)
                }));
              }
            })();
            """;
            await core.ExecuteScriptAsync(wrapper);

            using var cts = new CancellationTokenSource(timeoutMs);
            await using var reg = cts.Token.Register(() => tcs.TrySetResult(JsonSerializer.Serialize(new { ok = false, error = "WebView script timeout" })));
            return await tcs.Task;
        }
        finally
        {
            core.WebMessageReceived -= Handler;
        }
    }

    public static void OpenInSystemBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { }
    }
}
