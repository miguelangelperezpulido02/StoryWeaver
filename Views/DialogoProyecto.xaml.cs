using System.Windows;
using Scriptorum.Modelos;

namespace Scriptorum.Views;

// Sirve para crear y para editar: si recibe un proyecto existente,
// prellena los campos y cambia los textos a modo edición.
public partial class DialogoProyecto : Window
{
    public string NombreProyecto => TxtNombre.Text.Trim();
    public string DescripcionProyecto => TxtDescripcion.Text.Trim();

    public DialogoProyecto(Proyecto? existente = null)
    {
        InitializeComponent();
        if (existente != null)
        {
            Title = "Editar proyecto";
            TxtTitulo.Text = "Editar proyecto";
            BtnAceptar.Content = "Guardar";
            TxtNombre.Text = existente.Nombre;
            TxtDescripcion.Text = existente.Descripcion;
        }
    }

    private void Aceptar_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NombreProyecto))
        {
            TxtError.Text = "Escribe un nombre para el proyecto.";
            TxtError.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
