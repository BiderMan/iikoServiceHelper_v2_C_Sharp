using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using iikoServiceHelper.Models;
using iikoServiceHelper.Services;

namespace iikoServiceHelper
{
    public partial class App : Application
    {
        public IServiceProvider Services { get; private set; } = null!;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Global exception handler
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;

            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);
            Services = serviceCollection.BuildServiceProvider();

            var mainWindow = Services.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            var logger = Services.GetService<ILogger<App>>();
            logger?.LogCritical(e.Exception, "An unhandled exception occurred and the application is terminating.");

            // Attempt to log to a file as a fallback
            try
            {
                var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "iikoServiceHelper_v2");
                var crashLogPath = Path.Combine(appDataPath, "crash_log.txt");
                File.AppendAllText(crashLogPath, $"[{DateTime.Now:O}]\n{e.Exception}\n\n");
            }
            catch (Exception logEx)
            {
                // Log the logging failure itself for diagnostics
                System.Diagnostics.Debug.WriteLine($"Failed to write crash log: {logEx.Message}");
            }

            MessageBox.Show("Произошла критическая ошибка, и приложение будет закрыто.\nПодробности записаны в файл crash_log.txt в папке с данными приложения.", "Критическая ошибка", MessageBoxButton.OK, MessageBoxImage.Error);

            e.Handled = true; // Mark as handled to allow for graceful shutdown
            Application.Current.Shutdown();
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // Configuration
            var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "iikoServiceHelper_v2");
            Directory.CreateDirectory(appDataPath);
            var settingsPath = Path.Combine(appDataPath, "settings.json");
            AppSettings settings = new AppSettings();
            if (File.Exists(settingsPath))
            {
                try { settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settingsPath)) ?? new AppSettings(); } catch { }
            }
            services.AddSingleton(settings);

            // Logging
            services.AddLogging(configure => configure.AddConsole().SetMinimumLevel(LogLevel.Debug));

            // Services
            services.AddSingleton<HotkeyManager>();
            services.AddSingleton<ICommandExecutionService, CommandExecutionService>();
            services.AddSingleton(new CustomCommandService(appDataPath));
            services.AddTransient<UpdateService>(); // Assuming UpdateService is stateless or transient
            services.AddSingleton<MainWindow>();
        }
    }
}