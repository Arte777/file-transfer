using System;
using System.Windows;
using System.Windows.Threading;

namespace NexusBuilder
{
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            System.Windows.MessageBox.Show(
                $"Произошла ошибка при работе приложения:\n\n{e.Exception.Message}\n\nСтек:\n{e.Exception.StackTrace}",
                "NEXUS Builder Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Критическая ошибка:\n\n{ex.Message}\n\nСтек:\n{ex.StackTrace}",
                    "NEXUS Builder Fatal Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }
    }
}
