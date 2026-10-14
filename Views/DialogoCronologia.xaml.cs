using System.Windows;
using System.Windows.Controls;
using Scriptorum.Modelos;

namespace Scriptorum.Views;

// Sirve para crear y para editar: si recibe un evento existente,
// prellena los campos y cambia los textos a modo edición.
// El ComboBox mezcla la opción "(Sin personaje)" (string) con Personajes:
// Personaje.ToString() muestra su Nombre.
public partial class DialogoCronologia : Window
{
    public string TituloEvento => TxtTitulo.Text.Trim();
    public string DescripcionEvento => TxtDescripcion.Text.Trim();
    public string TipoEvento => (CboTipo.SelectedItem as ComboBoxItem)?.Content as string ?? "BEAT";
    public Personaje? PersonajeSeleccionado => CboPersonaje.SelectedItem as Personaje;

    public DialogoCronologia(IList<Personaje> personajes, Cronologia? existente = null)
    {
        InitializeComponent();

        var opciones = new List<object> { "(Sin personaje)" };
        opciones.AddRange(personajes);
        CboPersonaje.ItemsSource = opciones;
        CboPersonaje.SelectedIndex = 0;
        foreach (ComboBoxItem item in CboTipo.Items)
        {
            if ((item.Content as string) == "BEAT")
            {
                CboTipo.SelectedItem = item;
                break;
            }
        }

        if (existente != null)
        {
            Title = "Editar evento";
            TxtTituloVentana.Text = "Editar evento";
            BtnAceptar.Content = "Guardar";
            TxtTitulo.Text = existente.Titulo;
            TxtDescripcion.Text = existente.Descripcion;
            foreach (ComboBoxItem item in CboTipo.Items)
            {
                if ((item.Content as string) == existente.Tipo)
                {
                    CboTipo.SelectedItem = item;
                    break;
                }
            }
            if (existente.PersonajeId is int id)
            {
                var p = personajes.FirstOrDefault(x => x.Id == id);
                if (p != null)
                    CboPersonaje.SelectedItem = p;
            }
        }
    }

    private void Aceptar_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TituloEvento))
        {
            TxtError.Text = "Escribe un título para el evento.";
            TxtError.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
