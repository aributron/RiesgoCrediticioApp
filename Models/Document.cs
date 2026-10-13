using System.ComponentModel;

namespace RiesgoFiscalApp.Models;

public class Document : INotifyPropertyChanged
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid CreditApplicationId { get; init; }
    public string Tipo { get; init; } = "Recibo de sueldo";
    public string FileName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public FinancialData? FinancialData { get; private set; }
    public DocumentProcessingStatus ProcessingStatus { get; private set; } = DocumentProcessingStatus.Pending;
    public string ProcessingMessage { get; private set; } = string.Empty;
    public bool HasFinancialData => FinancialData != null;
    public string ReviewStatus => HasFinancialData ? "Pendiente de revisión" : "Sin resultado documental";
    public string ProcessingStatusText => ProcessingStatus switch
    {
        DocumentProcessingStatus.Pending => "Pendiente",
        DocumentProcessingStatus.Processing => "Procesando",
        DocumentProcessingStatus.Processed => "Procesado",
        DocumentProcessingStatus.NoText => "Sin texto extraíble",
        _ => "Error"
    };

    public void BeginProcessing()
    {
        FinancialData = null;
        ProcessingStatus = DocumentProcessingStatus.Processing;
        ProcessingMessage = "Leyendo PDF y extrayendo información financiera…";
        NotifyProcessingChanged();
    }

    public void CompleteProcessing(FinancialData data)
    {
        FinancialData = data;
        ProcessingStatus = DocumentProcessingStatus.Processed;
        ProcessingMessage = "Extracción disponible. Los campos ausentes se muestran como No obtenido.";
        NotifyProcessingChanged();
    }

    public void FailProcessing(DocumentProcessingStatus status, string message)
    {
        FinancialData = null;
        ProcessingStatus = status;
        ProcessingMessage = message;
        NotifyProcessingChanged();
    }

    private void NotifyProcessingChanged()
    {
        foreach (var name in new[] { nameof(FinancialData), nameof(ProcessingStatus), nameof(ProcessingMessage),
            nameof(HasFinancialData), nameof(ReviewStatus), nameof(ProcessingStatusText) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

