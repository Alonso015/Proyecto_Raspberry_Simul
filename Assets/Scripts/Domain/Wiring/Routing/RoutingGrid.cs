using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ImatPiSim.Domain.Wiring.Routing
{
    /// <summary>Casilla de la rejilla de trazado (fila y columna desde 0).</summary>
    public readonly struct Cell : IEquatable<Cell>
    {
        public Cell(int row, int col)
        {
            Row = row;
            Col = col;
        }

        public int Row { get; }
        public int Col { get; }

        public static Cell FromHole(HoleId hole) => new Cell(hole.GridRow, hole.Column - 1);
        public HoleId ToHole() => new HoleId((BreadboardRow)Row, Col + 1);

        /// <summary>Distancia Manhattan: pasos mínimos moviéndose en horizontal y vertical.</summary>
        public int ManhattanTo(Cell other) => Math.Abs(Row - other.Row) + Math.Abs(Col - other.Col);

        public bool Equals(Cell other) => Row == other.Row && Col == other.Col;
        public override bool Equals(object obj) => obj is Cell c && Equals(c);
        public override int GetHashCode() => Row * 1000 + Col;
        public static bool operator ==(Cell a, Cell b) => a.Equals(b);
        public static bool operator !=(Cell a, Cell b) => !a.Equals(b);
        public override string ToString() => $"({Row},{Col})";
    }

    /// <summary>
    /// Lo que la IA sabe del entorno (requisito R8): una rejilla con, para cada agujero, si está
    /// <b>ocupado</b> (no se puede pasar por él) y cuántos cables o cuerpos de componente pasan
    /// ya por encima (<b>carga</b>: pasar por ahí es cruzarlos).
    /// </summary>
    /// <remarks>
    /// Se construye a partir del modelo lógico (<see cref="FromCircuit"/>), nunca de la escena
    /// de Unity: el router no sabe nada de sprites ni de coordenadas de pantalla. Las pruebas
    /// pueden crear rejillas pequeñas a mano.
    /// </remarks>
    public sealed class RoutingGrid
    {
        private readonly bool[,] _blocked;
        private readonly int[,] _load;

        public RoutingGrid(int rows, int cols)
        {
            if (rows <= 0 || cols <= 0) throw new ArgumentOutOfRangeException(rows <= 0 ? nameof(rows) : nameof(cols));
            Rows = rows;
            Cols = cols;
            _blocked = new bool[rows, cols];
            _load = new int[rows, cols];
        }

        public int Rows { get; }
        public int Cols { get; }

        public bool Contains(Cell c) => c.Row >= 0 && c.Row < Rows && c.Col >= 0 && c.Col < Cols;
        public bool IsBlocked(Cell c) => _blocked[c.Row, c.Col];

        /// <summary>Cuántos cables (o cuerpos de componente) pasan ya por encima de esta casilla.</summary>
        public int LoadAt(Cell c) => _load[c.Row, c.Col];

        public RoutingGrid Block(Cell c)
        {
            _blocked[c.Row, c.Col] = true;
            return this;
        }

        public RoutingGrid AddLoad(Cell c, int amount = 1)
        {
            _load[c.Row, c.Col] += amount;
            return this;
        }

        /// <summary>Las casillas vecinas (arriba, abajo, izquierda, derecha) que están dentro de la rejilla.</summary>
        public IEnumerable<(Cell cell, Direction dir)> Neighbours(Cell c)
        {
            var candidates = new[]
            {
                (new Cell(c.Row, c.Col + 1), Direction.Right),
                (new Cell(c.Row + 1, c.Col), Direction.Down),
                (new Cell(c.Row, c.Col - 1), Direction.Left),
                (new Cell(c.Row - 1, c.Col), Direction.Up)
            };
            foreach (var n in candidates)
                if (Contains(n.Item1)) yield return n;
        }

        /// <summary>
        /// Rejilla de la protoboard de un circuito:
        /// <list type="bullet">
        /// <item>ocupados: los agujeros con una patilla o el extremo de un cable;</item>
        /// <item>carga: cada casilla intermedia del recorrido de un cable ya tendido, y las
        /// casillas entre las patillas de un componente (su cuerpo pasa por encima).</item>
        /// </list>
        /// </summary>
        public static RoutingGrid FromCircuit(Simulation.Circuit circuit)
        {
            if (circuit?.Breadboard == null) throw new ArgumentException("El circuito no tiene protoboard.", nameof(circuit));
            var grid = new RoutingGrid(Breadboard.RowCount, Breadboard.Columns);
            foreach (var hole in circuit.Breadboard.Occupied.Keys) grid.Block(Cell.FromHole(hole));
            foreach (var wire in circuit.Wires)
                for (int i = 1; i < wire.Route.Count - 1; i++) grid.AddLoad(Cell.FromHole(wire.Route[i]));
            foreach (var p in circuit.Placements)
                for (int i = 0; i + 1 < p.Holes.Count; i++)
                    foreach (var cell in Between(Cell.FromHole(p.Holes[i]), Cell.FromHole(p.Holes[i + 1])))
                        grid.AddLoad(cell);
            return grid;
        }

        /// <summary>Casillas estrictamente entre dos casillas alineadas.</summary>
        private static IEnumerable<Cell> Between(Cell a, Cell b)
        {
            int dr = Math.Sign(b.Row - a.Row), dc = Math.Sign(b.Col - a.Col);
            var c = new Cell(a.Row + dr, a.Col + dc);
            while (c != b)
            {
                yield return c;
                c = new Cell(c.Row + dr, c.Col + dc);
            }
        }

        /// <summary>
        /// Dibujo en texto (para depurar y para las pruebas): <c>.</c> libre, <c>#</c> ocupado,
        /// <c>~</c> con carga, <c>*</c> camino, <c>S</c>/<c>G</c> inicio y destino.
        /// </summary>
        /// <param name="rowLabels">Nombre de cada fila (opcional), p. ej. "T+", "a"...</param>
        public string Render(IReadOnlyList<Cell> path = null, IReadOnlyList<string> rowLabels = null)
        {
            var onPath = new HashSet<Cell>(path ?? Array.Empty<Cell>());
            int labelWidth = rowLabels?.Max(l => l.Length) ?? 0;
            var sb = new StringBuilder();
            for (int r = 0; r < Rows; r++)
            {
                if (rowLabels != null) sb.Append(rowLabels[r].PadLeft(labelWidth)).Append(' ');
                for (int c = 0; c < Cols; c++)
                {
                    var cell = new Cell(r, c);
                    char ch = IsBlocked(cell) ? '#' : LoadAt(cell) > 0 ? '~' : '.';
                    if (onPath.Contains(cell)) ch = '*';
                    if (path != null && path.Count > 0 && cell == path[0]) ch = 'S';
                    if (path != null && path.Count > 0 && cell == path[path.Count - 1]) ch = 'G';
                    sb.Append(ch);
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Etiquetas de las 14 filas de la protoboard, para <see cref="Render"/>.</summary>
        public static readonly IReadOnlyList<string> BreadboardRowLabels =
            Enum.GetValues(typeof(BreadboardRow)).Cast<BreadboardRow>()
                .Select(r => new HoleId(r, 1).Name.TrimEnd('1')).ToArray();
    }
}
