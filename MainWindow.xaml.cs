using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using Scriptorum.Modelos;
using Scriptorum.Utils;
using Scriptorum.ViewModels;
using Scriptorum.Views;

namespace Scriptorum;

public partial class MainWindow : Window
{
    private const double AnchoNodo = 124;
    private const double AltoNodo = 58;
    private const double AnchoSuperficie = 4000;
    private const double AltoSuperficie = 4000;
    private const double ZoomMin = 0.25;
    private const double ZoomMax = 2.5;
    private const double ZoomPaso = 1.15;

    private bool _arrastrando;
    private Personaje? _personajeArrastrado;
    private Point _desplazamiento; // diferencia entre cursor y origen del nodo

    private double _zoom = 1.0;
    private bool _moviendoVista;
    private Point _panOffsetsInicio; // HorizontalOffset/VerticalOffset al empezar el pan
    private Point _panCursorInicio;  // cursor (en viewport) al empezar el pan
    private Cursor? _cursorAnterior;

    private Personaje? _origenPendiente; // origen elegido con clic derecho, a la espera de destino
    private Cursor? _cursorMapaPrevio;

    private bool _arrastreListaPosible;
    private Personaje? _personajeArrastre;
    private Point _arrastreInicio;

    private Nota? _notaEditando; // nota suscrita al autoguardado del editor integrado
    private readonly DispatcherTimer _temporizadorNotas = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private bool _sincronizandoNota;
    private bool _cargandoDocumento; // evita que cargar el documento dispare el autoguardado

    public MainWindow()
    {
        InitializeComponent();
        var vm = new MainViewModel();
        DataContext = vm;

        vm.CambiosMapa += RedibujarRelaciones;
        vm.CambiosMapa += RedibujarCronologia;
        vm.Relaciones.CollectionChanged += (_, _) => RedibujarRelaciones();
        vm.Personajes.CollectionChanged += Personajes_CollectionChanged;
        vm.PropertyChanged += Vm_PropertyChanged;

        _temporizadorNotas.Tick += (_, _) => VolcarNotaEditando();
        SincronizarNotaEditando();

        foreach (var p in vm.Personajes)
            p.PropertyChanged += Personaje_PropertyChanged;

        RedibujarRelaciones();
        RedibujarCronologia();
    }

    private MainViewModel? VM => DataContext as MainViewModel;

    private void Personajes_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (Personaje p in e.NewItems)
                p.PropertyChanged += Personaje_PropertyChanged;
        if (e.OldItems != null)
            foreach (Personaje p in e.OldItems)
                p.PropertyChanged -= Personaje_PropertyChanged;
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.RelacionSeleccionada))
            RedibujarRelaciones();
        if (e.PropertyName == nameof(MainViewModel.CronologiaSeleccionada))
            RedibujarCronologia();
        if (e.PropertyName == nameof(MainViewModel.NotaSeleccionada))
            SincronizarNotaEditando();
    }

    // Durante el arrastre las lineas siguen al nodo en vivo.
    private void Personaje_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Personaje.PosX) || e.PropertyName == nameof(Personaje.PosY))
            RedibujarRelaciones();
    }

    // ---- Nodos: arrastrar ----

    private void Nodo_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement elemento || elemento.DataContext is not Personaje personaje)
            return;

        // Modo "crear relación": el clic elige destino (el origen se arrastra con normalidad).
        if (_origenPendiente != null && personaje != _origenPendiente)
        {
            CompletarRelacionDesdeMapa(personaje);
            e.Handled = true;
            return;
        }

        _arrastrando = true;
        _personajeArrastrado = personaje;

        // Selecciona el nodo al hacer clic (sincroniza lista y panel).
        if (VM != null)
            VM.PersonajeSeleccionado = personaje;

        // Calcula el desplazamiento para que el nodo no "salte" al cursor.
        Point cursorEnMapa = e.GetPosition(MapaItems);
        _desplazamiento = new Point(cursorEnMapa.X - personaje.PosX, cursorEnMapa.Y - personaje.PosY);

        elemento.CaptureMouse();
        e.Handled = true;
    }

    private void Nodo_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_arrastrando || _personajeArrastrado is null)
            return;
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        Point cursorEnMapa = e.GetPosition(MapaItems);
        double nuevaX = cursorEnMapa.X - _desplazamiento.X;
        double nuevaY = cursorEnMapa.Y - _desplazamiento.Y;

        // Limita a coordenadas no negativas dentro del lienzo.
        if (nuevaX < 0) nuevaX = 0;
        if (nuevaY < 0) nuevaY = 0;
        if (nuevaX > AnchoSuperficie - AnchoNodo) nuevaX = AnchoSuperficie - AnchoNodo;
        if (nuevaY > AltoSuperficie - AltoNodo) nuevaY = AltoSuperficie - AltoNodo;

        _personajeArrastrado.PosX = nuevaX;
        _personajeArrastrado.PosY = nuevaY;
    }

    private void Nodo_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_arrastrando)
            return;

        _arrastrando = false;

        if (sender is FrameworkElement elemento)
            elemento.ReleaseMouseCapture();

        // Persiste las coordenadas en SQLite al soltar.
        if (_personajeArrastrado != null && VM != null)
            VM.GuardarPosicion(_personajeArrastrado);

        _personajeArrastrado = null;
        e.Handled = true;
    }

    // ---- Zoom y desplazamiento de la vista ----
    // El zoom es solo visual (LayoutTransform): PosX/PosY siguen en
    // coordenadas de contenido, asi que la persistencia no cambia.

    private void VisorMapa_Wheel(object sender, MouseWheelEventArgs e)
    {
        double factor = e.Delta > 0 ? ZoomPaso : 1.0 / ZoomPaso;
        AplicarZoom(_zoom * factor, e.GetPosition(VisorMapa));
        e.Handled = true; // evita que el ScrollViewer haga scroll con la rueda
    }

    private void ZoomMas_Click(object sender, RoutedEventArgs e)
        => AplicarZoom(_zoom * ZoomPaso, CentroDelViewport());

    private void ZoomMenos_Click(object sender, RoutedEventArgs e)
        => AplicarZoom(_zoom / ZoomPaso, CentroDelViewport());

    private void ZoomReset_Click(object sender, RoutedEventArgs e)
        => AplicarZoom(1.0, CentroDelViewport());

    private Point CentroDelViewport()
        => new(VisorMapa.ViewportWidth / 2, VisorMapa.ViewportHeight / 2);

    private void AplicarZoom(double nuevoZoom, Point anclaEnViewport)
    {
        if (nuevoZoom < ZoomMin) nuevoZoom = ZoomMin;
        if (nuevoZoom > ZoomMax) nuevoZoom = ZoomMax;

        // Punto de contenido bajo el cursor antes del cambio, para que siga bajo el cursor despues.
        double contenidoX = (VisorMapa.HorizontalOffset + anclaEnViewport.X) / _zoom;
        double contenidoY = (VisorMapa.VerticalOffset + anclaEnViewport.Y) / _zoom;

        _zoom = nuevoZoom;
        ZoomMapa.ScaleX = _zoom;
        ZoomMapa.ScaleY = _zoom;
        TxtZoom.Text = $"{(int)Math.Round(_zoom * 100)}%";

        VisorMapa.UpdateLayout(); // asegura Extent actualizado antes de mover los offsets
        VisorMapa.ScrollToHorizontalOffset(contenidoX * _zoom - anclaEnViewport.X);
        VisorMapa.ScrollToVerticalOffset(contenidoY * _zoom - anclaEnViewport.Y);
    }

    // Pan con boton central: no interfiere con el arrastre de nodos (boton izquierdo).
    private void VisorMapa_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.MiddleButton != MouseButtonState.Pressed || _moviendoVista)
            return;

        _moviendoVista = true;
        _panOffsetsInicio = new Point(VisorMapa.HorizontalOffset, VisorMapa.VerticalOffset);
        _panCursorInicio = e.GetPosition(VisorMapa);
        _cursorAnterior = VisorMapa.Cursor;
        VisorMapa.Cursor = Cursors.ScrollAll;
        VisorMapa.CaptureMouse();
        e.Handled = true;
    }

    private void VisorMapa_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_moviendoVista)
            return;

        Point cursor = e.GetPosition(VisorMapa);
        VisorMapa.ScrollToHorizontalOffset(_panOffsetsInicio.X - (cursor.X - _panCursorInicio.X));
        VisorMapa.ScrollToVerticalOffset(_panOffsetsInicio.Y - (cursor.Y - _panCursorInicio.Y));
    }

    private void VisorMapa_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_moviendoVista || e.ChangedButton != MouseButton.Middle)
            return;

        _moviendoVista = false;
        VisorMapa.ReleaseMouseCapture();
        VisorMapa.Cursor = _cursorAnterior;
        e.Handled = true;
    }

    // ---- Crear relación con clic derecho ----

    private void CrearRelacionDesdeNodo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || item.Parent is not ContextMenu menu)
            return;
        if (menu.PlacementTarget is not FrameworkElement borde || borde.DataContext is not Personaje personaje)
            return;

        if (VM != null)
            VM.PersonajeSeleccionado = personaje;
        EntrarModoRelacion(personaje);
    }

    private void EntrarModoRelacion(Personaje origen)
    {
        QuitarResaltadoOrigen();
        _origenPendiente = origen;
        ResaltarOrigen(origen);
        TxtAvisoRelacion.Text = $"Relación desde «{origen.Nombre}»: haz clic en el personaje destino · Esc para cancelar";
        AvisoModoRelacion.Visibility = Visibility.Visible;
        _cursorMapaPrevio = VisorMapa.Cursor;
        VisorMapa.Cursor = Cursors.Cross;
    }

    private void CancelarModoRelacion()
    {
        QuitarResaltadoOrigen();
        _origenPendiente = null;
        AvisoModoRelacion.Visibility = Visibility.Collapsed;
        VisorMapa.Cursor = _cursorMapaPrevio;
    }

    private void CompletarRelacionDesdeMapa(Personaje destino)
    {
        var vm = VM;
        var origen = _origenPendiente;
        if (vm is null || origen is null)
        {
            CancelarModoRelacion();
            return;
        }

        vm.PersonajeSeleccionado = destino;
        var dialogo = new DialogoRelacion(vm.Personajes, origen, destino, fijarPersonajes: true) { Owner = this };
        if (dialogo.ShowDialog() != true)
        {
            CancelarModoRelacion(); // cerrar el diálogo sin guardar aborta el proceso
            return;
        }

        bool creada = vm.CrearRelacion(
            origen.Id,
            destino.Id,
            dialogo.DescripcionGeneral,
            dialogo.DescripcionOrigenADestino,
            dialogo.DescripcionDestinoAOrigen,
            dialogo.ColorHex);

        if (!creada)
        {
            // La arquitectura solo permite un vínculo por dirección: se sigue pendiente para elegir otro destino.
            MessageBox.Show(this, "Ya existe una relación en esa dirección (origen → destino). Elige otro destino o pulsa Esc para cancelar.",
                "Scriptorum", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        CancelarModoRelacion();
    }

    // Resalta el nodo origen con el borde rojo; ClearValue restaura el estilo original.
    private void ResaltarOrigen(Personaje origen)
    {
        var borde = BuscarBordeNodo(origen);
        if (borde is null)
            return;
        borde.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D64545"));
        borde.BorderThickness = new Thickness(3);
    }

    private void QuitarResaltadoOrigen()
    {
        if (_origenPendiente is null)
            return;
        var borde = BuscarBordeNodo(_origenPendiente);
        borde?.ClearValue(Border.BorderBrushProperty);
        borde?.ClearValue(Border.BorderThicknessProperty);
    }

    private Border? BuscarBordeNodo(Personaje personaje)
    {
        if (MapaItems.ItemContainerGenerator.ContainerFromItem(personaje) is not DependencyObject contenedor)
            return null;
        return BuscarBordeDescendiente(contenedor);
    }

    private static Border? BuscarBordeDescendiente(DependencyObject raiz)
    {
        int hijos = VisualTreeHelper.GetChildrenCount(raiz);
        for (int i = 0; i < hijos; i++)
        {
            var hijo = VisualTreeHelper.GetChild(raiz, i);
            if (hijo is Border borde && borde.DataContext is Personaje)
                return borde;
            var nieto = BuscarBordeDescendiente(hijo);
            if (nieto != null)
                return nieto;
        }
        return null;
    }

    // Escape cancela el modo pendiente sin crear nada.
    private void Ventana_Tecla(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _origenPendiente != null)
        {
            CancelarModoRelacion();
            e.Handled = true;
        }
    }

    // Clic fuera del mapa cancela el modo pendiente; el clic sigue su curso normal.
    private void Ventana_ClickFueraMapa(object sender, MouseButtonEventArgs e)
    {
        if (_origenPendiente is null)
            return;
        if (e.OriginalSource is DependencyObject origen && EstaDentroDe(origen, MarcoMapa))
            return;
        CancelarModoRelacion();
    }

    private static bool EstaDentroDe(DependencyObject objeto, DependencyObject ancestro)
    {
        DependencyObject? actual = objeto;
        while (actual != null)
        {
            if (actual == ancestro)
                return true;
            actual = PadreSeguro(actual);
        }
        return false;
    }

    // ---- Arrastrar personajes entre grupos (lista lateral) ----
    // El clic simple sigue seleccionando; solo hay arrastre tras superar el umbral.

    private void ListaPersonajes_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _arrastreListaPosible = false;
        _personajeArrastre = null;
        if (e.LeftButton != MouseButtonState.Pressed)
            return;
        var item = EncontrarAncestro<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is Personaje p)
        {
            _arrastreListaPosible = true;
            _personajeArrastre = p;
            _arrastreInicio = e.GetPosition(ListaPersonajes);
        }
    }

    private void ListaPersonajes_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_arrastreListaPosible || _personajeArrastre is null || e.LeftButton != MouseButtonState.Pressed)
            return;
        var pos = e.GetPosition(ListaPersonajes);
        if (Math.Abs(pos.X - _arrastreInicio.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _arrastreInicio.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        _arrastreListaPosible = false;
        DragDrop.DoDragDrop(ListaPersonajes, _personajeArrastre, DragDropEffects.Move);
        _personajeArrastre = null;
    }

    private void ListaPersonajes_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _arrastreListaPosible = false;
        _personajeArrastre = null;
    }

    private void Grupo_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(Personaje)) is Personaje)
        {
            e.Effects = DragDropEffects.Move;
            if (sender is GroupItem g)
                g.Background = new SolidColorBrush(Color.FromRgb(0x45, 0x47, 0x5E));
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void Grupo_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is GroupItem g)
            g.ClearValue(Control.BackgroundProperty);
        e.Handled = true;
    }

    private void Grupo_Drop(object sender, DragEventArgs e)
    {
        if (sender is GroupItem g)
            g.ClearValue(Control.BackgroundProperty);
        if (e.Data.GetData(typeof(Personaje)) is not Personaje p || VM is null)
            return;
        if ((sender as GroupItem)?.DataContext is not CollectionViewGroup grupo)
            return;
        VM.MoverAGrupo(p, grupo.Name as string ?? string.Empty);
        e.Handled = true;
    }

    // Zona vacía de la lista (fuera de cualquier grupo): saca al personaje del grupo.
    // Los drops sobre un grupo los procesa Grupo_Drop (Handled) y no llegan aquí.
    private void ListaPersonajes_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetData(typeof(Personaje)) is Personaje
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void ListaPersonajes_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(Personaje)) is not Personaje p || VM is null)
            return;
        VM.MoverAGrupo(p, "Sin grupo");
        e.Handled = true;
    }

    private static T? EncontrarAncestro<T>(DependencyObject? actual) where T : DependencyObject
    {
        while (actual != null)
        {
            if (actual is T coincidencia)
                return coincidencia;
            actual = PadreSeguro(actual);
        }
        return null;
    }

    // VisualTreeHelper.GetParent lanza InvalidOperationException con elementos
    // de contenido (ej. Run): solo se usa con Visual/Visual3D, el resto va por el árbol lógico.
    private static DependencyObject? PadreSeguro(DependencyObject actual)
        => (actual is Visual || actual is Visual3D)
            ? VisualTreeHelper.GetParent(actual)
            : LogicalTreeHelper.GetParent(actual);

    // ---- Proyectos: crear, editar, eliminar ----
    // El cambio de proyecto llega por el ComboBox (ProyectoActual):
    // el ViewModel limpia selecciones y recarga datos filtrados.

    private void NuevoProyecto_Click(object sender, RoutedEventArgs e)
    {
        var vm = VM;
        if (vm is null)
            return;
        var dialogo = new DialogoProyecto { Owner = this };
        if (dialogo.ShowDialog() != true)
            return;
        vm.CrearProyecto(dialogo.NombreProyecto, dialogo.DescripcionProyecto);
    }

    private void EditarProyecto_Click(object sender, RoutedEventArgs e)
    {
        var vm = VM;
        if (vm?.ProyectoActual is null)
            return;
        var dialogo = new DialogoProyecto(vm.ProyectoActual) { Owner = this };
        if (dialogo.ShowDialog() != true)
            return;
        vm.RenombrarProyectoActual(dialogo.NombreProyecto, dialogo.DescripcionProyecto);
    }

    private void EliminarProyecto_Click(object sender, RoutedEventArgs e)
    {
        VM?.EliminarProyectoActual();
    }

    // ---- Notas: editor integrado con autoguardado (800 ms tras dejar de escribir) ----

    // Guarda lo pendiente de la nota anterior y se suscribe a la nueva.
    // Se relee la selección tras guardar porque el guardado puede cambiarla
    // (un borrador vacío descartado deja NotaSeleccionada a null).
    private void SincronizarNotaEditando()
    {
        if (_sincronizandoNota)
            return;
        _sincronizandoNota = true;
        try
        {
            _temporizadorNotas.Stop();
            if (_notaEditando != null)
            {
                VM?.GuardarNotaEditada(_notaEditando);
                _notaEditando.PropertyChanged -= NotaEditando_Cambiada;
                _notaEditando = null;
            }
            _notaEditando = VM?.NotaSeleccionada;
            if (_notaEditando != null)
                _notaEditando.PropertyChanged += NotaEditando_Cambiada;
            // Al cambiar de nota el documento visible debe ser el de la nueva
            // (aquí el salto del cursor es lo esperado).
            CargarDocumento(_notaEditando);
        }
        finally
        {
            _sincronizandoNota = false;
        }
    }

    // Vuelca el documento visible a la entidad y lo guarda. Si el guardado
    // revirtió la nota (se había vaciado del todo), recarga el documento
    // para que lo visible coincida con lo guardado.
    private bool VolcarNotaEditando()
    {
        _temporizadorNotas.Stop();
        if (_notaEditando is null || VM is null)
            return false;
        SincronizarTextoDesdeDocumento();
        bool revertida = VM.GuardarNotaEditada(_notaEditando);
        if (revertida)
            CargarDocumento(_notaEditando);
        return revertida;
    }

    // Los botones ejecutan el comando directamente sobre el editor: funcionan
    // aunque el foco esté en el propio botón, así el formato se activa Y se
    // desactiva con la misma pulsación. Tras pulsar, el foco vuelve al texto.
    private void FormatoNegrita_Click(object sender, RoutedEventArgs e)
    {
        EditingCommands.ToggleBold.Execute(null, CajaTexto);
        CajaTexto.Focus();
        SincronizarBotonesFormato();
    }

    private void FormatoCursiva_Click(object sender, RoutedEventArgs e)
    {
        EditingCommands.ToggleItalic.Execute(null, CajaTexto);
        CajaTexto.Focus();
        SincronizarBotonesFormato();
    }

    // Cada botón refleja el formato bajo el cursor (también al moverse por el
    // texto o al usar Ctrl+B / Ctrl+I desde el teclado).
    private void CajaTexto_SelectionChanged(object sender, RoutedEventArgs e) => SincronizarBotonesFormato();

    private void SincronizarBotonesFormato()
    {
        BtnNegrita.IsChecked = Equals(CajaTexto.Selection.GetPropertyValue(TextElement.FontWeightProperty), FontWeights.Bold);
        BtnCursiva.IsChecked = Equals(CajaTexto.Selection.GetPropertyValue(TextElement.FontStyleProperty), FontStyles.Italic);
    }

    // El documento es la fuente de verdad visual; la entidad solo recibe
    // su serialización XAML (o texto plano heredado al abrir notas antiguas).
    private void CargarDocumento(Nota? nota)
    {
        _cargandoDocumento = true;
        try
        {
            if (nota is null)
            {
                CajaTexto.Document = new FlowDocument();
                return;
            }
            if (MainViewModel.EsDocumentoXaml(nota.Texto))
            {
                try
                {
                    if (XamlReader.Parse(nota.Texto) is FlowDocument doc)
                    {
                        CajaTexto.Document = doc;
                        return;
                    }
                }
                catch
                {
                    // XAML dañado: se muestra como texto plano.
                }
            }
            CajaTexto.Document = new FlowDocument(new Paragraph(new Run(nota.Texto ?? string.Empty)));
        }
        finally
        {
            _cargandoDocumento = false;
        }
    }

    private void CajaTexto_TextChanged(object sender, TextChangedEventArgs e) => SincronizarTextoDesdeDocumento();

    // El documento manda: se vuelca a la entidad antes de cada guardado para
    // no depender de que cada cambio de formato haya disparado TextChanged.
    private void SincronizarTextoDesdeDocumento()
    {
        if (_cargandoDocumento || _notaEditando is null)
            return;
        // La asignación avisa a NotaEditando_Cambiada, que rearma el autoguardado.
        _notaEditando.Texto = XamlWriter.Save(CajaTexto.Document);
    }

    private void NotaEditando_Cambiada(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Nota.Titulo) && e.PropertyName != nameof(Nota.Texto))
            return;
        VM?.MarcarNotaEditando();
        _temporizadorNotas.Stop();
        _temporizadorNotas.Start();
    }

    private void NuevaNota_Click(object sender, RoutedEventArgs e)
    {
        VM?.NuevaNotaBorrador();
    }

    private void EliminarNota_Click(object sender, RoutedEventArgs e)
    {
        _temporizadorNotas.Stop();
        VM?.EliminarNotaSeleccionada();
    }

    private void EliminarLineaTiempo_Click(object sender, RoutedEventArgs e)
    {
        VM?.EliminarLineaTiempo();
    }

    // Cambiar de pestaña o cerrar la app vuelca lo escrito para no perderlo.
    // El guard solo reacciona al cambio de pestaña propio: los SelectionChanged
    // de listas internas (p. ej. la de notas) burbujean hasta aquí y deben ignorarse,
    // o un borrador recién creado se descartaría al instante por estar vacío.
    private void Pestanas_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, Pestanas))
            return;
        VolcarNotaEditando();
    }

    private void Ventana_Closing(object? sender, CancelEventArgs e)
    {
        VolcarNotaEditando();
    }

    // ---- Cronología: carriles con tarjetas ordenadas ----

    private const double AnchoTarjeta = 170;
    private const double PasoTarjeta = 180; // tarjeta + margen derecho

    private bool _arrastreTarjetaPosible;
    private Cronologia? _tarjetaArrastre;
    private Point _arrastreTarjetaInicio;

    private void NuevaLinea_Click(object sender, RoutedEventArgs e)
    {
        var vm = VM;
        if (vm is null)
            return;
        var dialogo = new DialogoLineaTiempo { Owner = this };
        if (dialogo.ShowDialog() != true)
            return;
        vm.CrearLinea(dialogo.NombreLinea);
        RedibujarCronologia();
    }

    // Doble clic en el nombre del carril para renombrarlo.
    private void NombreLinea_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var vm = VM;
        if (sender is not FrameworkElement elemento || elemento.Tag is not LineaTiempo linea || vm is null)
            return;
        if (e.ClickCount < 2)
            return;
        var dialogo = new DialogoLineaTiempo(linea) { Owner = this };
        if (dialogo.ShowDialog() != true)
            return;
        vm.RenombrarLinea(linea, dialogo.NombreLinea);
        RedibujarCronologia();
        e.Handled = true;
    }

    // Botón ＋ al final de un carril: añade evento en ese carril (Tag = LineaId).
    private void NuevoEventoEnLinea_Click(object sender, RoutedEventArgs e)
    {
        var vm = VM;
        if (vm is null || sender is not Button boton || boton.Tag is not int lineaId)
            return;
        var dialogo = new DialogoCronologia(vm.Personajes) { Owner = this };
        if (dialogo.ShowDialog() != true)
            return;
        vm.CrearEvento(dialogo.TituloEvento, dialogo.DescripcionEvento,
            dialogo.PersonajeSeleccionado?.Id, dialogo.TipoEvento, lineaId);
        RedibujarCronologia();
    }

    private void EditarEvento_Click(object sender, RoutedEventArgs e)
    {
        var vm = VM;
        if (vm?.CronologiaSeleccionada is null)
        {
            MessageBox.Show(this, "Selecciona un evento primero (clic en su tarjeta).",
                "Scriptorum", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        AbrirEdicionEvento(vm, vm.CronologiaSeleccionada);
    }

    private void EliminarEvento_Click(object sender, RoutedEventArgs e)
    {
        var vm = VM;
        if (vm?.CronologiaSeleccionada is null)
        {
            MessageBox.Show(this, "Selecciona un evento primero (clic en su tarjeta).",
                "Scriptorum", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        vm.EliminarEventoSeleccionado();
        RedibujarCronologia();
    }

    private void AbrirEdicionEvento(MainViewModel vm, Cronologia evento)
    {
        var dialogo = new DialogoCronologia(vm.Personajes, evento) { Owner = this };
        if (dialogo.ShowDialog() != true)
            return;
        vm.ActualizarEvento(evento, dialogo.TituloEvento,
            dialogo.DescripcionEvento,
            dialogo.PersonajeSeleccionado?.Id, dialogo.TipoEvento);
        RedibujarCronologia();
    }

    // Arrastre de tarjetas para ordenar (mismo patrón que la lista de personajes):
    // el clic simple selecciona; el arrastre empieza tras superar el umbral.
    private void Tarjeta_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var vm = VM;
        _arrastreTarjetaPosible = false;
        _tarjetaArrastre = null;
        if (sender is not FrameworkElement elemento || elemento.Tag is not Cronologia evento || vm is null)
            return;

        // Si el evento tiene una nota asociada, seleccionarla y cambiar a la pestaña Notas.
        if (evento.NotaId.HasValue)
        {
            var nota = vm.Notas.FirstOrDefault(n => n.Id == evento.NotaId.Value);
            if (nota != null)
            {
                vm.NotaSeleccionada = nota;
                if (Pestanas.SelectedIndex != 2)
                    Pestanas.SelectedIndex = 2;
            }
        }

        vm.CronologiaSeleccionada = evento;
        // Border no tiene evento de doble clic: se detecta por conteo.
        if (e.ClickCount >= 2)
        {
            AbrirEdicionEvento(vm, evento);
            e.Handled = true;
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            _arrastreTarjetaPosible = true;
            _tarjetaArrastre = evento;
            _arrastreTarjetaInicio = e.GetPosition(elemento);
        }
        e.Handled = true;
    }

    private void Tarjeta_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_arrastreTarjetaPosible || _tarjetaArrastre is null || e.LeftButton != MouseButtonState.Pressed)
            return;
        if (sender is not FrameworkElement elemento)
            return;
        var pos = e.GetPosition(elemento);
        if (Math.Abs(pos.X - _arrastreTarjetaInicio.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _arrastreTarjetaInicio.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        _arrastreTarjetaPosible = false;
        DragDrop.DoDragDrop(PilaLineas, _tarjetaArrastre, DragDropEffects.Move);
        _tarjetaArrastre = null;
    }

    private void Tarjeta_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _arrastreTarjetaPosible = false;
        _tarjetaArrastre = null;
    }

    // Soltado sobre la tira de un carril: calcula el índice por posición X.
    private void PistaLinea_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(Cronologia)) is Cronologia)
        {
            e.Effects = DragDropEffects.Move;
            if (sender is StackPanel tira)
                tira.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EFE9DC"));
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void PistaLinea_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is StackPanel tira)
            tira.ClearValue(StackPanel.BackgroundProperty);
        e.Handled = true;
    }

    private void PistaLinea_Drop(object sender, DragEventArgs e)
    {
        if (sender is StackPanel tira)
            tira.ClearValue(StackPanel.BackgroundProperty);
        if (e.Data.GetData(typeof(Cronologia)) is not Cronologia evento || VM is null)
            return;
        if (sender is not StackPanel pista || pista.Tag is not int lineaId)
            return;
        int indice = (int)(e.GetPosition(pista).X / PasoTarjeta);
        VM.MoverEvento(evento, lineaId, indice);
        RedibujarCronologia();
        e.Handled = true;
    }

    private void RedibujarCronologia()
    {
        var vm = VM;
        if (vm is null || PilaLineas is null)
            return;

        PilaLineas.Children.Clear();

        if (vm.Lineas.Count == 0)
        {
            var vacio = new TextBlock
            {
                Text = "Sin líneas de tiempo. Pulsa «＋ Nueva línea de tiempo» para crear la primera.",
                FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8A8578")),
                Margin = new Thickness(8)
            };
            PilaLineas.Children.Add(vacio);
            return;
        }

        foreach (var linea in vm.Lineas)
            PilaLineas.Children.Add(CrearCarril(vm, linea));
    }

    // Carril estilo referencia: columna con nombre + tira horizontal de tarjetas.
    private Border CrearCarril(MainViewModel vm, LineaTiempo linea)
    {
        var eventos = vm.Cronologias.Where(e => e.LineaId == linea.Id).ToList();

        var carril = new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(10),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D8D2C4")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 12)
        };
        carril.Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 2, Opacity = 0.2, Color = Colors.Gray };

        var rejilla = new Grid();
        rejilla.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        rejilla.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var cabecera = new StackPanel { Tag = linea, Cursor = Cursors.Hand, ToolTip = "Doble clic para renombrar" };
        cabecera.PreviewMouseLeftButtonDown += NombreLinea_MouseDown;
        cabecera.Children.Add(new TextBlock
        {
            Text = "⋮⋮",
            FontSize = 12,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B8B4A8")),
            Margin = new Thickness(0, 0, 0, 2)
        });
        cabecera.Children.Add(new TextBlock
        {
            Text = linea.Nombre,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2B2D42")),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        });
        cabecera.Children.Add(new TextBlock
        {
            Text = eventos.Count == 1 ? "1 evento" : $"{eventos.Count} eventos",
            FontSize = 11,
            FontStyle = FontStyles.Italic,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8A8578"))
        });
        Grid.SetColumn(cabecera, 0);
        rejilla.Children.Add(cabecera);

        var tira = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Background = Brushes.Transparent,
            Tag = linea.Id,
            AllowDrop = true
        };
        tira.DragOver += PistaLinea_DragOver;
        tira.DragLeave += PistaLinea_DragLeave;
        tira.Drop += PistaLinea_Drop;

        int numero = 1;
        foreach (var ev in eventos)
        {
            tira.Children.Add(CrearTarjeta(vm, ev, numero));
            numero++;
        }

        var mas = new Button
        {
            Content = "+",
            Width = 40,
            Height = 40,
            Margin = new Thickness(0, 30, 0, 0),
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D8D2C4")),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8A8578")),
            FontSize = 18,
            Cursor = Cursors.Hand,
            Tag = linea.Id,
            ToolTip = "Añadir evento a esta línea"
        };
        mas.Click += NuevoEventoEnLinea_Click;
        tira.Children.Add(mas);

        var visor = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = tira
        };
        Grid.SetColumn(visor, 1);
        rejilla.Children.Add(visor);

        carril.Child = rejilla;
        return carril;
    }

    // Tarjeta de evento dentro de su carril.
    private Border CrearTarjeta(MainViewModel vm, Cronologia ev, int numero)
    {
        var (fondoHex, acentoHex) = TiposCronologia.Colores(ev.Tipo);
        var fondo = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fondoHex));
        var acento = new SolidColorBrush((Color)ColorConverter.ConvertFromString(acentoHex));
        bool seleccionada = vm.CronologiaSeleccionada == ev;

        var tarjeta = new Border
        {
            Width = AnchoTarjeta,
            MinHeight = 110,
            CornerRadius = new CornerRadius(8),
            Background = fondo,
            BorderBrush = seleccionada ? acento : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D8D2C4")),
            BorderThickness = seleccionada ? new Thickness(3) : new Thickness(4, 1, 1, 1),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 10, 0),
            Cursor = Cursors.Hand,
            Tag = ev
        };
        tarjeta.Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Opacity = 0.2, Color = Colors.Gray };

        string tooltip = ev.Titulo;
        tooltip += "\n" + (ev.PersonajeId is null ? "(Sin personaje)" : vm.NombrePersonaje(ev.PersonajeId.Value));
        if (!string.IsNullOrWhiteSpace(ev.Descripcion))
            tooltip += "\n" + ev.Descripcion;
        tarjeta.ToolTip = tooltip;

        tarjeta.PreviewMouseLeftButtonDown += Tarjeta_MouseLeftButtonDown;
        tarjeta.PreviewMouseMove += Tarjeta_MouseMove;
        tarjeta.PreviewMouseLeftButtonUp += Tarjeta_MouseUp;

        var cabecera = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
        cabecera.Children.Add(new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            Background = Brushes.White,
            BorderBrush = acento,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(0, 0, 6, 0),
            Child = new TextBlock
            {
                Text = numero.ToString(),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = acento,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        });
        cabecera.Children.Add(new TextBlock
        {
            Text = ev.Tipo,
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = acento,
            VerticalAlignment = VerticalAlignment.Center
        });

        var contenido = new StackPanel();
        contenido.Children.Add(cabecera);
        contenido.Children.Add(new TextBlock
        {
            Text = ev.Titulo,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2B2D42")),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 2)
        });
        if (!string.IsNullOrWhiteSpace(ev.Descripcion))
        {
            contenido.Children.Add(new TextBlock
            {
                Text = ev.Descripcion,
                FontSize = 11,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B675C")),
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 60,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }
            tarjeta.Child = contenido;
        return tarjeta;
    }

    // ---- Relaciones: crear, dibujar, seleccionar ----

    private void CrearRelacion_Click(object sender, RoutedEventArgs e)
    {
        CancelarModoRelacion(); // evita mezclar el menú con el modo del mapa
        var vm = VM;
        if (vm is null)
            return;
        if (vm.Personajes.Count < 2)
        {
            MessageBox.Show(this, "Necesitas al menos dos personajes para crear una relación.",
                "Scriptorum", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialogo = new DialogoRelacion(vm.Personajes, vm.PersonajeSeleccionado) { Owner = this };
        if (dialogo.ShowDialog() != true)
            return;
        if (dialogo.OrigenSeleccionado is null || dialogo.DestinoSeleccionado is null)
            return;

        bool creada = vm.CrearRelacion(
            dialogo.OrigenSeleccionado.Id,
            dialogo.DestinoSeleccionado.Id,
            dialogo.DescripcionGeneral,
            dialogo.DescripcionOrigenADestino,
            dialogo.DescripcionDestinoAOrigen,
            dialogo.ColorHex);

        if (!creada)
            MessageBox.Show(this, "Ya existe una relación en esa dirección (A → B).",
                "Scriptorum", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void Linea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement elemento && elemento.Tag is Relacion relacion && VM != null)
        {
            VM.RelacionSeleccionada = relacion;
            e.Handled = true;
        }
    }

    private void RedibujarRelaciones()
    {
        var vm = VM;
        if (vm is null || LienzoRelaciones is null)
            return;

        LienzoRelaciones.Children.Clear();

        foreach (var relacion in vm.Relaciones)
        {
            var origen = vm.BuscarPersonaje(relacion.PersonajeOrigenId);
            var destino = vm.BuscarPersonaje(relacion.PersonajeDestinoId);
            if (origen is null || destino is null)
                continue;

            double x1 = origen.PosX + AnchoNodo / 2;
            double y1 = origen.PosY + AltoNodo / 2;
            double x2 = destino.PosX + AnchoNodo / 2;
            double y2 = destino.PosY + AltoNodo / 2;

            double dx = x2 - x1;
            double dy = y2 - y1;
            double longitud = Math.Sqrt(dx * dx + dy * dy);
            if (longitud < 1)
                continue;

            bool seleccionada = vm.RelacionSeleccionada == relacion;
            Brush pincel = ColoresRelacion.PincelDeHex(relacion.Color);

            var linea = new Line
            {
                X1 = x1,
                Y1 = y1,
                X2 = x2,
                Y2 = y2,
                Stroke = pincel,
                StrokeThickness = seleccionada ? 4 : 2.5,
                Tag = relacion,
                Cursor = Cursors.Hand,
                ToolTip = string.IsNullOrWhiteSpace(relacion.DescripcionGeneral)
                    ? $"{vm.NombrePersonaje(relacion.PersonajeOrigenId)} → {vm.NombrePersonaje(relacion.PersonajeDestinoId)}"
                    : relacion.DescripcionGeneral
            };
            linea.PreviewMouseLeftButtonDown += Linea_MouseLeftButtonDown;
            LienzoRelaciones.Children.Add(linea);

            // Punta de flecha orientada hacia el destino, retranqueada para no quedar bajo el nodo.
            double ux = dx / longitud;
            double uy = dy / longitud;
            const double retroceso = 48;
            const double largoFlecha = 14;
            const double anchoFlecha = 7;

            double tx = x2 - ux * retroceso;
            double ty = y2 - uy * retroceso;
            double bx = tx - ux * largoFlecha;
            double by = ty - uy * largoFlecha;
            double px = -uy;
            double py = ux;

            var flecha = new Polygon
            {
                Points = new PointCollection
                {
                    new(tx, ty),
                    new(bx + px * anchoFlecha, by + py * anchoFlecha),
                    new(bx - px * anchoFlecha, by - py * anchoFlecha)
                },
                Fill = pincel,
                Tag = relacion,
                Cursor = Cursors.Hand,
                ToolTip = linea.ToolTip
            };
            flecha.PreviewMouseLeftButtonDown += Linea_MouseLeftButtonDown;
            LienzoRelaciones.Children.Add(flecha);
        }
    }
}
