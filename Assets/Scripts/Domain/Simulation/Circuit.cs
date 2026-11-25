using System;
using System.Collections.Generic;
using System.Linq;
using ImatPiSim.Domain.Components;
using ImatPiSim.Domain.Electrical;
using ImatPiSim.Domain.Wiring;
using ImatPiSim.Domain.Wiring.Routing;

namespace ImatPiSim.Domain.Simulation
{
    /// <summary>
    /// El montaje completo: la Raspberry Pi, la IMAT HAT (opcional), la protoboard (opcional), los
    /// componentes externos y los cables. Sabe convertirse en una <see cref="Netlist"/>.
    /// </summary>
    /// <remarks>
    /// <para>Cada cambio de estructura (añadir un componente o un cable) incrementa <see cref="Version"/>;
    /// el motor lo usa para saber cuándo reconstruir la netlist. Los cambios de estado (pulsar un
    /// botón) no cambian la estructura: solo obligan a volver a resolver.</para>
    /// <para>Hay dos formas de unir pines:</para>
    /// <list type="bullet">
    /// <item><b>Montaje</b> (<see cref="Place"/>, <see cref="AddWire"/>): con las reglas de una placa
    /// real (una patilla por agujero, un cable por pin de la HAT, separación fija entre patillas).
    /// Es lo que usa el juego.</item>
    /// <item><b>Conexión directa</b> (<see cref="Connect"/>): une dos pines cualesquiera sin
    /// reglas. Sirve para pruebas de componentes sueltos.</item>
    /// </list>
    /// </remarks>
    public sealed class Circuit
    {
        private readonly List<Component> _extras = new List<Component>();
        private readonly List<(Pin a, Pin b)> _connections = new List<(Pin, Pin)>();
        private readonly List<Placement> _placements = new List<Placement>();
        private readonly List<Wire> _wires = new List<Wire>();
        private readonly Dictionary<Pin, Wire> _wiredPins = new Dictionary<Pin, Wire>();
        private int _nextWire = 1;

        public Circuit(RaspberryPi pi, ImatHat hat = null, Breadboard breadboard = null)
        {
            Pi = pi ?? throw new ArgumentNullException(nameof(pi));
            if (hat != null && hat.Pi != pi) throw new ArgumentException("La HAT debe estar montada sobre esta Raspberry Pi.", nameof(hat));
            Hat = hat;
            Breadboard = breadboard;
        }

        /// <summary>Montaje habitual: una Raspberry Pi con la IMAT HAT encima y una protoboard al lado.</summary>
        public static Circuit WithImatHat()
        {
            var pi = new RaspberryPi();
            return new Circuit(pi, new ImatHat(pi), new Breadboard());
        }

        public RaspberryPi Pi { get; }
        public ImatHat Hat { get; }
        public Breadboard Breadboard { get; }
        public IReadOnlyList<Component> ExternalComponents => _extras;

        /// <summary>Conexiones directas (<see cref="Connect"/>), sin contar el montaje.</summary>
        public IReadOnlyList<(Pin a, Pin b)> DirectConnections => _connections;

        public IReadOnlyList<Placement> Placements => _placements;
        public IReadOnlyList<Wire> Wires => _wires;
        public int Version { get; private set; }

        /// <summary>Componentes de primer nivel: la Pi, la HAT, la protoboard y los externos.</summary>
        public IEnumerable<Component> Components
        {
            get
            {
                yield return Pi;
                if (Hat != null) yield return Hat;
                if (Breadboard != null) yield return Breadboard;
                foreach (var c in _extras) yield return c;
            }
        }

        /// <summary>Todos los componentes, incluidos los hijos de los compuestos (LEDs de la HAT...).</summary>
        public IEnumerable<Component> AllComponents => Components.SelectMany(Flatten);

        private static IEnumerable<Component> Flatten(Component c)
        {
            yield return c;
            if (c is CompositeComponent composite)
                foreach (var child in composite.Children.SelectMany(Flatten)) yield return child;
        }

        public T Add<T>(T component) where T : Component
        {
            if (component == null) throw new ArgumentNullException(nameof(component));
            if (Components.Contains(component)) throw new ArgumentException($"{component.Id} ya está en el circuito.");
            if (Components.Any(c => c.Id == component.Id)) throw new ArgumentException($"Ya hay un componente con el id {component.Id}.");
            _extras.Add(component);
            Version++;
            return component;
        }

        /// <summary>Quita un componente externo con todo lo suyo: sus agujeros, sus cables y sus conexiones.</summary>
        public void Remove(Component component)
        {
            if (!_extras.Remove(component)) return;
            var placement = PlacementOf(component);
            if (placement != null)
            {
                foreach (var hole in placement.Holes) Breadboard.Release(hole);
                _placements.Remove(placement);
            }
            foreach (var wire in _wires.Where(w => w.A.Owner == component || w.B.Owner == component).ToList())
                RemoveWire(wire);
            _connections.RemoveAll(w => w.a.Owner == component || w.b.Owner == component);
            Version++;
        }

        // ------------------------------------------------------------------ montaje

        /// <summary>Dónde está pinchado un componente, o <c>null</c>.</summary>
        public Placement PlacementOf(Component component) => _placements.FirstOrDefault(p => p.Component == component);

        /// <summary>
        /// Añade un componente pinchándolo en la protoboard: la primera patilla en
        /// <paramref name="start"/> y las demás en línea en la dirección dada, separadas
        /// <see cref="ComponentType.LeadSpacing"/> agujeros. La dirección decide la polaridad:
        /// un LED pinchado en a10 hacia la derecha tiene el ánodo en a10 y el cátodo en a11.
        /// </summary>
        /// <exception cref="AssemblyException">No hay protoboard, el componente no se pincha, alguna patilla cae fuera o en un agujero ocupado.</exception>
        public Placement Place(Component component, HoleId start, Direction direction)
        {
            if (component == null) throw new ArgumentNullException(nameof(component));
            if (Components.Contains(component)) throw new AssemblyException($"{component.Id} ya está montado.");
            if (!CanPlace(component.Type, start, direction, null, out string reason)) throw new AssemblyException(reason);

            var holes = Placement.HolesFor(component.Type, start, direction);
            Add(component);
            for (int i = 0; i < holes.Length; i++) Breadboard.Occupy(holes[i], component.Pins[i]);
            var placement = new Placement(component, start, direction, holes);
            _placements.Add(placement);
            return placement;
        }

        /// <summary>
        /// Si un componente del tipo dado cabe en la protoboard empezando en <paramref name="start"/>
        /// y en la dirección dada. No cambia nada: la interfaz lo usa para colorear la vista previa.
        /// </summary>
        /// <param name="ignore">Componente cuyos agujeros cuentan como libres (el que se está moviendo).</param>
        /// <param name="reason">Por qué no cabe (para mostrarlo al jugador), o <c>null</c>.</param>
        public bool CanPlace(ComponentType type, HoleId start, Direction direction, Component ignore, out string reason)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            reason = null;
            if (Breadboard == null) reason = "Este montaje no tiene protoboard.";
            else if (!type.MountsOnBreadboard) reason = $"{type.DisplayName} no se pincha en la protoboard.";
            else
            {
                var holes = Placement.HolesFor(type, start, direction);
                for (int i = 0; i < holes.Length && reason == null; i++)
                {
                    if (!Breadboard.Contains(holes[i]))
                        reason = $"La patilla {type.PinNames[i]} de {type.DisplayName} quedaría fuera de la protoboard.";
                    else
                    {
                        var occupant = Breadboard.Occupant(holes[i]);
                        bool own = ignore != null && occupant is Pin pin && pin.Owner == ignore;
                        if (occupant != null && !own)
                            reason = $"El agujero {holes[i]} ya está ocupado por {Breadboard.Describe(occupant)}.";
                    }
                }
            }
            return reason == null;
        }

        /// <summary>Mueve (o gira) un componente pinchado a otra posición. Si no cabe, no cambia nada.</summary>
        /// <exception cref="AssemblyException">El componente no está pinchado o no cabe en la nueva posición.</exception>
        public Placement Move(Component component, HoleId start, Direction direction)
        {
            var old = PlacementOf(component) ?? throw new AssemblyException($"{component?.Id} no está pinchado en la protoboard.");
            if (!CanPlace(component.Type, start, direction, component, out string reason)) throw new AssemblyException(reason);

            foreach (var hole in old.Holes) Breadboard.Release(hole);
            var holes = Placement.HolesFor(component.Type, start, direction);
            for (int i = 0; i < holes.Length; i++) Breadboard.Occupy(holes[i], component.Pins[i]);
            var placement = new Placement(component, start, direction, holes);
            _placements[_placements.IndexOf(old)] = placement;
            Version++;
            return placement;
        }

        public Placement Place(Component component, string start, Direction direction) =>
            Place(component, HoleId.Parse(start), direction);

        /// <summary>
        /// Tiende un cable entre dos puntos de conexión: agujeros de la protoboard, pines de los
        /// conectores de la HAT o patillas de componentes que no se pinchan (el servo).
        /// </summary>
        /// <param name="route">Recorrido por la protoboard, de A a B (opcional; ver <see cref="RouteWire"/>).</param>
        /// <exception cref="AssemblyException">Algún extremo no admite cables o ya tiene uno, o el recorrido no es válido.</exception>
        public Wire AddWire(Pin a, Pin b, IReadOnlyList<HoleId> route = null)
        {
            if (a == null || b == null) throw new ArgumentNullException(a == null ? nameof(a) : nameof(b));
            if (a == b) throw new AssemblyException("Los dos extremos del cable son el mismo punto.");
            CheckWireEnd(a);
            CheckWireEnd(b);
            if (route != null && route.Count > 0) CheckRoute(a, b, route);

            var wire = new Wire("W" + _nextWire++, a, b, route);
            _wires.Add(wire);
            Occupy(a, wire);
            Occupy(b, wire);
            Version++;
            return wire;
        }

        /// <summary>Cable entre dos agujeros o pines dados por su nombre: "a10", "T+3", "J7.1"...</summary>
        public Wire AddWire(string a, string b) => AddWire(PinByName(a), PinByName(b));

        /// <summary>
        /// Tiende un cable entre dos agujeros de la protoboard con el recorrido que calcule
        /// <paramref name="router"/> (el A*, normalmente) sobre el estado actual del montaje.
        /// </summary>
        /// <exception cref="AssemblyException">Algún extremo no es un agujero libre, o no hay camino.</exception>
        public Wire RouteWire(string a, string b, IRouter router)
        {
            if (router == null) throw new ArgumentNullException(nameof(router));
            var pa = PinByName(a);
            var pb = PinByName(b);
            var ha = Breadboard?.HoleOf(pa);
            var hb = Breadboard?.HoleOf(pb);
            if (!ha.HasValue || !hb.HasValue)
                throw new AssemblyException("Solo se trazan cables entre agujeros de la protoboard; a la HAT van al aire.");
            CheckWireEnd(pa);
            CheckWireEnd(pb);

            var route = router.FindRoute(RoutingGrid.FromCircuit(this), Cell.FromHole(ha.Value), Cell.FromHole(hb.Value));
            if (!route.Found) throw new AssemblyException($"No hay camino libre de {a} a {b}.");
            return AddWire(pa, pb, route.Cells.Select(c => c.ToHole()).ToList());
        }

        /// <summary>El recorrido va de un extremo al otro, paso a paso, sin atravesar agujeros ocupados.</summary>
        private void CheckRoute(Pin a, Pin b, IReadOnlyList<HoleId> route)
        {
            var ha = Breadboard?.HoleOf(a);
            var hb = Breadboard?.HoleOf(b);
            if (!ha.HasValue || !hb.HasValue || route[0] != ha.Value || route[route.Count - 1] != hb.Value)
                throw new AssemblyException("El recorrido del cable debe empezar y terminar en sus dos agujeros.");
            for (int i = 1; i < route.Count; i++)
            {
                if (Cell.FromHole(route[i - 1]).ManhattanTo(Cell.FromHole(route[i])) != 1)
                    throw new AssemblyException($"El recorrido salta de {route[i - 1]} a {route[i]}.");
                if (i < route.Count - 1 && Breadboard.Occupant(route[i]) != null)
                    throw new AssemblyException($"El recorrido atraviesa el agujero ocupado {route[i]}.");
            }
        }

        public void RemoveWire(Wire wire)
        {
            if (wire == null || !_wires.Remove(wire)) return;
            foreach (var end in new[] { wire.A, wire.B })
            {
                var hole = Breadboard?.HoleOf(end);
                if (hole.HasValue) Breadboard.Release(hole.Value);
                else _wiredPins.Remove(end);
            }
            Version++;
        }

        /// <summary>
        /// Busca un punto de conexión por su nombre: un agujero de la protoboard ("a10", "T+3") o un
        /// pin de un conector de la HAT ("J7.1", "J12.3", "TP1").
        /// </summary>
        public Pin PinByName(string name)
        {
            if (Breadboard != null && HoleId.TryParse(name, out var hole) && Breadboard.Contains(hole))
                return Breadboard.Hole(hole);
            var pin = Hat?.Pins.FirstOrDefault(p => p.Name == name);
            return pin ?? throw new AssemblyException($"No hay ningún punto de conexión llamado '{name}'.");
        }

        private void CheckWireEnd(Pin end)
        {
            var hole = Breadboard?.HoleOf(end);
            if (hole.HasValue)
            {
                var occupant = Breadboard.Occupant(hole.Value);
                if (occupant != null) throw new AssemblyException($"El agujero {hole} ya está ocupado por {Breadboard.Describe(occupant)}.");
                return;
            }

            bool hatConnector = Hat != null && end.Owner == Hat;
            bool ownLead = _extras.Contains(end.Owner) && !end.Owner.Type.MountsOnBreadboard;
            if (!hatConnector && !ownLead)
                throw new AssemblyException($"No se puede conectar un cable a {end.FullName}: solo a la protoboard, " +
                                            "a los conectores de la HAT o a componentes con cable propio.");
            if (_wiredPins.TryGetValue(end, out var other))
                throw new AssemblyException($"{end.FullName} ya tiene un cable ({other.Id}).");
        }

        private void Occupy(Pin end, Wire wire)
        {
            var hole = Breadboard?.HoleOf(end);
            if (hole.HasValue) Breadboard.Occupy(hole.Value, wire);
            else _wiredPins[end] = wire;
        }

        // ------------------------------------------------------------------ conexión directa

        /// <summary>Une dos pines directamente, sin reglas de montaje (pruebas).</summary>
        public void Connect(Pin a, Pin b)
        {
            if (a == null || b == null) throw new ArgumentNullException(a == null ? nameof(a) : nameof(b));
            _connections.Add((a, b));
            Version++;
        }

        public void Disconnect(Pin a, Pin b)
        {
            if (_connections.RemoveAll(w => (w.a == a && w.b == b) || (w.a == b && w.b == a)) > 0) Version++;
        }

        internal Netlist BuildNetlist()
        {
            var builder = new NetlistBuilder();
            foreach (var c in Components) builder.AddComponent(c);
            foreach (var p in _placements)
                for (int i = 0; i < p.Holes.Count; i++)
                    builder.Connect(p.Component.Pins[i], Breadboard.Hole(p.Holes[i]));
            foreach (var w in _wires) builder.Connect(w.A, w.B);
            foreach (var (a, b) in _connections) builder.Connect(a, b);
            return builder.Build();
        }
    }
}
