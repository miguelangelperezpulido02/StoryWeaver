using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Scriptorum.Modelos;

// Carril de la cronología (ej. "Historia principal"): pertenece a un proyecto
// y contiene sus eventos ordenados. El nombre se edita con doble clic.
public class LineaTiempo : INotifyPropertyChanged
{
    private string _nombre = string.Empty;
    private int _orden;

    public int Id { get; set; }
    public int ProyectoId { get; set; }

    public string Nombre
    {
        get => _nombre;
        set { _nombre = value; OnPropertyChanged(); }
    }

    // Orden vertical de los carriles (1, 2, 3...).
    public int Orden
    {
        get => _orden;
        set { _orden = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
