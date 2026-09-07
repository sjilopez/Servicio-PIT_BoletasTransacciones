using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Docnet.Core;
using Docnet.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaddleOCRSharp;
using PIT.Boletas.Application.Abstractions;
using PIT.Boletas.Application.Configuration;

namespace PIT.Boletas.Infrastructure.Services;

[SupportedOSPlatform("windows")]
public sealed class LocalPaddleOcrService(
    ILogger<LocalPaddleOcrService> logger,
    IOptions<LocalOcrOptions> options) : ILocalOcrService, IDisposable
{
    private const int PaddlePageSegmentCount = 4;
    private readonly LocalOcrOptions _options = options.Value;
    private readonly object _engineLock = new();
    private PaddleOCREngine? _engine;
    private bool _disposed;

    public Task<string> ExtractTextFromPdfAsync(string pdfPath, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return Task.FromResult(string.Empty);
        }

        ValidateModelFiles();
        PaddleOCREngine engine = GetEngine();
        string text = string.Empty;

        using var docReader = DocLib.Instance.GetDocReader(
            pdfPath,
            new PageDimensions(Math.Max(1, _options.RenderWidth), Math.Max(1, _options.RenderHeight)));

        for (int pageIndex = 0; pageIndex < docReader.GetPageCount(); pageIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var pageReader = docReader.GetPageReader(pageIndex);
            using Bitmap bitmap = CreateBitmap(
                pageReader.GetImage(),
                pageReader.GetPageWidth(),
                pageReader.GetPageHeight());

            if (pageIndex == 0 && _options.PaddleReadHeader)
            {
                using Bitmap header = CropHeader(bitmap);
                using Bitmap preparedHeader = PrepareHeader(header);
                AppendSegmentedResults(engine, preparedHeader, ref text, cancellationToken);
            }

            AppendSegmentedResults(engine, bitmap, ref text, cancellationToken);
        }

        string normalized = text.Trim();
        if (normalized.Length < _options.MinTextLength)
        {
            logger.LogWarning(
                "PaddleOCR devolvio poco texto ({Length} chars) para {FilePath}",
                normalized.Length,
                pdfPath);
        }

        return Task.FromResult(normalized);
    }

    public Task<int> GetPageCountAsync(string pdfPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var docReader = DocLib.Instance.GetDocReader(
            pdfPath,
            new PageDimensions(Math.Max(1, _options.RenderWidth), Math.Max(1, _options.RenderHeight)));
        return Task.FromResult(docReader.GetPageCount());
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_engineLock)
        {
            _engine?.Dispose();
            _engine = null;
            _disposed = true;
        }
    }

    private PaddleOCREngine GetEngine()
    {
        lock (_engineLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_engine is not null)
            {
                return _engine;
            }

            OCRParameter parameters = new()
            {
                use_gpu = false,
                max_side_len = Math.Max(32, _options.PaddleMaxSideLength),
                det_db_box_thresh = (float)Math.Clamp(_options.PaddleDetDbBoxThreshold, 0.1, 0.99)
            };

            _engine = new PaddleOCREngine(
                new OCRModelConfig(
                    ResolvePath(_options.PaddleDetModelPath),
                    ResolvePath(_options.PaddleClsModelPath),
                    ResolvePath(_options.PaddleRecModelPath),
                    ResolvePath(_options.PaddleDictionaryPath)),
                parameters);
            return _engine;
        }
    }

    private void ValidateModelFiles()
    {
        string[] required =
        [
            ResolvePath(_options.PaddleDetModelPath),
            ResolvePath(_options.PaddleClsModelPath),
            ResolvePath(_options.PaddleRecModelPath),
            ResolvePath(_options.PaddleDictionaryPath)
        ];

        foreach (string path in required)
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                throw new InvalidOperationException($"No se encontro un recurso local de PaddleOCR: {path}");
            }
        }
    }

    private static string ResolvePath(string path)
    {
        return Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
    }

    private void AppendResult(OCRResult? result, ref string text)
    {
        if (result is not null && !string.IsNullOrWhiteSpace(result.Text))
        {
            text += result.Text.Trim() + Environment.NewLine;
        }
    }

    private void AppendSegmentedResults(
        PaddleOCREngine engine,
        Bitmap source,
        ref string text,
        CancellationToken cancellationToken)
    {
        int segmentWidth = (int)Math.Ceiling(source.Width / (double)PaddlePageSegmentCount);
        for (int segmentIndex = 0; segmentIndex < PaddlePageSegmentCount; segmentIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int x = segmentIndex * segmentWidth;
            int width = Math.Min(segmentWidth, source.Width - x);
            if (width <= 0)
            {
                break;
            }

            using Bitmap segment = source.Clone(
                new Rectangle(x, 0, width, source.Height),
                PixelFormat.Format32bppArgb);
            OCRResult? result;
            lock (_engineLock)
            {
                result = engine.DetectText(segment);
            }

            AppendResult(result, ref text);
        }
    }

    private Bitmap CropHeader(Bitmap source)
    {
        double ratio = Math.Clamp(_options.PaddleHeaderCropRatio, 0.05, 0.6);
        int x = Math.Clamp(_options.PaddleHeaderCropX, 0, source.Width - 1);
        int y = Math.Clamp(_options.PaddleHeaderCropY, 0, source.Height - 1);
        int width = source.Width - x;
        int height = Math.Clamp((int)Math.Round(source.Height * ratio), 1, source.Height - y);
        return source.Clone(new Rectangle(x, y, width, height), PixelFormat.Format32bppArgb);
    }

    private Bitmap PrepareHeader(Bitmap source)
    {
        if (!_options.PaddleHeaderPreprocessing)
        {
            return (Bitmap)source.Clone();
        }

        int scale = Math.Clamp(_options.PaddleHeaderScale, 1, 4);
        Bitmap prepared = new(source.Width * scale, source.Height * scale, PixelFormat.Format24bppRgb);

        using (Graphics graphics = Graphics.FromImage(prepared))
        {
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(source, new Rectangle(0, 0, prepared.Width, prepared.Height));
        }

        Rectangle rect = new(0, 0, prepared.Width, prepared.Height);
        BitmapData data = prepared.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
        try
        {
            int bytes = Math.Abs(data.Stride) * data.Height;
            byte[] buffer = new byte[bytes];
            Marshal.Copy(data.Scan0, buffer, 0, bytes);
            double contrast = Math.Clamp(_options.PaddleHeaderContrast, 0.5, 3.0);

            for (int y = 0; y < data.Height; y++)
            {
                int rowOffset = y * data.Stride;
                for (int x = 0; x < data.Width; x++)
                {
                    int offset = rowOffset + (x * 3);
                    int gray = (int)((0.114 * buffer[offset]) + (0.587 * buffer[offset + 1]) + (0.299 * buffer[offset + 2]));
                    byte adjusted = (byte)Math.Clamp(((gray - 128) * contrast) + 128, 0, 255);
                    buffer[offset] = adjusted;
                    buffer[offset + 1] = adjusted;
                    buffer[offset + 2] = adjusted;
                }
            }

            Marshal.Copy(buffer, 0, data.Scan0, bytes);
        }
        finally
        {
            prepared.UnlockBits(data);
        }

        return prepared;
    }

    private static Bitmap CreateBitmap(byte[] bgraBytes, int width, int height)
    {
        Bitmap bitmap = new(width, height, PixelFormat.Format32bppArgb);
        Rectangle rect = new(0, 0, width, height);
        BitmapData data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        try
        {
            Marshal.Copy(bgraBytes, 0, data.Scan0, bgraBytes.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }
}