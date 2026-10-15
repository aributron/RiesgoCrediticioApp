using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RiesgoFiscalApp.Models;

public class Customer : INotifyPropertyChanged
{
    public const string SupportedEmployment = "Relación de dependencia";
    private string _nombre = string.Empty;
    private string _dni = string.Empty;
    private decimal? _ingresoBrutoMensualDeclarado;
    private decimal? _compromisosMensuales;
    private int? _edad;
    private int? _atrasos30a59;
    private int? _atrasos60a89;

    public string Nombre { get => _nombre; set { _nombre = value; Notify(); } }
    public string Dni { get => _dni; set { _dni = value; Notify(); } }
    public string SituacionLaboral => SupportedEmployment;
    public decimal? IngresoBrutoMensualDeclarado
    {
        get => _ingresoBrutoMensualDeclarado;
        set { _ingresoBrutoMensualDeclarado = value; Notify(); }
    }
    public decimal? CompromisosMensuales { get => _compromisosMensuales; set { _compromisosMensuales = value; Notify(); } }
    public int? Edad { get => _edad; set { _edad = value; Notify(); } }
    public int? Atrasos30a59Ultimos24Meses { get => _atrasos30a59; set { _atrasos30a59 = value; Notify(); } }
    public int? Atrasos60a89Ultimos24Meses { get => _atrasos60a89; set { _atrasos60a89 = value; Notify(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

