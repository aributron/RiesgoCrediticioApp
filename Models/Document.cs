namespace RiesgoFiscalApp.Models
{
    public class Document
    {
        public string Tipo { get; set; } = string.Empty; // e.g., "Recibo Sueldo", "DDJJ"
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string ContenidoTexto { get; set; } = string.Empty; 
    }
}