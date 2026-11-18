using System;
using ImatPiSim.Domain.Components;
using ImatPiSim.Domain.Electrical;
using NUnit.Framework;

namespace ImatPiSim.Domain.Tests.Electrical
{
    /// <summary>Etapa E1: solver nodal con resistencias (docs/hoja_de_ruta.md).</summary>
    public class NodalSolverTests
    {
        private const double Tol = 1e-9;   // V o A; gmin introduce un error ~1e-9 relativo

        private VoltageRail _v33;
        private VoltageRail _gnd;
        private NodalSolver _solver;

        [SetUp]
        public void SetUp()
        {
            _v33 = new VoltageRail("3V3", 3.3);
            _gnd = VoltageRail.Ground();
            _solver = new NodalSolver();
        }

        /// <summary>3,3 V ── R1 ── medio ── R2 ── GND.</summary>
        private (Netlist netlist, Resistor r1, Resistor r2) Divider(double r1Ohms, double r2Ohms)
        {
            var r1 = new Resistor("R1", r1Ohms);
            var r2 = new Resistor("R2", r2Ohms);
            var netlist = new NetlistBuilder()
                .Add(_v33, _gnd, r1, r2)
                .Connect(_v33.Terminal, r1.A)
                .Connect(r1.B, r2.A)
                .Connect(r2.B, _gnd.Terminal)
                .Build();
            return (netlist, r1, r2);
        }

        [Test]
        public void T1_DivisorIgual_DaLaMitadDeLaTension()
        {
            var (netlist, r1, _) = Divider(1000, 1000);

            var s = _solver.Solve(netlist);

            Assert.That(s.VoltageAt(r1.B.Node), Is.EqualTo(1.65).Within(Tol));
            TestContext.WriteLine(_solver.LastSystem.Describe());
        }

        [Test]
        public void T2_Divisor270y1k_CoincideConElCalculoAMano()
        {
            // Vmedio = 3,3 · 1000 / (270 + 1000) = 3300 / 1270
            double expected = 3.3 * 1000.0 / 1270.0;   // ≈ 2,598425 V
            var (netlist, r1, _) = Divider(270, 1000);

            var s = _solver.Solve(netlist);

            Assert.That(s.VoltageAt(r1.B.Node), Is.EqualTo(expected).Within(Tol));
        }

        [Test]
        public void T3_CorrientePorUnaResistencia_EsVDivididoEntreR()
        {
            var r = new Resistor("R", 1000);
            var netlist = new NetlistBuilder()
                .Add(_v33, _gnd, r)
                .Connect(_v33.Terminal, r.A)
                .Connect(r.B, _gnd.Terminal)
                .Build();

            _solver.Solve(netlist);

            Assert.That(r.Current, Is.EqualTo(3.3e-3).Within(Tol));          // 3,3 mA
            Assert.That(r.VoltageDrop, Is.EqualTo(3.3).Within(Tol));
            Assert.That(r.Power, Is.EqualTo(3.3 * 3.3e-3).Within(Tol));      // 10,89 mW
        }

        [Test]
        public void T3b_ElDivisorCumpleKirchhoff_LaMismaCorrientePorLasDos()
        {
            var (netlist, r1, r2) = Divider(270, 1000);

            _solver.Solve(netlist);

            Assert.That(r1.Current, Is.EqualTo(3.3 / 1270.0).Within(Tol));
            Assert.That(r2.Current, Is.EqualTo(r1.Current).Within(Tol));
        }

        [Test]
        public void T4_NodoSinConectar_DaCeroVoltiosYNoRompeElSolver()
        {
            // R2 está suelta: sus dos terminales no tocan nada (nodos flotantes).
            var (netlist, r1, _) = Divider(1000, 1000);
            var suelta = new Resistor("Suelta", 4700);
            netlist = new NetlistBuilder()
                .Add(_v33, _gnd, r1, suelta)
                .Connect(_v33.Terminal, r1.A)
                .Connect(r1.B, _gnd.Terminal)
                .Build();

            ISolution s = null;
            Assert.DoesNotThrow(() => s = _solver.Solve(netlist));

            Assert.That(s.VoltageAt(suelta.A.Node), Is.EqualTo(0).Within(Tol));
            Assert.That(s.VoltageAt(suelta.B.Node), Is.EqualTo(0).Within(Tol));
            Assert.That(suelta.Current, Is.EqualTo(0).Within(Tol));
        }

        [Test]
        public void T4b_ResistenciaConUnExtremoAlAire_ElExtremoLibreQuedaALaTensionDelOtro()
        {
            // 3,3 V ── R ── (nada). Sin corriente no hay caída: el extremo libre está a 3,3 V.
            // Es lo que mediría un polímetro (que, con su alta impedancia, apenas carga el nodo).
            var r = new Resistor("R", 10_000);
            var netlist = new NetlistBuilder()
                .Add(_v33, _gnd, r)
                .Connect(_v33.Terminal, r.A)
                .Build();

            var s = _solver.Solve(netlist);

            Assert.That(s.VoltageAt(r.B.Node), Is.EqualTo(3.3).Within(1e-6));
            Assert.That(r.Current, Is.EqualTo(0).Within(1e-9));
        }

        [Test]
        public void T5_UnirTresTresYGnd_SeDetectaComoCortocircuitoAlConstruir()
        {
            var cable = new Resistor("Puente", 1000);   // solo para tener terminales intermedios
            var netlist = new NetlistBuilder()
                .Add(_v33, _gnd, cable)
                .Connect(_v33.Terminal, cable.A)
                .Connect(cable.A, _gnd.Terminal)       // 3,3 V y GND en el mismo nodo
                .Build();

            Assert.That(netlist.HasShortCircuit, Is.True);
            Assert.That(netlist.ShortCircuits, Has.Count.EqualTo(1));
            Assert.That(netlist.ShortCircuits[0].ToString(), Does.Contain("3V3").And.Contain("GND"));
            Assert.Throws<InvalidOperationException>(() => _solver.Solve(netlist));
        }

        [Test]
        public void T5b_DosRailesDeLaMismaTensionUnidos_NoEsCortocircuito()
        {
            var otroGnd = VoltageRail.Ground();
            var netlist = new NetlistBuilder()
                .Add(_v33, _gnd, otroGnd)
                .Connect(_gnd.Terminal, otroGnd.Terminal)
                .Build();

            Assert.That(netlist.HasShortCircuit, Is.False);
        }

        [Test]
        public void ResistenciasEnParalelo_SumanCorrientes()
        {
            // Dos de 1 kΩ en paralelo entre 3,3 V y GND: 3,3 mA cada una, 6,6 mA en total.
            var ra = new Resistor("Ra", 1000);
            var rb = new Resistor("Rb", 1000);
            var netlist = new NetlistBuilder()
                .Add(_v33, _gnd, ra, rb)
                .Connect(_v33.Terminal, ra.A).Connect(_v33.Terminal, rb.A)
                .Connect(ra.B, _gnd.Terminal).Connect(rb.B, _gnd.Terminal)
                .Build();

            _solver.Solve(netlist);

            Assert.That(ra.Current + rb.Current, Is.EqualTo(6.6e-3).Within(Tol));
        }

        [Test]
        public void MatrizDelDivisor_TieneLaFormaEsperada()
        {
            // Una sola incógnita (el nodo medio): (1/R1 + 1/R2) · Vm = 3,3 / R1  (+ gmin en la diagonal)
            var (netlist, _, _) = Divider(1000, 1000);
            _solver.Solve(netlist);
            var sys = _solver.LastSystem;

            Assert.That(sys.Unknowns, Has.Count.EqualTo(1));
            Assert.That(sys.G(0, 0), Is.EqualTo(1 / 1000.0 + 1 / 1000.0 + NodalSolver.DefaultGmin).Within(1e-15));
            Assert.That(sys.I(0), Is.EqualTo(3.3 / 1000.0).Within(1e-15));
        }

        [Test]
        public void ResistenciaNoValida_SeRechaza()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Resistor("R", 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Resistor("R", -10));
        }
    }
}
