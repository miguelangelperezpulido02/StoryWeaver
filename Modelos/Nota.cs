using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Scriptorum.Modelos;

// Nota de texto libre: pertenece a un proyecto.
public class Nota : INotifyPropertyChanged
{
    private string _titulo = string.Empty;
    private string _texto = string.Empty;

    public int Id { get; set; }
    public int ProyectoId { get; set; }

    public string Titulo
    {
        get => _titulo;
        set { _titulo = value; OnPropertyChanged(); }
    }

    public string Texto
    {
        get => _texto;
        set { _texto = value; OnPropertyChanged(); }
    }

    public DateTime FechaCreacion { get; set; } = DateTime.Now;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
