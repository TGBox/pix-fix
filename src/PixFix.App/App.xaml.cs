using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PixFix.Infrastructure;
using PixFix.Infrastructure.Assets;
using PixFix.Infrastructure.Data;
using PixFix.Infrastructure.Legacy;
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
    }

    protected override void OnExit(ExitEventArgs e)
    {
        host?.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        host?.Dispose();
        Log.Information("PixFix beendet");
        Log.CloseAndFlush();
        base.OnExit(e);
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
