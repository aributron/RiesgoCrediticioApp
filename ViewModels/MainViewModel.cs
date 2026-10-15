using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using RiesgoFiscalApp.Models;
using RiesgoFiscalApp.Services;

namespace RiesgoFiscalApp.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly ICreditRiskService _riskService;
    private readonly RiskLevelService _riskLevels;
    private readonly IPdfTextExtractionService _pdfTextService;
    private readonly IDocumentExtractionService _documentExtractionService;
    private CreditApplication _currentApplication = new();
    private string _selectedSection = "Evaluación";
    private string _selectedTheme = "Claro";
    private string _incomeInput = string.Empty;
    private string _amountInput = string.Empty;
    private string _termInput = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isBusy;
    private string _ageInput = string.Empty;
    private string _commitmentsInput = string.Empty;
    private string _late30Input = string.Empty;
    private string _late60Input = string.Empty;
    private long _inputRevision;
    private CancellationTokenSource? _evaluationCts;

    public MainViewModel(IPdfTextExtractionService pdfTextService, IDocumentExtractionService documentExtractionService,
        ICreditRiskService riskService, RiskLevelService riskLevels)
    {
        _riskService = riskService;
        _riskLevels = riskLevels;
        _pdfTextService = pdfTextService;
        _documentExtractionService = documentExtractionService;
        EvaluateCommand = new RelayCommand(async _ => await EvaluateRiskAsync(), _ => !IsBusy && GetValidationErrors().Count == 0);
        AddDocumentCommand = new RelayCommand(async _ => await PickAndAddDocumentsAsync(), _ => !IsBusy);
        RemoveDocumentCommand = new RelayCommand(
            item => { if (!IsBusy && item is Document document) Documents.Remove(document); }, _ => !IsBusy);
        ProcessDocumentCommand = new RelayCommand(async item => await ProcessDocumentAsync(item as Document),
            item => !IsBusy && item is Document document && Documents.Contains(document));
        NewApplicationCommand = new RelayCommand(_ => StartNewApplication(), _ => !IsBusy);
        AttachCurrentApplication();
        Applications.Add(CurrentApplication);
        RefreshValidation();
        ApplyTheme();
    }

    public ObservableCollection<CreditApplication> Applications { get; } = new();
    public CreditApplication CurrentApplication => _currentApplication;
    public Customer CurrentCustomer => CurrentApplication.Solicitante;
    public ObservableCollection<Document> Documents => CurrentApplication.Documents;
    public RiskAssessment? AssessmentResult => CurrentApplication.Resultado;
    public bool HasAssessment => AssessmentResult != null;
    public int ApplicationCount => Applications.Count;
    public int EvaluatedCount => Applications.Count(a => a.Resultado != null);
    public List<string> ThemeOptions { get; } = new() { "Claro", "Oscuro" };
    public RelayCommand EvaluateCommand { get; }
    public RelayCommand AddDocumentCommand { get; }
    public RelayCommand RemoveDocumentCommand { get; }
    public RelayCommand NewApplicationCommand { get; }
    public RelayCommand ProcessDocumentCommand { get; }

    // Texto para preservar campos vacíos/incorrectos sin interpretarlos como cero.
    public string IncomeInput
    {
        get => _incomeInput;
        set
        {
            _incomeInput = value;
            CurrentCustomer.IngresoBrutoMensualDeclarado = TryAmount(value, out var income) ? income : null;
            OnPropertyChanged();
            InputsChanged();
        }
    }
    public string AmountInput
    {
        get => _amountInput;
        set
        {
            _amountInput = value;
            TryAmount(value, out var amount);
            CurrentApplication.MontoSolicitado = amount;
            OnPropertyChanged();
            InputsChanged();
        }
    }
    public string AgeInput
    {
        get => _ageInput;
        set { _ageInput = value; CurrentCustomer.Edad = ParseWholeNumber(value); OnPropertyChanged(); }
    }
    public string CommitmentsInput
    {
        get => _commitmentsInput;
        set
        {
            _commitmentsInput = value;
            CurrentCustomer.CompromisosMensuales = TryNonnegativeAmount(value, out var amount) ? amount : null;
            OnPropertyChanged();
        }
    }
    public string Late30Input
    {
        get => _late30Input;
        set { _late30Input = value; CurrentCustomer.Atrasos30a59Ultimos24Meses = ParseWholeNumber(value); OnPropertyChanged(); }
    }
    public string Late60Input
    {
        get => _late60Input;
        set { _late60Input = value; CurrentCustomer.Atrasos60a89Ultimos24Meses = ParseWholeNumber(value); OnPropertyChanged(); }
    }
    public string TermInput
    {
        get => _termInput;
        set
        {
            _termInput = value;
            int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var term);
            CurrentApplication.PlazoMeses = term;
            OnPropertyChanged();
            InputsChanged();
        }
    }
    public string StatusMessage
    {
        get => _statusMessage;
        private set { _statusMessage = value; OnPropertyChanged(); }
    }
    public bool IsBusy
    {
        get => _isBusy;
        private set { _isBusy = value; OnPropertyChanged(); RefreshCommands(); }
    }
    public string SelectedSection
    {
        get => _selectedSection;
        set
        {
            _selectedSection = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDashboardSection));
            OnPropertyChanged(nameof(IsApplicationsSection));
            OnPropertyChanged(nameof(IsEvaluationSection));
            OnPropertyChanged(nameof(IsConfiguracionSection));
        }
    }
    public bool IsDashboardSection
    {
        get => SelectedSection == "Dashboard";
        set { if (value) SelectedSection = "Dashboard"; }
    }
    public bool IsApplicationsSection
    {
        get => SelectedSection == "Solicitudes";
        set { if (value) SelectedSection = "Solicitudes"; }
    }
    public bool IsEvaluationSection
    {
        get => SelectedSection == "Evaluación";
        set { if (value) SelectedSection = "Evaluación"; }
    }
    public bool IsConfiguracionSection
    {
        get => SelectedSection == "Configuración";
        set { if (value) SelectedSection = "Configuración"; }
    }
    public string SelectedTheme
    {
        get => _selectedTheme;
        set { _selectedTheme = value; OnPropertyChanged(); ApplyTheme(); }
    }

    private static bool TryAmount(string input, out decimal value) =>
        decimal.TryParse(input, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite |
            NumberStyles.AllowTrailingWhite, CultureInfo.GetCultureInfo("es-AR"), out value) && value > 0;

    private static bool TryNonnegativeAmount(string input, out decimal value) =>
        decimal.TryParse(input, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite |
            NumberStyles.AllowTrailingWhite, CultureInfo.GetCultureInfo("es-AR"), out value) && value >= 0;

    private static int? ParseWholeNumber(string input) =>
        int.TryParse(input.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;

    private List<string> GetValidationErrors()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(CurrentCustomer.Nombre)) errors.Add("nombre");
        var dni = CurrentCustomer.Dni.Trim();
        if (dni.Length < 7 || dni.Length > 8 || !dni.All(c => c >= '0' && c <= '9'))
            errors.Add("DNI de 7 u 8 dígitos, sin puntos");
        CreditRiskInputBuilder.TryBuild(CurrentCustomer, out _, out var riskErrors);
        errors.AddRange(riskErrors);
        if (!TryAmount(AmountInput, out _)) errors.Add("monto solicitado positivo");
        if (!int.TryParse(TermInput, NumberStyles.None, CultureInfo.InvariantCulture, out var term) || term <= 0)
            errors.Add("plazo entero positivo en meses");
        if (!Documents.Any(d => d.CreditApplicationId == CurrentApplication.Id &&
            d.Tipo == "Recibo de sueldo" && File.Exists(d.FilePath) &&
            string.Equals(Path.GetExtension(d.FilePath), ".pdf", StringComparison.OrdinalIgnoreCase)))
            errors.Add("al menos un recibo de sueldo PDF local");
        return errors;
    }

    private void AttachCurrentApplication()
    {
        CurrentCustomer.PropertyChanged += OnCustomerChanged;
        Documents.CollectionChanged += OnDocumentsChanged;
        CurrentApplication.PropertyChanged += OnApplicationChanged;
    }
    private void OnApplicationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CreditApplication.MontoSolicitado) or nameof(CreditApplication.PlazoMeses)) InputsChanged();
    }
    private void OnCustomerChanged(object? sender, PropertyChangedEventArgs e) => InputsChanged();
    private void OnDocumentsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        InputsChanged();

    private void InputsChanged()
    {
        _inputRevision++;
        var wasEvaluating = _evaluationCts != null;
        _evaluationCts?.Cancel();
        CurrentApplication.Resultado = null;
        CurrentApplication.EvaluationStatus = CreditEvaluationStatus.Pending;
        CurrentApplication.EvaluationMessage = wasEvaluating ? "Los datos cambiaron. Volvé a evaluar la solicitud actualizada." : string.Empty;
        NotifyEvaluationChanged();
        RefreshValidation();
    }
    private void NotifyEvaluationChanged()
    {
        OnPropertyChanged(nameof(AssessmentResult));
        OnPropertyChanged(nameof(HasAssessment));
        OnPropertyChanged(nameof(EvaluatedCount));
    }
    private void RefreshCommands()
    {
        EvaluateCommand.RaiseCanExecuteChanged();
        AddDocumentCommand.RaiseCanExecuteChanged();
        RemoveDocumentCommand.RaiseCanExecuteChanged();
        NewApplicationCommand.RaiseCanExecuteChanged();
        ProcessDocumentCommand.RaiseCanExecuteChanged();
    }
    private void RefreshValidation()
    {
        var errors = GetValidationErrors();
        StatusMessage = errors.Count == 0
            ? "Solicitud lista para evaluar con el modelo ML. La extracción documental se procesa por separado."
            : "Completar: " + string.Join("; ", errors) + ".";
        RefreshCommands();
    }

    public async Task EvaluateRiskAsync()
    {
        if (IsBusy) return;
        if (GetValidationErrors().Count != 0)
        {
            InputsChanged();
            return;
        }
        if (!CreditRiskInputBuilder.TryBuild(CurrentCustomer, out var input, out _)) return;
        var application = CurrentApplication;
        var revision = _inputRevision;
        using var cancellation = new CancellationTokenSource();
        _evaluationCts = cancellation;
        IsBusy = true;
        application.Resultado = null;
        application.EvaluationStatus = CreditEvaluationStatus.Evaluating;
        application.EvaluationMessage = "Consultando el modelo de Machine Learning…";
        NotifyEvaluationChanged();
        bool IsCurrent() => application.Id == CurrentApplication.Id && revision == _inputRevision;
        void Fail(string message)
        {
            if (!IsCurrent()) return;
            application.Resultado = null;
            application.EvaluationStatus = CreditEvaluationStatus.Unavailable;
            application.EvaluationMessage = message;
            NotifyEvaluationChanged();
        }
        try
        {
            var response = await _riskService.PredictAsync(input!, cancellation.Token);
            if (!IsCurrent() || cancellation.IsCancellationRequested) return;
            response.ValidateContract();
            application.Resultado = new RiskAssessment
            {
                ProbabilidadMorosidadGrave = response.DefaultProbability,
                NivelRiesgo = _riskLevels.Classify(response.DefaultProbability),
                VersionModelo = response.ModelVersion,
                HorizonteMeses = response.PredictionHorizonMonths,
                EventoObjetivo = response.TargetEvent
            };
            application.EvaluationStatus = CreditEvaluationStatus.Available;
            application.EvaluationMessage = "Evaluación disponible para revisión del analista.";
            NotifyEvaluationChanged();
        }
        catch (CreditRiskServiceException ex) { Fail(ex.Message); }
        catch (InvalidDataException ex) { Fail(ex.Message); }
        catch (OperationCanceledException) { Fail("La evaluación fue cancelada. Podés reintentar."); }
        catch (Exception) { Fail("No se pudo completar la evaluación. Revisá el servicio Python y reintentá."); }
        finally
        {
            _evaluationCts = null;
            IsBusy = false;
        }
    }

    private void StartNewApplication()
    {
        if (IsBusy) return;
        CurrentCustomer.PropertyChanged -= OnCustomerChanged;
        Documents.CollectionChanged -= OnDocumentsChanged;
        CurrentApplication.PropertyChanged -= OnApplicationChanged;
        _currentApplication = new CreditApplication();
        _incomeInput = _amountInput = _termInput = string.Empty;
        _ageInput = _commitmentsInput = _late30Input = _late60Input = string.Empty;
        _inputRevision++;
        AttachCurrentApplication();
        Applications.Add(CurrentApplication);
        foreach (var name in new[] { nameof(CurrentApplication), nameof(CurrentCustomer), nameof(Documents),
            nameof(IncomeInput), nameof(AmountInput), nameof(TermInput), nameof(ApplicationCount),
            nameof(AssessmentResult), nameof(HasAssessment), nameof(AgeInput), nameof(CommitmentsInput), nameof(Late30Input), nameof(Late60Input) })
            OnPropertyChanged(name);
        RefreshValidation();
        SelectedSection = "Evaluación";
    }

    private async Task PickAndAddDocumentsAsync()
    {
        if (IsBusy) return;
        var window = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        var topLevel = window == null ? null : TopLevel.GetTopLevel(window);
        if (topLevel == null)
        {
            StatusMessage = "No se pudo abrir el selector de archivos.";
            return;
        }
        IsBusy = true;
        try
        {
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Adjuntar recibos de sueldo (PDF)",
                AllowMultiple = true,
                FileTypeFilter = new[] { FilePickerFileTypes.Pdf }
            });
            var skipped = 0;
            foreach (var file in files)
            {
                var path = file.TryGetLocalPath();
                if (path == null || !File.Exists(path) ||
                    !string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    continue;
                }
                if (Documents.Any(d => string.Equals(d.FilePath, path, StringComparison.OrdinalIgnoreCase)))
                    continue;
                Documents.Add(new Document
                {
                    CreditApplicationId = CurrentApplication.Id,
                    FileName = file.Name,
                    FilePath = path
                });
            }
            RefreshValidation();
            if (skipped > 0) StatusMessage += " Se omitieron archivos que no son PDF locales disponibles.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"No se pudieron adjuntar los documentos: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    public async Task ProcessDocumentAsync(Document? document)
    {
        if (IsBusy || document == null || !Documents.Contains(document) ||
            document.CreditApplicationId != CurrentApplication.Id) return;
        IsBusy = true;
        document.BeginProcessing();
        try
        {
            var text = await _pdfTextService.ExtractTextAsync(document.FilePath);
            if (string.IsNullOrWhiteSpace(text))
            {
                document.FailProcessing(DocumentProcessingStatus.NoText,
                    "Este documento no pudo procesarse en esta versión: no contiene texto extraíble. OCR no disponible.");
                return;
            }
            var data = await _documentExtractionService.ExtractAsync(text);
            document.CompleteProcessing(data);
        }
        catch (InvalidDataException ex)
        {
            document.FailProcessing(DocumentProcessingStatus.Error, ex.Message);
        }
        catch (DocumentExtractionException ex)
        {
            document.FailProcessing(DocumentProcessingStatus.Error, ex.Message);
        }
        catch (OperationCanceledException)
        {
            document.FailProcessing(DocumentProcessingStatus.Error, "El procesamiento fue cancelado. Podés reintentar.");
        }
        catch (Exception)
        {
            document.FailProcessing(DocumentProcessingStatus.Error,
                "No se pudo procesar el recibo. Verificá el archivo y la configuración, y reintentá.");
        }
        finally { IsBusy = false; }
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
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
