using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using RiesgoFiscalApp.Models;

namespace RiesgoFiscalApp.Services;

public sealed class CreditRiskServiceException : Exception
{
    public CreditRiskServiceException(string message, Exception? inner = null) : base(message, inner) { }
}

public sealed class PythonCreditRiskService : ICreditRiskService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private static readonly string[] ResponseFields =
        { "defaultProbability", "modelVersion", "predictionHorizonMonths", "targetEvent" };

    public PythonCreditRiskService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<CreditRiskResponse> PredictAsync(CreditRiskRequest input, CancellationToken cancellationToken = default)
    {
        if (!input.IsValid()) throw new CreditRiskServiceException("Los datos de entrada no cumplen el contrato de predicción.");
        var configuredUrl = _configuration["CreditRiskApi:BaseUrl"] ?? "http://127.0.0.1:8000";
        if (!Uri.TryCreate(configuredUrl, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme != Uri.UriSchemeHttp || !baseUri.IsLoopback ||
            !string.IsNullOrEmpty(baseUri.UserInfo) || !string.IsNullOrEmpty(baseUri.Query) ||
            !string.IsNullOrEmpty(baseUri.Fragment) || baseUri.AbsolutePath != "/")
            throw new CreditRiskServiceException("Configurá CreditRiskApi:BaseUrl como una URL HTTP local, por ejemplo http://127.0.0.1:8000.");
        var timeoutText = _configuration["CreditRiskApi:TimeoutSeconds"];
        var timeoutSeconds = 15;
        if (timeoutText != null && (!int.TryParse(timeoutText, out timeoutSeconds) || timeoutSeconds < 1 || timeoutSeconds > 300))
            throw new CreditRiskServiceException("El timeout de evaluación debe estar entre 1 y 300 segundos.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "v1/predict"));
            // Solo el DTO de cuatro predictores. Sin identidad, documentación ni credenciales de IA.
            request.Content = new StringContent(JsonSerializer.Serialize(input), Encoding.UTF8, "application/json");
            using var response = await _httpClient.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw new CreditRiskServiceException(response.StatusCode switch
                {
                    HttpStatusCode.UnprocessableEntity => "El servicio Python rechazó los datos enviados (HTTP 422). Revisá los campos de evaluación.",
                    HttpStatusCode.InternalServerError => "El servicio Python no pudo ejecutar la predicción (HTTP 500). Podés reintentar.",
                    HttpStatusCode.ServiceUnavailable => "El modelo del servicio Python no está disponible (HTTP 503). Revisá sus artefactos y reiniciá el servicio.",
                    _ => $"El servicio Python devolvió un error (HTTP {(int)response.StatusCode})."
                });
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
            var names = json.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
            if (names.Length != ResponseFields.Length || names.Distinct().Count() != ResponseFields.Length ||
                ResponseFields.Any(name => !names.Contains(name))) throw new JsonException();
            var result = JsonSerializer.Deserialize<CreditRiskResponse>(body, JsonOptions) ?? throw new JsonException();
            result.ValidateContract();
            return result;
        }
        catch (CreditRiskServiceException) { throw; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CreditRiskServiceException("Se agotó el tiempo de espera del servicio Python. Podés reintentar.");
        }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException ex)
        {
            throw new CreditRiskServiceException("No se pudo conectar con el servicio Python. Verificá que FastAPI esté iniciado y que la URL sea correcta.", ex);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or InvalidOperationException or IOException)
        {
            throw new CreditRiskServiceException("El servicio Python devolvió una respuesta inválida o incompatible.", ex);
        }
    }
}

