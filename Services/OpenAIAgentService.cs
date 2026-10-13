using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using RiesgoFiscalApp.Models;
using UglyToad.PdfPig;

namespace RiesgoFiscalApp.Services
{
    public class OpenAIAgentService : IAgentService
    {
        private readonly HttpClient _httpClient = new HttpClient();
        private readonly IConfiguration _config;

        public OpenAIAgentService()
        {
            _config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();
        }

        private (string Url, string Key, string Provider) GetProviderDetails(string modelId)
        {
            if (modelId.Contains("OpenAI")) 
                return ("https://api.openai.com/v1/chat/completions", _config["AiConfig:OpenAIApiKey"] ?? "", "OpenAI");
            if (modelId.Contains("Claude")) 
                return ("https://api.anthropic.com/v1/messages", _config["AiConfig:ClaudeApiKey"] ?? "", "Claude");
            
            // Gemini (Default)
            string key = _config["AiConfig:GeminiApiKey"] ?? "";
            return ($"https://generativelanguage.googleapis.com/v1beta/models/gemini-3-flash-preview:generateContent?key={key}", key, "Gemini");
        }

        private object BuildPayload(string provider, string systemPrompt, string userPrompt)
        {
            if (provider == "Gemini")
                return new { contents = new[] { new { parts = new[] { new { text = $"{systemPrompt}\n\nDATOS:\n{userPrompt}" } } } } };
            
            return new { 
                model = "gpt-4o", 
                messages = new[] { 
                    new { role = "system", content = systemPrompt }, 
                    new { role = "user", content = userPrompt } 
                } 
            };
        }

        private string ExtractResponseText(string json, string provider)
        {
            using var doc = JsonDocument.Parse(json);
            if (provider == "Gemini") return doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString() ?? "";
            return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        }

        private void LogAudit(string url, object payload, string response, string provider)
        {
            string jsonReq = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine($"\n>>> [CURL DEBUG - {provider}]");
            Console.WriteLine($"curl -X POST \"{url}\" -H \"Content-Type: application/json\" -d '{jsonReq.Replace("'", "\\'")}'");
            Console.WriteLine($"\n<<< [HTTP RESPONSE - {provider}]");
            Console.WriteLine(response);
            Console.WriteLine("---------------------------------------------------------\n");
        }

        public async Task<Customer> ExtractDataFromDocumentsAsync(List<Document> documents, Customer baseCustomer, string modelId)
        {
            var (url, key, provider) = GetProviderDetails(modelId);
            string megaContext = "";
            foreach (var doc in documents) megaContext += $"--- DOCUMENTO: {doc.Tipo} ({doc.FileName}) ---\n{ExtractText(doc.FilePath)}\n\n";

            var payload = BuildPayload(provider, 
                "Eres un Auditor Fiscal de un Banco Argentino. Analiza TODOS los documentos adjuntos (Facturas, IVAs, Recibos, DDJJ). " +
                "Extrae y responde ESTRICTAMENTE en JSON: { 'cuit': '...', 'nombre': '...', 'ingreso_mensual': 0.0, 'es_pep': bool, 'categoria': '...', 'alerta_fraude': '...' }. " +
                "Si hay 3 facturas o 3 IVAs, calcula el promedio mensual de ingresos.", 
                megaContext);

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            if (provider == "Gemini") request.Headers.Add("x-goog-api-key", key);
            else request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await _httpClient.SendAsync(request);
            var result = await response.Content.ReadAsStringAsync();

            LogAudit(url, payload, result, provider);

            if (!response.IsSuccessStatusCode) throw new Exception($"API {provider} Error: {response.StatusCode}");

            string aiText = ExtractResponseText(result, provider);
            try {
                var json = aiText.Substring(aiText.IndexOf("{"), aiText.LastIndexOf("}") - aiText.IndexOf("{") + 1);
                var data = JsonSerializer.Deserialize<JsonElement>(json);
                baseCustomer.CuitExtraido = data.GetProperty("cuit").GetString() ?? "";
                baseCustomer.NombreExtraido = data.GetProperty("nombre").GetString() ?? "";
                baseCustomer.IngresoMensualValidado = data.GetProperty("ingreso_mensual").GetDecimal();
                baseCustomer.EsPepDocumento = data.GetProperty("es_pep").GetBoolean();
                baseCustomer.CategoriaMonotributo = data.GetProperty("categoria").GetString() ?? "";
            } catch { }

            return baseCustomer;
        }

        public async Task<RiskAssessment> EvaluateRiskAsync(Customer customer, string modelId)
        {
            // El scoring se calcula localmente para asegurar cumplimiento de reglas de negocio
            int score = 100;
            var logs = new List<string>();

            // 1. REGLA NOMBRES (FUZZY MATCHING)
            string inputName = customer.Nombre?.ToLower().Trim() ?? "";
            string docName = customer.NombreExtraido?.ToLower().Trim() ?? "";

            if (inputName != docName)
            {
                if (string.IsNullOrEmpty(docName) || docName == "not_found")
                {
                    score -= 50;
                    logs.Add("ALERTA: No se pudo verificar la identidad nominal en los documentos (-50 pts).");
                }
                else if (docName.Contains(inputName) || inputName.Contains(docName))
                {
                    score -= 20;
                    logs.Add("PRECAUCIÓN: El nombre coincide parcialmente con los documentos (-20 pts).");
                }
                else
                {
                    score -= 80;
                    logs.Add("RIESGO CRÍTICO: Discrepancia significativa de nombres entre formulario y documentos (-80 pts).");
                }
            }

            // 2. REGLA CUIT (CRÍTICA)
            if (customer.CuitCuil.Replace("-","") != customer.CuitExtraido.Replace("-",""))
                return new RiskAssessment { ScoreRiesgo = 1, DictamenPreliminar = "No cumple", Observaciones = "BLOQUEO: El CUIT de los documentos no coincide con el declarado." };

            // 3. REGLA PEP (COMPLIANCE)
            if (customer.EsPep != customer.EsPepDocumento)
                return new RiskAssessment { ScoreRiesgo = 1, DictamenPreliminar = "No cumple", Observaciones = "RECHAZO: Declaración PEP inconsistente contra DDJJ." };

            if (customer.EsPep) { score -= 40; logs.Add("Perfil PEP detectado (-40 pts)."); }

            // 4. CAPACIDAD ECONÓMICA
            if (customer.IngresoMensualValidado > 0) {
                decimal ratio = customer.MontoOperado / customer.IngresoMensualValidado;
                if (ratio > 5) { score -= 60; logs.Add("Alerta AML: Monto excede capacidad de ingresos (-60 pts)."); }
            }

            if (score < 1) score = 1;
            string dictamen = score >= 70 ? "Cumple" : (score >= 40 ? "Cumple con Observaciones" : "No cumple");

            return new RiskAssessment { ScoreRiesgo = score, DictamenPreliminar = dictamen, Observaciones = logs.Any() ? string.Join(" | ", logs) : "Auditoría finalizada con éxito." };
        }

        private string ExtractText(string path) {
            try { using var pdf = PdfDocument.Open(path); return string.Join(" ", pdf.GetPages().Select(p => p.Text)); }
            catch { return ""; }
        }
    }
}