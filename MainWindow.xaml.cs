using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

    public MainWindow()
    {
        InitializeComponent();
        var vm = new MainViewModel();
        DataContext = vm;

        vm.CambiosMapa += RedibujarRelaciones;
        vm.Relaciones.CollectionChanged += (_, _) => RedibujarRelaciones();
        vm.Personajes.CollectionChanged += Personajes_CollectionChanged;
        vm.PropertyChanged += Vm_PropertyChanged;

        foreach (var p in vm.Personajes)
            p.PropertyChanged += Personaje_PropertyChanged;

        RedibujarRelaciones();
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
            actual = VisualTreeHelper.GetParent(actual) ?? LogicalTreeHelper.GetParent(actual);
        }
        return false;
    }

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
