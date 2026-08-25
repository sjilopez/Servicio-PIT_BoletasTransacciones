using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Docnet.Core;
using Docnet.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PIT.Boletas.Application.Abstractions;
using PIT.Boletas.Application.Configuration;
using PIT.Boletas.Domain.Entities;
using PIT.Boletas.Infrastructure.Utils;
using UglyToad.PdfPig;

namespace PIT.Boletas.Infrastructure.Services;

[SupportedOSPlatform("windows")]
public sealed class StageFiveCompressionService(
    ILogger<StageFiveCompressionService> logger,
    IOperationalEventService operationalEventService,
    IOptions<PipelineFoldersOptions> folderOptions,
    IOptions<CompressionOptions> compressionOptions) : IStageFiveCompressionService
{
    private readonly PipelineFoldersOptions _folders = folderOptions.Value;
    private readonly CompressionOptions _compression = compressionOptions.Value;

    public async Task<int> ProcessPendingAsync(CancellationToken cancellationToken)
    {
        string sourcePath = PipelinePathResolver.StagePath(_folders.BasePath, "6_COMPRESS");
        string targetPath = PipelinePathResolver.StagePath(_folders.BasePath, "7_COPY_AZURE_FILES");

        if (!Directory.Exists(sourcePath))
        {
            return 0;
        }

        bool enabled = _compression.Enabled;
        int moved = 0;

        foreach (string pdfPath in Directory.GetFiles(sourcePath, "*.pdf", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            FileInfo info = new(pdfPath);
            long before = info.Length;
            long after = before;
            DocumentProcessingMetadata metadata = MetadataSidecarStore.LoadOrCreate(pdfPath);

            if (enabled)
            {
                string tempCompressed = Path.Combine(Path.GetTempPath(), $"pit_cmp_{Guid.NewGuid():N}.pdf");
                try
                {
                    CompressPdf(pdfPath, tempCompressed, _compression);
                    if (File.Exists(tempCompressed))
                    {
                        long compressedSize = new FileInfo(tempCompressed).Length;
                        if (compressedSize < before)
                        {
                            File.Copy(tempCompressed, pdfPath, overwrite: true);
                            after = compressedSize;
                        }
                        else
                        {
                            logger.LogInformation(
                                "Compressed file ({CompressedSize} bytes) is not smaller than original ({OriginalSize} bytes) for {FileName}. Keeping original file.",
                                compressedSize,
                                before,
                                Path.GetFileName(pdfPath));
                        }
                    }
                }
                catch (Exception ex)
                {
                    await operationalEventService.TrackAsync(
                        "error",
                        "operational",
                        "CMP001",
                        "Fallo en compresion PDF",
                        ex.Message,
                        "6_COMPRESS",
                        metadata,
                        null,
                        cancellationToken);
                    logger.LogWarning(ex, "Compression failed for {FileName}; file will continue without compression.", Path.GetFileName(pdfPath));
                }
                finally
                {
                    if (File.Exists(tempCompressed))
                    {
                        File.Delete(tempCompressed);
                    }
                }
            }

            double pct = before == 0 ? 0 : (1 - (double)after / before) * 100;
            logger.LogInformation(
                "Stage5 compress | File: {FileName} | Before: {Before} | After: {After} | ReductionPct: {ReductionPct}",
                Path.GetFileName(pdfPath),
                before,
                after,
                pct);

            string destination = Path.Combine(targetPath, Path.GetFileName(pdfPath));
            MetadataSidecarStore.MoveWithMetadata(pdfPath, destination);
            metadata.FileName = Path.GetFileName(destination);
            MetadataSidecarStore.Save(destination, metadata);
            moved++;
        }

        return moved;
    }

    private static void CompressPdf(string sourcePdfPath, string outputPdfPath, CompressionOptions options)
    {
        int dpi = Math.Max(72, options.TargetDpi);

        using PdfSharp.Pdf.PdfDocument output = new();
        using UglyToad.PdfPig.PdfDocument sourceDocument = UglyToad.PdfPig.PdfDocument.Open(sourcePdfPath);

        int pageCount = sourceDocument.NumberOfPages;
        for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            UglyToad.PdfPig.Content.Page sourcePage = sourceDocument.GetPage(pageIndex + 1);
            double pageWidthPoints = sourcePage.Width;
            double pageHeightPoints = sourcePage.Height;

            int targetWidth = (int)Math.Max(1, Math.Round(pageWidthPoints * dpi / 72.0));
            int targetHeight = (int)Math.Max(1, Math.Round(pageHeightPoints * dpi / 72.0));
            int smallerDimension = Math.Min(targetWidth, targetHeight);
            int largerDimension = Math.Max(targetWidth, targetHeight);

            using var reader = DocLib.Instance.GetDocReader(
                sourcePdfPath,
                new PageDimensions(smallerDimension, largerDimension));
            using var pageReader = reader.GetPageReader(pageIndex);

            byte[] imageBytes = pageReader.GetImage();
            int width = pageReader.GetPageWidth();
            int height = pageReader.GetPageHeight();

            using Bitmap bitmap = CreateBitmap(imageBytes, width, height);
            using Bitmap processedBitmap = options.Grayscale ? ToGrayscale(bitmap) : (Bitmap)bitmap.Clone();
            byte[] jpgBytes = ToJpegBytes(processedBitmap, options.JpegQuality);

            PdfPage page = output.AddPage();
            page.Width = XUnit.FromPoint(pageWidthPoints);
            page.Height = XUnit.FromPoint(pageHeightPoints);

            using XGraphics gfx = XGraphics.FromPdfPage(page);
            using MemoryStream ms = new(jpgBytes);
            using XImage xImage = XImage.FromStream(ms);
            gfx.DrawImage(xImage, 0.0, 0.0, page.Width.Point, page.Height.Point);
        }

        output.Save(outputPdfPath);
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

    private static Bitmap ToGrayscale(Bitmap source)
    {
        Bitmap gray = new(source.Width, source.Height);
        using Graphics graphics = Graphics.FromImage(gray);

        ColorMatrix colorMatrix = new(
        [
            [0.3f, 0.3f, 0.3f, 0, 0],
            [0.59f, 0.59f, 0.59f, 0, 0],
            [0.11f, 0.11f, 0.11f, 0, 0],
            [0, 0, 0, 1, 0],
            [0, 0, 0, 0, 1]
        ]);

        using ImageAttributes attributes = new();
        attributes.SetColorMatrix(colorMatrix);
        graphics.DrawImage(
            source,
            new Rectangle(0, 0, source.Width, source.Height),
            0,
            0,
            source.Width,
            source.Height,
            GraphicsUnit.Pixel,
            attributes);

        return gray;
    }

    private static byte[] ToJpegBytes(Bitmap bitmap, int quality)
    {
        ImageCodecInfo jpgEncoder = ImageCodecInfo.GetImageEncoders()
            .First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);

        using EncoderParameters parameters = new(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, Math.Clamp(quality, 10, 100));

        using MemoryStream ms = new();
        bitmap.Save(ms, jpgEncoder, parameters);
        return ms.ToArray();
    }
}
