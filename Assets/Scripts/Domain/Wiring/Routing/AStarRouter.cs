using System.Collections.Generic;

namespace ImatPiSim.Domain.Wiring.Routing
{
    /// <summary>
    /// Trazado óptimo de cables con <b>A*</b>: la IA del proyecto (docs/requisitos.md §2.3).
    /// </summary>
    /// <remarks>
    /// <para><b>Idea.</b> A* explora caminos desde el inicio ordenándolos por
    /// <c>f = g + h</c>, donde <c>g</c> es lo que ya ha costado llegar y <c>h</c> una estimación
    /// optimista de lo que falta. Siempre expande el estado con menor <c>f</c>. Si <c>h</c> nunca
    /// sobreestima (es <i>admisible</i>), el primer camino que llega al destino es el más barato.</para>
    /// <para><b>Estado = (casilla, dirección de llegada).</b> Hace falta la dirección porque girar
    /// cuesta: llegar a la misma casilla en horizontal o en vertical no es lo mismo para lo que
    /// queda. Con solo la casilla, A* podría descartar un camino que después gira menos.</para>
    /// <para><b>Heurística.</b> <c>h = Step · distancia Manhattan</c>. Es admisible: cada paso
    /// mueve una casilla en horizontal o vertical y cuesta al menos <c>Step</c> (giros y
    /// cruces solo suman), así que no se puede llegar con menos de <c>Manhattan</c> pasos.</para>
    /// <para><b>Desempates</b>: a igual <c>f</c>, primero el de mayor <c>g</c> (el más cercano al
    /// destino) y después el más antiguo, para que el resultado sea siempre el mismo.</para>
    /// </remarks>
    public sealed class AStarRouter : IRouter
    {
        private const int Start = 4;   // "dirección" del estado inicial: aún no ha llegado de ningún lado

        private readonly RoutingCosts _costs;

        public AStarRouter(RoutingCosts costs = null) => _costs = costs ?? RoutingCosts.Default;

        public string Name => "A*";
        public RoutingCosts Costs => _costs;

        /// <summary>Estimación optimista del coste de <paramref name="from"/> a <paramref name="to"/>.</summary>
        public double Heuristic(Cell from, Cell to) => _costs.Step * from.ManhattanTo(to);

        internal static bool EndpointsValid(RoutingGrid grid, Cell from, Cell to) =>
            grid.Contains(from) && grid.Contains(to) && from != to && !grid.IsBlocked(from) && !grid.IsBlocked(to);

        public Route FindRoute(RoutingGrid grid, Cell from, Cell to)
        {
            if (!EndpointsValid(grid, from, to)) return Route.None(0);

            var g = new double[grid.Rows, grid.Cols, 5];
            var parent = new (int row, int col, int dir)[grid.Rows, grid.Cols, 5];
            var closed = new bool[grid.Rows, grid.Cols, 5];
            for (int r = 0; r < grid.Rows; r++)
                for (int c = 0; c < grid.Cols; c++)
                    for (int d = 0; d < 5; d++) g[r, c, d] = double.PositiveInfinity;

            var open = new MinHeap();
            g[from.Row, from.Col, Start] = 0;
            open.Push(new Entry(Heuristic(from, to), 0, from, Start));
            int expanded = 0;

            while (open.Count > 0)
            {
                var e = open.Pop();
                int dir = e.Dir;
                var cell = e.Cell;
                if (closed[cell.Row, cell.Col, dir]) continue;   // entrada vieja: ya se expandió con un g menor
                closed[cell.Row, cell.Col, dir] = true;
                expanded++;

                if (cell == to) return new Route(Rebuild(parent, from, cell, dir), e.G, expanded);

                Direction? previous = dir == Start ? (Direction?)null : (Direction)dir;
                foreach (var (next, moveDir) in grid.Neighbours(cell))
                {
                    bool isGoal = next == to;
                    if (grid.IsBlocked(next) && !isGoal) continue;   // no se atraviesan agujeros ocupados
                    int nd = (int)moveDir;
                    if (closed[next.Row, next.Col, nd]) continue;
                    double ng = e.G + _costs.StepCost(grid, next, moveDir, previous, isGoal);
                    if (ng >= g[next.Row, next.Col, nd]) continue;
                    g[next.Row, next.Col, nd] = ng;
                    parent[next.Row, next.Col, nd] = (cell.Row, cell.Col, dir);
                    open.Push(new Entry(ng + Heuristic(next, to), ng, next, nd));
                }
            }
            return Route.None(expanded);   // se agotó la frontera: no hay camino
        }

        private static List<Cell> Rebuild((int row, int col, int dir)[,,] parent, Cell from, Cell goal, int goalDir)
        {
            var path = new List<Cell> { goal };
            var (r, c, d) = (goal.Row, goal.Col, goalDir);
            while (!(r == from.Row && c == from.Col && d == Start))
            {
                (r, c, d) = parent[r, c, d];
                path.Add(new Cell(r, c));
            }
            path.Reverse();
            return path;
        }

        // ------------------------------------------------------------------ frontera (montículo binario)

        private readonly struct Entry
        {
            public Entry(double f, double g, Cell cell, int dir)
            {
                F = f;
                G = g;
                Cell = cell;
                Dir = dir;
                Seq = 0;
            }

            private Entry(Entry e, long seq)
            {
                F = e.F;
                G = e.G;
                Cell = e.Cell;
                Dir = e.Dir;
                Seq = seq;
            }

            public double F { get; }
            public double G { get; }
            public Cell Cell { get; }
            public int Dir { get; }
            public long Seq { get; }

            public Entry WithSeq(long seq) => new Entry(this, seq);

            /// <summary>Menor f; a igual f, mayor g; a igualdad, el que entró antes.</summary>
            public bool Before(Entry o) =>
                F < o.F || (F == o.F && (G > o.G || (G == o.G && Seq < o.Seq)));
        }

        /// <summary>Cola de prioridad mínima (.NET Standard 2.1 no trae <c>PriorityQueue</c>).</summary>
        private sealed class MinHeap
        {
            private readonly List<Entry> _items = new List<Entry>();
            private long _seq;

            public int Count => _items.Count;

            public void Push(Entry e)
            {
                _items.Add(e.WithSeq(_seq++));
                int i = _items.Count - 1;
                while (i > 0)
                {
                    int p = (i - 1) / 2;
                    if (!_items[i].Before(_items[p])) break;
                    (_items[i], _items[p]) = (_items[p], _items[i]);
                    i = p;
                }
            }

            public Entry Pop()
            {
                var top = _items[0];
                int last = _items.Count - 1;
                _items[0] = _items[last];
                _items.RemoveAt(last);
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1, r = l + 1, m = i;
                    if (l < _items.Count && _items[l].Before(_items[m])) m = l;
                    if (r < _items.Count && _items[r].Before(_items[m])) m = r;
                    if (m == i) break;
                    (_items[i], _items[m]) = (_items[m], _items[i]);
                    i = m;
                }
                return top;
            }
        }
    }
}
