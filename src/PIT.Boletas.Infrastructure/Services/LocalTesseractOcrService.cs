using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Docnet.Core;
using Docnet.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PIT.Boletas.Application.Abstractions;
using PIT.Boletas.Application.Configuration;
using Tesseract;

namespace PIT.Boletas.Infrastructure.Services;

[SupportedOSPlatform("windows")]
public sealed class LocalTesseractOcrService(
    ILogger<LocalTesseractOcrService> logger,
    IOptions<LocalOcrOptions> options) : ILocalOcrService
{
    private readonly LocalOcrOptions _options = options.Value;

    public Task<string> ExtractTextFromPdfAsync(string pdfPath, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return Task.FromResult(string.Empty);
        }

        string tessDataPath = ResolveTessDataPath();
        string language = string.IsNullOrWhiteSpace(_options.Language) ? "spa" : _options.Language;

        if (!File.Exists(Path.Combine(tessDataPath, language + ".traineddata")))
        {
            throw new InvalidOperationException(
                $"No se encontro el archivo de idioma OCR: {language}.traineddata en {tessDataPath}. Verifica el empaquetado del servicio.");
        }

        StringBuilder fullText = new();

        EngineMode engineMode = ResolveEngineMode();
        PageSegMode pageSegMode = ResolvePageSegMode();

        using TesseractEngine engine = new(tessDataPath, language, engineMode);
        if (_options.UserDefinedDpi > 0)
        {
            engine.SetVariable("user_defined_dpi", _options.UserDefinedDpi.ToString(CultureInfo.InvariantCulture));
        }

        engine.SetVariable("preserve_interword_spaces", "1");
        using var docReader = DocLib.Instance.GetDocReader(pdfPath, new PageDimensions(_options.RenderWidth, _options.RenderHeight));

        int pageCount = docReader.GetPageCount();
        for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var pageReader = docReader.GetPageReader(pageIndex);
            byte[] imageBytes = pageReader.GetImage();
            int width = pageReader.GetPageWidth();
            int height = pageReader.GetPageHeight();

            using Bitmap bitmap = CreateBitmap(imageBytes, width, height);
            using Bitmap preparedBitmap = PrepareBitmapForOcr(bitmap);
            string tempImagePath = Path.Combine(Path.GetTempPath(), $"pit_ocr_{Guid.NewGuid():N}.png");

            try
            {
                preparedBitmap.Save(tempImagePath, System.Drawing.Imaging.ImageFormat.Png);
                using Pix pix = Pix.LoadFromFile(tempImagePath);
                using Page page = engine.Process(pix, pageSegMode);
                fullText.AppendLine(page.GetText());
            }
            finally
            {
                if (File.Exists(tempImagePath))
                {
                    File.Delete(tempImagePath);
                }
            }
        }

        string result = fullText.ToString().Trim();

        if (result.Length < _options.MinTextLength)
        {
            logger.LogWarning(
                "OCR local devolvio poco texto ({Length} chars) para {FilePath}",
                result.Length,
                pdfPath);
        }

        return Task.FromResult(result);
    }

    private EngineMode ResolveEngineMode()
    {
        if (Enum.TryParse(_options.EngineMode, ignoreCase: true, out EngineMode engineMode))
        {
            return engineMode;
        }

        logger.LogWarning("LocalOcr: EngineMode invalido '{EngineMode}'. Usando LstmOnly.", _options.EngineMode);
        return EngineMode.LstmOnly;
    }

    private PageSegMode ResolvePageSegMode()
    {
        if (Enum.TryParse(_options.PageSegMode, ignoreCase: true, out PageSegMode pageSegMode))
        {
            return pageSegMode;
        }

        logger.LogWarning("LocalOcr: PageSegMode invalido '{PageSegMode}'. Usando Auto.", _options.PageSegMode);
        return PageSegMode.Auto;
    }

    private string ResolveTessDataPath()
    {
        if (Path.IsPathRooted(_options.TessDataPath))
        {
            return _options.TessDataPath;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, _options.TessDataPath));
    }

    private static Bitmap CreateBitmap(byte[] bgraBytes, int width, int height)
    {
        Bitmap bitmap = new(width, height, PixelFormat.Format32bppArgb);
        Rectangle rect = new(0, 0, width, height);
        BitmapData bmpData = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        try
        {
            Marshal.Copy(bgraBytes, 0, bmpData.Scan0, bgraBytes.Length);
        }
        finally
        {
            bitmap.UnlockBits(bmpData);
        }

        return bitmap;
    }

    private Bitmap PrepareBitmapForOcr(Bitmap source)
    {
        Bitmap prepared = new(source.Width, source.Height, PixelFormat.Format24bppRgb);

        using (Graphics graphics = Graphics.FromImage(prepared))
        {
            graphics.DrawImage(source, 0, 0, source.Width, source.Height);
        }

        if (!_options.EnableImagePreprocessing)
        {
            return prepared;
        }

        Rectangle rect = new(0, 0, prepared.Width, prepared.Height);
        BitmapData data = prepared.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);

        try
        {
            int bytes = Math.Abs(data.Stride) * data.Height;
            byte[] buffer = new byte[bytes];
            Marshal.Copy(data.Scan0, buffer, 0, bytes);

            double contrast = Math.Clamp(_options.ContrastBoost, 0.8, 2.2);
            int threshold = Math.Clamp(_options.BinarizationThreshold, 0, 255);

            for (int y = 0; y < data.Height; y++)
            {
                int rowOffset = y * data.Stride;
                for (int x = 0; x < data.Width; x++)
                {
                    int offset = rowOffset + (x * 3);

                    byte b = buffer[offset];
                    byte g = buffer[offset + 1];
                    byte r = buffer[offset + 2];

                    int gray = (int)((0.299 * r) + (0.587 * g) + (0.114 * b));
                    int contrasted = (int)(((gray - 128) * contrast) + 128);
                    contrasted = Math.Clamp(contrasted, 0, 255);

                    byte bin = contrasted >= threshold ? (byte)255 : (byte)0;
                    buffer[offset] = bin;
                    buffer[offset + 1] = bin;
                    buffer[offset + 2] = bin;
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
}
