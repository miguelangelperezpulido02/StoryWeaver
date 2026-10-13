using System.Windows;
using System.Windows.Controls;
using Scriptorum.Modelos;
using Scriptorum.Utils;

namespace Scriptorum.Views;

public partial class DialogoRelacion : Window
{
    public Personaje? OrigenSeleccionado => CboOrigen.SelectedItem as Personaje;
    public Personaje? DestinoSeleccionado => CboDestino.SelectedItem as Personaje;
    public string DescripcionGeneral => TxtGeneral.Text.Trim();
    public string DescripcionOrigenADestino => TxtOrigenADestino.Text.Trim();
    public string DescripcionDestinoAOrigen => TxtDestinoAOrigen.Text.Trim();
    public string ColorHex
    {
        get
        {
            var nombre = (CboColor.SelectedItem as ComboBoxItem)?.Content as string ?? "Azul";
            return ColoresRelacion.HexDeNombre(nombre);
        }
    }

    public DialogoRelacion(IList<Personaje> personajes, Personaje? origenPreseleccionado = null,
        Personaje? destinoPreseleccionado = null, bool fijarPersonajes = false)
    {
        InitializeComponent();
        CboOrigen.ItemsSource = personajes;
        CboDestino.ItemsSource = personajes;
        if (origenPreseleccionado != null)
            CboOrigen.SelectedItem = personajes.FirstOrDefault(p => p.Id == origenPreseleccionado.Id);
        if (destinoPreseleccionado != null)
            CboDestino.SelectedItem = personajes.FirstOrDefault(p => p.Id == destinoPreseleccionado.Id);
        // Desde el mapa los personajes ya estan elegidos: se muestran bloqueados.
        if (fijarPersonajes)
        {
            CboOrigen.IsEnabled = false;
            CboDestino.IsEnabled = false;
        }
    }

    private void Crear_Click(object sender, RoutedEventArgs e)
    {
        if (OrigenSeleccionado is null || DestinoSeleccionado is null)
        {
            MostrarError("Elige origen y destino.");
            return;
        }
        if (OrigenSeleccionado.Id == DestinoSeleccionado.Id)
        {
            MostrarError("Origen y destino deben ser personajes distintos.");
            return;
        }
        if (string.IsNullOrWhiteSpace(DescripcionGeneral))
        {
            MostrarError("Escribe una descripción general (es la que verás en el mapa).");
            return;
        }

        DialogResult = true;
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void MostrarError(string mensaje)
    {
        TxtError.Text = mensaje;
        TxtError.Visibility = Visibility.Visible;
    }
}
