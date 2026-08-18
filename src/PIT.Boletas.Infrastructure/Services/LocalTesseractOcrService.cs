using System.Drawing;
using System.Drawing.Imaging;
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

        using TesseractEngine engine = new(tessDataPath, language, EngineMode.Default);
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
            string tempImagePath = Path.Combine(Path.GetTempPath(), $"pit_ocr_{Guid.NewGuid():N}.png");

            try
            {
                bitmap.Save(tempImagePath, System.Drawing.Imaging.ImageFormat.Png);
                using Pix pix = Pix.LoadFromFile(tempImagePath);
                using Page page = engine.Process(pix);
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
}
