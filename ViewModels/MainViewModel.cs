using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using RiesgoFiscalApp.Models;
using RiesgoFiscalApp.Services;

namespace RiesgoFiscalApp.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly IAgentService _agentService;
        private Customer? _currentCustomer;
        private RiskAssessment? _assessmentResult;
        private bool _isBusy;
        private string _statusMessage = string.Empty;
        private string _selectedModel;
        private string _selectedSection = "Auditoría";
        private string _selectedTheme = "Claro";
        private bool _isOnboarded = false;
        private bool _canUnlockFeatures = false;

        public MainViewModel()
        {
            _agentService = new OpenAIAgentService();
            
            Documents = new ObservableCollection<Document>();
            CustomerTypes = new List<string> { 
                "PF - Monotributista", 
                "PF - Responsable Inscripto", 
                "PF - Relación Dependencia",
                "Persona Jurídica (S.A./S.R.L.)" 
            };
            AvailableModels = new List<string> { "Gemini 3 Flash Preview", "OpenAI GPT-4o", "Claude 3.5 Sonnet" };
            ThemeOptions = new List<string> { "Claro", "Oscuro" };
            _selectedModel = "Gemini 3 Flash Preview";
            _selectedTheme = "Claro";

            SimulatedClients = new ObservableCollection<Customer>();
            LoadSimulatedClients();
            UpdateDashboardMetrics();
            
            EvaluateCommand = new RelayCommand(async _ => await EvaluateRiskAsync(), _ => CanAnalyze());
            AddDocumentCommand = new RelayCommand(async param => await PickAndAddDocumentAsync(param as string));
            FinishOnboardingCommand = new RelayCommand(_ => ActivateAccountAura(), _ => CanUnlockFeatures);

            CurrentCustomer = new Customer { Clasificacion = "PF - Monotributista" };
            SelectedSection = "Auditoría";
            ApplyTheme();
        }

        public List<string> CustomerTypes { get; }
        public List<string> AvailableModels { get; }
        public List<string> ThemeOptions { get; }
        public ObservableCollection<Customer> SimulatedClients { get; }

        public string SelectedModel
        {
            get => _selectedModel;
            set { _selectedModel = value; OnPropertyChanged(); }
        }

        public string SelectedTheme
        {
            get => _selectedTheme;
            set
            {
                if (_selectedTheme == value) return;
                _selectedTheme = value;
                OnPropertyChanged();
                ApplyTheme();
            }
        }

        public int ClientCount => SimulatedClients.Count;
        public int AuditedClients { get; private set; }
        public int AverageRiskScore { get; private set; }
        public string TotalPortfolio => SimulatedClients.Sum(c => c.MontoOperado).ToString("C0");

        public string RiskSummary => AverageRiskScore switch
        {
            >= 75 => "Bajo",
            >= 45 => "Medio",
            _ => "Alto"
        };

        public string SelectedSection
        {
            get => _selectedSection;
            set
            {
                if (_selectedSection == value) return;
                _selectedSection = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDashboardSection));
                OnPropertyChanged(nameof(IsClientesSection));
                OnPropertyChanged(nameof(IsAuditoriaSection));
                OnPropertyChanged(nameof(IsConfiguracionSection));
            }
        }

        public bool IsDashboardSection
        {
            get => SelectedSection == "Dashboard";
            set { if (value) SelectedSection = "Dashboard"; }
        }

        public bool IsClientesSection
        {
            get => SelectedSection == "Clientes";
            set { if (value) SelectedSection = "Clientes"; }
        }

        public bool IsAuditoriaSection
        {
            get => SelectedSection == "Auditoría";
            set { if (value) SelectedSection = "Auditoría"; }
        }

        public bool IsConfiguracionSection
        {
            get => SelectedSection == "Configuración";
            set { if (value) SelectedSection = "Configuración"; }
        }

        public bool IsOnboarded
        {
            get => _isOnboarded;
            set { _isOnboarded = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowOnboarding)); }
        }

        public bool ShowOnboarding => !IsOnboarded;

        public bool CanUnlockFeatures
        {
            get => _canUnlockFeatures;
            set { _canUnlockFeatures = value; OnPropertyChanged(); ((RelayCommand)FinishOnboardingCommand).RaiseCanExecuteChanged(); }
        }

        public Customer CurrentCustomer
        {
            get => _currentCustomer!;
            set 
            { 
                if (_currentCustomer != null) _currentCustomer.PropertyChanged -= OnCustomerPropertyChanged;
                _currentCustomer = value; 
                if (_currentCustomer != null) _currentCustomer.PropertyChanged += OnCustomerPropertyChanged;
                OnPropertyChanged(); 
                ValidateInputs(); 
            }
        }

        public bool IsMonotributista => CurrentCustomer?.Clasificacion == "PF - Monotributista";
        public bool IsResponsableInscripto => CurrentCustomer?.Clasificacion == "PF - Responsable Inscripto";
        public bool IsRelacionDependencia => CurrentCustomer?.Clasificacion == "PF - Relación Dependencia";
        public bool IsPersonaJuridica => CurrentCustomer?.Clasificacion == "Persona Jurídica (S.A./S.R.L.)";

        private void OnCustomerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Customer.Clasificacion))
            {
                Documents.Clear(); // Reiniciamos docs al cambiar de perfil
                OnPropertyChanged(nameof(IsMonotributista));
                OnPropertyChanged(nameof(IsResponsableInscripto));
                OnPropertyChanged(nameof(IsRelacionDependencia));
                OnPropertyChanged(nameof(IsPersonaJuridica));
            }
            ValidateInputs();
        }

        public RiskAssessment? AssessmentResult
        {
            get => _assessmentResult;
            set { _assessmentResult = value; OnPropertyChanged(); }
        }

        public ObservableCollection<Document> Documents { get; }

        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); ValidateInputs(); }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public RelayCommand EvaluateCommand { get; }
        public ICommand AddDocumentCommand { get; }
        public ICommand FinishOnboardingCommand { get; }

        private bool CanAnalyze()
        {
            if (Documents == null || _currentCustomer == null) return false;
            
            bool hasDdjj = Documents.Any(d => d.Tipo == "DDJJ");
            if (!hasDdjj || !CurrentCustomer.AceptaDeclaracionJurada) return false;

            switch (CurrentCustomer.Clasificacion)
            {
                case "PF - Monotributista":
                    return Documents.Any(d => d.Tipo == "Comprobante Monotributo") && 
                           Documents.Count(d => d.Tipo == "Factura") >= 3;

                case "PF - Responsable Inscripto":
                    return Documents.Count(d => d.Tipo == "Posición IVA") >= 3 && 
                           Documents.Any(d => d.Tipo == "Autónomo") &&
                           Documents.Any(d => d.Tipo == "Factura");

                case "PF - Relación Dependencia":
                    return Documents.Any(d => d.Tipo == "Recibo Sueldo");

                case "Persona Jurídica (S.A./S.R.L.)":
                    return Documents.Any(d => d.Tipo == "Estatuto") && 
                           Documents.Count(d => d.Tipo == "Posición IVA") >= 3;

                default:
                    return false;
            }
        }

        private void ValidateInputs()
        {
            if (EvaluateCommand == null) return;
            EvaluateCommand.RaiseCanExecuteChanged();

            if (!IsBusy && _currentCustomer != null)
            {
                var missing = new List<string>();
                if (!Documents.Any(d => d.Tipo == "DDJJ")) missing.Add("DDJJ");

                switch (CurrentCustomer.Clasificacion)
                {
                    case "PF - Monotributista":
                        if (!Documents.Any(d => d.Tipo == "Comprobante Monotributo")) missing.Add("Comprobante Monotributo");
                        int facturas = Documents.Count(d => d.Tipo == "Factura");
                        if (facturas < 3) missing.Add($"Facturas ({facturas}/3)");
                        break;

                    case "PF - Responsable Inscripto":
                        int ivas = Documents.Count(d => d.Tipo == "Posición IVA");
                        if (ivas < 3) missing.Add($"Posiciones IVA ({ivas}/3)");
                        if (!Documents.Any(d => d.Tipo == "Autónomo")) missing.Add("Credencial Autónomo");
                        if (!Documents.Any(d => d.Tipo == "Factura")) missing.Add("Factura");
                        break;

                    case "PF - Relación Dependencia":
                        if (!Documents.Any(d => d.Tipo == "Recibo Sueldo")) missing.Add("Recibo Sueldo");
                        break;
                }

                if (missing.Any()) StatusMessage = "Faltante: " + string.Join(", ", missing);
                else StatusMessage = "Documentación completa. Inicie auditoría.";
            }
        }

        private async Task PickAndAddDocumentAsync(string? docType)
        {
            if (string.IsNullOrEmpty(docType)) return;

            var topLevel = TopLevel.GetTopLevel((Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = $"Adjuntar {docType} (PDF)",
                AllowMultiple = true,
                FileTypeFilter = new[] { FilePickerFileTypes.Pdf }
            });

            foreach (var file in files)
            {
                if (!file.Name.ToLower().EndsWith(".pdf")) continue;

                Documents.Add(new Document 
                { 
                    Tipo = docType, 
                    FileName = file.Name,
                    FilePath = file.Path.LocalPath,
                    ContenidoTexto = "Listo para auditoría..." 
                });
            }
            StatusMessage = $"Documentos cargados exitosamente.";
            ValidateInputs();
        }

        private async Task EvaluateRiskAsync()
        {
            IsBusy = true;
            AssessmentResult = null;
            CanUnlockFeatures = false;

            try
            {
                StatusMessage = "Agentes de IA auditando perfiles fiscales...";
                var docList = new List<Document>(Documents);
                CurrentCustomer = await _agentService.ExtractDataFromDocumentsAsync(docList, CurrentCustomer, SelectedModel);
                OnPropertyChanged(nameof(CurrentCustomer)); 
                
                AssessmentResult = await _agentService.EvaluateRiskAsync(CurrentCustomer, SelectedModel);
                StatusMessage = AssessmentResult.DictamenPreliminar == "Cumple" ? "Perfil Aprobado." : "Perfil Rechazado.";
                CanUnlockFeatures = AssessmentResult.DictamenPreliminar == "Cumple";
            }
            catch (Exception ex) { StatusMessage = $"❌ ERROR: {ex.Message}"; }
            finally { IsBusy = false; }
        }

        private void LoadSimulatedClients()
        {
            SimulatedClients.Add(new Customer { Nombre = "Lucía Martínez", CuitCuil = "20-12345678-9", Clasificacion = "PF - Monotributista", MontoOperado = 420000, Edad = 29, EsPep = false });
            SimulatedClients.Add(new Customer { Nombre = "Rodrigo Gómez", CuitCuil = "27-87654321-0", Clasificacion = "PF - Responsable Inscripto", MontoOperado = 910000, Edad = 41, EsPep = false });
            SimulatedClients.Add(new Customer { Nombre = "Inversiones Alfa SRL", CuitCuil = "30-98765432-1", Clasificacion = "Persona Jurídica (S.A./S.R.L.)", MontoOperado = 3200000, Edad = 0, EsPep = false });
            SimulatedClients.Add(new Customer { Nombre = "María Pérez", CuitCuil = "27-24681357-4", Clasificacion = "PF - Relación Dependencia", MontoOperado = 180000, Edad = 33, EsPep = false });
            AuditedClients = 87;
            AverageRiskScore = 62;
            OnPropertyChanged(nameof(ClientCount));
            OnPropertyChanged(nameof(AuditedClients));
            OnPropertyChanged(nameof(AverageRiskScore));
            OnPropertyChanged(nameof(TotalPortfolio));
            OnPropertyChanged(nameof(RiskSummary));
        }

        private void UpdateDashboardMetrics()
        {
            AuditedClients = 87;
            AverageRiskScore = 62;
            OnPropertyChanged(nameof(AuditedClients));
            OnPropertyChanged(nameof(AverageRiskScore));
            OnPropertyChanged(nameof(RiskSummary));
        }

        private void ActivateAccountAura()
        {
            if (CurrentCustomer == null) return;

            if (!string.IsNullOrWhiteSpace(CurrentCustomer.CuitCuil) &&
                !SimulatedClients.Any(c => c.CuitCuil == CurrentCustomer.CuitCuil))
            {
                SimulatedClients.Add(new Customer
                {
                    Nombre = CurrentCustomer.Nombre,
                    CuitCuil = CurrentCustomer.CuitCuil,
                    Clasificacion = CurrentCustomer.Clasificacion,
                    MontoOperado = CurrentCustomer.MontoOperado,
                    Edad = CurrentCustomer.Edad,
                    EsPep = CurrentCustomer.EsPep
                });
                OnPropertyChanged(nameof(ClientCount));
                OnPropertyChanged(nameof(TotalPortfolio));
            }

            IsOnboarded = true;
            StatusMessage = "Cliente agregado a Clientes y cuenta Aura activada.";
        }

        private void ApplyTheme()
        {
            if (Application.Current == null) return;

            var isDark = SelectedTheme == "Oscuro";
            Application.Current.RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;

            var resources = Application.Current.Resources;
            resources["WindowBackground"] = new SolidColorBrush(Color.Parse(isDark ? "#0B1220" : "#F4F7FB"));
            resources["ShellBackground"] = new SolidColorBrush(Color.Parse(isDark ? "#0D1523" : "#EFF2F7"));
            resources["SidebarBackground"] = new SolidColorBrush(Color.Parse(isDark ? "#0F172A" : "#FFFFFF"));
            resources["SidebarPrimaryText"] = new SolidColorBrush(Color.Parse(isDark ? "#FFFFFF" : "#0F172A"));
            resources["SidebarSecondaryText"] = new SolidColorBrush(Color.Parse(isDark ? "#94A3B8" : "#6B7280"));
            resources["TopBarBackground"] = new SolidColorBrush(Color.Parse(isDark ? "#08121F" : "#E2E8F0"));
            resources["PanelBackground"] = new SolidColorBrush(Color.Parse(isDark ? "#111827" : "#E2E8F0"));
            resources["MainPanelBackground"] = new SolidColorBrush(Color.Parse(isDark ? "#111827" : "#F8FAFC"));
            resources["CardBackground"] = new SolidColorBrush(Color.Parse(isDark ? "#1F2937" : "#FFFFFF"));
            resources["PageForeground"] = new SolidColorBrush(Color.Parse(isDark ? "#E5E7EB" : "#111827"));
            resources["SecondaryForeground"] = new SolidColorBrush(Color.Parse(isDark ? "#CBD5E1" : "#4B5563"));
            resources["SubtleForeground"] = new SolidColorBrush(Color.Parse(isDark ? "#9CA3AF" : "#6B7280"));
            resources["BorderPanelBackground"] = new SolidColorBrush(Color.Parse(isDark ? "#1B2430" : "#F1F5F9"));
            resources["InputBackground"] = new SolidColorBrush(Color.Parse(isDark ? "#111827" : "#FFFFFF"));
            resources["InputForeground"] = new SolidColorBrush(Color.Parse(isDark ? "#E5E7EB" : "#111827"));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}