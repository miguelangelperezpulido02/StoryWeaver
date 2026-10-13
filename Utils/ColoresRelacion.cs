using System.Windows.Media;

namespace Scriptorum.Utils;

// Paleta cerrada para las lineas de relacion: Azul, Verde, Rojo.
// Se guarda el Hex en SQLite para no depender de conversores.
public static class ColoresRelacion
{
    public const string Azul = "#3B82F6";
    public const string Verde = "#22A06B";
    public const string Rojo = "#D64545";

    public static readonly IReadOnlyList<string> Nombres = ["Azul", "Verde", "Rojo"];

    public static string HexDeNombre(string nombre) => nombre switch
    {
        "Verde" => Verde,
        "Rojo" => Rojo,
        _ => Azul,
    };

    public static string NombreDeHex(string hex) => hex switch
    {
        Verde => "Verde",
        Rojo => "Rojo",
        _ => "Azul",
    };

    public static Brush PincelDeHex(string hex)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            return new SolidColorBrush(color);
        }
        catch
        {
            return Brushes.Gray;
        }
    }
}
