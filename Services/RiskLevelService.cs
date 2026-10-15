using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace RiesgoFiscalApp.Services;

public sealed class RiskLevelService
{
    private const double DefaultLowUpperExclusive = 0.10;
    private const double DefaultMediumUpperExclusive = 0.25;
    private readonly IConfiguration? _configuration;

    public RiskLevelService(IConfiguration? configuration = null) => _configuration = configuration;

    public string Classify(double probability)
    {
        if (!double.IsFinite(probability) || probability < 0 || probability > 1)
            throw new CreditRiskServiceException("La probabilidad recibida no es válida.");
        var low = ReadThreshold("LowUpperExclusive", DefaultLowUpperExclusive);
        var medium = ReadThreshold("MediumUpperExclusive", DefaultMediumUpperExclusive);
        if (!(0 < low && low < medium && medium < 1))
            throw new CreditRiskServiceException("Los umbrales deben cumplir 0 < Bajo < Medio < 1. Revisá RiskLevels en appsettings.");
        return probability < low ? "Bajo" : probability < medium ? "Medio" : "Alto";
    }

    private double ReadThreshold(string name, double defaultValue)
    {
        var raw = _configuration?[$"RiskLevels:{name}"];
        if (raw == null) return defaultValue;
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
            throw new CreditRiskServiceException("La configuración de umbrales de riesgo no es válida.");
        return value;
    }
}

