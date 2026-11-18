using System;
using System.Collections.Generic;

namespace ImatPiSim.Domain.Electrical
{
    /// <summary>
    /// Construye una <see cref="Netlist"/>: recoge elementos, registra qué terminales están
    /// unidos y agrupa cada conjunto de terminales unidos en un único <see cref="Node"/>.
    /// </summary>
    public sealed class NetlistBuilder
    {
        /// <summary>Diferencia de tensión a partir de la cual dos raíles se consideran distintos.</summary>
        private const double SameVoltageTolerance = 1e-9;

        private readonly List<IElectricalElement> _elements = new List<IElectricalElement>();
        private readonly HashSet<IElectricalElement> _elementSet = new HashSet<IElectricalElement>();
        private readonly DisjointSet<Terminal> _sets = new DisjointSet<Terminal>();

        /// <summary>Añade un elemento y registra sus terminales.</summary>
        public NetlistBuilder Add(IElectricalElement element)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            if (!_elementSet.Add(element)) return this;
            _elements.Add(element);
            foreach (var t in element.Terminals) _sets.Add(t);
            return this;
        }

        public NetlistBuilder Add(params IElectricalElement[] elements)
        {
            foreach (var e in elements) Add(e);
            return this;
        }

        /// <summary>Une eléctricamente dos terminales (un cable ideal entre ellos).</summary>
        public NetlistBuilder Connect(Terminal a, Terminal b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (!_sets.Contains(a) || !_sets.Contains(b))
                throw new InvalidOperationException(
                    $"No se puede conectar {a} con {b}: algún terminal no pertenece a un elemento añadido.");
            _sets.Union(a, b);
            return this;
        }

        /// <summary>
        /// Agrupa los terminales en nodos, fija la tensión de los nodos con raíl y detecta
        /// cortocircuitos entre raíles. Asigna <see cref="Terminal.Node"/> a cada terminal.
        /// </summary>
        public Netlist Build()
        {
            // 1. Raíles de cada grupo de terminales (por su representante en el union-find).
            var railsByRoot = new Dictionary<Terminal, List<IVoltageReference>>();
            foreach (var e in _elements)
            {
                if (!(e is IVoltageReference rail)) continue;
                var root = _sets.Find(rail.Terminal);
                if (!railsByRoot.TryGetValue(root, out var list))
                    railsByRoot[root] = list = new List<IVoltageReference>();
                list.Add(rail);
            }

            // 2. Un nodo por grupo, en el orden en que aparecen los terminales (determinista).
            var nodeByRoot = new Dictionary<Terminal, Node>();
            var nodes = new List<Node>();
            var shorts = new List<ShortCircuit>();
            foreach (var e in _elements)
            {
                foreach (var t in e.Terminals)
                {
                    var root = _sets.Find(t);
                    if (!nodeByRoot.TryGetValue(root, out var node))
                    {
                        double? fixedVoltage = null;
                        var name = root.Name;
                        if (railsByRoot.TryGetValue(root, out var rails))
                        {
                            fixedVoltage = rails[0].Voltage;
                            name = rails[0].Terminal.Name;
                            for (int i = 1; i < rails.Count; i++)
                                if (Math.Abs(rails[i].Voltage - rails[0].Voltage) > SameVoltageTolerance)
                                    shorts.Add(new ShortCircuit(rails[0], rails[i]));
                        }
                        node = new Node(nodes.Count, fixedVoltage, name);
                        nodeByRoot[root] = node;
                        nodes.Add(node);
                    }
                    t.Node = node;
                }
            }

            return new Netlist(nodes, _elements.ToArray(), shorts);
        }
    }
}
