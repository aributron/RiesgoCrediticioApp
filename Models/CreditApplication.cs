using System.Collections.ObjectModel;
using System.ComponentModel;

namespace RiesgoFiscalApp.Models;

public class CreditApplication : INotifyPropertyChanged
{
    private RiskAssessment? _resultado;
    public Guid Id { get; } = Guid.NewGuid();
    public DateTimeOffset FechaCreacion { get; } = DateTimeOffset.Now;
    public Customer Solicitante { get; } = new();
    public decimal MontoSolicitado { get; set; }
    public int PlazoMeses { get; set; }
    public ObservableCollection<Document> Documents { get; } = new();
    public RiskAssessment? Resultado
    {
        get => _resultado;
        set
        {
            _resultado = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Resultado)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Estado)));
        }
    }
    public string Estado => Resultado == null ? "Pendiente de simulación" : "Simulación disponible";
    public event PropertyChangedEventHandler? PropertyChanged;
}

