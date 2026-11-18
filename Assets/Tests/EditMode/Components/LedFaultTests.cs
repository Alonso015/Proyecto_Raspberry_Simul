using System.Linq;
using ImatPiSim.Domain.Components;
using ImatPiSim.Domain.Electrical;
using NUnit.Framework;

namespace ImatPiSim.Domain.Tests.Components
{
    /// <summary>Etapa E3: LED lineal a trozos e iteración del solver; detección de fallos.</summary>
    public class LedFaultTests
    {
        private const double Tol = 1e-9;

        private VoltageRail _v33;
        private VoltageRail _v5;
        private VoltageRail _gnd;
        private NodalSolver _solver;

        // Supuestos (docs/requisitos.md §2.6)
        private const double Rint = GpioPin.DefaultOutputResistance;   // 40 Ω
        private const double Vf = Led.DefaultForwardVoltage;           // 2,0 V
        private const double Rs = Led.DefaultSeriesResistance;         // 10 Ω

        [SetUp]
        public void SetUp()
        {
            _v33 = new VoltageRail("3V3", 3.3);
            _v5 = new VoltageRail("5V", 5.0);
            _gnd = VoltageRail.Ground();
            _solver = new NodalSolver();
        }

        /// <summary>GPIO ── R (opcional) ── LED ── GND, como cada LED de la IMAT HAT (hardware.md §3.2).</summary>
        private (Netlist, GpioPin, Led) GpioLed(double? resistor, bool reversed = false)
        {
            var gpio = new GpioPin(20);
            var led = new Led("D1");
            var b = new NetlistBuilder().Add(_gnd, gpio, led);
            var into = reversed ? led.Cathode : led.Anode;
            var outOf = reversed ? led.Anode : led.Cathode;
            if (resistor.HasValue)
            {
                var r = new Resistor("R1", resistor.Value);
                b.Add(r).Connect(gpio.Pin, r.A).Connect(r.B, into);
            }
            else b.Connect(gpio.Pin, into);
            b.Connect(outOf, _gnd.Terminal);
            gpio.ConfigureAsOutput(true);
            return (b.Build(), gpio, led);
        }

        [Test]
        public void T1_CircuitoRealDeLaHat_Gpio270OhmLed_SeEnciendeSinFallos()
        {
            var (netlist, gpio, led) = GpioLed(270);

            _solver.Solve(netlist);

            // I = (3,3 − V_F) / (R_int + 270 + R_S) = 1,3 / 320 ≈ 4,06 mA
            double expected = (3.3 - Vf) / (Rint + 270 + Rs);
            Assert.That(led.IsConducting, Is.True);
            Assert.That(led.Current, Is.EqualTo(expected).Within(Tol));
            Assert.That(gpio.Current, Is.EqualTo(expected).Within(Tol));
            Assert.That(led.Current, Is.InRange(0.004, 0.005), "≈ 4–5 mA, como en hardware.md §3.2");
            Assert.That(FaultDetector.Detect(netlist), Is.Empty);
            Assert.That(_solver.LastIterations, Is.EqualTo(2), "1.ª: supone apagado; 2.ª: corrige a encendido");
        }

        [Test]
        public void T2_SinResistencia_SeQuemaElLed_YSeDañaElGpio()
        {
            var (netlist, gpio, led) = GpioLed(null);

            _solver.Solve(netlist);

            // I = 1,3 / (40 + 10) = 26 mA: > 20 mA del LED y > 7 mA del GPIO
            double expected = (3.3 - Vf) / (Rint + Rs);
            Assert.That(led.BurnCurrent, Is.EqualTo(expected).Within(Tol));
            var faults = FaultDetector.Detect(netlist);
            Assert.That(faults.Select(f => f.Kind),
                Is.EquivalentTo(new[] { FaultKind.LedOvercurrent, FaultKind.GpioOvercurrent }));
            Assert.That(faults.First(f => f.Kind == FaultKind.LedOvercurrent).Message, Does.Contain("D1 quemado"));
            TestContext.WriteLine(string.Join("\n", faults));

            // Un LED quemado es un circuito abierto: ya no circula corriente, pero sigue roto.
            _solver.Solve(netlist);
            Assert.That(led.IsBurnt, Is.True);
            Assert.That(led.Current, Is.EqualTo(0));
            Assert.That(gpio.IsDamaged, Is.True);
        }

        [Test]
        public void T3_LedAlReves_NoConduce()
        {
            var (netlist, _, led) = GpioLed(270, reversed: true);

            _solver.Solve(netlist);

            Assert.That(led.IsConducting, Is.False);
            Assert.That(led.Current, Is.EqualTo(0));
            Assert.That(FaultDetector.Detect(netlist), Is.Empty);
        }

        [Test]
        public void T4_SalidaHighUnidaAGnd_DañaElGpio()
        {
            var gpio = new GpioPin(20);
            var netlist = new NetlistBuilder().Add(_gnd, gpio).Connect(gpio.Pin, _gnd.Terminal).Build();
            gpio.ConfigureAsOutput(true);

            _solver.Solve(netlist);

            // Solo la resistencia interna limita: 3,3 / 40 = 82,5 mA
            Assert.That(gpio.Current, Is.EqualTo(3.3 / Rint).Within(Tol));
            var fault = FaultDetector.Detect(netlist).Single();
            Assert.That(fault.Kind, Is.EqualTo(FaultKind.GpioOvercurrent));
            Assert.That(fault.Limit, Is.EqualTo(0.007));
        }

        [Test]
        public void T5_DivisorAlimentadoA5VHaciaUnGpio_EsSobretension()
        {
            // 5 V ── 1 kΩ ── GPIO (entrada) ── 10 kΩ ── GND  →  5 · 10/11 ≈ 4,55 V en el pin
            var gpio = new GpioPin(19);
            var r1 = new Resistor("R1", 1000);
            var r2 = new Resistor("R2", 10_000);
            var netlist = new NetlistBuilder()
                .Add(_v5, _gnd, gpio, r1, r2)
                .Connect(_v5.Terminal, r1.A).Connect(r1.B, gpio.Pin)
                .Connect(gpio.Pin, r2.A).Connect(r2.B, _gnd.Terminal)
                .Build();
            gpio.ConfigureAsInput();

            _solver.Solve(netlist);

            // Tolerancia de 1 µV: gmin (1 TΩ a 0 V) desvía el resultado ~4 nV con estas resistencias.
            Assert.That(gpio.Voltage, Is.EqualTo(5.0 * 10 / 11).Within(1e-6));
            var fault = FaultDetector.Detect(netlist).Single();
            Assert.That(fault.Kind, Is.EqualTo(FaultKind.Overvoltage));
            Assert.That(fault.Message, Does.Contain("GPIO19"));
        }

        [Test]
        public void Cortocircuito_SeInformaComoFallo()
        {
            var netlist = new NetlistBuilder().Add(_v33, _gnd).Connect(_v33.Terminal, _gnd.Terminal).Build();

            var fault = FaultDetector.Detect(netlist).Single();

            Assert.That(fault.Kind, Is.EqualTo(FaultKind.ShortCircuit));
        }

        [Test]
        public void LedDesdeElRailDe3V3Con270Ohm_SinGpio()
        {
            var led = new Led("D");
            var r = new Resistor("R", 270);
            var netlist = new NetlistBuilder()
                .Add(_v33, _gnd, led, r)
                .Connect(_v33.Terminal, r.A).Connect(r.B, led.Anode).Connect(led.Cathode, _gnd.Terminal)
                .Build();

            _solver.Solve(netlist);

            Assert.That(led.Current, Is.EqualTo((3.3 - Vf) / (270 + Rs)).Within(Tol));   // ≈ 4,64 mA
            Assert.That(led.Brightness, Is.EqualTo(led.Current / Led.DefaultMaxCurrent).Within(Tol));
        }

        [Test]
        public void DosLedsEnParaleloDesdeUnGpio_SeRepartenLaCorriente_YSuperanElLimiteDelPin()
        {
            var gpio = new GpioPin(20);
            var ra = new Resistor("Ra", 270);
            var rb = new Resistor("Rb", 270);
            var la = new Led("La");
            var lb = new Led("Lb");
            var netlist = new NetlistBuilder()
                .Add(_gnd, gpio, ra, rb, la, lb)
                .Connect(gpio.Pin, ra.A).Connect(ra.B, la.Anode).Connect(la.Cathode, _gnd.Terminal)
                .Connect(gpio.Pin, rb.A).Connect(rb.B, lb.Anode).Connect(lb.Cathode, _gnd.Terminal)
                .Build();
            gpio.ConfigureAsOutput(true);

            _solver.Solve(netlist);

            // Nodo del pin Vp:  (3,3 − Vp)/40 = 2·(Vp − 2)/280  →  7·(3,3 − Vp) = 2·(Vp − 2)
            //                  →  Vp = 27,1 / 9 ≈ 3,011 V
            // Cada LED: (3,011 − 2)/280 ≈ 3,61 mA; el pin entrega ≈ 7,22 mA > 7 mA.
            // Cada LED está bien, pero el GPIO no aguanta los dos: un fallo que el jugador
            // no vería mirando solo los LED.
            Assert.That(la.Current, Is.EqualTo(lb.Current).Within(Tol));
            Assert.That(gpio.Current, Is.EqualTo(la.Current + lb.Current).Within(Tol));
            Assert.That(gpio.Current, Is.EqualTo(2 * (27.1 / 9 - Vf) / (270 + Rs)).Within(1e-9));
            var fault = FaultDetector.Detect(netlist).Single();
            Assert.That(fault.Kind, Is.EqualTo(FaultKind.GpioOvercurrent));
            Assert.That(la.IsBurnt || lb.IsBurnt, Is.False);
        }

        [Test]
        public void Reset_ReponeElLedYElGpio()
        {
            var (netlist, gpio, led) = GpioLed(null);
            _solver.Solve(netlist);
            Assume.That(led.IsBurnt && gpio.IsDamaged);

            led.Reset();
            gpio.Reset();

            Assert.That(led.IsBurnt, Is.False);
            Assert.That(gpio.IsDamaged, Is.False);
            Assert.That(FaultDetector.Detect(netlist), Is.Empty);
        }
    }
}
