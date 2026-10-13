using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Scriptorum.Modelos;

// Personaje con coordenadas persistidas para el mapa.
// Implementa INotifyPropertyChanged para que el Canvas se actualice al arrastrar.
public class Personaje : INotifyPropertyChanged
{
    private string _nombre = string.Empty;
    private double _posX = 100;
    private double _posY = 100;

    public int Id { get; set; }

    // Historia a la que pertenece. Aísla sus datos de otros proyectos.
    public int ProyectoId { get; set; }

    public string Nombre
    {
        get => _nombre;
        set { _nombre = value; OnPropertyChanged(); }
    }

    // Coordenadas del nodo en el Canvas. Se guardan en SQLite.
    public double PosX
    {
        get => _posX;
        set { _posX = value; OnPropertyChanged(); }
    }

    public double PosY
    {
        get => _posY;
        set { _posY = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
