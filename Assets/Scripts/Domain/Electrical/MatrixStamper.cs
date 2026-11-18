using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ImatPiSim.Domain.Electrical
{
    /// <summary>
    /// Monta y resuelve el sistema nodal G·V = I de una netlist.
    /// </summary>
    /// <remarks>
    /// Las incógnitas son las tensiones de los nodos sin raíl. Los nodos con tensión fija no
    /// son incógnitas: lo que aportan pasa al término independiente I.
    /// </remarks>
    public sealed class MatrixStamper : IStampContext
    {
        private readonly Dictionary<Node, int> _unknownIndex = new Dictionary<Node, int>();
        private readonly List<Node> _unknowns = new List<Node>();
        private readonly double[,] _g;
        private readonly double[] _i;

        public MatrixStamper(Netlist netlist)
        {
            if (netlist == null) throw new ArgumentNullException(nameof(netlist));
            foreach (var n in netlist.Nodes)
            {
                if (n.IsFixed) continue;
                _unknownIndex[n] = _unknowns.Count;
                _unknowns.Add(n);
            }
            _g = new double[_unknowns.Count, _unknowns.Count];
            _i = new double[_unknowns.Count];
        }

        /// <summary>Nodos incógnita, en el orden de filas y columnas de G.</summary>
        public IReadOnlyList<Node> Unknowns => _unknowns;

        public double G(int row, int col) => _g[row, col];
        public double I(int row) => _i[row];

        public void AddConductance(Node a, Node b, double g)
        {
            if (a == null || b == null) throw new ArgumentNullException(a == null ? nameof(a) : nameof(b));
            if (g < 0 || double.IsNaN(g) || double.IsInfinity(g))
                throw new ArgumentOutOfRangeException(nameof(g), g, "La conductancia debe ser finita y no negativa.");
            if (a == b || g == 0) return;   // una conductancia de un nodo a sí mismo no hace nada

            bool aUnknown = _unknownIndex.TryGetValue(a, out var ia);
            bool bUnknown = _unknownIndex.TryGetValue(b, out var ib);

            // Ley de Kirchhoff de corrientes en a:  ... + g·(Va − Vb) ... = corrientes que entran.
            // Si un extremo tiene tensión fija, su término g·Vfijo es conocido y pasa a I.
            if (aUnknown)
            {
                _g[ia, ia] += g;
                if (bUnknown) _g[ia, ib] -= g;
                else _i[ia] += g * b.FixedVoltage.Value;
            }
            if (bUnknown)
            {
                _g[ib, ib] += g;
                if (aUnknown) _g[ib, ia] -= g;
                else _i[ib] += g * a.FixedVoltage.Value;
            }
        }

        public void AddConductanceTo(Node a, double g, double voltage)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (g < 0 || double.IsNaN(g) || double.IsInfinity(g))
                throw new ArgumentOutOfRangeException(nameof(g), g, "La conductancia debe ser finita y no negativa.");
            // Equivale a conectar g hacia un nodo de tensión fija "voltage": la parte conocida
            // g·V pasa al término independiente (equivalente de Norton: g en paralelo con g·V).
            if (g == 0 || !_unknownIndex.TryGetValue(a, out var ia)) return;
            _g[ia, ia] += g;
            _i[ia] += g * voltage;
        }

        public void AddCurrentSource(Node into, double amps)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            // Una fuente que entra en un nodo de tensión fija la absorbe el raíl ideal.
            if (_unknownIndex.TryGetValue(into, out var idx)) _i[idx] += amps;
        }

        /// <summary>
        /// Conductancia mínima de cada nodo incógnita a 0 V. Evita que la matriz sea singular
        /// cuando hay nodos flotantes (sin camino a ningún raíl).
        /// </summary>
        public void AddGmin(double gmin)
        {
            for (int k = 0; k < _unknowns.Count; k++) _g[k, k] += gmin;
        }

        /// <summary>
        /// Resuelve G·V = I por eliminación gaussiana con pivoteo parcial.
        /// No modifica el sistema montado (trabaja sobre una copia).
        /// </summary>
        /// <exception cref="InvalidOperationException">Si la matriz es singular.</exception>
        public double[] SolveLinear()
        {
            int n = _unknowns.Count;
            var a = (double[,])_g.Clone();
            var b = (double[])_i.Clone();

            for (int col = 0; col < n; col++)
            {
                // Pivoteo parcial: se elige la fila con mayor |valor| en esta columna para
                // no dividir entre números pequeños (estabilidad numérica).
                int pivot = col;
                for (int r = col + 1; r < n; r++)
                    if (Math.Abs(a[r, col]) > Math.Abs(a[pivot, col])) pivot = r;
                if (Math.Abs(a[pivot, col]) < 1e-300)
                    throw new InvalidOperationException(
                        $"Matriz singular: el nodo {_unknowns[col]} no tiene ninguna referencia de tensión.");
                if (pivot != col)
                {
                    for (int c = col; c < n; c++) (a[col, c], a[pivot, c]) = (a[pivot, c], a[col, c]);
                    (b[col], b[pivot]) = (b[pivot], b[col]);
                }

                // Eliminación: anula la columna por debajo del pivote.
                for (int r = col + 1; r < n; r++)
                {
                    double f = a[r, col] / a[col, col];
                    if (f == 0) continue;
                    for (int c = col; c < n; c++) a[r, c] -= f * a[col, c];
                    b[r] -= f * b[col];
                }
            }

            // Sustitución hacia atrás.
            var x = new double[n];
            for (int r = n - 1; r >= 0; r--)
            {
                double sum = b[r];
                for (int c = r + 1; c < n; c++) sum -= a[r, c] * x[c];
                x[r] = sum / a[r, r];
            }
            return x;
        }

        /// <summary>Representación legible del sistema, para depurar y explicar.</summary>
        public string Describe()
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("G (S) · V = I (A)   incógnitas: " + string.Join(", ", _unknowns));
            for (int r = 0; r < _unknowns.Count; r++)
            {
                sb.Append("[ ");
                for (int c = 0; c < _unknowns.Count; c++) sb.Append(_g[r, c].ToString("E4", inv)).Append(' ');
                sb.Append("] · V").Append(_unknowns[r].Index).Append(" = ").AppendLine(_i[r].ToString("E4", inv));
            }
            return sb.ToString();
        }
    }
}
