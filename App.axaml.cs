using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using RiesgoFiscalApp.Services;
using RiesgoFiscalApp.ViewModels;

namespace RiesgoFiscalApp;

public partial class App : Application
{
    private readonly HttpClient _httpClient = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    private readonly HttpClient _creditRiskHttpClient = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .Build();
            var viewModel = new MainViewModel(new PdfTextExtractionService(),
                new DocumentExtractionService(_httpClient, configuration),
                new PythonCreditRiskService(_creditRiskHttpClient, configuration),
                new RiskLevelService(configuration));
            desktop.MainWindow = new MainWindow { DataContext = viewModel };
            desktop.Exit += (_, _) => { _httpClient.Dispose(); _creditRiskHttpClient.Dispose(); };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
