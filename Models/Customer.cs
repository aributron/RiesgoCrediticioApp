using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RiesgoFiscalApp.Models
{
    public class Customer : INotifyPropertyChanged
    {
        private string _cuitCuil = string.Empty;
        private decimal _montoOperado;
        private string _nombre = string.Empty;
        private string _clasificacion = "PF - Monotributista";
        private bool _aceptaDeclaracionJurada;
        private bool _esPep;
        private int _edad = 18;
        private bool _isJuridica;
        private string _razonSocial = string.Empty;
        private string _representanteLegalNombre = string.Empty;
        private decimal _facturacionAnual;

        public string CuitCuil { get => _cuitCuil; set { _cuitCuil = value; OnPropertyChanged(); } }
        public decimal MontoOperado { get => _montoOperado; set { _montoOperado = value; OnPropertyChanged(); } }
        public string Nombre { get => _nombre; set { _nombre = value; OnPropertyChanged(); } }
        public string Clasificacion 
        { 
            get => _clasificacion; 
            set { 
                _clasificacion = value; 
                IsJuridica = value.Contains("Jurídica");
                OnPropertyChanged(); 
            } 
        }
        public bool AceptaDeclaracionJurada { get => _aceptaDeclaracionJurada; set { _aceptaDeclaracionJurada = value; OnPropertyChanged(); } }
        public bool EsPep { get => _esPep; set { _esPep = value; OnPropertyChanged(); } }
        public int Edad { get => _edad; set { _edad = value; OnPropertyChanged(); } }
        public bool IsJuridica { get => _isJuridica; set { _isJuridica = value; OnPropertyChanged(); } }
        
        public string RazonSocial { get => _razonSocial; set { _razonSocial = value; OnPropertyChanged(); } }
        public string RepresentanteLegalNombre { get => _representanteLegalNombre; set { _representanteLegalNombre = value; OnPropertyChanged(); } }
        public decimal FacturacionAnual { get => _facturacionAnual; set { _facturacionAnual = value; OnPropertyChanged(); } }

        // Campos de Auditoría IA
        public string CuitExtraido { get; set; } = string.Empty;
        public string NombreExtraido { get; set; } = string.Empty;
        public bool EsPepDocumento { get; set; }
        public decimal IngresoMensualValidado { get; set; }
        public string CategoriaMonotributo { get; set; } = string.Empty;
        public string FechaDocumento { get; set; } = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}