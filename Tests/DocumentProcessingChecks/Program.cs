using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using RiesgoFiscalApp.Models;
using RiesgoFiscalApp.Services;
using RiesgoFiscalApp.ViewModels;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;

var checks = 0;
void Check(bool condition, string label)
{
    checks++;
    if (!condition) throw new Exception("FALLÓ: " + label);
}
async Task Fails<T>(Func<Task> action, string label) where T : Exception
{
    try { await action(); }
    catch (T) { Check(true, label); return; }
    throw new Exception("FALLÓ: " + label);
}
const string validJson = """
    {"employer":"Empresa XYZ","grossIncome":1450000,"netIncome":1180000,"receiptDate":"2026-09-30","employmentSeniorityMonths":36}
    """;
const string nullJson = """
    {"employer":null,"grossIncome":null,"netIncome":null,"receiptDate":null,"employmentSeniorityMonths":null}
    """;
string Envelope(string content, string finish = "stop", string? refusal = null) =>
    JsonSerializer.Serialize(new { choices = new[] { new { finish_reason = finish, message = new { content, refusal } } } });
var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{
    ["AiConfig:OpenAIApiKey"] = "test-key-not-real",
    ["AiConfig:DocumentExtractionModel"] = "gpt-4o",
    ["AiConfig:DocumentExtractionTimeoutSeconds"] = "1"
}).Build();
var previousKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
var temp = Path.Combine(Path.GetTempPath(), "DocumentProcessingChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    var pdfPath = Path.Combine(temp, "receipt.pdf");
    var builder = new PdfDocumentBuilder();
    var font = builder.AddStandard14Font(Standard14Font.Helvetica);
    builder.AddPage(595, 842).AddText("Recibo de sueldo Empresa XYZ", 12, new PdfPoint(30, 800), font);
    File.WriteAllBytes(pdfPath, builder.Build());
    var emptyPath = Path.Combine(temp, "empty.pdf");
    var blank = new PdfDocumentBuilder();
    blank.AddPage(595, 842);
    File.WriteAllBytes(emptyPath, blank.Build());
    var damagedPath = Path.Combine(temp, "damaged.pdf");
    File.WriteAllText(damagedPath, "not a PDF");
    var reader = new PdfTextExtractionService();
    Check((await reader.ExtractTextAsync(pdfPath)).Contains("Empresa"), "PdfPig extrae texto embebido");
    Check(string.IsNullOrWhiteSpace(await reader.ExtractTextAsync(emptyPath)), "PDF sin texto");
    await Fails<System.IO.InvalidDataException>(() => reader.ExtractTextAsync(damagedPath), "PDF inválido");
    await Fails<System.IO.InvalidDataException>(() => reader.ExtractTextAsync(Path.Combine(temp, "missing.pdf")), "PDF ausente");
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    await Fails<OperationCanceledException>(() => reader.ExtractTextAsync(pdfPath, cancelled.Token), "Cancelación lectura");

    var responseBody = Envelope(validJson);
    var status = HttpStatusCode.OK;
    var httpCalls = 0;
    using var http = new HttpClient(new TestHandler(async (request, token) =>
    {
        httpCalls++;
        Check(request.RequestUri!.AbsoluteUri == "https://api.openai.com/v1/chat/completions", "Endpoint");
        using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
        var root = payload.RootElement;
        Check(root.GetProperty("messages")[1].GetProperty("content").GetString() == "texto del recibo", "Solo texto documental como entrada");
        var schema = root.GetProperty("response_format").GetProperty("json_schema");
        Check(schema.GetProperty("strict").GetBoolean(), "Esquema estricto");
        Check(schema.GetProperty("schema").GetProperty("required").GetArrayLength() == 5, "Cinco campos obligatorios");
        Check(!schema.GetProperty("schema").GetProperty("additionalProperties").GetBoolean(), "Sin campos adicionales");
        return new HttpResponseMessage(status) { Content = new StringContent(responseBody, Encoding.UTF8, "application/json") };
    }));
    var extraction = new DocumentExtractionService(http, config);
    var financial = await extraction.ExtractAsync("texto del recibo");
    Check(financial.Employer == "Empresa XYZ" && financial.GrossIncome == 1450000m &&
        financial.NetIncome == 1180000m && financial.ReceiptDate == new DateOnly(2026, 9, 30) &&
        financial.EmploymentSeniorityMonths == 36, "Deserialización completa");

    responseBody = Envelope(nullJson);
    var unknown = await extraction.ExtractAsync("texto del recibo");
    Check(unknown.Employer == null && unknown.GrossIncome == null && unknown.NetIncome == null &&
        unknown.ReceiptDate == null && unknown.EmploymentSeniorityMonths == null, "Null no se reemplaza por cero");
    foreach (var badJson in new[]
    {
        "not JSON", "null", "[]", "{}", validJson.Replace("1450000", "-1"),
        validJson.Replace("1450000", "\"1450000\""),
        validJson.Replace("2026-09-30", "2026-09"),
        validJson.Replace("2026-09-30", "2026-02-30"),
        validJson.Replace("\"employmentSeniorityMonths\":36", "\"employmentSeniorityMonths\":-1"),
        validJson.Replace("\"employmentSeniorityMonths\":36", "\"employmentSeniorityMonths\":1.5"),
        validJson.Replace("Empresa XYZ", " "),
        validJson.Replace("\"employer\":", "\"extra\":1,\"employer\":"),
        validJson.Replace("\"employer\":", "\"employer\":null,\"employer\":")
    })
    {
        responseBody = Envelope(badJson);
        await Fails<DocumentExtractionException>(() => extraction.ExtractAsync("texto del recibo"), "Rechaza JSON/valores inválidos");
    }
    foreach (var malformed in new[] { "not JSON", "{}", "{\"choices\":[]}", Envelope(validJson, "length"), Envelope(validJson, "stop", "refusal"), Envelope("") })
    {
        responseBody = malformed;
        await Fails<DocumentExtractionException>(() => extraction.ExtractAsync("texto del recibo"), "Respuesta API inválida/incompleta/negativa");
    }
    foreach (var errorStatus in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests, HttpStatusCode.InternalServerError })
    {
        status = errorStatus;
        await Fails<DocumentExtractionException>(() => extraction.ExtractAsync("texto del recibo"), "Error HTTP controlado");
    }
    status = HttpStatusCode.OK;
    var before = httpCalls;
    await Fails<DocumentExtractionException>(() => extraction.ExtractAsync(""), "No envía texto vacío");
    await Fails<DocumentExtractionException>(() => extraction.ExtractAsync(new string('a', 60001)), "No trunca texto extenso");
    var missingConfig = new DocumentExtractionService(http, new ConfigurationBuilder().Build());
    await Fails<DocumentExtractionException>(() => missingConfig.ExtractAsync("texto del recibo"), "Clave ausente");
    Check(httpCalls == before, "Sin llamadas con entrada/configuración inválida");

    using var disconnectedHttp = new HttpClient(new TestHandler((_, _) => throw new HttpRequestException("simulada")));
    await Fails<DocumentExtractionException>(() => new DocumentExtractionService(disconnectedHttp, config).ExtractAsync("texto del recibo"), "Fallo de red");
    using var slowHttp = new HttpClient(new TestHandler(async (_, token) =>
    {
        await Task.Delay(5000, token);
        return new HttpResponseMessage(HttpStatusCode.OK);
    }));
    var slowService = new DocumentExtractionService(slowHttp, config);
    await Fails<DocumentExtractionException>(() => slowService.ExtractAsync("texto del recibo"), "Timeout controlado");
    await Fails<OperationCanceledException>(() => slowService.ExtractAsync("texto del recibo", cancelled.Token), "Cancelación externa");

    var fakeExtraction = new FakeExtraction(financial);
    var vm = new MainViewModel(reader, fakeExtraction, new TestRiskService(), new RiskLevelService());
    var document = new Document { CreditApplicationId = vm.CurrentApplication.Id, FilePath = pdfPath, FileName = "receipt.pdf" };
    vm.Documents.Add(document);
    Check(document.ProcessingStatus == DocumentProcessingStatus.Pending && !document.HasFinancialData, "Estado inicial");
    vm.CurrentCustomer.Nombre = "María García";
    vm.CurrentCustomer.Dni = "30123456";
    vm.IncomeInput = "850000,50";
    vm.AmountInput = "1500000";
    vm.TermInput = "12";
    vm.AgeInput = "35"; vm.CommitmentsInput = "420000"; vm.Late30Input = "1"; vm.Late60Input = "0";
    Check(vm.EvaluateCommand.CanExecute(null), "Evaluación disponible antes de extracción");
    await vm.EvaluateRiskAsync();
    var evaluation = vm.AssessmentResult;
    await vm.ProcessDocumentAsync(document);
    Check(document.ProcessingStatus == DocumentProcessingStatus.Processed && document.HasFinancialData, "Extracción por documento");
    Check(document.ReviewStatus == "Pendiente de revisión", "Estado revisión inicial");
    Check(vm.CurrentCustomer.IngresoBrutoMensualDeclarado == 850000.50m && vm.IncomeInput == "850000,50", "No sobrescribe datos declarados");
    Check(ReferenceEquals(evaluation, vm.AssessmentResult) && vm.AssessmentResult!.ProbabilidadMorosidadGrave == 0.2, "Evaluación conservada");
    fakeExtraction.Fail = true;
    await vm.ProcessDocumentAsync(document);
    Check(document.ProcessingStatus == DocumentProcessingStatus.Error && document.FinancialData == null &&
        document.ReviewStatus == "Sin resultado documental", "Reintento fallido limpia extracción anterior");
    fakeExtraction.Fail = false;
    await vm.ProcessDocumentAsync(document);
    Check(document.HasFinancialData, "Recupera extracción con reintento");
    var emptyDocument = new Document { CreditApplicationId = vm.CurrentApplication.Id, FilePath = emptyPath };
    vm.Documents.Add(emptyDocument);
    before = fakeExtraction.Calls;
    await vm.ProcessDocumentAsync(emptyDocument);
    Check(emptyDocument.ProcessingStatus == DocumentProcessingStatus.NoText && emptyDocument.ProcessingMessage.Contains("OCR no disponible"), "Sin texto avisa versión");
    Check(fakeExtraction.Calls == before, "Sin texto no llama IA");
    var brokenDocument = new Document { CreditApplicationId = vm.CurrentApplication.Id, FilePath = damagedPath };
    vm.Documents.Add(brokenDocument);
    await vm.ProcessDocumentAsync(brokenDocument);
    Check(brokenDocument.ProcessingStatus == DocumentProcessingStatus.Error && !vm.IsBusy, "Error lectura recuperable");
    Check(fakeExtraction.Calls == before, "Error lectura no llama IA");
    var foreign = new Document { CreditApplicationId = Guid.NewGuid(), FilePath = pdfPath };
    vm.Documents.Add(foreign);
    await vm.ProcessDocumentAsync(foreign);
    Check(foreign.ProcessingStatus == DocumentProcessingStatus.Pending, "No procesa documento ajeno");

    var waitingPdf = new WaitingPdf();
    var busyVm = new MainViewModel(waitingPdf, new FakeExtraction(financial), new TestRiskService(), new RiskLevelService());
    var busyDoc = new Document { CreditApplicationId = busyVm.CurrentApplication.Id, FilePath = pdfPath };
    busyVm.Documents.Add(busyDoc);
    var processing = busyVm.ProcessDocumentAsync(busyDoc);
    Check(busyVm.IsBusy && busyDoc.ProcessingStatus == DocumentProcessingStatus.Processing, "Estado procesando observable");
    Check(!busyVm.ProcessDocumentCommand.CanExecute(busyDoc) && !busyVm.RemoveDocumentCommand.CanExecute(busyDoc) &&
        !busyVm.NewApplicationCommand.CanExecute(null), "No permite operaciones concurrentes");
    await busyVm.ProcessDocumentAsync(busyDoc);
    Check(waitingPdf.Calls == 1, "No duplica procesamiento");
    waitingPdf.Completion.SetResult("texto del recibo");
    await processing;
    Check(!busyVm.IsBusy && busyDoc.HasFinancialData, "Libera estado ocupado");
    Console.WriteLine($"{checks} comprobaciones satisfactorias. No se realizaron llamadas reales a OpenAI.");
}
finally
{
    Environment.SetEnvironmentVariable("OPENAI_API_KEY", previousKey);
    // Solo los archivos sintéticos creados por esta ejecución.
    foreach (var file in Directory.GetFiles(temp)) File.Delete(file);
    Directory.Delete(temp);
}

sealed class TestHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
}
sealed class FakeExtraction(FinancialData data) : IDocumentExtractionService
{
    public int Calls { get; private set; }
    public bool Fail { get; set; }
    public Task<FinancialData> ExtractAsync(string text, CancellationToken cancellationToken = default)
    {
        Calls++;
        if (Fail) throw new DocumentExtractionException("Error simulado");
        return Task.FromResult(data);
    }
}
sealed class WaitingPdf : IPdfTextExtractionService
{
    public int Calls { get; private set; }
    public TaskCompletionSource<string> Completion { get; } = new();
    public Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Completion.Task;
    }
}

sealed class TestRiskService : ICreditRiskService
{
    public Task<CreditRiskResponse> PredictAsync(CreditRiskRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new CreditRiskResponse
        {
            DefaultProbability = 0.2, ModelVersion = "document-check-only",
            PredictionHorizonMonths = 24, TargetEvent = CreditRiskResponse.ExpectedTargetEvent
        });
}


