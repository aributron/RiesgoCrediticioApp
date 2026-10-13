using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RiesgoFiscalApp.Models;

public class Customer : INotifyPropertyChanged
{
    public const string SupportedEmployment = "Relación de dependencia";
    private string _nombre = string.Empty;
    private string _dni = string.Empty;
    private decimal _ingresoMensualDeclarado;

    public string Nombre { get => _nombre; set { _nombre = value; OnPropertyChanged(); } }
    public string Dni { get => _dni; set { _dni = value; OnPropertyChanged(); } }
    public string SituacionLaboral => SupportedEmployment;
    public decimal IngresoMensualDeclarado
    {
        get => _ingresoMensualDeclarado;
        set { _ingresoMensualDeclarado = value; OnPropertyChanged(); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

