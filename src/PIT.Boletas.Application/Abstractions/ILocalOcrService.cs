namespace PIT.Boletas.Application.Abstractions;

public interface ILocalOcrService
{
    Task<string> ExtractTextFromPdfAsync(string pdfPath, CancellationToken cancellationToken);
}
