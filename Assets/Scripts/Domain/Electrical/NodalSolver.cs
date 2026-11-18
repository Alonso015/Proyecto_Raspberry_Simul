using System;
using System.Collections.Generic;

namespace ImatPiSim.Domain.Electrical
{
    /// <summary>
    /// Resuelve un circuito en continua por análisis nodal.
    /// </summary>
    /// <remarks>
    /// Pasos: (1) cada elemento estampa su contribución; (2) se añade gmin; (3) se resuelve
    /// G·V = I; (4) cada elemento recibe la solución en <see cref="IElectricalElement.OnSolved"/>.
    /// En esta etapa solo hay elementos lineales; los diodos (iteración) llegan en la etapa 3.
    /// </remarks>
    public sealed class NodalSolver
    {
        /// <summary>
        /// Conductancia mínima a 0 V (1 pS ≡ 1 TΩ). Es despreciable frente a cualquier resistencia
        /// real del circuito (el error relativo que introduce en un divisor de 1 kΩ es ~1e-9).
        /// </summary>
        public const double DefaultGmin = 1e-12;

        public NodalSolver(double gmin = DefaultGmin)
        {
            if (gmin <= 0) throw new ArgumentOutOfRangeException(nameof(gmin), "gmin debe ser positivo.");
            Gmin = gmin;
        }

        public double Gmin { get; }

        /// <summary>Sistema montado en la última llamada a <see cref="Solve"/> (para depurar).</summary>
        public MatrixStamper LastSystem { get; private set; }

        /// <exception cref="InvalidOperationException">Si la netlist tiene un cortocircuito entre raíles.</exception>
        public ISolution Solve(Netlist netlist)
        {
            if (netlist == null) throw new ArgumentNullException(nameof(netlist));
            if (netlist.HasShortCircuit)
                throw new InvalidOperationException(
                    "No se puede resolver un circuito con cortocircuito entre raíles: " + netlist.ShortCircuits[0]);

            var stamper = new MatrixStamper(netlist);
            foreach (var e in netlist.Elements) e.Stamp(stamper);
            stamper.AddGmin(Gmin);
            LastSystem = stamper;

            var x = stamper.SolveLinear();
            var voltages = new Dictionary<Node, double>();
            foreach (var n in netlist.Nodes)
                if (n.IsFixed) voltages[n] = n.FixedVoltage.Value;
            for (int k = 0; k < x.Length; k++) voltages[stamper.Unknowns[k]] = x[k];

            var solution = new NodalSolution(voltages);
            foreach (var e in netlist.Elements) e.OnSolved(solution);
            return solution;
        }

        private sealed class NodalSolution : ISolution
        {
            private readonly Dictionary<Node, double> _voltages;

            public NodalSolution(Dictionary<Node, double> voltages) => _voltages = voltages;

            public double VoltageAt(Node node)
            {
                if (node == null) throw new ArgumentNullException(nameof(node));
                if (!_voltages.TryGetValue(node, out var v))
                    throw new ArgumentException($"El nodo {node} no pertenece a esta solución.", nameof(node));
                return v;
            }
        }
    }
}
