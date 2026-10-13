using System.Collections.Generic;
using System.Threading.Tasks;
using RiesgoFiscalApp.Models;

namespace RiesgoFiscalApp.Services
{
    public interface IAgentService
    {
        // Agente 1: Extractor de Datos (ahora recibe el modelo seleccionado)
        Task<Customer> ExtractDataFromDocumentsAsync(List<Document> documents, Customer baseCustomer, string modelId);

        // Agente 2: Evaluador de Riesgo (ahora recibe el modelo seleccionado)
        Task<RiskAssessment> EvaluateRiskAsync(Customer customerData, string modelId);
    }
}