namespace RiesgoFiscalApp.Services;

public interface IPdfTextExtractionService
{
    Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken = default);
}

