using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Scriptorum.Modelos;

// Historia independiente: sus personajes, relaciones y futuros datos
// (tramas, capítulos, notas...) pertenecen a un único proyecto.
public class Proyecto : INotifyPropertyChanged
{
    private string _nombre = string.Empty;
    private string _descripcion = string.Empty;

    public int Id { get; set; }

    public string Nombre
    {
        get => _nombre;
        set { _nombre = value; OnPropertyChanged(); }
    }

    public string Descripcion
    {
        get => _descripcion;
        set { _descripcion = value; OnPropertyChanged(); }
    }

    public DateTime FechaCreacion { get; set; } = DateTime.Now;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
