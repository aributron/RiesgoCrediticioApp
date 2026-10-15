using RiesgoFiscalApp.Models;

namespace RiesgoFiscalApp.Services;

public static class CreditRiskInputBuilder
{
    public static bool TryBuild(Customer customer, out CreditRiskRequest? request, out List<string> errors)
    {
        request = null;
        errors = new List<string>();
        if (customer.Edad is not (>= 18 and <= 120)) errors.Add("edad entera entre 18 y 120 años");
        if (customer.IngresoBrutoMensualDeclarado is not > 0) errors.Add("ingreso bruto mensual declarado positivo");
        if (customer.CompromisosMensuales is not >= 0) errors.Add("compromisos y gastos mensuales declarados no negativos");
        if (customer.Atrasos30a59Ultimos24Meses is not (>= 0 and <= 24)) errors.Add("atrasos de 30–59 días: entero entre 0 y 24");
        if (customer.Atrasos60a89Ultimos24Meses is not (>= 0 and <= 24)) errors.Add("atrasos de 60–89 días: entero entre 0 y 24");
        if (errors.Count > 0) return false;
        try
        {
            // Mismo período y moneda. No incorpora la cuota del crédito ni limita el ratio a 1.
            var ratio = customer.CompromisosMensuales!.Value / customer.IngresoBrutoMensualDeclarado!.Value;
            request = new CreditRiskRequest
            {
                AgeYears = customer.Edad!.Value,
                DebtRatio = (double)ratio,
                LatePayments30To59DaysLast24Months = customer.Atrasos30a59Ultimos24Meses!.Value,
                LatePayments60To89DaysLast24Months = customer.Atrasos60a89Ultimos24Meses!.Value
            };
            return true;
        }
        catch (OverflowException)
        {
            errors.Add("importes dentro de un rango válido para calcular la relación compromisos/ingreso");
            return false;
        }
    }
}

