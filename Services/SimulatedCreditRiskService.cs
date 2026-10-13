using RiesgoFiscalApp.Models;

namespace RiesgoFiscalApp.Services;

public class SimulatedCreditRiskService
{
    public RiskAssessment Evaluate() => new()
    {
        ProbabilidadIncumplimiento = 0.15,
        NivelRiesgo = "Medio (simulado)"
    };
}

