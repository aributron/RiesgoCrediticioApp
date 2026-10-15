using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RiesgoFiscalApp.Models;

public class CreditApplication : INotifyPropertyChanged
{
    private RiskAssessment? _resultado;
    private CreditEvaluationStatus _evaluationStatus;
    private string _evaluationMessage = string.Empty;
    private decimal _montoSolicitado;
    private int _plazoMeses;
    public Guid Id { get; } = Guid.NewGuid();
    public DateTimeOffset FechaCreacion { get; } = DateTimeOffset.Now;
    public Customer Solicitante { get; } = new();
    public decimal MontoSolicitado { get => _montoSolicitado; set { _montoSolicitado = value; Notify(); } }
    public int PlazoMeses { get => _plazoMeses; set { _plazoMeses = value; Notify(); } }
    public ObservableCollection<Document> Documents { get; } = new();
    public RiskAssessment? Resultado { get => _resultado; set { _resultado = value; Notify(); } }
    public CreditEvaluationStatus EvaluationStatus
    {
        get => _evaluationStatus;
        set { _evaluationStatus = value; Notify(); Notify(nameof(Estado)); }
    }
    public string EvaluationMessage { get => _evaluationMessage; set { _evaluationMessage = value; Notify(); } }
    public string Estado => EvaluationStatus switch
    {
        CreditEvaluationStatus.Evaluating => "Evaluando riesgo",
        CreditEvaluationStatus.Available => "Evaluación disponible",
        CreditEvaluationStatus.Unavailable => "Evaluación no disponible",
        _ => "Pendiente de evaluación"
    };
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

