using System.Windows;
using Scriptorum.Modelos;

namespace Scriptorum.Views;

// Sirve para crear y para renombrar: si recibe una línea existente,
// prellena el nombre y cambia los textos a modo edición.
public partial class DialogoLineaTiempo : Window
{
    public string NombreLinea => TxtNombre.Text.Trim();

    public DialogoLineaTiempo(LineaTiempo? existente = null)
    {
        InitializeComponent();
        if (existente != null)
        {
            Title = "Renombrar línea de tiempo";
            TxtTituloVentana.Text = "Renombrar línea de tiempo";
            BtnAceptar.Content = "Guardar";
            TxtNombre.Text = existente.Nombre;
        }
    }

    private void Aceptar_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NombreLinea))
        {
            TxtError.Text = "Escribe un nombre para la línea de tiempo.";
            TxtError.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
