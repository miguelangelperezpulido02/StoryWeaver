using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Scriptorum.Modelos;

namespace Scriptorum.Views;

// Sirve para crear y para editar: si recibe un evento existente,
// prellena los campos y cambia los textos a modo edición.
public partial class DialogoCronologia : Window
{
    public string TituloEvento => TxtTitulo.Text.Trim();
    public string DescripcionEvento => TxtDescripcion.Text.Trim();
    public string TipoEvento => (CboTipo.SelectedItem as ComboBoxItem)?.Content as string ?? "BEAT";
    public Personaje? PersonajeSeleccionado => CboPersonaje.SelectedItem as Personaje;

    // Nota asociada a este evento (opcional).
    public int? NotaId { get; private set; }

    // Nota seleccionada en el combo (para llenar el combo desde el VM).
    public Nota? NotaSeleccionada { get; set; }

    public DialogoCronologia(IList<Personaje> personajes, IList<Nota>? notas = null, Cronologia? existente = null)
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

        // Cargar notas para el ComboBoxNota (solo si se proporcionaron).
        if (notas != null)
        {
            CboNota.ItemsSource = notas;
            CboNota.DisplayMemberPath = nameof(Nota.Titulo); // ← muestra el título en lugar del tipo
            // Si hay una nota existente, intentamos seleccionarla.
            if (existente != null && existente.NotaId.HasValue)
                CboNota.SelectedItem = notas.FirstOrDefault(n => n.Id == existente.NotaId.Value);
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
            // Si ya tiene nota, seleccionarla en el combo.
            if (existente.NotaId.HasValue && CboNota.ItemsSource is IList<Nota> listaNotas)
                CboNota.SelectedItem = listaNotas.FirstOrDefault(n => n.Id == existente.NotaId.Value);
        }
    }

    private void CboNota_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Cuando el usuario selecciona una nota, guardamos el Id.
        if (CboNota.SelectedItem is Nota n)
            NotaId = n.Id;
        else
            NotaId = null;
    }

    private void Aceptar_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TituloEvento))
        {
            TxtError.Text = "Escribe un título para el evento.";
            TxtError.Visibility = Visibility.Visible;
            return;
        }

        // Guardar el NotaId si hay selección.
        if (CboNota.SelectedItem is Nota n)
            NotaId = n.Id;
        else
            NotaId = null;

        DialogResult = true;
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
