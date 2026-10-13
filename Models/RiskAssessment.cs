namespace RiesgoFiscalApp.Models
{
    public class RiskAssessment
    {
        public double PorcentajeConsistencia { get; set; }
        public int ScoreRiesgo { get; set; }
        public string DictamenPreliminar { get; set; } = string.Empty; // Cumple, Cumple con observaciones, No cumple
        public string Observaciones { get; set; } = string.Empty;
    }
}