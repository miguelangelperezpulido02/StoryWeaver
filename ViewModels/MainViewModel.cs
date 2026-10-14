using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Markup;
using Microsoft.EntityFrameworkCore;
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
    private string _nuevoGrupo = string.Empty;
    private bool _panelVisible = true;

    public ObservableCollection<Proyecto> Proyectos { get; } = new();
    public ObservableCollection<Personaje> Personajes { get; } = new();
    public ObservableCollection<Relacion> Relaciones { get; } = new();
    public ObservableCollection<Cronologia> Cronologias { get; } = new();
    public ObservableCollection<Nota> Notas { get; } = new();
    public ObservableCollection<LineaTiempo> Lineas { get; } = new();

    // Vista agrupada estilo Obsidian: cabecera de grupo + personajes con "--".
    public ICollectionView PersonajesVista { get; }

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
        set { _personajeSeleccionado = value; OnPropertyChanged(); OnPropertyChanged(nameof(HaySeleccion)); }
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
            OnPropertyChanged(nameof(HaySeleccion));
        }
    }

    // El panel derecho solo se muestra si hay personaje o relación seleccionada.
    public bool HaySeleccion => PersonajeSeleccionado != null || RelacionSeleccionada != null;

    private Cronologia? _cronologiaSeleccionada;
    private Nota? _notaSeleccionada;

    public Cronologia? CronologiaSeleccionada
    {
        get => _cronologiaSeleccionada;
        set { _cronologiaSeleccionada = value; OnPropertyChanged(); }
    }

    public Nota? NotaSeleccionada
    {
        get => _notaSeleccionada;
        set { _notaSeleccionada = value; OnPropertyChanged(); }
    }

    private string _estadoGuardadoNotas = string.Empty;

    // Pie del editor integrado: "Editando…" mientras se escribe,
    // "Guardado ✓ HH:mm:ss" tras el autoguardado.
    public string EstadoGuardadoNotas
    {
        get => _estadoGuardadoNotas;
        private set { _estadoGuardadoNotas = value; OnPropertyChanged(); }
    }

    public string NuevoNombre
    {
        get => _nuevoNombre;
        set { _nuevoNombre = value; OnPropertyChanged(); }
    }

    public string NuevoGrupo
    {
        get => _nuevoGrupo;
        set { _nuevoGrupo = value; OnPropertyChanged(); }
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

        // Agrupación estilo Obsidian por GrupoVista ("Sin grupo" si está vacío).
        PersonajesVista = CollectionViewSource.GetDefaultView(Personajes);
        PersonajesVista.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Personaje.GrupoVista)));
        PersonajesVista.SortDescriptions.Add(new SortDescription(nameof(Personaje.GrupoVista), ListSortDirection.Ascending));
        PersonajesVista.SortDescriptions.Add(new SortDescription(nameof(Personaje.Nombre), ListSortDirection.Ascending));
        if (PersonajesVista is ICollectionViewLiveShaping viva)
        {
            viva.IsLiveGrouping = true;
            viva.LiveGroupingProperties.Add(nameof(Personaje.GrupoVista));
            viva.IsLiveSorting = true;
            viva.LiveSortingProperties.Add(nameof(Personaje.GrupoVista));
            viva.LiveSortingProperties.Add(nameof(Personaje.Nombre));
        }

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
        CronologiaSeleccionada = null;
        NotaSeleccionada = null;
        EstadoGuardadoNotas = string.Empty;
        Personajes.Clear();
        Relaciones.Clear();
        Cronologias.Clear();
        Notas.Clear();
        Lineas.Clear();
        if (ProyectoActual != null)
        {
            foreach (var p in _db.Personajes.Where(x => x.ProyectoId == ProyectoActual.Id).OrderBy(x => x.Id))
                Personajes.Add(p);
            foreach (var r in _db.Relaciones.Where(x => x.ProyectoId == ProyectoActual.Id).OrderBy(x => x.Id))
                Relaciones.Add(r);
            foreach (var l in _db.LineasTiempo.Where(x => x.ProyectoId == ProyectoActual.Id).OrderBy(x => x.Orden).ThenBy(x => x.Id))
                Lineas.Add(l);
            foreach (var c in _db.Cronologias.Where(x => x.ProyectoId == ProyectoActual.Id).OrderBy(x => x.LineaId).ThenBy(x => x.Orden).ThenBy(x => x.Id))
                Cronologias.Add(c);
            foreach (var n in _db.Notas.Where(x => x.ProyectoId == ProyectoActual.Id).OrderByDescending(x => x.FechaCreacion).ThenByDescending(x => x.Id))
                Notas.Add(n);
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
        int nCro = _db.Cronologias.Count(x => x.ProyectoId == p.Id);
        int nNot = _db.Notas.Count(x => x.ProyectoId == p.Id);
        int nLin = _db.LineasTiempo.Count(x => x.ProyectoId == p.Id);
        string mensaje = $"¿Eliminar el proyecto «{p.Nombre}»?"
            + ((nPer > 0 || nRel > 0 || nCro > 0 || nNot > 0 || nLin > 0)
                ? $"\nSe perderán sus {nPer} personajes, {nRel} relaciones, {nCro} eventos, {nNot} notas y {nLin} líneas."
                : "");
        if (MessageBox.Show(mensaje, "Scriptorum", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        // Las FK usan Restrict: primero sus relaciones, eventos, notas y personajes.
        foreach (var r in _db.Relaciones.Where(x => x.ProyectoId == p.Id).ToList())
            _db.Relaciones.Remove(r);
        foreach (var c in _db.Cronologias.Where(x => x.ProyectoId == p.Id).ToList())
            _db.Cronologias.Remove(c);
        foreach (var n in _db.Notas.Where(x => x.ProyectoId == p.Id).ToList())
            _db.Notas.Remove(n);
        foreach (var c in _db.Personajes.Where(x => x.ProyectoId == p.Id).ToList())
            _db.Personajes.Remove(c);
        foreach (var l in _db.LineasTiempo.Where(x => x.ProyectoId == p.Id).ToList())
            _db.LineasTiempo.Remove(l);
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

        var p = new Personaje { Nombre = nombre, Grupo = NuevoGrupo.Trim(), PosX = x, PosY = y, ProyectoId = ProyectoActual.Id };
        _db.Personajes.Add(p);
        _db.SaveChanges();

        Personajes.Add(p);
        PersonajeSeleccionado = p;
        NuevoNombre = string.Empty;
        NuevoGrupo = string.Empty;
        CambiosMapa?.Invoke();
    }

    private void GuardarEdicion()
    {
        if (PersonajeSeleccionado is null)
            return;

        // Normaliza el grupo: sin espacios laterales; vacío = Sin grupo.
        PersonajeSeleccionado.Grupo = PersonajeSeleccionado.Grupo.Trim();
        _db.SaveChanges();
        OnPropertyChanged(nameof(TextoRelacionSeleccionada));
        CambiosMapa?.Invoke();
    }

    // Mueve un personaje a otro grupo (arrastrar y soltar en la lista).
    // grupoDestino es el texto de la cabecera: "Sin grupo" equivale a vacío.
    public void MoverAGrupo(Personaje personaje, string grupoDestino)
    {
        string nuevo = grupoDestino == "Sin grupo" ? string.Empty : grupoDestino.Trim();
        if (personaje.Grupo.Trim() == nuevo)
            return;
        personaje.Grupo = nuevo;
        _db.SaveChanges();
    }

    private void EliminarPersonajeSeleccionado()
    {
        var p = PersonajeSeleccionado;
        if (p is null)
            return;

        int nRel = Relaciones.Count(r => r.PersonajeOrigenId == p.Id || r.PersonajeDestinoId == p.Id);
        int nCro = _db.Cronologias.Count(c => c.PersonajeId == p.Id);
        string mensaje = $"¿Eliminar a «{p.Nombre}»?"
            + ((nRel > 0 || nCro > 0) ? $"\nTambién se eliminarán sus {nRel} relaciones y {nCro} eventos." : "");
        if (MessageBox.Show(mensaje, "Scriptorum", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        // Las FK usan Restrict: primero se borran sus relaciones y eventos.
        var rels = Relaciones.Where(r => r.PersonajeOrigenId == p.Id || r.PersonajeDestinoId == p.Id).ToList();
        foreach (var r in rels)
        {
            _db.Relaciones.Remove(r);
            Relaciones.Remove(r);
        }
        foreach (var c in _db.Cronologias.Where(c => c.PersonajeId == p.Id).ToList())
        {
            _db.Cronologias.Remove(c);
            Cronologias.Remove(c);
        }
        if (CronologiaSeleccionada != null && !Cronologias.Contains(CronologiaSeleccionada))
            CronologiaSeleccionada = null;
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

    // ---- Notas: editor integrado con autoguardado (sin diálogos) ----

    // Crea un borrador visible en la lista pero SIN guardar en BD:
    // solo se persiste al primer autoguardado con contenido.
    public void NuevaNotaBorrador()
    {
        if (ProyectoActual is null)
            return;
        if (NotaSeleccionada != null)
            GuardarNotaEditada(NotaSeleccionada);
        var n = new Nota
        {
            ProyectoId = ProyectoActual.Id,
            FechaCreacion = DateTime.Now
        };
        _db.Notas.Add(n);
        Notas.Insert(0, n); // las nuevas arriba (lista ordenada por fecha desc.)
        NotaSeleccionada = n;
        EstadoGuardadoNotas = string.Empty;
    }

    // Guarda la nota editada en el panel integrado. Es idempotente y segura:
    // - recorta espacios; un borrador (Id == 0) totalmente vacío se descarta sin dejar rastro;
    // - una nota existente vaciada del todo se revierte a su último estado guardado;
    // - sin título pero con texto, el título se deduce de la primera línea.
    // El contenido puede ser texto plano (notas antiguas) o XAML de FlowDocument
    // (al aplicar negrita/cursiva); las comprobaciones usan siempre el texto plano.
    // Devuelve true si revirtió la nota (la vista debe recargar el documento).
    public bool GuardarNotaEditada(Nota nota)
    {
        var estado = _db.Entry(nota).State;
        if (estado == EntityState.Deleted || estado == EntityState.Detached)
            return false;
        nota.Titulo = nota.Titulo.Trim();
        bool esRico = EsDocumentoXaml(nota.Texto);
        string plano = esRico ? TextoPlano(nota.Texto) : nota.Texto.Trim();
        if (!esRico)
            nota.Texto = plano;
        if (string.IsNullOrWhiteSpace(nota.Titulo) && string.IsNullOrWhiteSpace(plano))
        {
            if (nota.Id == 0)
            {
                _db.Notas.Remove(nota);
                Notas.Remove(nota);
                if (NotaSeleccionada == nota)
                    NotaSeleccionada = null;
                EstadoGuardadoNotas = string.Empty;
                return false;
            }
            _db.Entry(nota).Reload();
            EstadoGuardadoNotas = "Sin cambios";
            return true;
        }
        if (string.IsNullOrWhiteSpace(nota.Titulo))
            nota.Titulo = PrimeraLinea(plano, 60);
        _db.SaveChanges();
        EstadoGuardadoNotas = $"Guardado ✓ {DateTime.Now:HH:mm:ss}";
        return false;
    }

    // El editor rico guarda XAML de FlowDocument; el texto plano heredado no.
    public static bool EsDocumentoXaml(string? texto) =>
        texto != null && texto.TrimStart().StartsWith("<FlowDocument", StringComparison.Ordinal);

    // Extrae el texto visible (sin marcas) para vacíos, títulos y búsquedas.
    public static string TextoPlano(string texto)
    {
        if (!EsDocumentoXaml(texto))
            return texto;
        try
        {
            if (XamlReader.Parse(texto) is FlowDocument doc)
                return new TextRange(doc.ContentStart, doc.ContentEnd).Text.Trim();
        }
        catch
        {
            // XAML dañado: se trata como texto tal cual.
        }
        return texto.Trim();
    }

    public void MarcarNotaEditando()
    {
        EstadoGuardadoNotas = "Editando…";
    }

    private static string PrimeraLinea(string texto, int maximo)
    {
        string linea = texto.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        linea = linea.Trim();
        return linea.Length <= maximo ? linea : linea[..maximo].TrimEnd() + "…";
    }

    public void EliminarNotaSeleccionada()
    {
        var n = NotaSeleccionada;
        if (n is null)
            return;
        if (MessageBox.Show($"¿Eliminar la nota «{n.Titulo}»?", "Scriptorum",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        _db.Notas.Remove(n);
        _db.SaveChanges();
        Notas.Remove(n);
        NotaSeleccionada = null;
    }

    // ---- Líneas de tiempo (carriles de la pestaña Cronología) ----

    public void CrearLinea(string nombre)
    {
        if (ProyectoActual is null || string.IsNullOrWhiteSpace(nombre))
            return;
        var l = new LineaTiempo
        {
            ProyectoId = ProyectoActual.Id,
            Nombre = nombre.Trim(),
            Orden = Lineas.Any() ? Lineas.Max(x => x.Orden) + 1 : 1
        };
        _db.LineasTiempo.Add(l);
        _db.SaveChanges();
        Lineas.Add(l);
    }

    public void RenombrarLinea(LineaTiempo linea, string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre))
            return;
        linea.Nombre = nombre.Trim();
        _db.SaveChanges();
    }

    // ---- Cronología (carriles con tarjetas ordenadas, aislada por proyecto) ----

    public void CrearEvento(string titulo, string descripcion, int? personajeId, string tipo, int lineaId)
    {
        if (ProyectoActual is null || string.IsNullOrWhiteSpace(titulo))
            return;

        var enLinea = Cronologias.Where(x => x.LineaId == lineaId).ToList();
        var c = new Cronologia
        {
            ProyectoId = ProyectoActual.Id,
            LineaId = lineaId,
            PersonajeId = personajeId,
            Titulo = titulo.Trim(),
            Descripcion = descripcion.Trim(),
            Tipo = tipo,
            Orden = enLinea.Any() ? enLinea.Max(e => e.Orden) + 1 : 1
        };
        _db.Cronologias.Add(c);
        _db.SaveChanges();
        Cronologias.Add(c);
        OrdenarCronologias();
        CronologiaSeleccionada = c;
    }

    public void ActualizarEvento(Cronologia evento, string titulo, string descripcion, int? personajeId, string tipo)
    {
        if (string.IsNullOrWhiteSpace(titulo))
            return;
        evento.Titulo = titulo.Trim();
        evento.Descripcion = descripcion.Trim();
        evento.PersonajeId = personajeId;
        evento.Tipo = tipo;
        _db.SaveChanges();
        OrdenarCronologias();
    }

    // Mueve un evento a una posición de un carril (mismo u otro):
    // renumera el carril destino de 1..N y persiste.
    public void MoverEvento(Cronologia evento, int lineaDestinoId, int indice)
    {
        var destino = Cronologias
            .Where(x => x.LineaId == lineaDestinoId && x != evento)
            .OrderBy(x => x.Orden).ThenBy(x => x.Id)
            .ToList();
        if (indice < 0) indice = 0;
        if (indice > destino.Count) indice = destino.Count;
        destino.Insert(indice, evento);
        evento.LineaId = lineaDestinoId;
        for (int i = 0; i < destino.Count; i++)
            destino[i].Orden = i + 1;
        _db.SaveChanges();
        OrdenarCronologias();
    }

    public void EliminarEventoSeleccionado()
    {
        var c = CronologiaSeleccionada;
        if (c is null)
            return;
        if (MessageBox.Show($"¿Eliminar el evento «{c.Titulo}»?", "Scriptorum",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        _db.Cronologias.Remove(c);
        _db.SaveChanges();
        Cronologias.Remove(c);
        CronologiaSeleccionada = null;
    }

    private void OrdenarCronologias()
    {
        var ordenados = Cronologias.OrderBy(x => x.LineaId).ThenBy(x => x.Orden).ThenBy(x => x.Id).ToList();
        for (int i = 0; i < ordenados.Count; i++)
            Cronologias.Move(Cronologias.IndexOf(ordenados[i]), i);
    }

    public string NombrePersonaje(int id)
        => Personajes.FirstOrDefault(p => p.Id == id)?.Nombre ?? $"#{id}";

    public Personaje? BuscarPersonaje(int id)
        => Personajes.FirstOrDefault(p => p.Id == id);

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
