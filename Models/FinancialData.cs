using System.Text.Json.Serialization;

namespace RiesgoFiscalApp.Models;

public sealed class FinancialData
{
    [JsonPropertyName("employer")]
    public required string? Employer { get; init; }
    [JsonPropertyName("grossIncome")]
    public required decimal? GrossIncome { get; init; }
    [JsonPropertyName("netIncome")]
    public required decimal? NetIncome { get; init; }
    [JsonPropertyName("receiptDate")]
    public required DateOnly? ReceiptDate { get; init; }
    [JsonPropertyName("employmentSeniorityMonths")]
    public required int? EmploymentSeniorityMonths { get; init; }
}

