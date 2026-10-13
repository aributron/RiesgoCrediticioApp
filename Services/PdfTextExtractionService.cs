using System.IO;
using System.Text;
using UglyToad.PdfPig;

namespace RiesgoFiscalApp.Services;

public sealed class PdfTextExtractionService : IPdfTextExtractionService
{
    public Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var pdf = PdfDocument.Open(filePath);
                var text = new StringBuilder();
                foreach (var page in pdf.GetPages())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // Solo texto embebido. No se interpretan importes ni se procesan imágenes.
                    text.AppendLine(page.Text);
                    text.AppendLine();
                }
                return text.ToString().Trim();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw new InvalidDataException(
                    "No se pudo leer el PDF. Verificá que el archivo exista, sea válido y no esté protegido.", ex);
            }
        }, cancellationToken);
}

