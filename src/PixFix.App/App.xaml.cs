using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PixFix.Infrastructure;
using PixFix.Infrastructure.Assets;
using PixFix.Infrastructure.Backup;
using PixFix.Infrastructure.Calls;
using PixFix.Infrastructure.Communication;
using PixFix.Infrastructure.Settings;
using PixFix.Infrastructure.Data;
using PixFix.Infrastructure.Knowledge;
using PixFix.Infrastructure.Ocr;
using PixFix.Infrastructure.Templates;
using PixFix.Infrastructure.Ti;
using PixFix.Infrastructure.Images;
using System.Windows.Input;
using PixFix.App.Backup;
using PixFix.App.Common;
using PixFix.App.Calls;
using PixFix.App.Help;
using PixFix.App.Knowledge;
using PixFix.App.Settings;
using PixFix.App.Ti;
using PixFix.App.Updates;
using PixFix.App.Workspaces;
using PixFix.Infrastructure.Updates;
using PixFix.Infrastructure.Workspaces;
using Serilog;

namespace PixFix.App;

public partial class App : Application
{
    private IHost? host;
    private GlobalHotkey? hotkey;
    private bool exiting;

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
        builder.Services.AddSingleton(sp => new KnowledgeRepository(
            sp.GetRequiredService<PixFixDatabase>(), sp.GetRequiredService<AssetStore>()));
        builder.Services.AddSingleton<OcrService>();
        builder.Services.AddSingleton<TemplateRepository>();
        builder.Services.AddSingleton<ImageSearchService>();
        builder.Services.AddSingleton<KnowledgeDocumentService>();
        builder.Services.AddSingleton<KnowledgeViewModel>();
        builder.Services.AddSingleton(sp => new InformationRepository(
            sp.GetRequiredService<PixFixDatabase>(), sp.GetRequiredService<AssetStore>()));
        builder.Services.AddSingleton<WorkspacesViewModel>();
        builder.Services.AddSingleton(sp => new CallRepository(sp.GetRequiredService<PixFixDatabase>()));
        builder.Services.AddSingleton(sp => new FaxDocumentRepository(
            sp.GetRequiredService<PixFixDatabase>(), sp.GetRequiredService<AssetStore>()));
        builder.Services.AddSingleton<SettingsRepository>();
        builder.Services.AddSingleton<ThemeService>();
        builder.Services.AddSingleton<CobraQuickTextRepository>();
        builder.Services.AddSingleton<CallsViewModel>();
        builder.Services.AddSingleton<GeneralSettingsViewModel>();
        builder.Services.AddSingleton<CallSettingsViewModel>();
        builder.Services.AddSingleton(sp => new BackupWriter(paths, sp.GetRequiredService<PixFixDatabase>()));
        builder.Services.AddSingleton(sp => new RestoreService(paths, sp.GetRequiredService<BackupWriter>()));
        builder.Services.AddSingleton<BackupCoordinator>();
        builder.Services.AddSingleton<AutoBackupService>();
        builder.Services.AddSingleton<BackupViewModel>();
        builder.Services.AddSingleton(_ => new TiMonitorClient(TiMonitorClient.CreateHttpClient()));
        builder.Services.AddSingleton<TiMonitorViewModel>();
        builder.Services.AddSingleton(_ => new UpdateChecker(
            UpdateChecker.CreateHttpClient(PixFix.Core.Updates.AppVersion.Format(UpdateViewModel.CurrentVersion))));
        builder.Services.AddSingleton<UpdateViewModel>();
        builder.Services.AddSingleton<HelpViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();
        builder.Services.AddSingleton<ImportViewModel>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainWindow>();
        builder.Services.AddSingleton<TrayService>();
        builder.Services.AddSingleton<UndoService>();
        host = builder.Build();
        host.Start();

        host.Services.GetRequiredService<PixFixDatabase>().Migrate();

        // Fenstergrößen und Splitterbreiten merken (vor dem ersten Fenster).
        LayoutMemory.Initialize(host.Services.GetRequiredService<SettingsRepository>());
        host.Services.GetRequiredService<ThemeService>().ApplySaved();

        // Lösungsbausteine im Editor (Wissensdatenbank und Schreibbereiche).
        var startupServices = host.Services;
        var templateStore = startupServices.GetRequiredService<TemplateRepository>();
        PixFix.App.Editing.DocumentEditor.SnippetStore = templateStore;
        PixFix.App.Editing.DocumentEditor.ManageSnippets = owner =>
        {
            var templates = new TemplatesViewModel(
                templateStore,
                startupServices.GetRequiredService<CobraQuickTextRepository>(),
                startupServices.GetRequiredService<FaxDocumentRepository>());
            templates.SelectedKind = templates.Kinds.First(k => k.Kind == PixFix.Core.Templates.TemplateKind.Snippet);
            TemplatesWindow.ShowFor(owner, templates);
        };

        Log.Information("PixFix gestartet, Datenordner {DataDirectory}", paths.DataDirectory);

        var window = host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();

        // Anrufe laden und Rückruf-Erinnerungen prüfen – unabhängig vom gerade angezeigten Bereich.
        host.Services.GetRequiredService<CallsViewModel>().Start();

        // Bildsuch-Index im Hintergrund ergänzen (erste Bildsuche ist dann sofort schnell) – auch nach einem Import.
        var imageSearch = host.Services.GetRequiredService<ImageSearchService>();
        _ = WarmUpImageIndexAsync(imageSearch);
        host.Services.GetRequiredService<SettingsViewModel>().DataReplaced += (_, _) => _ = WarmUpImageIndexAsync(imageSearch);

        // Stille Tagessicherung, wenn PixFix eine Weile nicht benutzt wird.
        host.Services.GetRequiredService<AutoBackupService>().Start();

        // TI-Monitor: erste Prüfung im Hintergrund, danach alle 5 Minuten (falls eingeschaltet).
        host.Services.GetRequiredService<TiMonitorViewModel>().Start();

        SetUpTrayAndHotkey(window);
        SetUpHelpAndUpdates(window);
    }

    /// <summary>Update-Hinweis (Prüfung beim Start und alle 6 Stunden) und Einführungstour beim ersten Start.</summary>
    private void SetUpHelpAndUpdates(MainWindow window)
    {
        var services = host!.Services;
        var main = services.GetRequiredService<MainViewModel>();
        var settings = services.GetRequiredService<SettingsRepository>();
        var tray = services.GetRequiredService<TrayService>();
        var update = services.GetRequiredService<UpdateViewModel>();

        void StartTour()
        {
            WindowFlash.BringToFront(window);
            TourWindow.Start(window, main.ShowSection, () => settings.SetFlag(SettingKeys.TourCompleted, true));
        }

        main.TourRequested += (_, _) => StartTour();

        // Windows-Benachrichtigung nur, wenn PixFix gerade nicht im Vordergrund ist – im Fenster steht die Hinweisleiste.
        update.UpdateFound += (_, info) =>
        {
            if (!window.IsActive)
            {
                tray.Notify(
                    "Neue PixFix-Version",
                    $"PixFix {info.VersionText} ist verfügbar. Klicken, um PixFix zu öffnen und das Update herunterzuladen.",
                    onClick: () => WindowFlash.BringToFront(window));
            }
        };
        update.Start();

        // Neue Mitarbeiter: Tour beim allerersten Start, sobald das Fenster steht.
        if (!settings.GetFlag(SettingKeys.TourCompleted, false))
        {
            _ = Dispatcher.InvokeAsync(StartTour, DispatcherPriority.ApplicationIdle);
        }
    }

    /// <summary>Infobereich, Windows-Benachrichtigungen und das systemweite Kürzel Strg+Alt+P.</summary>
    private void SetUpTrayAndHotkey(MainWindow window)
    {
        var services = host!.Services;
        var main = services.GetRequiredService<MainViewModel>();
        var settings = services.GetRequiredService<SettingsRepository>();
        var calls = services.GetRequiredService<CallsViewModel>();
        var ti = services.GetRequiredService<TiMonitorViewModel>();
        var tray = services.GetRequiredService<TrayService>();

        void Open() => WindowFlash.BringToFront(window);

        void OpenImageSearch()
        {
            Open();
            main.ShowKnowledge();
            var knowledge = services.GetRequiredService<KnowledgeViewModel>();
            _ = Dispatcher.InvokeAsync(() => knowledge.RequestImageSearch(pasteClipboard: true), DispatcherPriority.Background);
        }

        tray.AddMenuItem("PixFix öffnen", Open, bold: true);
        tray.AddMenuItem("Anrufe", () =>
        {
            Open();
            main.ShowCalls();
        });
        tray.AddMenuItem("Bildsuche mit Zwischenablage (Strg+Alt+P)", OpenImageSearch);
        tray.AddMenuItem("TI-Monitor", () =>
        {
            Open();
            main.ShowTi();
        });
        tray.AddMenuItem("Hilfe", () =>
        {
            Open();
            main.ShowSection(HelpContent.Help);
        });
        tray.OpenRequested += (_, _) => Open();
        tray.ExitRequested += (_, _) => ExitApplication();
        tray.Show();

        // Schließen-Kreuz: im Infobereich weiterlaufen, damit Erinnerungen und TI-Monitor aktiv bleiben.
        window.Closing += (_, e) =>
        {
            if (!exiting && settings.GetFlag(SettingKeys.CloseToTray, true))
            {
                e.Cancel = true;
                window.Hide();
                tray.NotifyMinimizedOnce();
            }
        };
        SessionEnding += (_, _) => exiting = true;

        // Windows-Benachrichtigungen nur, wenn PixFix gerade nicht im Vordergrund ist.
        calls.ReminderDue += (_, text) =>
        {
            if (!window.IsActive)
            {
                tray.Notify("Rückruf fällig", text, onClick: () =>
                {
                    Open();
                    main.ShowCalls();
                });
            }
        };
        var tiWasBlinking = false;
        ti.PropertyChanged += (_, e) =>
        {
            var blinking = ti.IsBlinking;
            var started = blinking && !tiWasBlinking;
            tiWasBlinking = blinking;
            if (started && !window.IsActive)
            {
                tray.Notify("Störung in der TI", ti.IncidentText + "\n" + ti.LageText, warning: true, onClick: () =>
                {
                    Open();
                    main.ShowTi();
                });
            }
        };

        if (settings.GetFlag(SettingKeys.GlobalHotkey, true))
        {
            hotkey = GlobalHotkey.TryRegister(window, ModifierKeys.Control | ModifierKeys.Alt, Key.P, OpenImageSearch);
            if (hotkey is null)
            {
                Log.Warning("Strg+Alt+P ist bereits von einem anderen Programm belegt – systemweites Kürzel nicht aktiv");
            }
        }
    }

    /// <summary>Wirklich beenden (Menü im Infobereich): offene Eingaben speichern lassen und schließen.</summary>
    private void ExitApplication()
    {
        exiting = true;
        hotkey?.Dispose();
        hotkey = null;
        MainWindow?.Close();
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
