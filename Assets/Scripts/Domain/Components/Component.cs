using System;
using System.Collections.Generic;
using System.Linq;
using ImatPiSim.Domain.Electrical;

namespace ImatPiSim.Domain.Components
{
    /// <summary>
    /// Componente del simulador (resistencia, LED, IMAT HAT...): lo que el jugador coloca,
    /// conecta y ve. Raíz del patrón <b>Composite</b>.
    /// </summary>
    /// <remarks>
    /// El modelo eléctrico no conoce los componentes: cada componente le entrega sus
    /// <see cref="ElectricalElements"/> y las uniones fijas entre sus terminales
    /// (<see cref="InternalLinks"/>, p. ej. las pistas de una placa).
    /// </remarks>
    public abstract class Component
    {
        private readonly List<Pin> _pins = new List<Pin>();

        protected Component(string id, ComponentType type)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("El componente necesita un id.", nameof(id));
            Id = id;
            Type = type ?? throw new ArgumentNullException(nameof(type));
        }

        public string Id { get; }

        /// <summary>Datos compartidos por todos los componentes del mismo tipo (Flyweight).</summary>
        public ComponentType Type { get; }

        public IReadOnlyList<Pin> Pins => _pins;

        /// <summary>Se lanza cuando cambia algo visible del componente (para las vistas, patrón Observer).</summary>
        public event Action<Component> StateChanged;

        public Pin GetPin(string name)
        {
            var pin = _pins.FirstOrDefault(p => p.Name == name);
            if (pin == null) throw new ArgumentException($"{Id} no tiene el pin '{name}'.", nameof(name));
            return pin;
        }

        /// <summary>Elementos eléctricos que aporta este componente al circuito.</summary>
        public abstract IEnumerable<IElectricalElement> ElectricalElements();

        /// <summary>Uniones fijas entre terminales (pistas de una placa). Por defecto, ninguna.</summary>
        public virtual IEnumerable<(Terminal a, Terminal b)> InternalLinks() =>
            Enumerable.Empty<(Terminal, Terminal)>();

        /// <summary>Vuelve al estado inicial (nuevo intento de un reto).</summary>
        public virtual void Reset() { }

        protected Pin AddPin(string name, Terminal terminal)
        {
            var pin = new Pin(name, this, terminal);
            _pins.Add(pin);
            return pin;
        }

        protected void RaiseStateChanged() => StateChanged?.Invoke(this);

        public override string ToString() => $"{Id} ({Type.DisplayName})";
    }

    /// <summary>Patilla o contacto de un componente, con el terminal eléctrico que le corresponde.</summary>
    public sealed class Pin
    {
        internal Pin(string name, Component owner, Terminal terminal)
        {
            Name = name;
            Owner = owner;
            Terminal = terminal ?? throw new ArgumentNullException(nameof(terminal));
        }

        public string Name { get; }
        public Component Owner { get; }
        public Terminal Terminal { get; }
        public string FullName => Owner.Id + "." + Name;
        public override string ToString() => FullName;
    }

    /// <summary>
    /// Componente simple (hoja del Composite) que es a la vez un elemento eléctrico.
    /// </summary>
    public abstract class ElectricalComponent : Component, IElectricalElement
    {
        private readonly List<Terminal> _terminals = new List<Terminal>();

        protected ElectricalComponent(string id, ComponentType type) : base(id, type) { }

        public IReadOnlyList<Terminal> Terminals => _terminals;

        /// <summary>Crea un terminal y su pin a la vez.</summary>
        protected Terminal CreateTerminal(string pinName)
        {
            var t = new Terminal(Id + "." + pinName);
            _terminals.Add(t);
            AddPin(pinName, t);
            return t;
        }

        public override IEnumerable<IElectricalElement> ElectricalElements()
        {
            yield return this;
        }

        public abstract void Stamp(IStampContext ctx);
        public abstract void OnSolved(ISolution solution);
    }

    /// <summary>
    /// Componente formado por otros (rol "Composite"): la IMAT HAT. Delega en sus hijos y
    /// añade las pistas que los unen.
    /// </summary>
    public abstract class CompositeComponent : Component
    {
        private readonly List<Component> _children = new List<Component>();
        private readonly List<(Terminal, Terminal)> _links = new List<(Terminal, Terminal)>();

        protected CompositeComponent(string id, ComponentType type) : base(id, type) { }

        public IReadOnlyList<Component> Children => _children;

        protected T AddChild<T>(T child) where T : Component
        {
            if (child == null) throw new ArgumentNullException(nameof(child));
            _children.Add(child);
            return child;
        }

        /// <summary>Une dos terminales con una pista fija de la placa.</summary>
        protected void Link(Terminal a, Terminal b) => _links.Add((a, b));

        public override IEnumerable<IElectricalElement> ElectricalElements() =>
            _children.SelectMany(c => c.ElectricalElements());

        public override IEnumerable<(Terminal a, Terminal b)> InternalLinks() =>
            _links.Concat(_children.SelectMany(c => c.InternalLinks()));

        public override void Reset()
        {
            foreach (var c in _children) c.Reset();
        }
    }

    /// <summary>Puente entre el mundo de los componentes y el constructor de netlists.</summary>
    public static class NetlistBuilderExtensions
    {
        /// <summary>Añade los elementos de un componente y sus uniones internas.</summary>
        public static NetlistBuilder AddComponent(this NetlistBuilder builder, Component component)
        {
            foreach (var e in component.ElectricalElements()) builder.Add(e);
            foreach (var (a, b) in component.InternalLinks()) builder.Connect(a, b);
            return builder;
        }

        /// <summary>Une dos pines con un cable.</summary>
        public static NetlistBuilder Connect(this NetlistBuilder builder, Pin a, Pin b) =>
            builder.Connect(a.Terminal, b.Terminal);
    }
}
