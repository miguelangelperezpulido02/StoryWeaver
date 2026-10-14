using System.Windows;
using Scriptorum.Modelos;

namespace Scriptorum.Views;

// Sirve para crear y para editar: si recibe una nota existente,
// prellena los campos y cambia los textos a modo edición.
public partial class DialogoNota : Window
{
    public string TituloNota => TxtTitulo.Text.Trim();
    public string TextoNota => TxtTexto.Text.Trim();

    public DialogoNota(Nota? existente = null)
    {
        InitializeComponent();
        if (existente != null)
        {
            Title = "Editar nota";
            TxtTituloVentana.Text = "Editar nota";
            BtnAceptar.Content = "Guardar";
            TxtTitulo.Text = existente.Titulo;
            TxtTexto.Text = existente.Texto;
        }
    }

    private void Aceptar_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TituloNota))
        {
            TxtError.Text = "Escribe un título para la nota.";
            TxtError.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
