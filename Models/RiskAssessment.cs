namespace RiesgoFiscalApp.Models;

public sealed class RiskAssessment
{
    // Fracción entre 0 y 1; la interfaz la presenta como porcentaje.
    public required double ProbabilidadMorosidadGrave { get; init; }
    public required string NivelRiesgo { get; init; }
    public required string VersionModelo { get; init; }
    public required int HorizonteMeses { get; init; }
    public required string EventoObjetivo { get; init; }

    public string EventoEvaluado => EventoObjetivo == CreditRiskResponse.ExpectedTargetEvent
        ? "Morosidad grave de 90 días o más" : "Evento no reconocido";
    public string Descripcion => "Resultado de apoyo para el analista. No representa aprobación o rechazo automático del crédito.";
}

