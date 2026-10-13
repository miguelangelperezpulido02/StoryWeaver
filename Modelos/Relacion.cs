using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Scriptorum.Modelos;

// Relacion manual entre dos personajes.
// Guarda las dos perspectivas narrativas por separado:
// - DescripcionOrigenADestino: como ve Origen a Destino.
// - DescripcionDestinoAOrigen: como ve Destino a Origen.
public class Relacion : INotifyPropertyChanged
{
    private string _descripcionGeneral = string.Empty;
    private string _descripcionOrigenADestino = string.Empty;
    private string _descripcionDestinoAOrigen = string.Empty;
    private string _color = Utils.ColoresRelacion.Azul;

    public int Id { get; set; }

    // Historia a la que pertenece. Aísla sus datos de otros proyectos.
    public int ProyectoId { get; set; }

    public int PersonajeOrigenId { get; set; }
    public Personaje? PersonajeOrigen { get; set; }

    public int PersonajeDestinoId { get; set; }
    public Personaje? PersonajeDestino { get; set; }

    public string DescripcionGeneral
    {
        get => _descripcionGeneral;
        set { _descripcionGeneral = value; OnPropertyChanged(); }
    }

    public string DescripcionOrigenADestino
    {
        get => _descripcionOrigenADestino;
        set { _descripcionOrigenADestino = value; OnPropertyChanged(); }
    }

    public string DescripcionDestinoAOrigen
    {
        get => _descripcionDestinoAOrigen;
        set { _descripcionDestinoAOrigen = value; OnPropertyChanged(); }
    }

    // Hex del color de la linea: solo Azul / Verde / Rojo (ver ColoresRelacion).
    public string Color
    {
        get => _color;
        set { _color = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
