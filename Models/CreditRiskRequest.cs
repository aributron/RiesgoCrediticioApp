using System.Text.Json.Serialization;

namespace RiesgoFiscalApp.Models;

public sealed class CreditRiskRequest
{
    [JsonPropertyName("ageYears")]
    public required int AgeYears { get; init; }
    [JsonPropertyName("debtRatio")]
    public required double DebtRatio { get; init; }
    [JsonPropertyName("latePayments30To59DaysLast24Months")]
    public required int LatePayments30To59DaysLast24Months { get; init; }
    [JsonPropertyName("latePayments60To89DaysLast24Months")]
    public required int LatePayments60To89DaysLast24Months { get; init; }

    public bool IsValid() => AgeYears is >= 18 and <= 120 &&
        double.IsFinite(DebtRatio) && DebtRatio >= 0 &&
        LatePayments30To59DaysLast24Months is >= 0 and <= 24 &&
        LatePayments60To89DaysLast24Months is >= 0 and <= 24;
}

