namespace Scriptorum.Utils;

// Tipos de tarjeta de la cronología con sus colores (fondo y acento).
public static class TiposCronologia
{
    public const string Act = "ACT";
    public const string Beat = "BEAT";
    public const string Scene = "SCENE";
    public const string Note = "NOTE";

    public static readonly IReadOnlyList<string> Nombres = [Act, Beat, Scene, Note];

    // Devuelve (fondo, acento) en Hex para el tipo indicado.
    public static (string Fondo, string Acento) Colores(string tipo) => tipo switch
    {
        Act => ("#F7E3E1", "#C96A6A"),
        Scene => ("#DCE7F5", "#5B7C99"),
        Note => ("#E4DCF3", "#7E6BB5"),
        _ => ("#DDEAD3", "#6FA060"), // BEAT (y cualquier valor antiguo) en verde
    };
}
