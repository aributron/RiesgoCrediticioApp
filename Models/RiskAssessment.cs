namespace RiesgoFiscalApp.Models;

public class RiskAssessment
{
    // Fracción entre 0 y 1, presentada como porcentaje en la interfaz.
    public double ProbabilidadIncumplimiento { get; init; }
    public string NivelRiesgo { get; init; } = string.Empty;
    public bool EsSimulado => true;
    public string Descripcion => "Simulación — sin modelo predictivo. Valores fijos de demostración, independientes de los datos del solicitante.";
}

