using System;
using System.Collections.Generic;
using System.Linq;
using ImatPiSim.Domain.Simulation;
using ImatPiSim.Domain.Components;
using ImatPiSim.Domain.Wiring;
using ImatPiSim.Domain.Wiring.Routing;
using NUnit.Framework;

namespace ImatPiSim.Domain.Tests.Wiring
{
    /// <summary>Etapa E13: autorouter A* (y el router en L para comparar).</summary>
    public class RouterTests
    {
        private static readonly AStarRouter AStar = new AStarRouter();
        private static readonly LRouter LShape = new LRouter();

        private static Cell C(int r, int c) => new Cell(r, c);

        private static void Show(RoutingGrid grid, Route route, IReadOnlyList<string> labels = null) =>
            TestContext.Out.WriteLine($"coste {route.Cost}, giros {route.Turns}, expandidos {route.Expanded}\n" +
                                      grid.Render(route.Cells, labels));

        // ------------------------------------------------------------------ casos calculables a mano

        [Test]
        public void LineaRecta_Cuesta4()
        {
            var grid = new RoutingGrid(5, 5);
            var r = AStar.FindRoute(grid, C(0, 0), C(0, 4));
            Assert.That(r.Cost, Is.EqualTo(4));
            Assert.That(r.Cells, Has.Count.EqualTo(5));
            Assert.That(r.Turns, Is.EqualTo(0));
        }

        [Test]
        public void EnDiagonal_UnSoloGiro()
        {
            // De (0,0) a (3,3): 6 pasos como mínimo y al menos 1 giro → 7.
            var grid = new RoutingGrid(5, 5);
            var r = AStar.FindRoute(grid, C(0, 0), C(3, 3));
            Show(grid, r);
            Assert.That(r.Cost, Is.EqualTo(7));
            Assert.That(r.Turns, Is.EqualTo(1), "una L: el giro cuesta, así que no hace escaleras");
            Assert.That(LShape.FindRoute(grid, C(0, 0), C(3, 3)).Cost, Is.EqualTo(7), "sin obstáculos, la L es óptima");
        }

        [Test]
        public void RodeaUnMuro_YLaLNoPuede()
        {
            // Muro en la columna 3, filas 0–3. Hay que bajar a la fila 4:
            // 4 abajo + 6 a la derecha + 4 arriba = 14 pasos y 2 giros → 16.
            var grid = new RoutingGrid(5, 7);
            for (int r = 0; r <= 3; r++) grid.Block(C(r, 3));
            var route = AStar.FindRoute(grid, C(0, 0), C(0, 6));
            Show(grid, route);
            Assert.That(route.Cost, Is.EqualTo(16));
            Assert.That(route.Cells, Has.Member(C(4, 3)));
            Assert.That(route.Cells.Any(grid.IsBlocked), Is.False);

            Assert.That(LShape.FindRoute(grid, C(0, 0), C(0, 6)).Found, Is.False, "las dos L chocan con el muro");
        }

        [Test]
        public void CruzaUnCable_SiRodearSaleMasCaro()
        {
            // Cable ya tendido en toda la fila 1 menos la última columna.
            // Cruzar: 2 pasos + 5 = 7. Rodear por la columna 4: 6 pasos + 2 giros = 8. → cruza.
            var grid = new RoutingGrid(3, 5);
            for (int c = 0; c <= 3; c++) grid.AddLoad(C(1, c));
            var r = AStar.FindRoute(grid, C(0, 2), C(2, 2));
            Show(grid, r);
            Assert.That(r.Cost, Is.EqualTo(7));
            Assert.That(r.Cells, Is.EqualTo(new[] { C(0, 2), C(1, 2), C(2, 2) }));
        }

        [Test]
        public void RodeaUnCable_SiSaleMasBarato()
        {
            // El hueco está en la columna 3: rodear = 4 pasos + 2 giros = 6 < 7 de cruzar.
            var grid = new RoutingGrid(3, 5);
            for (int c = 0; c <= 2; c++) grid.AddLoad(C(1, c));
            grid.AddLoad(C(1, 4));
            var r = AStar.FindRoute(grid, C(0, 2), C(2, 2));
            Show(grid, r);
            Assert.That(r.Cost, Is.EqualTo(6));
            Assert.That(r.Cells, Has.Member(C(1, 3)));
        }

        [Test]
        public void SinCaminoPosible_ResultadoVacio()
        {
            var grid = new RoutingGrid(5, 5);
            foreach (var n in new[] { C(1, 2), C(3, 2), C(2, 1), C(2, 3) }) grid.Block(n);   // el destino (2,2) encerrado
            var r = AStar.FindRoute(grid, C(0, 0), C(2, 2));
            Assert.That(r.Found, Is.False);
            Assert.That(r.Cells, Is.Empty);
            Assert.That(r.Cost, Is.EqualTo(double.PositiveInfinity));
        }

        [Test]
        public void ExtremosNoValidos_ResultadoVacio()
        {
            var grid = new RoutingGrid(3, 3).Block(C(2, 2));
            Assert.That(AStar.FindRoute(grid, C(0, 0), C(2, 2)).Found, Is.False, "destino ocupado");
            Assert.That(AStar.FindRoute(grid, C(0, 0), C(0, 0)).Found, Is.False, "mismo agujero");
            Assert.That(AStar.FindRoute(grid, C(0, 0), C(5, 0)).Found, Is.False, "fuera de la rejilla");
        }

        [Test]
        public void ElCosteDeclarado_EsElDelCamino()
        {
            var rnd = new Random(7);
            int found = 0;
            for (int n = 0; n < 50; n++)
            {
                var grid = RandomGrid(rnd, 8, 10);
                var (from, to) = RandomEnds(rnd, grid);
                var r = AStar.FindRoute(grid, from, to);
                if (!r.Found) continue;
                found++;
                Assert.That(RoutingCosts.Default.Evaluate(grid, r.Cells), Is.EqualTo(r.Cost));
            }
            Assert.That(found, Is.GreaterThan(20));
        }

        // ------------------------------------------------------------------ heurística y optimalidad

        [Test]
        public void LaHeuristica_NoSobreestima()
        {
            // En cada casilla del camino óptimo, h ≤ coste real de lo que queda.
            var rnd = new Random(1234);
            int checkedCells = 0;
            for (int n = 0; n < 200; n++)
            {
                var grid = RandomGrid(rnd, 8, 12);
                var (from, to) = RandomEnds(rnd, grid);
                var r = AStar.FindRoute(grid, from, to);
                if (!r.Found) continue;
                for (int i = 0; i < r.Cells.Count; i++)
                {
                    double remaining = r.Cost - Prefix(grid, r.Cells, i);
                    Assert.That(AStar.Heuristic(r.Cells[i], to), Is.LessThanOrEqualTo(remaining + 1e-9));
                    checkedCells++;
                }
            }
            Assert.That(checkedCells, Is.GreaterThan(500));
        }

        [Test]
        public void AStar_DaElMismoCosteQueUnaBusquedaExhaustiva()
        {
            // Dijkstra (sin heurística) explora todo: si A* coincide en 300 rejillas al azar, es óptimo.
            var rnd = new Random(42);
            int found = 0, expandedAStar = 0, expandedDijkstra = 0;
            for (int n = 0; n < 300; n++)
            {
                var grid = RandomGrid(rnd, 9, 14);
                var (from, to) = RandomEnds(rnd, grid);
                var r = AStar.FindRoute(grid, from, to);
                var (best, expanded) = Dijkstra(grid, from, to, RoutingCosts.Default);
                Assert.That(r.Cost, Is.EqualTo(best).Within(1e-9), grid.Render(r.Cells));
                if (r.Found) { found++; expandedAStar += r.Expanded; expandedDijkstra += expanded; }
            }
            Assert.That(found, Is.GreaterThan(150));
            TestContext.Out.WriteLine($"estados expandidos: A* {expandedAStar}, Dijkstra {expandedDijkstra}");
            Assert.That(expandedAStar, Is.LessThan(expandedDijkstra), "la heurística ahorra trabajo");
        }

        [Test]
        public void LaL_NuncaEsMejorQueAStar()
        {
            var rnd = new Random(99);
            int worse = 0, lFails = 0;
            for (int n = 0; n < 200; n++)
            {
                var grid = RandomGrid(rnd, 8, 12);
                var (from, to) = RandomEnds(rnd, grid);
                var a = AStar.FindRoute(grid, from, to);
                var l = LShape.FindRoute(grid, from, to);
                Assert.That(a.Cost, Is.LessThanOrEqualTo(l.Cost));
                if (a.Found && !l.Found) lFails++;
                else if (l.Cost > a.Cost) worse++;
            }
            TestContext.Out.WriteLine($"de 200: la L no encuentra camino en {lFails} y lo encuentra más caro en {worse}");
            Assert.That(lFails + worse, Is.GreaterThan(0));
        }

        // ------------------------------------------------------------------ sobre la protoboard

        [Test]
        public void EnLaProtoboard_RodeaLasPatillasDeUnaResistencia()
        {
            // R1 en d5–d9: sus patillas ocupan d5 y d9 y su cuerpo pasa por encima de d6–d8.
            var c = Circuit.WithImatHat();
            c.Place(new Resistor("R1", 330), "d5", Direction.Right);
            var wire = c.RouteWire("d4", "d10", AStar);

            var grid = RoutingGrid.FromCircuit(c);
            var cells = wire.Route.Select(Cell.FromHole).ToList();
            TestContext.Out.WriteLine(string.Join(" ", wire.Route) + "\n" + grid.Render(cells, RoutingGrid.BreadboardRowLabels));

            Assert.That(wire.Route.First().Name, Is.EqualTo("d4"));
            Assert.That(wire.Route.Last().Name, Is.EqualTo("d10"));
            Assert.That(wire.Route.Select(h => h.Name), Has.No.Member("d5").And.No.Member("d9"));
            Assert.That(wire.Route.Select(h => h.Name).Any(n => n.StartsWith("d") && n != "d4" && n != "d10"), Is.False,
                        "no pasa por encima del cuerpo: rodea por la fila c o la e");
            // 6 pasos a lo largo + 2 para salir y volver de la fila d = 8, y 2 giros → 10
            Assert.That(RoutingCosts.Default.Evaluate(RoutingGridWithout(c, wire), cells), Is.EqualTo(10));
        }

        [Test]
        public void UnSegundoCable_TieneEnCuentaElPrimero()
        {
            var c = Circuit.WithImatHat();
            c.RouteWire("a3", "e3", AStar);                   // vertical en la columna 3: a3 y e3 ocupados
            var grid = RoutingGrid.FromCircuit(c);
            Assert.That(grid.LoadAt(Cell.FromHole(HoleId.Parse("c3"))), Is.EqualTo(1), "el primer cable pasa por c3");
            Assert.That(grid.IsBlocked(Cell.FromHole(HoleId.Parse("a3"))), Is.True, "su extremo ocupa a3");

            var w2 = c.RouteWire("c1", "c6", AStar);          // tiene que pasar al otro lado de la columna 3
            var cells = w2.Route.Select(Cell.FromHole).ToList();
            TestContext.Out.WriteLine(grid.Render(cells, RoutingGrid.BreadboardRowLabels));

            // Cruzar por c3: 5 pasos + 5 = 10. Rodear por T− (a3 y e3 están ocupados):
            // 3 arriba + 5 + 3 abajo = 11 pasos + 2 giros = 13. → cruza, y lo sabe.
            Assert.That(w2.Route.Select(h => h.Name), Has.Member("c3"));
            Assert.That(RoutingCosts.Default.Evaluate(grid, cells), Is.EqualTo(10));
        }

        [Test]
        public void ElCableTrazadoConectaComoCualquierOtro()
        {
            var c = Circuit.WithImatHat();
            c.AddWire("J7.1", "a5");
            c.Place(new Resistor("R1", 330), "b5", Direction.Right);
            var led = (Led)c.Place(new Led("LED1"), "c9", Direction.Right).Component;
            c.RouteWire("d10", "d14", AStar);                  // puente trazado por el A*
            c.AddWire("e14", "T-14");
            c.AddWire("J7.3", "T-1");

            var sim = new SimulatorFacade(c);
            Assert.That(sim.LoadProgram("from gpiozero import LED\nfrom signal import pause\nLED(14).on()\npause()\n").Success);
            sim.Run();
            for (int i = 0; i < 4; i++) sim.Tick(0.05);
            Assert.That(led.Current, Is.EqualTo(1.3 / 380).Within(0.02e-3));
        }

        [Test]
        public void SoloSeTrazanCablesEntreAgujeros()
        {
            var c = Circuit.WithImatHat();
            Assert.Throws<AssemblyException>(() => c.RouteWire("J7.1", "a5", AStar));
        }

        [Test]
        public void SinCaminoEnLaProtoboard_Excepcion()
        {
            var c = Circuit.WithImatHat();
            // Encierra c10 con cables en sus cuatro vecinos.
            foreach (var (a, b) in new[] { ("b10", "b12"), ("d10", "d12"), ("c9", "c7"), ("c11", "c13") })
                c.AddWire(a, b);
            var ex = Assert.Throws<AssemblyException>(() => c.RouteWire("c10", "a1", AStar));
            Assert.That(ex.Message, Does.Contain("No hay camino"));
        }

        [Test]
        public void UnRecorridoQueSalta_SeRechaza()
        {
            var c = Circuit.WithImatHat();
            var route = new[] { "a1", "a2", "a4" }.Select(HoleId.Parse).ToList();
            Assert.Throws<AssemblyException>(() => c.AddWire(c.PinByName("a1"), c.PinByName("a4"), route));
        }

        // ------------------------------------------------------------------ utilidades

        /// <summary>Rejilla del circuito sin el propio cable (para medir su coste como lo vio el router).</summary>
        private static RoutingGrid RoutingGridWithout(Circuit c, Wire wire)
        {
            c.RemoveWire(wire);
            var grid = RoutingGrid.FromCircuit(c);
            c.AddWire(wire.A, wire.B, wire.Route);
            return grid;
        }

        private static RoutingGrid RandomGrid(Random rnd, int rows, int cols)
        {
            var grid = new RoutingGrid(rows, cols);
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    double x = rnd.NextDouble();
                    if (x < 0.22) grid.Block(C(r, c));
                    else if (x < 0.40) grid.AddLoad(C(r, c), 1 + rnd.Next(2));
                }
            return grid;
        }

        private static (Cell, Cell) RandomEnds(Random rnd, RoutingGrid grid)
        {
            Cell Pick()
            {
                while (true)
                {
                    var c = C(rnd.Next(grid.Rows), rnd.Next(grid.Cols));
                    if (!grid.IsBlocked(c)) return c;
                }
            }
            var a = Pick();
            Cell b;
            do b = Pick(); while (b == a);
            return (a, b);
        }

        /// <summary>Coste de los <paramref name="count"/> primeros pasos de un camino.</summary>
        private static double Prefix(RoutingGrid grid, IReadOnlyList<Cell> path, int count)
        {
            if (count == 0) return 0;
            double total = 0;
            Direction? previous = null;
            for (int i = 1; i <= count; i++)
            {
                var dir = Dir(path[i - 1], path[i]);
                total += RoutingCosts.Default.StepCost(grid, path[i], dir, previous, i == path.Count - 1);
                previous = dir;
            }
            return total;
        }

        private static Direction Dir(Cell a, Cell b) =>
            b.Col > a.Col ? Direction.Right : b.Col < a.Col ? Direction.Left : b.Row > a.Row ? Direction.Down : Direction.Up;

        /// <summary>Búsqueda exhaustiva de coste uniforme (Dijkstra) sobre (casilla, dirección), escrita aparte como referencia.</summary>
        private static (double cost, int expanded) Dijkstra(RoutingGrid grid, Cell from, Cell to, RoutingCosts costs)
        {
            if (from == to || grid.IsBlocked(from) || grid.IsBlocked(to)) return (double.PositiveInfinity, 0);
            var dist = new Dictionary<(Cell, int), double> { [(from, 4)] = 0 };
            var done = new HashSet<(Cell, int)>();
            var frontier = new List<((Cell cell, int dir) state, double d)> { ((from, 4), 0) };
            int expanded = 0;
            while (frontier.Count > 0)
            {
                int k = 0;
                for (int i = 1; i < frontier.Count; i++) if (frontier[i].d < frontier[k].d) k = i;
                var (state, d) = frontier[k];
                frontier.RemoveAt(k);
                if (!done.Add(state)) continue;
                expanded++;
                if (state.cell == to) return (d, expanded);
                Direction? prev = state.dir == 4 ? (Direction?)null : (Direction)state.dir;
                foreach (var (next, dir) in grid.Neighbours(state.cell))
                {
                    bool goal = next == to;
                    if (grid.IsBlocked(next) && !goal) continue;
                    double nd = d + costs.StepCost(grid, next, dir, prev, goal);
                    var key = (next, (int)dir);
                    if (dist.TryGetValue(key, out var old) && old <= nd) continue;
                    dist[key] = nd;
                    frontier.Add((key, nd));
                }
            }
            return (double.PositiveInfinity, expanded);
        }
    }
}
