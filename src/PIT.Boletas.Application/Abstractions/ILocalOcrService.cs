namespace PIT.Boletas.Application.Abstractions;

public interface ILocalOcrService
{
    Task<string> ExtractTextFromPdfAsync(string pdfPath, CancellationToken cancellationToken);

    Task<int> GetPageCountAsync(string pdfPath, CancellationToken cancellationToken);
}
