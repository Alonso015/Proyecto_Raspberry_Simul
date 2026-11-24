using System;
using System.Collections.Generic;

namespace ImatPiSim.Domain.Wiring.Routing
{
    /// <summary>
    /// Costes del trazado (decididos el 24/11/2026): cada agujero recorrido cuesta
    /// <see cref="Step"/>, cada giro <see cref="Turn"/> más y cada cable o cuerpo de componente
    /// que se cruza <see cref="Crossing"/> más. Los agujeros ocupados no se pueden atravesar.
    /// </summary>
    public sealed class RoutingCosts
    {
        public static readonly RoutingCosts Default = new RoutingCosts(step: 1, turn: 1, crossing: 5);

        public RoutingCosts(double step, double turn, double crossing)
        {
            if (step <= 0) throw new ArgumentOutOfRangeException(nameof(step), "Cada paso debe costar algo (> 0).");
            if (turn < 0 || crossing < 0) throw new ArgumentOutOfRangeException(turn < 0 ? nameof(turn) : nameof(crossing));
            Step = step;
            Turn = turn;
            Crossing = crossing;
        }

        public double Step { get; }
        public double Turn { get; }
        public double Crossing { get; }

        /// <summary>
        /// Coste de entrar en <paramref name="to"/> llegando en la dirección <paramref name="dir"/>.
        /// <paramref name="previous"/> es la dirección del paso anterior (<c>null</c> en el primero).
        /// El destino no cuenta como cruce: es donde se pincha el cable.
        /// </summary>
        public double StepCost(RoutingGrid grid, Cell to, Direction dir, Direction? previous, bool isGoal)
        {
            double cost = Step;
            if (previous.HasValue && previous.Value != dir) cost += Turn;
            if (!isGoal) cost += Crossing * grid.LoadAt(to);
            return cost;
        }

        /// <summary>
        /// Coste total de un camino ya hecho (lo usan el router en L y las pruebas). Devuelve
        /// infinito si el camino no es válido: pasos que no son vecinos, o atraviesa un agujero ocupado.
        /// </summary>
        public double Evaluate(RoutingGrid grid, IReadOnlyList<Cell> path)
        {
            if (path == null || path.Count == 0) return double.PositiveInfinity;
            double total = 0;
            Direction? previous = null;
            for (int i = 1; i < path.Count; i++)
            {
                var from = path[i - 1];
                var to = path[i];
                if (from.ManhattanTo(to) != 1 || !grid.Contains(to)) return double.PositiveInfinity;
                bool isGoal = i == path.Count - 1;
                if (grid.IsBlocked(to) && !isGoal) return double.PositiveInfinity;
                var dir = DirectionOf(from, to);
                total += StepCost(grid, to, dir, previous, isGoal);
                previous = dir;
            }
            return total;
        }

        internal static Direction DirectionOf(Cell from, Cell to) =>
            to.Col > from.Col ? Direction.Right :
            to.Col < from.Col ? Direction.Left :
            to.Row > from.Row ? Direction.Down : Direction.Up;
    }

    /// <summary>Resultado de un trazado: las casillas del cable (de inicio a destino) y su coste.</summary>
    public sealed class Route
    {
        public static Route None(int expanded) => new Route(Array.Empty<Cell>(), double.PositiveInfinity, expanded);

        public Route(IReadOnlyList<Cell> cells, double cost, int expanded)
        {
            Cells = cells;
            Cost = cost;
            Expanded = expanded;
        }

        /// <summary>Casillas del camino, incluidos inicio y destino. Vacío si no hay camino.</summary>
        public IReadOnlyList<Cell> Cells { get; }

        public bool Found => Cells.Count > 0;
        public double Cost { get; }

        /// <summary>Estados que el algoritmo ha sacado de la frontera (mide cuánto ha tenido que buscar).</summary>
        public int Expanded { get; }

        /// <summary>Número de giros del camino.</summary>
        public int Turns
        {
            get
            {
                int turns = 0;
                for (int i = 2; i < Cells.Count; i++)
                    if (RoutingCosts.DirectionOf(Cells[i - 2], Cells[i - 1]) != RoutingCosts.DirectionOf(Cells[i - 1], Cells[i])) turns++;
                return turns;
            }
        }
    }

    /// <summary>
    /// Algoritmo de trazado de cables (patrón <b>Strategy</b>): el juego pide un camino sin
    /// saber cómo se calcula, y se puede cambiar el A* por otro algoritmo (el router en L, para
    /// comparar o depurar) sin tocar nada más.
    /// </summary>
    public interface IRouter
    {
        string Name { get; }

        /// <summary>Camino de <paramref name="from"/> a <paramref name="to"/>, o uno vacío si no hay.</summary>
        /// <remarks>Inicio y destino deben estar libres: son los agujeros donde se pinchará el cable.</remarks>
        Route FindRoute(RoutingGrid grid, Cell from, Cell to);
    }

    /// <summary>
    /// Router simple: prueba los dos caminos en L (primero en horizontal o primero en vertical)
    /// y se queda con el más barato que no atraviese agujeros ocupados. Es rápido, pero no
    /// rodea obstáculos: si las dos L están cortadas, no encuentra camino aunque exista.
    /// </summary>
    public sealed class LRouter : IRouter
    {
        private readonly RoutingCosts _costs;

        public LRouter(RoutingCosts costs = null) => _costs = costs ?? RoutingCosts.Default;

        public string Name => "En L";

        public Route FindRoute(RoutingGrid grid, Cell from, Cell to)
        {
            if (!AStarRouter.EndpointsValid(grid, from, to)) return Route.None(0);
            Route best = Route.None(2);
            foreach (bool horizontalFirst in new[] { true, false })
            {
                var path = LPath(from, to, horizontalFirst);
                double cost = _costs.Evaluate(grid, path);
                if (cost < best.Cost) best = new Route(path, cost, 2);
            }
            return best;
        }

        private static List<Cell> LPath(Cell from, Cell to, bool horizontalFirst)
        {
            var path = new List<Cell> { from };
            var c = from;
            void Walk(int dr, int dc, int steps)
            {
                for (int i = 0; i < steps; i++)
                {
                    c = new Cell(c.Row + dr, c.Col + dc);
                    path.Add(c);
                }
            }
            int sr = Math.Sign(to.Row - from.Row), sc = Math.Sign(to.Col - from.Col);
            int nr = Math.Abs(to.Row - from.Row), nc = Math.Abs(to.Col - from.Col);
            if (horizontalFirst) { Walk(0, sc, nc); Walk(sr, 0, nr); }
            else { Walk(sr, 0, nr); Walk(0, sc, nc); }
            return path;
        }
    }
}
