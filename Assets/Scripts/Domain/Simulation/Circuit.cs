using System;
using System.Collections.Generic;
using System.Linq;
using ImatPiSim.Domain.Components;
using ImatPiSim.Domain.Electrical;
using ImatPiSim.Domain.Wiring;

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
            if (Breadboard == null) throw new AssemblyException("Este montaje no tiene protoboard.");
            if (!component.Type.MountsOnBreadboard)
                throw new AssemblyException($"{component.Type.DisplayName} no se pincha en la protoboard.");
            if (Components.Contains(component)) throw new AssemblyException($"{component.Id} ya está montado.");

            var holes = Placement.HolesFor(component.Type, start, direction);
            for (int i = 0; i < holes.Length; i++)
            {
                if (!Breadboard.Contains(holes[i]))
                    throw new AssemblyException($"La patilla {component.Pins[i].Name} de {component.Id} quedaría fuera de la protoboard.");
                var occupant = Breadboard.Occupant(holes[i]);
                if (occupant != null)
                    throw new AssemblyException($"El agujero {holes[i]} ya está ocupado por {Breadboard.Describe(occupant)}.");
            }

            Add(component);
            for (int i = 0; i < holes.Length; i++) Breadboard.Occupy(holes[i], component.Pins[i]);
            var placement = new Placement(component, start, direction, holes);
            _placements.Add(placement);
            return placement;
        }

        public Placement Place(Component component, string start, Direction direction) =>
            Place(component, HoleId.Parse(start), direction);

        /// <summary>
        /// Tiende un cable entre dos puntos de conexión: agujeros de la protoboard, pines de los
        /// conectores de la HAT o patillas de componentes que no se pinchan (el servo).
        /// </summary>
        /// <exception cref="AssemblyException">Algún extremo no admite cables o ya tiene uno.</exception>
        public Wire AddWire(Pin a, Pin b)
        {
            if (a == null || b == null) throw new ArgumentNullException(a == null ? nameof(a) : nameof(b));
            if (a == b) throw new AssemblyException("Los dos extremos del cable son el mismo punto.");
            CheckWireEnd(a);
            CheckWireEnd(b);

            var wire = new Wire("W" + _nextWire++, a, b);
            _wires.Add(wire);
            Occupy(a, wire);
            Occupy(b, wire);
            Version++;
            return wire;
        }

        /// <summary>Cable entre dos agujeros o pines dados por su nombre: "a10", "T+3", "J7.1"...</summary>
        public Wire AddWire(string a, string b) => AddWire(PinByName(a), PinByName(b));

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
