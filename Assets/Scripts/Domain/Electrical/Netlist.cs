using System.Collections.Generic;

namespace ImatPiSim.Domain.Electrical
{
    /// <summary>
    /// Circuito listo para resolver: los nodos y los elementos que los unen.
    /// </summary>
    public sealed class Netlist
    {
        internal Netlist(IReadOnlyList<Node> nodes, IReadOnlyList<IElectricalElement> elements,
                         IReadOnlyList<ShortCircuit> shortCircuits)
        {
            Nodes = nodes;
            Elements = elements;
            ShortCircuits = shortCircuits;
        }

        public IReadOnlyList<Node> Nodes { get; }
        public IReadOnlyList<IElectricalElement> Elements { get; }

        /// <summary>
        /// Nodos unidos a dos raíles de tensiones distintas (p. ej. 3,3 V y GND en el mismo
        /// nodo). Se detectan sin resolver nada: con fuentes ideales, la corriente sería infinita.
        /// </summary>
        public IReadOnlyList<ShortCircuit> ShortCircuits { get; }

        public bool HasShortCircuit => ShortCircuits.Count > 0;
    }

    /// <summary>Dos raíles con tensiones distintas unidos en el mismo nodo.</summary>
    public sealed class ShortCircuit
    {
        internal ShortCircuit(IVoltageReference first, IVoltageReference second)
        {
            First = first;
            Second = second;
        }

        public IVoltageReference First { get; }
        public IVoltageReference Second { get; }

        public override string ToString() =>
            $"Cortocircuito: {First.Terminal.Name} ({First.Voltage} V) unido a " +
            $"{Second.Terminal.Name} ({Second.Voltage} V)";
    }
}
