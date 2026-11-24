using System;
using System.Collections.Generic;
using System.Linq;
using ImatPiSim.Domain.Components;
using ImatPiSim.Domain.Electrical;

namespace ImatPiSim.Domain.Simulation
{
    /// <summary>
    /// El montaje completo: la Raspberry Pi, la IMAT HAT (opcional), los componentes externos y
    /// los cables entre pines. Sabe convertirse en una <see cref="Netlist"/>.
    /// </summary>
    /// <remarks>
    /// Cada cambio de estructura (añadir un componente o un cable) incrementa <see cref="Version"/>;
    /// el motor lo usa para saber cuándo reconstruir la netlist. Los cambios de estado (pulsar un
    /// botón) no cambian la estructura: solo obligan a volver a resolver.
    /// </remarks>
    public sealed class Circuit
    {
        private readonly List<Component> _extras = new List<Component>();
        private readonly List<(Pin a, Pin b)> _wires = new List<(Pin, Pin)>();

        public Circuit(RaspberryPi pi, ImatHat hat = null)
        {
            Pi = pi ?? throw new ArgumentNullException(nameof(pi));
            if (hat != null && hat.Pi != pi) throw new ArgumentException("La HAT debe estar montada sobre esta Raspberry Pi.", nameof(hat));
            Hat = hat;
        }

        /// <summary>Montaje habitual: una Raspberry Pi con la IMAT HAT encima.</summary>
        public static Circuit WithImatHat()
        {
            var pi = new RaspberryPi();
            return new Circuit(pi, new ImatHat(pi));
        }

        public RaspberryPi Pi { get; }
        public ImatHat Hat { get; }
        public IReadOnlyList<Component> ExternalComponents => _extras;
        public IReadOnlyList<(Pin a, Pin b)> Wires => _wires;
        public int Version { get; private set; }

        /// <summary>Componentes de primer nivel: la Pi, la HAT y los externos.</summary>
        public IEnumerable<Component> Components
        {
            get
            {
                yield return Pi;
                if (Hat != null) yield return Hat;
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
            _extras.Add(component);
            Version++;
            return component;
        }

        public void Remove(Component component)
        {
            if (!_extras.Remove(component)) return;
            _wires.RemoveAll(w => w.a.Owner == component || w.b.Owner == component);
            Version++;
        }

        /// <summary>Une dos pines con un cable.</summary>
        public void Connect(Pin a, Pin b)
        {
            if (a == null || b == null) throw new ArgumentNullException(a == null ? nameof(a) : nameof(b));
            _wires.Add((a, b));
            Version++;
        }

        public void Disconnect(Pin a, Pin b)
        {
            if (_wires.RemoveAll(w => (w.a == a && w.b == b) || (w.a == b && w.b == a)) > 0) Version++;
        }

        internal Netlist BuildNetlist()
        {
            var builder = new NetlistBuilder();
            foreach (var c in Components) builder.AddComponent(c);
            foreach (var (a, b) in _wires) builder.Connect(a, b);
            return builder.Build();
        }
    }
}
