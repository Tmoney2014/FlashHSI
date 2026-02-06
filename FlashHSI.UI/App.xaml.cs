using System;
using System.Diagnostics;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using FlashHSI.Core.Logging;
using FlashHSI.Core.Services;
using FlashHSI.Core.Engine;
using FlashHSI.Core.Control.Hardware;
using FlashHSI.UI.Services;
using FlashHSI.UI.ViewModels;
using CommunityToolkit.Mvvm.Messaging;

namespace FlashHSI.UI
{
    public partial class App : Application
    {
        public IServiceProvider Services { get; private set; } = null!;
        public new static App Current => (App)Application.Current;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 1. Initialize Logging
            Log.Logger = LoggingConfig.CreateLogger();
            Log.Information("=== FlashHSI Application Starting ===");

            // 2. Set Process Priority (Legacy Logic Re-applied)
            /// <ai>AI가 작성함: 실시간성 보장을 위해 프로세스 우선순위 상향</ai>
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;

            // 3. Configure DI
            Services = ConfigureServices();

            // 4. Global Exception Handling
            SetupExceptionHandling();

            // 5. Initialize Memory Monitoring
            var memoryService = Services.GetRequiredService<MemoryMonitoringService>();
            memoryService.Start();

            // 6. Show Main Window
            var mainWindow = new MainWindow { DataContext = Services.GetRequiredService<MainViewModel>() };
            MainWindow = mainWindow;
            mainWindow.Show();
        }

        private IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            // Core Infrastructure/Infrastructure
            services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);
            services.AddSingleton<MemoryMonitoringService>();
            services.AddSingleton<IWindowModalService, WindowModalService>();
            services.AddSingleton<CommonDataShareService>();

            // Business Services
            services.AddSingleton<HsiEngine>();
            services.AddSingleton<WaterfallService>();
            services.AddSingleton<IEtherCATService, EtherCATService>();

            // ViewModels
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<HomeViewModel>();
            services.AddSingleton<StatisticViewModel>();
            services.AddSingleton<SettingViewModel>();
            services.AddSingleton<LogViewModel>();

            return services.BuildServiceProvider();
        }

        private void SetupExceptionHandling()
        {
            /// <ai>AI가 작성함: 전역 예외 처리기</ai>
            DispatcherUnhandledException += (s, e) =>
            {
                Log.Fatal(e.Exception, "Unhandled UI Exception occurred.");
                MessageBox.Show($"Fatal Error: {e.Exception.Message}", "Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                {
                    Log.Fatal(ex, "Unhandled AppDomain Exception.");
                }
            };
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.Information("=== FlashHSI Application Exiting ===");
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}