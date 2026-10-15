using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using RiesgoFiscalApp.Models;
using RiesgoFiscalApp.Services;
using RiesgoFiscalApp.ViewModels;
using UglyToad.PdfPig.Writer;

var checks = 0;
void Check(bool condition, string label)
{
    checks++;
    if (!condition) throw new Exception("FALLÓ: " + label);
}
async Task<T> Fails<T>(Func<Task> action, string label) where T : Exception
{
    try { await action(); }
    catch (T error) { Check(true, label); return error; }
    throw new Exception("FALLÓ: " + label);
}
IConfiguration Config(params (string Key, string? Value)[] entries) => new ConfigurationBuilder()
    .AddInMemoryCollection(entries.ToDictionary(entry => entry.Key, entry => entry.Value)).Build();
CreditRiskResponse Response(double probability = 0.1164822345899684) => new()
{
    DefaultProbability = probability, ModelVersion = "gmsc-logreg-v1",
    PredictionHorizonMonths = 24, TargetEvent = CreditRiskResponse.ExpectedTargetEvent
};
Customer CustomerData() => new()
{
    Nombre = "Persona de prueba", Dni = "30123456", Edad = 35,
    IngresoBrutoMensualDeclarado = 1200000, CompromisosMensuales = 420000,
    Atrasos30a59Ultimos24Meses = 1, Atrasos60a89Ultimos24Meses = 0
};
var temporary = Path.Combine(Path.GetTempPath(), "CreditRiskChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
var receiptPath = Path.Combine(temporary, "receipt.pdf");
var pdf = new PdfDocumentBuilder();
pdf.AddPage(595, 842);
File.WriteAllBytes(receiptPath, pdf.Build());

MainViewModel MakeVm(ICreditRiskService service)
{
    var vm = new MainViewModel(new UnusedPdf(), new UnusedExtraction(), service, new RiskLevelService())
    {
        AgeInput = "35", IncomeInput = "1200000", CommitmentsInput = "420000",
        Late30Input = "1", Late60Input = "0", AmountInput = "1500000", TermInput = "12"
    };
    vm.CurrentCustomer.Nombre = "Persona de prueba";
    vm.CurrentCustomer.Dni = "30123456";
    vm.Documents.Add(new Document { CreditApplicationId = vm.CurrentApplication.Id, FileName = "receipt.pdf", FilePath = receiptPath });
    return vm;
}
try
{
    var customer = CustomerData();
    Check(CreditRiskInputBuilder.TryBuild(customer, out var builtInput, out _), "Construye entrada válida");
    var input = builtInput!;
    Check(input.DebtRatio == 0.35, "Ratio decimal sin multiplicar por 100");
    customer.CompromisosMensuales = 2400000;
    CreditRiskInputBuilder.TryBuild(customer, out var aboveOne, out _);
    Check(aboveOne!.DebtRatio == 2, "No limita el ratio a 1");
    customer.CompromisosMensuales = 0;
    CreditRiskInputBuilder.TryBuild(customer, out var zero, out _);
    Check(zero!.DebtRatio == 0, "Cero explícito válido");
    customer.CompromisosMensuales = null;
    Check(!CreditRiskInputBuilder.TryBuild(customer, out _, out _), "No asume cero para compromisos desconocidos");
    customer = CustomerData();
    foreach (var count in new int?[] { null, -1, 25 })
    {
        customer.Atrasos30a59Ultimos24Meses = count;
        Check(!CreditRiskInputBuilder.TryBuild(customer, out _, out _), "Rango atrasos 30–59");
        customer.Atrasos30a59Ultimos24Meses = 1;
        customer.Atrasos60a89Ultimos24Meses = count;
        Check(!CreditRiskInputBuilder.TryBuild(customer, out _, out _), "Rango atrasos 60–89");
        customer.Atrasos60a89Ultimos24Meses = 0;
    }
    foreach (var count in new[] { 0, 24 })
    {
        customer.Atrasos30a59Ultimos24Meses = customer.Atrasos60a89Ultimos24Meses = count;
        Check(CreditRiskInputBuilder.TryBuild(customer, out _, out _), "Límites inclusivos de atrasos");
    }
    customer = CustomerData();
    foreach (var age in new int?[] { null, 17, 121 })
    {
        customer.Edad = age;
        Check(!CreditRiskInputBuilder.TryBuild(customer, out _, out _), "Edad fuera de rango");
    }
    customer = CustomerData();
    foreach (var income in new decimal?[] { null, 0, -1 })
    {
        customer.IngresoBrutoMensualDeclarado = income;
        Check(!CreditRiskInputBuilder.TryBuild(customer, out _, out _), "Ingreso inválido");
    }
    customer = CustomerData();
    customer.CompromisosMensuales = decimal.MaxValue;
    customer.IngresoBrutoMensualDeclarado = 0.0000000000000000000000000001m;
    Check(!CreditRiskInputBuilder.TryBuild(customer, out _, out _), "Overflow del ratio controlado");

    var levels = new RiskLevelService();
    foreach (var (probability, expected) in new[] { (0d, "Bajo"), (0.099999d, "Bajo"), (0.1d, "Medio"),
        (0.249999d, "Medio"), (0.25d, "Alto"), (1d, "Alto") })
        Check(levels.Classify(probability) == expected, "Límites clasificación");
    foreach (var invalid in new[] { -0.01, 1.01, double.NaN, double.PositiveInfinity })
        await Fails<CreditRiskServiceException>(() => Task.FromResult(levels.Classify(invalid)), "Probabilidad inválida en clasificación");
    var customLevels = new RiskLevelService(Config(("RiskLevels:LowUpperExclusive", "0.2"), ("RiskLevels:MediumUpperExclusive", "0.4")));
    Check(customLevels.Classify(0.15) == "Bajo" && customLevels.Classify(0.2) == "Medio" &&
        customLevels.Classify(0.4) == "Alto", "Umbrales configurables centralizados");
    foreach (var low in new[] { "0.5", "-1", "NaN", "abc" })
        await Fails<CreditRiskServiceException>(() => Task.FromResult(new RiskLevelService(
            Config(("RiskLevels:LowUpperExclusive", low))).Classify(0.2)), "Configuración inválida de niveles");

    const string validJson = """
        {"defaultProbability":0.1164822345899684,"modelVersion":"gmsc-logreg-v1","predictionHorizonMonths":24,"targetEvent":"delinquency_90_days_or_more"}
        """;
    var body = validJson;
    var status = HttpStatusCode.OK;
    var calls = 0;
    using var http = new HttpClient(new TestHandler(async (request, token) =>
    {
        calls++;
        Check(request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/v1/predict", "Endpoint POST");
        Check(request.Headers.Authorization == null, "Sin credenciales de OpenAI");
        var payload = await request.Content!.ReadAsStringAsync(token);
        using var document = JsonDocument.Parse(payload);
        var fields = document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
        Check(fields.Length == 4 && fields.ToHashSet().SetEquals(new[] {
            "ageYears", "debtRatio", "latePayments30To59DaysLast24Months", "latePayments60To89DaysLast24Months" }), "Solo cuatro campos, sin identidad ni documentos");
        Check(document.RootElement.GetProperty("debtRatio").GetDouble() == 0.35, "Ratio en JSON como número");
        return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }));
    var config = Config(("CreditRiskApi:BaseUrl", "http://127.0.0.1:8000"), ("CreditRiskApi:TimeoutSeconds", "1"));
    var client = new PythonCreditRiskService(http, config);
    var result = await client.PredictAsync(input);
    Check(result.DefaultProbability == Response().DefaultProbability && result.ModelVersion == "gmsc-logreg-v1", "Deserialización del contrato");
    foreach (var badJson in new[]
    {
        "", "not JSON", "null", "[]", "{}",
        validJson.Replace("0.1164822345899684", "-0.01"),
        validJson.Replace("0.1164822345899684", "1.01"),
        validJson.Replace("0.1164822345899684", "\"NaN\""),
        validJson.Replace("0.1164822345899684", "null"),
        validJson.Replace("gmsc-logreg-v1", " "),
        validJson.Replace("\"predictionHorizonMonths\":24", "\"predictionHorizonMonths\":12"),
        validJson.Replace("delinquency_90_days_or_more", "unknown"),
        validJson.Replace("\"modelVersion\":", "\"extra\":1,\"modelVersion\":"),
        validJson.Replace("\"modelVersion\":", "\"modelVersion\":null,\"modelVersion\":"),
    })
    {
        body = badJson;
        await Fails<CreditRiskServiceException>(() => client.PredictAsync(input), "Respuesta inválida rechazada");
    }
    foreach (var field in new[] { "defaultProbability", "modelVersion", "predictionHorizonMonths", "targetEvent" })
    {
        var incomplete = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(validJson)!;
        incomplete.Remove(field);
        body = JsonSerializer.Serialize(incomplete);
        await Fails<CreditRiskServiceException>(() => client.PredictAsync(input), "Campo obligatorio ausente");
    }
    body = validJson;
    foreach (var errorCode in new[] { HttpStatusCode.UnprocessableEntity, HttpStatusCode.InternalServerError,
        HttpStatusCode.ServiceUnavailable, HttpStatusCode.BadGateway })
    {
        status = errorCode;
        var error = await Fails<CreditRiskServiceException>(() => client.PredictAsync(input), "Error HTTP controlado");
        Check(error.Message.Contains(((int)errorCode).ToString()), "Error distingue código HTTP");
    }
    status = HttpStatusCode.OK;
    var before = calls;
    foreach (var url in new[] { "invalid", "http://example.com", "http://127.0.0.1:8000/v1/predict" })
        await Fails<CreditRiskServiceException>(() => new PythonCreditRiskService(http,
            Config(("CreditRiskApi:BaseUrl", url))).PredictAsync(input), "URL inválida");
    await Fails<CreditRiskServiceException>(() => new PythonCreditRiskService(http,
        Config(("CreditRiskApi:TimeoutSeconds", "0"))).PredictAsync(input), "Timeout inválido");
    Check(calls == before, "Configuración inválida no envía solicitud");

    using var offlineHttp = new HttpClient(new TestHandler((_, _) => throw new HttpRequestException("connection refused")));
    await Fails<CreditRiskServiceException>(() => new PythonCreditRiskService(offlineHttp, config).PredictAsync(input), "Servicio no disponible");
    using var slowHttp = new HttpClient(new TestHandler(async (_, token) =>
    {
        await Task.Delay(5000, token);
        return new HttpResponseMessage(HttpStatusCode.OK);
    }));
    var slowClient = new PythonCreditRiskService(slowHttp, config);
    var timeoutError = await Fails<CreditRiskServiceException>(() => slowClient.PredictAsync(input), "Timeout");
    Check(timeoutError.Message.Contains("tiempo de espera"), "Mensaje de timeout");
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    await Fails<OperationCanceledException>(() => slowClient.PredictAsync(input, cancelled.Token), "Cancelación distinta de timeout");

    var immediate = new DelegateRiskService((_, _) => Task.FromResult(Response()));
    var vm = MakeVm(immediate);
    Check(vm.EvaluateCommand.CanExecute(null), "Formulario válido");
    await vm.EvaluateRiskAsync();
    Check(vm.CurrentApplication.EvaluationStatus == CreditEvaluationStatus.Available && vm.HasAssessment, "Estado disponible");
    Check(vm.AssessmentResult!.NivelRiesgo == "Medio" && vm.AssessmentResult.HorizonteMeses == 24 &&
        vm.AssessmentResult.EventoEvaluado == "Morosidad grave de 90 días o más", "Resultado humano y metadata");
    var saved = vm.AssessmentResult;
    vm.IncomeInput = "1300000";
    Check(!vm.HasAssessment && vm.CurrentApplication.EvaluationStatus == CreditEvaluationStatus.Pending, "Edición invalida resultado");
    foreach (var invalid in new[] { "", "-1", "25", "1,5", "abc", "2147483648" })
    {
        vm.Late30Input = invalid;
        Check(!vm.EvaluateCommand.CanExecute(null), "No admite atrasos inválidos/desconocidos");
        var priorCalls = immediate.Calls;
        await vm.EvaluateRiskAsync();
        Check(immediate.Calls == priorCalls, "Campos inválidos no envían petición");
    }
    vm.Late30Input = "24";
    vm.Late60Input = "24";
    Check(vm.EvaluateCommand.CanExecute(null), "Admite ambos límites 24");
    vm.NewApplicationCommand.Execute(null);
    Check(vm.AgeInput == "" && vm.CommitmentsInput == "" && vm.Late30Input == "" &&
        vm.Late60Input == "" && vm.CurrentCustomer.Atrasos30a59Ultimos24Meses == null, "Nueva solicitud sin ceros implícitos");

    foreach (var (label, edit) in new (string, Action<MainViewModel>)[]
    {
        ("edad", view => view.AgeInput = "36"), ("ingreso", view => view.IncomeInput = "1300000"),
        ("compromisos", view => view.CommitmentsInput = "480000"), ("atrasos30", view => view.Late30Input = "2"),
        ("atrasos60", view => view.Late60Input = "1"), ("monto", view => view.AmountInput = "2000000"),
        ("plazo", view => view.TermInput = "24"), ("nombre", view => view.CurrentCustomer.Nombre = "Otro solicitante"),
        ("DNI", view => view.CurrentCustomer.Dni = "32123456"), ("documentos", view => view.Documents.Clear()),
        ("monto programático", view => view.CurrentApplication.MontoSolicitado = 2500000)
    })
    {
        var completion = new TaskCompletionSource<CreditRiskResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var deferred = new DelegateRiskService((_, _) => completion.Task); // Intencionalmente ignora cancelación.
        var pendingVm = MakeVm(deferred);
        var operation = pendingVm.EvaluateRiskAsync();
        Check(pendingVm.IsBusy && !pendingVm.HasAssessment &&
            pendingVm.CurrentApplication.EvaluationStatus == CreditEvaluationStatus.Evaluating, "Estado evaluando");
        edit(pendingVm);
        Check(deferred.Token.IsCancellationRequested, "Cancela ante cambio: " + label);
        completion.SetResult(Response());
        await operation;
        Check(!pendingVm.HasAssessment && !pendingVm.IsBusy &&
            pendingVm.CurrentApplication.EvaluationStatus == CreditEvaluationStatus.Pending, "Descarta respuesta anterior: " + label);
        Check(deferred.LastInput!.DebtRatio == 0.35, "Snapshot conserva entrada inicial");
    }
    var lateError = new TaskCompletionSource<CreditRiskResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
    var errorVm = MakeVm(new DelegateRiskService((_, _) => lateError.Task));
    var lateOperation = errorVm.EvaluateRiskAsync();
    errorVm.AgeInput = "36";
    lateError.SetException(new CreditRiskServiceException("old-error-marker"));
    await lateOperation;
    Check(!errorVm.CurrentApplication.EvaluationMessage.Contains("old-error-marker") &&
        errorVm.CurrentApplication.EvaluationStatus == CreditEvaluationStatus.Pending, "Descarta error anterior");
    foreach (var error in new[] { "No se pudo conectar", "tiempo de espera", "HTTP 422", "HTTP 500", "HTTP 503", "respuesta inválida" })
    {
        var failedVm = MakeVm(new DelegateRiskService((_, _) => throw new CreditRiskServiceException(error)));
        await failedVm.EvaluateRiskAsync();
        Check(!failedVm.HasAssessment && failedVm.CurrentApplication.Estado == "Evaluación no disponible" &&
            failedVm.CurrentApplication.EvaluationMessage.Contains(error), "Sin fallback ante " + error);
    }

    if (args.Length == 2 && args[0] == "--integration")
    {
        using var liveHttp = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var live = new PythonCreditRiskService(liveHttp, Config(("CreditRiskApi:BaseUrl", args[1])));
        var liveVm = MakeVm(live);
        await liveVm.EvaluateRiskAsync();
        var actual = liveVm.AssessmentResult;
        Check(actual != null, "Integración real disponible: " + liveVm.CurrentApplication.EvaluationMessage);
        Check(Math.Abs(actual!.ProbabilidadMorosidadGrave - 0.1164822345899684) < 1e-12, "Coincide con baseline real aprobado");
        Check(actual.NivelRiesgo == "Medio" && actual.VersionModelo == "gmsc-logreg-v1" &&
            actual.HorizonteMeses == 24 && actual.EventoObjetivo == CreditRiskResponse.ExpectedTargetEvent, "Metadata end-to-end");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            request = new CreditRiskRequest { AgeYears = 35, DebtRatio = 0.35,
                LatePayments30To59DaysLast24Months = 1, LatePayments60To89DaysLast24Months = 0 },
            result = new { probability = actual.ProbabilidadMorosidadGrave, riskLevel = actual.NivelRiesgo,
                modelVersion = actual.VersionModelo, horizonMonths = actual.HorizonteMeses, evaluatedEvent = actual.EventoEvaluado }
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
    Console.WriteLine($"{checks} comprobaciones C# de riesgo satisfactorias.");
}
finally
{
    File.Delete(receiptPath);
    Directory.Delete(temporary);
}

sealed class TestHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
}
sealed class DelegateRiskService(Func<CreditRiskRequest, CancellationToken, Task<CreditRiskResponse>> predict) : ICreditRiskService
{
    public int Calls { get; private set; }
    public CreditRiskRequest? LastInput { get; private set; }
    public CancellationToken Token { get; private set; }
    public Task<CreditRiskResponse> PredictAsync(CreditRiskRequest input, CancellationToken cancellationToken = default)
    {
        Calls++;
        LastInput = input;
        Token = cancellationToken;
        return predict(input, cancellationToken);
    }
}
sealed class UnusedPdf : IPdfTextExtractionService
{
    public Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("La evaluación ML no debe extraer documentos.");
}
sealed class UnusedExtraction : IDocumentExtractionService
{
    public Task<FinancialData> ExtractAsync(string text, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("La evaluación ML no debe llamar a IA generativa.");
}

