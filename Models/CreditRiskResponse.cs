using System.Text.Json.Serialization;

namespace RiesgoFiscalApp.Models;

public sealed class CreditRiskResponse
{
    public const int ExpectedHorizonMonths = 24;
    public const string ExpectedTargetEvent = "delinquency_90_days_or_more";

    [JsonPropertyName("defaultProbability")]
    public required double DefaultProbability { get; init; }
    [JsonPropertyName("modelVersion")]
    public required string ModelVersion { get; init; }
    [JsonPropertyName("predictionHorizonMonths")]
    public required int PredictionHorizonMonths { get; init; }
    [JsonPropertyName("targetEvent")]
    public required string TargetEvent { get; init; }

    public void ValidateContract()
    {
        if (!double.IsFinite(DefaultProbability) || DefaultProbability < 0 || DefaultProbability > 1 ||
            string.IsNullOrWhiteSpace(ModelVersion) || PredictionHorizonMonths != ExpectedHorizonMonths ||
            TargetEvent != ExpectedTargetEvent)
            throw new InvalidDataException("El servicio Python devolvió una respuesta incompatible o una probabilidad inválida.");
    }
}

