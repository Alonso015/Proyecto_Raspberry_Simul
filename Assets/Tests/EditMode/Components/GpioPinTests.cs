using System;
using ImatPiSim.Domain.Components;
using ImatPiSim.Domain.Electrical;
using NUnit.Framework;

namespace ImatPiSim.Domain.Tests.Components
{
    /// <summary>Etapa E2: GPIO (entrada con pull, salida con resistencia interna) y pulsador.</summary>
    public class GpioPinTests
    {
        private const double Tol = 1e-9;

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

        /// <summary>Montaje de la IMAT HAT: GPIO ── pulsador ── GND (docs/hardware.md §3.3).</summary>
        private (Netlist netlist, GpioPin gpio, PushButton sw) ButtonToGround(int bcm)
        {
            var gpio = new GpioPin(bcm);
            var sw = new PushButton("SW");
            var netlist = new NetlistBuilder()
                .Add(_gnd, gpio, sw)
                .Connect(gpio.Pin, sw.A)
                .Connect(sw.B, _gnd.Terminal)
                .Build();
            return (netlist, gpio, sw);
        }

        [Test]
        public void T1_PulsadorAGndConPullUp_ReposoHigh_PulsadoLow()
        {
            var (netlist, gpio, sw) = ButtonToGround(19);   // SW4 de la HAT
            gpio.ConfigureAsInput(PullMode.Up);

            // En reposo no circula corriente por la pull-up: no hay caída, el pin está a 3,3 V.
            _solver.Solve(netlist);
            Assert.That(gpio.Voltage, Is.EqualTo(3.3).Within(1e-6));
            Assert.That(gpio.Level, Is.EqualTo(InputLevel.High));
            Assert.That(gpio.Read(), Is.True);

            // Pulsado: divisor entre la pull-up (50 kΩ) y el contacto (0,1 Ω). El pin queda a µV.
            sw.Press();
            _solver.Solve(netlist);
            double r = GpioPin.DefaultPullResistance, rc = PushButton.DefaultContactResistance;
            Assert.That(gpio.Voltage, Is.EqualTo(3.3 * rc / (r + rc)).Within(1e-9));
            Assert.That(gpio.Level, Is.EqualTo(InputLevel.Low));
            Assert.That(gpio.Read(), Is.False);
            // La corriente que circula es la de la pull-up: unos 66 µA, inofensiva.
            Assert.That(sw.Current, Is.EqualTo(3.3 / (r + rc)).Within(1e-9));

            // Soltar vuelve a HIGH: el mismo circuito se puede resolver tantas veces como cambie.
            sw.Release();
            _solver.Solve(netlist);
            Assert.That(gpio.Read(), Is.True);
        }

        [Test]
        public void T2_SinPullYSinPulsar_ElNodoFlota_YElSimuladorLoLleva_ACeroVoltios()
        {
            // Aproximación de la simulación: un nodo flotante queda a 0 V por gmin.
            // En la placa real la tensión no está definida: la entrada capta ruido y puede leer
            // 0 o 1 al azar. Por eso los pulsadores necesitan pull-up.
            var (netlist, gpio, _) = ButtonToGround(19);
            gpio.ConfigureAsInput(PullMode.None);

            _solver.Solve(netlist);

            Assert.That(gpio.Voltage, Is.EqualTo(0).Within(Tol));
            Assert.That(gpio.Level, Is.EqualTo(InputLevel.Low));
            Assert.That(gpio.Current, Is.EqualTo(0));
        }

        [Test]
        public void T3_SalidaHighConUnaResistenciaDe1kAGnd_DaUnos3Coma3mA()
        {
            // Con la resistencia interna (40 Ω, supuesto) la corriente es 3,3 / 1040 ≈ 3,17 mA,
            // algo menos que los 3,3 mA ideales; el pin baja a ≈ 3,17 V.
            var gpio = new GpioPin(20);
            var r = new Resistor("R", 1000);
            var netlist = new NetlistBuilder()
                .Add(_gnd, gpio, r)
                .Connect(gpio.Pin, r.A)
                .Connect(r.B, _gnd.Terminal)
                .Build();
            gpio.ConfigureAsOutput(true);

            _solver.Solve(netlist);

            double expected = 3.3 / (1000 + GpioPin.DefaultOutputResistance);
            Assert.That(gpio.Current, Is.EqualTo(expected).Within(Tol));
            Assert.That(r.Current, Is.EqualTo(expected).Within(Tol));
            Assert.That(gpio.Current, Is.EqualTo(3.3e-3).Within(0.2e-3));   // "≈ 3,3 mA"
            Assert.That(gpio.Voltage, Is.EqualTo(expected * 1000).Within(Tol));
        }

        [Test]
        public void SalidaLowConResistenciaDesde3V3_ElPinAbsorbeCorriente()
        {
            var gpio = new GpioPin(21);
            var r = new Resistor("R", 1000);
            var netlist = new NetlistBuilder()
                .Add(_v33, gpio, r)
                .Connect(_v33.Terminal, r.A)
                .Connect(r.B, gpio.Pin)
                .Build();
            gpio.ConfigureAsOutput(false);

            _solver.Solve(netlist);

            Assert.That(gpio.Current, Is.EqualTo(-3.3 / (1000 + GpioPin.DefaultOutputResistance)).Within(Tol));
        }

        [Test]
        public void PullDown_SinNadaConectado_LeeLow()
        {
            var (netlist, gpio, _) = ButtonToGround(16);
            gpio.ConfigureAsInput(PullMode.Down);

            _solver.Solve(netlist);

            Assert.That(gpio.Voltage, Is.EqualTo(0).Within(Tol));
            Assert.That(gpio.Read(), Is.False);
        }

        [Test]
        public void TensionEntreUmbrales_EsIndefinida_YSeMantieneLaUltimaLectura()
        {
            // Pull-up interna + pulsador + 50 kΩ a GND: al pulsar queda un divisor 50k/50k → 1,65 V,
            // entre V_IL (0,8 V) y V_IH (2,0 V).
            var gpio = new GpioPin(17);
            var sw = new PushButton("SW");
            var r = new Resistor("R", GpioPin.DefaultPullResistance);
            var netlist = new NetlistBuilder()
                .Add(_gnd, gpio, sw, r)
                .Connect(gpio.Pin, sw.A)
                .Connect(sw.B, r.A)
                .Connect(r.B, _gnd.Terminal)
                .Build();
            gpio.ConfigureAsInput(PullMode.Up);

            _solver.Solve(netlist);
            Assert.That(gpio.Read(), Is.True);

            sw.Press();
            _solver.Solve(netlist);
            Assert.That(gpio.Voltage, Is.EqualTo(1.65).Within(1e-4));
            Assert.That(gpio.Level, Is.EqualTo(InputLevel.Undefined));
            Assert.That(gpio.Read(), Is.True, "en la zona indefinida se mantiene la última lectura válida");
        }

        [Test]
        public void EscribirEnUnPinDeEntrada_EsUnError()
        {
            var gpio = new GpioPin(20);
            Assert.Throws<InvalidOperationException>(() => gpio.Write(true));
        }

        [Test]
        public void PinNuevo_EmpiezaComoEntradaSinPull()
        {
            var gpio = new GpioPin(4);
            Assert.That(gpio.Mode, Is.EqualTo(PinMode.Input));
            Assert.That(gpio.Pull, Is.EqualTo(PullMode.None));
        }
    }
}
