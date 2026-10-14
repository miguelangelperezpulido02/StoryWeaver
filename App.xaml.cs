using System.Configuration;
using System.Data;
using System.Windows;

namespace Scriptorum;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    // Red de seguridad: cualquier excepción no controlada muestra un aviso,
    // queda registrada en error.log junto al ejecutable y NO cierra la app.
    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            string ruta = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log");
            string linea = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e.Exception.GetType().Name}: {e.Exception.Message}{Environment.NewLine}{e.Exception.StackTrace}{Environment.NewLine}";
            System.IO.File.AppendAllText(ruta, linea);
        }
        catch
        {
            // Si ni siquiera se puede escribir el log, al menos avisar.
        }

        MessageBox.Show(
            "Se ha producido un error inesperado, pero la aplicación sigue abierta.\n" +
            "El detalle quedó guardado en error.log junto al programa.",
            "Scriptorum", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}

