using System.Configuration;
using System.Data;
using System.Windows;

namespace MizuLauncher
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += App_DispatcherUnhandledException;
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            string logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mizu_fatal.txt");
            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now}] FATAL: {e.Exception}\n");
            MessageBox.Show($"FATAL ERROR: {e.Exception.Message}");
            e.Handled = true;
        }
    }
}
