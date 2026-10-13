using RiesgoFiscalApp.Models;

namespace RiesgoFiscalApp.Services;

public interface IDocumentExtractionService
{
    Task<FinancialData> ExtractAsync(string receiptText, CancellationToken cancellationToken = default);
}

