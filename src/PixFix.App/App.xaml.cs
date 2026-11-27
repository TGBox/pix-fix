using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PixFix.Infrastructure;
using PixFix.Infrastructure.Assets;
using PixFix.Infrastructure.Data;
using PixFix.Infrastructure.Knowledge;
using PixFix.Infrastructure.Legacy;
using PixFix.Infrastructure.Ocr;
using PixFix.Infrastructure.Templates;
using PixFix.Infrastructure.Images;
using PixFix.App.Knowledge;
using PixFix.App.Settings;
using Serilog;

namespace PixFix.App;

public partial class App : Application
{
    private IHost? host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var paths = AppPaths.CreateDefault();
        paths.EnsureCreated();

        // Kein leerer catch: jeder unbehandelte Fehler landet im Log und wird angezeigt.
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(paths.LogsDirectory, "pixfix-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .CreateLogger();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Fatal(args.ExceptionObject as Exception, "Unbehandelter Fehler");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unbeobachteter Fehler in Hintergrundaufgabe");
            args.SetObserved();
        };

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSerilog();
        builder.Services.AddSingleton(paths);
        builder.Services.AddSingleton<PixFixDatabase>();
        builder.Services.AddSingleton<AssetStore>();
        builder.Services.AddSingleton(sp => new LegacyImporter(
            sp.GetRequiredService<PixFixDatabase>(), sp.GetRequiredService<AssetStore>()));
        builder.Services.AddSingleton(sp => new KnowledgeRepository(
            sp.GetRequiredService<PixFixDatabase>(), sp.GetRequiredService<AssetStore>()));
        builder.Services.AddSingleton<OcrService>();
        builder.Services.AddSingleton<TemplateRepository>();
        builder.Services.AddSingleton<ImageSearchService>();
        builder.Services.AddSingleton<KnowledgeDocumentService>();
        builder.Services.AddSingleton<KnowledgeViewModel>();
        builder.Services.AddSingleton<ImportViewModel>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainWindow>();
        host = builder.Build();
        host.Start();

        host.Services.GetRequiredService<PixFixDatabase>().Migrate();

        Log.Information("PixFix gestartet, Datenordner {DataDirectory}", paths.DataDirectory);

        var window = host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();

        // Bildsuch-Index im Hintergrund ergänzen (erste Bildsuche ist dann sofort schnell) – auch nach einem Import.
        var imageSearch = host.Services.GetRequiredService<ImageSearchService>();
        _ = WarmUpImageIndexAsync(imageSearch);
        host.Services.GetRequiredService<ImportViewModel>().ImportSucceeded += (_, _) => _ = WarmUpImageIndexAsync(imageSearch);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        host?.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        host?.Dispose();
        Log.Information("PixFix beendet");
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private static async Task WarmUpImageIndexAsync(ImageSearchService imageSearch)
    {
        try
        {
            var added = await Task.Run(() => imageSearch.WarmUpAsync()).ConfigureAwait(false);
            if (added > 0)
            {
                Log.Information("Bildsuch-Index um {Count} Bilder ergänzt", added);
            }
        }
        catch (Exception ex) when (ex is IOException or Microsoft.Data.Sqlite.SqliteException or InvalidOperationException)
        {
            Log.Warning(ex, "Bildsuch-Index konnte nicht ergänzt werden");
        }
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unbehandelter Fehler in der Oberfläche");
        MessageBox.Show(
            $"Es ist ein Fehler aufgetreten:\n\n{e.Exception.Message}\n\nDetails stehen im Protokoll.",
            "PixFix",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
