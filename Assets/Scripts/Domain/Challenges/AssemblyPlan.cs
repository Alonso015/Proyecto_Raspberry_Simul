using System;
using System.Collections.Generic;
using System.Linq;
using ImatPiSim.Domain.Components;
using ImatPiSim.Domain.Simulation;
using ImatPiSim.Domain.Wiring;

namespace ImatPiSim.Domain.Challenges
{
    /// <summary>
    /// Descripción del montaje del jugador: qué piezas hay, dónde están pinchadas, qué cables
    /// las unen y si el SPI está activado. Con ella se construye un circuito nuevo, idéntico pero
    /// independiente, para que el verificador lo pruebe sin tocar el del jugador.
    /// </summary>
    /// <remarks>
    /// Es lo que se copia, no los objetos: un componente ya usado (un LED quemado, un pulsador
    /// pulsado) no sirve para una prueba limpia. Cada pieza se vuelve a crear con
    /// <see cref="ComponentFactory"/> a partir de su tipo y sus parámetros.
    /// </remarks>
    public sealed class AssemblyPlan
    {
        public sealed class Part
        {
            public Part(string id, string typeId, IReadOnlyDictionary<string, double> parameters, string start, Direction direction)
            {
                Id = id;
                TypeId = typeId;
                Parameters = parameters;
                Start = start;
                Direction = direction;
            }

            public string Id { get; }
            public string TypeId { get; }
            public IReadOnlyDictionary<string, double> Parameters { get; }

            /// <summary>Agujero de la primera patilla, o <c>null</c> si no va pinchada (el servo).</summary>
            public string Start { get; }

            public Direction Direction { get; }
        }

        public sealed class Link
        {
            public Link(string a, string b, IReadOnlyList<string> route)
            {
                A = a;
                B = b;
                Route = route;
            }

            public string A { get; }
            public string B { get; }
            public IReadOnlyList<string> Route { get; }
        }

        public AssemblyPlan(IReadOnlyList<Part> parts, IReadOnlyList<Link> wires, bool spiEnabled)
        {
            Parts = parts ?? Array.Empty<Part>();
            Wires = wires ?? Array.Empty<Link>();
            SpiEnabled = spiEnabled;
        }

        public IReadOnlyList<Part> Parts { get; }
        public IReadOnlyList<Link> Wires { get; }
        public bool SpiEnabled { get; }

        /// <summary>Montaje vacío (solo la Pi con la HAT y la protoboard).</summary>
        public static AssemblyPlan Empty(bool spiEnabled = false) => new AssemblyPlan(null, null, spiEnabled);

        /// <summary>Describe el montaje actual de un circuito.</summary>
        public static AssemblyPlan Capture(Circuit circuit)
        {
            if (circuit == null) throw new ArgumentNullException(nameof(circuit));
            var parts = circuit.ExternalComponents.Select(c =>
            {
                var p = circuit.PlacementOf(c);
                return new Part(c.Id, c.Type.Id, ParametersOf(c), p?.Start.Name, p?.Direction ?? Direction.Right);
            }).ToList();
            var wires = circuit.Wires.Select(w => new Link(circuit.NameOf(w.A), circuit.NameOf(w.B),
                                                           w.Route.Select(h => h.Name).ToList())).ToList();
            return new AssemblyPlan(parts, wires, circuit.Pi.SpiEnabled);
        }

        /// <summary>Construye un circuito nuevo (Pi + HAT + protoboard) con este montaje.</summary>
        /// <exception cref="AssemblyException">El montaje no se puede reproducir (no debería pasar si viene de <see cref="Capture"/>).</exception>
        public Circuit Build(IComponentFactory factory = null)
        {
            factory ??= ComponentFactory.CreateDefault();
            var circuit = Circuit.WithImatHat();
            circuit.Pi.SpiEnabled = SpiEnabled;
            foreach (var part in Parts)
            {
                var component = factory.Create(part.TypeId, part.Id, part.Parameters);
                if (part.Start != null) circuit.Place(component, HoleId.Parse(part.Start), part.Direction);
                else circuit.Add(component);
            }
            foreach (var w in Wires)
            {
                var route = w.Route.Count > 0 ? w.Route.Select(HoleId.Parse).ToList() : null;
                circuit.AddWire(circuit.PinByName(w.A), circuit.PinByName(w.B), route);
            }
            return circuit;
        }

        /// <summary>Parámetros con los que la fábrica vuelve a crear el componente igual.</summary>
        public static IReadOnlyDictionary<string, double> ParametersOf(Component c) => c switch
        {
            Resistor r => new Dictionary<string, double> { ["resistance"] = r.Resistance },
            Led l => new Dictionary<string, double>
            {
                ["forwardVoltage"] = l.ForwardVoltage, ["seriesResistance"] = l.SeriesResistance, ["maxCurrent"] = l.MaxCurrent
            },
            Potentiometer p => new Dictionary<string, double> { ["resistance"] = p.TotalResistance, ["position"] = p.Position },
            Ldr l => new Dictionary<string, double> { ["illuminance"] = l.Illuminance },
            Servo s => new Dictionary<string, double> { ["initialAngle"] = s.InitialAngle, ["maxSpeed"] = s.MaxSpeed },
            _ => new Dictionary<string, double>()
        };
    }
}
