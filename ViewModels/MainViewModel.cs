using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Scriptorum.Modelos;
using Scriptorum.Utils;

namespace Scriptorum.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly WriterContext _db = new();
    private Proyecto? _proyectoActual;
    private Personaje? _personajeSeleccionado;
    private Relacion? _relacionSeleccionada;
    private string _nuevoNombre = string.Empty;
    private bool _panelVisible = true;

    public ObservableCollection<Proyecto> Proyectos { get; } = new();
    public ObservableCollection<Personaje> Personajes { get; } = new();
    public ObservableCollection<Relacion> Relaciones { get; } = new();

    // Al cambiar, se limpian selecciones y se recargan personajes y relaciones.
    public Proyecto? ProyectoActual
    {
        get => _proyectoActual;
        set
        {
            if (_proyectoActual == value)
                return;
            _proyectoActual = value;
            OnPropertyChanged();
            CargarDatosProyecto();
            EscribirProyectoActivo();
        }
    }

    public Personaje? PersonajeSeleccionado
    {
        get => _personajeSeleccionado;
        set { _personajeSeleccionado = value; OnPropertyChanged(); }
    }

    public Relacion? RelacionSeleccionada
    {
        get => _relacionSeleccionada;
        set
        {
            _relacionSeleccionada = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NombreColorRelacion));
            OnPropertyChanged(nameof(TextoRelacionSeleccionada));
        }
    }

    public string NuevoNombre
    {
        get => _nuevoNombre;
        set { _nuevoNombre = value; OnPropertyChanged(); }
    }

    public bool PanelVisible
    {
        get => _panelVisible;
        set { _panelVisible = value; OnPropertyChanged(); }
    }

    // Nombre de color ("Azul"/"Verde"/"Rojo") para el ComboBox del panel.
    public string NombreColorRelacion
    {
        get => RelacionSeleccionada == null
            ? "Azul"
            : ColoresRelacion.NombreDeHex(RelacionSeleccionada.Color);
        set
        {
            if (RelacionSeleccionada == null)
                return;
            RelacionSeleccionada.Color = ColoresRelacion.HexDeNombre(value);
            _db.SaveChanges();
            OnPropertyChanged();
            CambiosMapa?.Invoke();
        }
    }

    public List<string> ColoresDisponibles { get; } = new(ColoresRelacion.Nombres);

    public string TextoRelacionSeleccionada => RelacionSeleccionada == null
        ? string.Empty
        : $"{NombrePersonaje(RelacionSeleccionada.PersonajeOrigenId)} → {NombrePersonaje(RelacionSeleccionada.PersonajeDestinoId)}";

    // La vista se suscribe para redibujar lineas al cambiar datos.
    public event Action? CambiosMapa;

    public RelayCommand AnadirPersonajeCommand { get; }
    public RelayCommand EliminarPersonajeCommand { get; }
    public RelayCommand GuardarEdicionCommand { get; }
    public RelayCommand AlternarPanelCommand { get; }
    public RelayCommand EliminarRelacionCommand { get; }
    public RelayCommand LimpiarSeleccionRelacionCommand { get; }

    public MainViewModel()
    {
        AnadirPersonajeCommand = new RelayCommand(_ => AnadirPersonaje(), _ => !string.IsNullOrWhiteSpace(NuevoNombre));
        EliminarPersonajeCommand = new RelayCommand(_ => EliminarPersonajeSeleccionado(), _ => PersonajeSeleccionado != null);
        GuardarEdicionCommand = new RelayCommand(_ => GuardarEdicion(), _ => PersonajeSeleccionado != null);
        AlternarPanelCommand = new RelayCommand(_ => PanelVisible = !PanelVisible);
        EliminarRelacionCommand = new RelayCommand(_ => EliminarRelacionSeleccionada(), _ => RelacionSeleccionada != null);
        LimpiarSeleccionRelacionCommand = new RelayCommand(_ => RelacionSeleccionada = null);

        Cargar();
    }

    private void Cargar()
    {
        _db.AsegurarEsquema();
        Proyectos.Clear();
        foreach (var pr in _db.Proyectos.OrderBy(x => x.Id))
            Proyectos.Add(pr);
        // Restaura el último proyecto activo; si no existe, el primero.
        int? activo = LeerProyectoActivo();
        _proyectoActual = Proyectos.FirstOrDefault(x => x.Id == activo)
            ?? Proyectos.OrderBy(x => x.Id).FirstOrDefault();
        OnPropertyChanged(nameof(ProyectoActual));
        CargarDatosProyecto();
    }

    // Guarda el Id del proyecto activo junto a la BD para restaurarlo al abrir.
    // Es una preferencia no crítica: cualquier fallo se ignora y se abre el primero.
    private static string RutaProyectoActivo()
    {
        var carpeta = AppDomain.CurrentDomain.BaseDirectory;
        return System.IO.Path.Combine(carpeta, "proyecto_activo.txt");
    }

    private static int? LeerProyectoActivo()
    {
        try
        {
            string ruta = RutaProyectoActivo();
            if (!System.IO.File.Exists(ruta))
                return null;
            return int.TryParse(System.IO.File.ReadAllText(ruta).Trim(), out int id) ? id : null;
        }
        catch
        {
            return null;
        }
    }

    private void EscribirProyectoActivo()
    {
        try
        {
            if (ProyectoActual != null)
                System.IO.File.WriteAllText(RutaProyectoActivo(), ProyectoActual.Id.ToString());
        }
        catch
        {
            // Sin persistencia del activo: la sesión sigue funcionando.
        }
    }

    // Recarga personajes y relaciones del proyecto activo, sin restos del anterior.
    private void CargarDatosProyecto()
    {
        PersonajeSeleccionado = null;
        RelacionSeleccionada = null;
        Personajes.Clear();
        Relaciones.Clear();
        if (ProyectoActual != null)
        {
            foreach (var p in _db.Personajes.Where(x => x.ProyectoId == ProyectoActual.Id).OrderBy(x => x.Id))
                Personajes.Add(p);
            foreach (var r in _db.Relaciones.Where(x => x.ProyectoId == ProyectoActual.Id).OrderBy(x => x.Id))
                Relaciones.Add(r);
        }
        OnPropertyChanged(nameof(TextoRelacionSeleccionada));
        CambiosMapa?.Invoke();
    }

    public void CrearProyecto(string nombre, string descripcion)
    {
        nombre = nombre.Trim();
        if (string.IsNullOrEmpty(nombre))
            return;
        var p = new Proyecto { Nombre = nombre, Descripcion = descripcion.Trim(), FechaCreacion = DateTime.Now };
        _db.Proyectos.Add(p);
        _db.SaveChanges();
        Proyectos.Add(p);
        ProyectoActual = p; // nuevo proyecto, mapa vacío
    }

    public void RenombrarProyectoActual(string nombre, string descripcion)
    {
        if (ProyectoActual is null)
            return;
        nombre = nombre.Trim();
        if (string.IsNullOrEmpty(nombre))
            return;
        ProyectoActual.Nombre = nombre;
        ProyectoActual.Descripcion = descripcion.Trim();
        _db.SaveChanges();
    }

    public void EliminarProyectoActual()
    {
        var p = ProyectoActual;
        if (p is null)
            return;
        if (Proyectos.Count <= 1)
        {
            MessageBox.Show("Debe existir al menos un proyecto.", "Scriptorum",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int nPer = _db.Personajes.Count(x => x.ProyectoId == p.Id);
        int nRel = _db.Relaciones.Count(x => x.ProyectoId == p.Id);
        string mensaje = $"¿Eliminar el proyecto «{p.Nombre}»?"
            + ((nPer > 0 || nRel > 0) ? $"\nSe perderán sus {nPer} personajes y {nRel} relaciones." : "");
        if (MessageBox.Show(mensaje, "Scriptorum", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        // Las FK usan Restrict: primero sus relaciones y personajes.
        foreach (var r in _db.Relaciones.Where(x => x.ProyectoId == p.Id).ToList())
            _db.Relaciones.Remove(r);
        foreach (var c in _db.Personajes.Where(x => x.ProyectoId == p.Id).ToList())
            _db.Personajes.Remove(c);
        _db.Proyectos.Remove(p);
        _db.SaveChanges();

        Proyectos.Remove(p);
        ProyectoActual = Proyectos.OrderBy(x => x.Id).First();
    }

    private void AnadirPersonaje()
    {
        var nombre = NuevoNombre.Trim();
        if (string.IsNullOrEmpty(nombre) || ProyectoActual is null)
            return;

        // Posicion inicial en cascada para no apilar nodos.
        double x = 100 + (Personajes.Count % 6) * 90;
        double y = 80 + (Personajes.Count % 5) * 70;

        var p = new Personaje { Nombre = nombre, PosX = x, PosY = y, ProyectoId = ProyectoActual.Id };
        _db.Personajes.Add(p);
        _db.SaveChanges();

        Personajes.Add(p);
        PersonajeSeleccionado = p;
        NuevoNombre = string.Empty;
        CambiosMapa?.Invoke();
    }

    private void GuardarEdicion()
    {
        if (PersonajeSeleccionado is null)
            return;

        _db.SaveChanges();
        OnPropertyChanged(nameof(TextoRelacionSeleccionada));
        CambiosMapa?.Invoke();
    }

    private void EliminarPersonajeSeleccionado()
    {
        var p = PersonajeSeleccionado;
        if (p is null)
            return;

        int nRel = Relaciones.Count(r => r.PersonajeOrigenId == p.Id || r.PersonajeDestinoId == p.Id);
        string mensaje = $"¿Eliminar a «{p.Nombre}»?"
            + (nRel > 0 ? $"\nTambién se eliminarán sus {nRel} relaciones." : "");
        if (MessageBox.Show(mensaje, "Scriptorum", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        // Las FK usan Restrict: primero se borran sus relaciones.
        var rels = Relaciones.Where(r => r.PersonajeOrigenId == p.Id || r.PersonajeDestinoId == p.Id).ToList();
        foreach (var r in rels)
        {
            _db.Relaciones.Remove(r);
            Relaciones.Remove(r);
        }
        if (RelacionSeleccionada != null && rels.Contains(RelacionSeleccionada))
            RelacionSeleccionada = null;

        _db.Personajes.Remove(p);
        _db.SaveChanges();
        Personajes.Remove(p);
        PersonajeSeleccionado = null;
        OnPropertyChanged(nameof(TextoRelacionSeleccionada));
        CambiosMapa?.Invoke();
    }

    // Llamado al soltar un nodo tras arrastrarlo.
    public void GuardarPosicion(Personaje personaje)
    {
        _db.SaveChanges();
        CambiosMapa?.Invoke();
    }

    // Crea una relacion manual. Devuelve false si es duplicada o invalida.
    public bool CrearRelacion(int origenId, int destinoId, string general, string origenADestino, string destinoAOrigen, string colorHex)
    {
        if (ProyectoActual is null || origenId == destinoId)
            return false;
        if (Relaciones.Any(r => r.PersonajeOrigenId == origenId && r.PersonajeDestinoId == destinoId))
            return false;

        var r = new Relacion
        {
            ProyectoId = ProyectoActual.Id,
            PersonajeOrigenId = origenId,
            PersonajeDestinoId = destinoId,
            DescripcionGeneral = general.Trim(),
            DescripcionOrigenADestino = origenADestino.Trim(),
            DescripcionDestinoAOrigen = destinoAOrigen.Trim(),
            Color = colorHex
        };
        _db.Relaciones.Add(r);
        _db.SaveChanges();
        Relaciones.Add(r);
        RelacionSeleccionada = r;
        CambiosMapa?.Invoke();
        return true;
    }

    private void EliminarRelacionSeleccionada()
    {
        if (RelacionSeleccionada is null)
            return;
        _db.Relaciones.Remove(RelacionSeleccionada);
        _db.SaveChanges();
        Relaciones.Remove(RelacionSeleccionada);
        RelacionSeleccionada = null;
        CambiosMapa?.Invoke();
    }

    public string NombrePersonaje(int id)
        => Personajes.FirstOrDefault(p => p.Id == id)?.Nombre ?? $"#{id}";

    public Personaje? BuscarPersonaje(int id)
        => Personajes.FirstOrDefault(p => p.Id == id);

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
