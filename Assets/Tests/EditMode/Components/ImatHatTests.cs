using System;
using System.Collections.Generic;
using System.Linq;
using ImatPiSim.Domain.Components;
using ImatPiSim.Domain.Electrical;
using NUnit.Framework;

namespace ImatPiSim.Domain.Tests.Components
{
    /// <summary>Etapa E4: componentes (Composite, Flyweight, Factory) y la IMAT HAT completa.</summary>
    public class ImatHatTests
    {
        private RaspberryPi _pi;
        private ImatHat _hat;
        private Netlist _netlist;
        private NodalSolver _solver;

        [SetUp]
        public void SetUp()
        {
            _pi = new RaspberryPi();
            _hat = new ImatHat(_pi);
            _netlist = new NetlistBuilder().AddComponent(_pi).AddComponent(_hat).Build();
            _solver = new NodalSolver();
        }

        [Test]
        public void T1_Gpio20High_EnciendeSoloD1_ConUnos4mA()
        {
            _pi.Gpio(20).ConfigureAsOutput(true);

            _solver.Solve(_netlist);

            // GPIO20 ── R1 (270 Ω) ── D1 ── JP1 ── GND, con los supuestos del simulador
            double expected = (3.3 - Led.DefaultForwardVoltage) /
                (GpioPin.DefaultOutputResistance + 270 + Led.DefaultSeriesResistance + SolderJumper.BridgeResistance);
            Assert.That(_hat.Led(1).Current, Is.EqualTo(expected).Within(1e-9));
            for (int n = 2; n <= 8; n++)
                Assert.That(_hat.Led(n).Current, Is.EqualTo(0).Within(1e-9), $"D{n} debería estar apagado");
            Assert.That(FaultDetector.Detect(_netlist), Is.Empty);
        }

        [Test]
        public void T2_Sw1Pulsado_Gpio7LeeLow_YLosDemasHigh()
        {
            foreach (var bcm in ImatHat.ButtonGpio) _pi.Gpio(bcm).ConfigureAsInput(PullMode.Up);

            _hat.Button(1).Press();
            _solver.Solve(_netlist);

            Assert.That(_pi.Gpio(7).Read(), Is.False, "SW1 pulsado → GPIO7 a GND");
            Assert.That(_pi.Gpio(16).Read(), Is.True);
            Assert.That(_pi.Gpio(17).Read(), Is.True);
            Assert.That(_pi.Gpio(19).Read(), Is.True);
        }

        [Test]
        public void T3_Rv1AlCincuentaPorCiento_DaUnVoltioSesentaYCincoEnCh7()
        {
            _hat.Potentiometer.Position = 0.5;

            _solver.Solve(_netlist);

            Assert.That(_hat.Adc.ChannelVoltage(7), Is.EqualTo(1.65).Within(1e-6));
        }

        [Test]
        public void T4_Jp1Cortado_D1aD4NoSeEncienden_PeroD5Si()
        {
            for (int bcm = 20; bcm <= 24; bcm++) _pi.Gpio(bcm).ConfigureAsOutput(true);

            _hat.Jp1.Cut();
            _solver.Solve(_netlist);

            for (int n = 1; n <= 4; n++)
                Assert.That(_hat.Led(n).Current, Is.EqualTo(0).Within(1e-9), $"D{n} sin retorno a GND");
            Assert.That(_hat.Led(5).Current, Is.GreaterThan(0.004));

            // Volver a soldar el puente recupera los LED
            _hat.Jp1.Solder();
            _solver.Solve(_netlist);
            Assert.That(_hat.Led(1).Current, Is.GreaterThan(0.004));
        }

        [Test]
        public void Jp3HaciaJ1_Ch7DejaDeLeerElPotenciometro()
        {
            _hat.Potentiometer.Position = 0.8;
            _hat.Jp3.Selection = JumperSelection.Pad3;   // CH7 ← pin 8 de J1, que está al aire

            _solver.Solve(_netlist);

            Assert.That(_hat.Adc.ChannelVoltage(7), Is.EqualTo(0).Within(1e-9));
        }

        [Test]
        public void CanalesExternosDeJ1_LeenLoQueSeLesConecte()
        {
            // Divisor externo de 10 kΩ / 10 kΩ desde 3,3 V hacia J1.1 (CH0)
            var ra = new Resistor("Ra", 10_000);
            var rb = new Resistor("Rb", 10_000);
            var netlist = new NetlistBuilder().AddComponent(_pi).AddComponent(_hat).AddComponent(ra).AddComponent(rb)
                .Connect(_hat.GetPin("J12.3"), ra.GetPin("A"))
                .Connect(ra.GetPin("B"), _hat.GetPin("J1.1"))
                .Connect(_hat.GetPin("J1.1"), rb.GetPin("A"))
                .Connect(rb.GetPin("B"), _hat.GetPin("J12.2"))
                .Build();

            _solver.Solve(netlist);

            Assert.That(_hat.Adc.ChannelVoltage(0), Is.EqualTo(1.65).Within(1e-6));
        }

        [Test]
        public void PinesDeLosConectores_SonLosMismosTerminalesQueLosDeLaPi()
        {
            Assert.That(_hat.GetPin("J7.1").Terminal, Is.SameAs(_pi.Gpio(14).Pin));   // servo
            Assert.That(_hat.GetPin("J10.1").Terminal, Is.SameAs(_pi.Gpio(23).Pin));
            Assert.That(_hat.GetPin("J11.1").Terminal, Is.SameAs(_pi.Gpio(20).Pin));  // LEDs
            Assert.That(_hat.GetPin("J3.1").Terminal, Is.SameAs(_pi.Gpio(7).Pin));    // pulsadores
            Assert.That(_hat.GetPin("J2.2").Terminal, Is.SameAs(_pi.Gpio(3).Pin));    // I2C SCL
            Assert.That(_hat.GetPin("J12.1").Terminal, Is.SameAs(_pi.Rail5V.Terminal));
            Assert.Throws<ArgumentException>(() => _hat.GetPin("J99.1"));
        }

        [Test]
        public void Composite_LaHatEntregaLosElementosDeTodosSusHijos()
        {
            // 8 LED + 8 R + 4 SW + RV1 + U1 + JP1 + JP2 + JP3 = 25
            Assert.That(_hat.Children, Has.Count.EqualTo(25));
            Assert.That(_hat.ElectricalElements().Count(), Is.EqualTo(25));
            Assert.That(_hat.ElectricalElements(), Contains.Item(_hat.Led(3)));
            // La Pi aporta 3 raíles + 28 GPIO
            Assert.That(_pi.ElectricalElements().Count(), Is.EqualTo(31));
        }

        [Test]
        public void Flyweight_TodosLosLedsCompartenElMismoTipo()
        {
            var suelto = new Led("Dext");

            Assert.That(_hat.Led(1).Type, Is.SameAs(_hat.Led(8).Type));
            Assert.That(suelto.Type, Is.SameAs(ComponentTypes.Led));
            Assert.That(_hat.LedResistor(1).Type, Is.SameAs(ComponentTypes.Resistor));
            Assert.That(ComponentTypes.Resistor.PinNames, Is.EqualTo(new[] { "A", "B" }));
        }

        [Test]
        public void Factory_CreaComponentesPorTipo_SinConocerLaClase()
        {
            IComponentFactory factory = ComponentFactory.CreateDefault();

            var r = factory.Create("resistor", "R10", new Dictionary<string, double> { ["resistance"] = 330 });
            var pot = factory.Create("potentiometer", "P1");   // parámetros por defecto

            Assert.That(r, Is.TypeOf<Resistor>());
            Assert.That(((Resistor)r).Resistance, Is.EqualTo(330));
            Assert.That(r.Id, Is.EqualTo("R10"));
            Assert.That(((Potentiometer)pot).TotalResistance, Is.EqualTo(10_000));
            Assert.Throws<ArgumentException>(() => factory.Create("condensador", "C1"));
            Assert.Throws<ArgumentException>(() =>
                factory.Create("resistor", "R", new Dictionary<string, double> { ["color"] = 1 }));
        }

        [Test]
        public void StateChanged_AvisaALasVistasAlPulsar()
        {
            int avisos = 0;
            _hat.Button(2).StateChanged += _ => avisos++;

            _hat.Button(2).Press();
            _hat.Button(2).Press();   // ya estaba pulsado: no hay cambio
            _hat.Button(2).Release();

            Assert.That(avisos, Is.EqualTo(2));
        }

        [Test]
        public void Reset_DevuelveLaHatYLaPiAlEstadoInicial()
        {
            _hat.Button(1).Press();
            _hat.Jp1.Cut();
            _hat.Jp3.Selection = JumperSelection.Pad3;
            _pi.Gpio(20).ConfigureAsOutput(true);

            _hat.Reset();
            _pi.Reset();

            Assert.That(_hat.Button(1).IsPressed, Is.False);
            Assert.That(_hat.Jp1.IsBridged, Is.True);
            Assert.That(_hat.Jp3.Selection, Is.EqualTo(JumperSelection.Pad1));
            Assert.That(_pi.Gpio(20).Mode, Is.EqualTo(PinMode.Input));
        }
    }

    public class PotentiometerAndLdrTests
    {
        [TestCase(0.0, 0.0)]
        [TestCase(0.25, 0.825)]
        [TestCase(1.0, 3.3)]
        public void Potenciometro_EnVacio_DaTresTresPorLaPosicion(double position, double expected)
        {
            var v33 = new VoltageRail("3V3", 3.3);
            var gnd = VoltageRail.Ground();
            var pot = new Potentiometer("RV", 10_000, position);
            var netlist = new NetlistBuilder().Add(v33, gnd).AddComponent(pot)
                .Connect(v33.Terminal, pot.End1).Connect(pot.End3, gnd.Terminal).Build();

            new NodalSolver().Solve(netlist);

            Assert.That(pot.WiperVoltage, Is.EqualTo(expected).Within(1e-5));
        }

        [Test]
        public void Ldr_EnLosPuntosDeLaTabla_DaElValorMedido()
        {
            Assert.That(Ldr.ResistanceAt(100), Is.EqualTo(3700).Within(1e-9));
            Assert.That(Ldr.ResistanceAt(1000), Is.EqualTo(840).Within(1e-9));
        }

        [Test]
        public void Ldr_EntrePuntos_InterpolaEnLogLog_YDecreceConLaLuz()
        {
            // Entre 100 lx (3700 Ω) y 150 lx (2850 Ω): R(L) = 3700·(L/100)^pendiente
            double slope = Math.Log(2850.0 / 3700) / Math.Log(150.0 / 100);
            Assert.That(Ldr.ResistanceAt(120), Is.EqualTo(3700 * Math.Pow(1.2, slope)).Within(1e-9));

            double previous = double.MaxValue;
            foreach (var lux in new[] { 0.0, 1, 10, 40, 99, 101, 500, 1200, 5000 })
            {
                double r = Ldr.ResistanceAt(lux);
                Assert.That(r, Is.LessThanOrEqualTo(previous), $"a {lux} lx");
                previous = r;
            }
        }

        [Test]
        public void Ldr_SinLuz_UsaLaResistenciaDeOscuridad()
        {
            Assert.That(Ldr.ResistanceAt(0), Is.EqualTo(Ldr.DarkResistance));
            Assert.That(Ldr.ResistanceAt(0.001), Is.LessThanOrEqualTo(Ldr.DarkResistance));
        }
    }
}
