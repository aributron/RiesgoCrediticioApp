namespace RiesgoFiscalApp.Models;

public class Document
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid CreditApplicationId { get; init; }
    public string Tipo { get; init; } = "Recibo de sueldo";
    public string FileName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
}

