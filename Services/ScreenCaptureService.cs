using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Poe2OverlayCompanion.Localization;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.Dxgi;
using Windows.Win32.Graphics.Dxgi.Common;

namespace Poe2OverlayCompanion.Services;

/// <summary>
/// Захоплення екрана з потрійним ланцюжком (DXGI Desktop Duplication із HDR tone-mapping → PrintWindow DWM → GDI BitBlt)
/// та асинхронним кодуванням JPEG у фоновому потоці.
/// </summary>
public static class ScreenCaptureService
{
    private const int SRCCOPY = 0x00CC0020;
    private const int DESKTOPHORZRES = 118;
    private const int DESKTOPVERTRES = 117;
    private const uint PW_RENDERFULLCONTENT = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest,
        IntPtr hdcSrc, int xSrc, int ySrc, int rop);

    public static Task<byte[]> CaptureScreenJpegAsync(int maxWidth = 1600, int quality = 82)
    {
        BitmapSource? frozen = null;
        int w = 0;

        bool hasPoeRect = ClientLogWatcher.TryGetPoe2ClientRect(out int cx, out int cy, out int cw, out int ch);

        // 1. DXGI Desktop Duplication (апаратний GPU + підтримка Windows HDR 16-bit float)
        try
        {
            frozen = TryCaptureViaDxgi(hasPoeRect ? (cx, cy, cw, ch) : null);
            if (frozen != null) w = frozen.PixelWidth;
        }
        catch { frozen = null; }

        // 2. DWM PrintWindow для вікна PoE 2 (DX12 / Vulkan compositor)
        if (frozen == null && hasPoeRect)
        {
            try
            {
                IntPtr poeHwnd = ClientLogWatcher.FindPoe2WindowHandle();
                if (poeHwnd != IntPtr.Zero)
                {
                    frozen = TryCaptureViaPrintWindow(poeHwnd);
                    if (frozen != null) w = frozen.PixelWidth;
                }
            }
            catch { frozen = null; }
        }

        // 3. Класичний кропнутий GDI BitBlt fallback
        if (frozen == null)
            (frozen, w) = CaptureViaBitBlt(hasPoeRect ? (cx, cy, cw, ch) : null);

        var captured = frozen;
        int capturedWidth = w;
        return Task.Run(() => EncodeJpeg(captured, capturedWidth, maxWidth, quality));
    }

    private static unsafe BitmapSource? TryCaptureViaDxgi((int x, int y, int w, int h)? crop)
    {
        var gFact = IDXGIFactory1.IID_Guid;
        void* fPtr = null;
        if (!PInvoke.CreateDXGIFactory1(&gFact, &fPtr).Succeeded || fPtr == null) return null;
        var factory = (IDXGIFactory1*)fPtr;

        try
        {
            for (uint ai = 0; ; ai++)
            {
                IDXGIAdapter1* adapter = null;
                try { factory->EnumAdapters1(ai, &adapter); } catch { break; }
                if (adapter == null) break;

                try
                {
                    for (uint oi = 0; ; oi++)
                    {
                        IDXGIOutput* output = null;
                        try { adapter->EnumOutputs(oi, &output); } catch { break; }
                        if (output == null) break;

                        try
                        {
                            DXGI_OUTPUT_DESC od;
                            try { od = output->GetDesc(); } catch { continue; }
                            var fl = D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0;
                            ID3D11Device* dev = null;
                            ID3D11DeviceContext* ctx = null;
                            if (!PInvoke.D3D11CreateDevice((IDXGIAdapter*)adapter, D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_UNKNOWN,
                                    HMODULE.Null, 0, &fl, 1, 7, &dev, null, &ctx).Succeeded || dev == null || ctx == null)
                                continue;

                            try
                            {
                                IDXGIOutput1* out1 = null;
                                var gO1 = IDXGIOutput1.IID_Guid;
                                if (!output->QueryInterface(&gO1, (void**)&out1).Succeeded || out1 == null) continue;

                                try
                                {
                                    IDXGIOutputDuplication* dupl = null;
                                    try { out1->DuplicateOutput((Windows.Win32.System.Com.IUnknown*)dev, &dupl); }
                                    catch { dupl = null; }
                                    if (dupl == null) continue;

                                    try
                                    {
                                        DXGI_OUTDUPL_FRAME_INFO fi = default;
                                        IDXGIResource* res = null;
                                        try { dupl->AcquireNextFrame(120, &fi, &res); }
                                        catch { res = null; }

                                        if (res == null) continue;
                                        try
                                        {
                                            ID3D11Texture2D* tex = null;
                                            var gTex = ID3D11Texture2D.IID_Guid;
                                            if (res->QueryInterface(&gTex, (void**)&tex).Succeeded && tex != null)
                                            {
                                                try
                                                {
                                                    D3D11_TEXTURE2D_DESC td = default;
                                                    tex->GetDesc(&td);
                                                    td.Usage = D3D11_USAGE.D3D11_USAGE_STAGING;
                                                    td.BindFlags = 0;
                                                    td.CPUAccessFlags = D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ;
                                                    td.MiscFlags = 0;

                                                    ID3D11Texture2D* staging = null;
                                                    dev->CreateTexture2D(&td, null, &staging);
                                                    if (staging != null)
                                                    {
                                                        try
                                                        {
                                                            ctx->CopyResource((ID3D11Resource*)staging, (ID3D11Resource*)tex);
                                                            D3D11_MAPPED_SUBRESOURCE mapped = default;
                                                            ctx->Map((ID3D11Resource*)staging, 0, D3D11_MAP.D3D11_MAP_READ, 0, &mapped);
                                                            try
                                                            {
                                                                return ReadDxgiMappedToBitmap(mapped, (int)td.Width, (int)td.Height, td.Format, crop, od.DesktopCoordinates);
                                                            }
                                                            finally { ctx->Unmap((ID3D11Resource*)staging, 0); }
                                                        }
                                                        finally { staging->Release(); }
                                                    }
                                                }
                                                finally { tex->Release(); }
                                            }
                                        }
                                        finally
                                        {
                                            res->Release();
                                            try { dupl->ReleaseFrame(); } catch { }
                                        }
                                    }
                                    finally { dupl->Release(); }
                                }
                                finally { out1->Release(); }
                            }
                            finally
                            {
                                ctx->Release();
                                dev->Release();
                            }
                        }
                        finally { output->Release(); }
                    }
                }
                finally { adapter->Release(); }
            }
        }
        finally { factory->Release(); }
        return null;
    }

    private static unsafe BitmapSource? ReadDxgiMappedToBitmap(
        D3D11_MAPPED_SUBRESOURCE mapped,
        int texW,
        int texH,
        DXGI_FORMAT fmt,
        (int x, int y, int w, int h)? crop,
        Windows.Win32.Foundation.RECT outputRect)
    {
        int startX = 0, startY = 0, copyW = texW, copyH = texH;
        if (crop is { } c)
        {
            startX = Math.Clamp(c.x - outputRect.left, 0, Math.Max(0, texW - 1));
            startY = Math.Clamp(c.y - outputRect.top, 0, Math.Max(0, texH - 1));
            copyW = Math.Clamp(c.w, 1, texW - startX);
            copyH = Math.Clamp(c.h, 1, texH - startY);
        }

        int stride = copyW * 4;
        byte[] bgra = new byte[stride * copyH];
        byte* srcBase = (byte*)mapped.pData;
        int pitch = (int)mapped.RowPitch;

        if (fmt == DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM || fmt == DXGI_FORMAT.DXGI_FORMAT_B8G8R8X8_UNORM)
        {
            for (int y = 0; y < copyH; y++)
            {
                Marshal.Copy((IntPtr)(srcBase + (startY + y) * pitch + startX * 4), bgra, y * stride, stride);
                for (int i = 3; i < stride; i += 4) bgra[y * stride + i] = 255;
            }
        }
        else if (fmt == DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT)
        {
            // HDR scRGB (16-bit Half) -> Reinhard tone-mapped sRGB BGRA32
            for (int y = 0; y < copyH; y++)
            {
                ushort* row = (ushort*)(srcBase + (startY + y) * pitch) + startX * 4;
                int dstRow = y * stride;
                for (int x = 0; x < copyW; x++)
                {
                    float r = (float)BitConverter.UInt16BitsToHalf(row[x * 4]);
                    float g = (float)BitConverter.UInt16BitsToHalf(row[x * 4 + 1]);
                    float b = (float)BitConverter.UInt16BitsToHalf(row[x * 4 + 2]);
                    bgra[dstRow + x * 4] = ToneMapSrgb(b);
                    bgra[dstRow + x * 4 + 1] = ToneMapSrgb(g);
                    bgra[dstRow + x * 4 + 2] = ToneMapSrgb(r);
                    bgra[dstRow + x * 4 + 3] = 255;
                }
            }
        }
        else
        {
            return null;
        }

        var bmp = BitmapSource.Create(copyW, copyH, 96, 96, PixelFormats.Bgra32, null, bgra, stride);
        bmp.Freeze();
        return bmp;
    }

    private static byte ToneMapSrgb(float linear)
    {
        float v = Math.Max(0f, linear);
        v = v / (1f + v); // Reinhard
        v = MathF.Pow(v, 1f / 2.2f);
        return (byte)Math.Clamp((int)(v * 255f + 0.5f), 0, 255);
    }

    private static BitmapSource? TryCaptureViaPrintWindow(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out var rc)) return null;
        int w = rc.Right - rc.Left;
        int h = rc.Bottom - rc.Top;
        if (w < 320 || h < 200) return null;

        IntPtr hdcSrc = GetDC(IntPtr.Zero);
        if (hdcSrc == IntPtr.Zero) return null;
        IntPtr hdcDest = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr hOld = IntPtr.Zero;
        try
        {
            hdcDest = CreateCompatibleDC(hdcSrc);
            hBitmap = CreateCompatibleBitmap(hdcSrc, w, h);
            hOld = SelectObject(hdcDest, hBitmap);

            if (!PrintWindow(hwnd, hdcDest, PW_RENDERFULLCONTENT))
                return null;

            SelectObject(hdcDest, hOld);
            hOld = IntPtr.Zero;

            var source = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            if (hOld != IntPtr.Zero) SelectObject(hdcDest, hOld);
            if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
            if (hdcDest != IntPtr.Zero) DeleteDC(hdcDest);
            ReleaseDC(IntPtr.Zero, hdcSrc);
        }
    }

    private static (BitmapSource bitmap, int width) CaptureViaBitBlt((int x, int y, int w, int h)? crop)
    {
        IntPtr hdcSrc = GetDC(IntPtr.Zero);
        if (hdcSrc == IntPtr.Zero)
            throw new InvalidOperationException(Loc.T("Не вдалося отримати HDC екрана.", "Failed to acquire screen HDC.", "Не удалось получить HDC экрана."));

        IntPtr hdcDest = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr hOld = IntPtr.Zero;

        try
        {
            int srcX = 0, srcY = 0, width, height;
            if (crop is { } c)
            {
                srcX = c.x; srcY = c.y; width = c.w; height = c.h;
            }
            else
            {
                width = GetDeviceCaps(hdcSrc, DESKTOPHORZRES);
                height = GetDeviceCaps(hdcSrc, DESKTOPVERTRES);
                if (width <= 0) width = (int)SystemParameters.PrimaryScreenWidth;
                if (height <= 0) height = (int)SystemParameters.PrimaryScreenHeight;
            }

            hdcDest = CreateCompatibleDC(hdcSrc);
            hBitmap = CreateCompatibleBitmap(hdcSrc, width, height);
            hOld = SelectObject(hdcDest, hBitmap);

            BitBlt(hdcDest, 0, 0, width, height, hdcSrc, srcX, srcY, SRCCOPY);
            SelectObject(hdcDest, hOld);
            hOld = IntPtr.Zero;

            BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return (source, width);
        }
        finally
        {
            if (hOld != IntPtr.Zero) SelectObject(hdcDest, hOld);
            if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
            if (hdcDest != IntPtr.Zero) DeleteDC(hdcDest);
            ReleaseDC(IntPtr.Zero, hdcSrc);
        }
    }

    private static byte[] EncodeJpeg(BitmapSource source, int width, int maxWidth, int quality)
    {
        BitmapSource target = source;
        if (width > maxWidth)
        {
            double scale = (double)maxWidth / width;
            var scaled = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            scaled.Freeze();
            target = scaled;
        }

        var encoder = new JpegBitmapEncoder { QualityLevel = quality };
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }
}
