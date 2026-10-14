using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Scriptorum.Modelos;

// Evento de la cronología: pertenece a un proyecto y a una línea de tiempo;
// opcionalmente está vinculado a un personaje.
public class Cronologia : INotifyPropertyChanged
{
    private string _titulo = string.Empty;
    private string _descripcion = string.Empty;
    private string _tipo = "BEAT";
    private int? _personajeId;
    private int _orden;
    private int _lineaId;

    public int Id { get; set; }
    public int ProyectoId { get; set; }

    // Carril al que pertenece dentro del proyecto.
    public int LineaId
    {
        get => _lineaId;
        set { _lineaId = value; OnPropertyChanged(); }
    }

    // Posición dentro de su carril (1, 2, 3...). Se muestra como número.
    public int Orden
    {
        get => _orden;
        set { _orden = value; OnPropertyChanged(); }
    }

    // ACT, BEAT, SCENE o NOTE. Define el color de la tarjeta.
    public string Tipo
    {
        get => _tipo;
        set { _tipo = value; OnPropertyChanged(); }
    }

    public int? PersonajeId
    {
        get => _personajeId;
        set { _personajeId = value; OnPropertyChanged(); }
    }

    public string Titulo
    {
        get => _titulo;
        set { _titulo = value; OnPropertyChanged(); }
    }

    public string Descripcion
    {
        get => _descripcion;
        set { _descripcion = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
