using RiesgoFiscalApp.Models;

namespace RiesgoFiscalApp.Services;

public interface ICreditRiskService
{
    Task<CreditRiskResponse> PredictAsync(CreditRiskRequest request, CancellationToken cancellationToken = default);
}

