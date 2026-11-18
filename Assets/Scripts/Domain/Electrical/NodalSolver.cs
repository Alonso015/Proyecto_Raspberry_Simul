using System;
using System.Collections.Generic;

namespace ImatPiSim.Domain.Electrical
{
    /// <summary>
    /// Resuelve un circuito en continua por análisis nodal.
    /// </summary>
    /// <remarks>
    /// Pasos: (1) cada elemento estampa su contribución; (2) se añade gmin; (3) se resuelve
    /// G·V = I; (4) si hay elementos no lineales (<see cref="INonlinearElement"/>), se comprueba
    /// que su región sea coherente y, si no, se repite; (5) cada elemento recibe la solución
    /// definitiva en <see cref="IElectricalElement.OnSolved"/>.
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

            var nonlinear = new List<INonlinearElement>();
            foreach (var e in netlist.Elements)
                if (e is INonlinearElement nl) nonlinear.Add(nl);

            for (int iteration = 1; iteration <= MaxIterations; iteration++)
            {
                var solution = SolveOnce(netlist);
                LastIterations = iteration;

                // ¿Eran coherentes las regiones supuestas (conduce / no conduce)?
                bool changed = false;
                foreach (var nl in nonlinear) changed |= nl.UpdateRegion(solution);
                if (changed) continue;

                solution.SetPwm(PropagatePwm(netlist, LastSystem.DeclaredPwm));
                foreach (var e in netlist.Elements) e.OnSolved(solution);
                return solution;
            }
            throw new InvalidOperationException(
                $"El circuito no converge en {MaxIterations} iteraciones: algún elemento no lineal oscila entre regiones.");
        }

        /// <summary>Máximo de resoluciones para que los elementos no lineales sean coherentes.</summary>
        public int MaxIterations { get; set; } = 50;

        /// <summary>Resoluciones lineales que necesitó la última llamada a <see cref="Solve"/>.</summary>
        public int LastIterations { get; private set; }

        private NodalSolution SolveOnce(Netlist netlist)
        {
            var stamper = new MatrixStamper(netlist);
            foreach (var e in netlist.Elements) e.Stamp(stamper);
            stamper.AddGmin(Gmin);
            LastSystem = stamper;

            var x = stamper.SolveLinear();
            var voltages = new Dictionary<Node, double>();
            foreach (var n in netlist.Nodes)
                if (n.IsFixed) voltages[n] = n.FixedVoltage.Value;
            for (int k = 0; k < x.Length; k++) voltages[stamper.Unknowns[k]] = x[k];
            return new NodalSolution(voltages);
        }

        /// <summary>
        /// Lleva cada señal PWM desde su nodo de origen a todos los nodos unidos a él a través de
        /// elementos (resistencias, LEDs...), sin atravesar nodos de tensión fija (raíles) ni
        /// elementos de entradas independientes (<see cref="ISignalIsolating"/>).
        /// </summary>
        /// <remarks>
        /// Aproximación: si dos PWM llegan al mismo nodo, se queda la de mayor factor de servicio.
        /// </remarks>
        private static Dictionary<Node, PwmSignal> PropagatePwm(Netlist netlist, IReadOnlyDictionary<Node, PwmSignal> sources)
        {
            var result = new Dictionary<Node, PwmSignal>();
            if (sources.Count == 0) return result;

            var neighbours = new Dictionary<Node, HashSet<Node>>();
            foreach (var e in netlist.Elements)
            {
                if (e is ISignalIsolating || e.Terminals.Count < 2) continue;
                foreach (var a in e.Terminals)
                foreach (var b in e.Terminals)
                {
                    if (a.Node == b.Node) continue;
                    if (!neighbours.TryGetValue(a.Node, out var set)) neighbours[a.Node] = set = new HashSet<Node>();
                    set.Add(b.Node);
                }
            }

            foreach (var source in sources)
            {
                var queue = new Queue<Node>();
                var seen = new HashSet<Node> { source.Key };
                queue.Enqueue(source.Key);
                while (queue.Count > 0)
                {
                    var n = queue.Dequeue();
                    if (!result.TryGetValue(n, out var existing) || existing.DutyCycle < source.Value.DutyCycle)
                        result[n] = source.Value;
                    if (n.IsFixed && n != source.Key) continue;   // un raíl fija la tensión: la señal no pasa
                    if (!neighbours.TryGetValue(n, out var next)) continue;
                    foreach (var m in next)
                        if (seen.Add(m) && !m.IsFixed) queue.Enqueue(m);
                }
            }
            return result;
        }

        private sealed class NodalSolution : ISolution
        {
            private readonly Dictionary<Node, double> _voltages;
            private Dictionary<Node, PwmSignal> _pwm = new Dictionary<Node, PwmSignal>();

            public NodalSolution(Dictionary<Node, double> voltages) => _voltages = voltages;

            public void SetPwm(Dictionary<Node, PwmSignal> pwm) => _pwm = pwm;

            public PwmSignal PwmAt(Node node)
            {
                if (node == null) throw new ArgumentNullException(nameof(node));
                return _pwm.TryGetValue(node, out var s) ? s : null;
            }

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
