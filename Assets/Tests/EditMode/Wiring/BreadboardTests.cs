using System.Linq;
using ImatPiSim.Domain.Components;
using ImatPiSim.Domain.Electrical;
using ImatPiSim.Domain.Simulation;
using ImatPiSim.Domain.Wiring;
using NUnit.Framework;

namespace ImatPiSim.Domain.Tests.Wiring
{
    /// <summary>Etapa E12: modelo de la protoboard, reglas de montaje y paso al circuito.</summary>
    public class BreadboardTests
    {
        // ------------------------------------------------------------------ geometría y tiras

        [Test]
        public void Tiene30ColumnasCon10FilasY4Railes()
        {
            var pb = new Breadboard();
            Assert.That(pb.Holes.Count(), Is.EqualTo(14 * 30));
            Assert.That(pb.Holes.Count(h => !h.IsRail), Is.EqualTo(300));
            Assert.That(pb.Contains(HoleId.Parse("j30")), Is.True);
            Assert.That(pb.Contains(HoleId.Parse("a31")), Is.False);
        }

        [TestCase("a1", "e1", true, Description = "a–e de una columna: misma tira")]
        [TestCase("f7", "j7", true)]
        [TestCase("e1", "f1", false, Description = "el canal central separa a–e de f–j")]
        [TestCase("a1", "a2", false, Description = "columnas distintas: tiras distintas")]
        [TestCase("T+1", "T+30", true, Description = "un raíl es continuo de punta a punta")]
        [TestCase("T+1", "T-1", false)]
        [TestCase("T-5", "B-5", false, Description = "los raíles de arriba y abajo no están unidos")]
        [TestCase("a1", "T+1", false)]
        public void ConexionesInternas(string a, string b, bool connected)
        {
            var pb = new Breadboard();
            Assert.That(pb.AreConnected(HoleId.Parse(a), HoleId.Parse(b)), Is.EqualTo(connected));
        }

        [Test]
        public void UnaTiraTieneCincoAgujeros_YUnRailTreinta()
        {
            var pb = new Breadboard();
            Assert.That(pb.StripOf(HoleId.Parse("c12")).Select(h => h.Name),
                        Is.EquivalentTo(new[] { "a12", "b12", "c12", "d12", "e12" }));
            Assert.That(pb.StripOf(HoleId.Parse("B+3")).Count(), Is.EqualTo(30));
        }

        [TestCase("a1")]
        [TestCase("j30")]
        [TestCase("T+5")]
        [TestCase("B-12")]
        public void NombreDeAgujero_IdaYVuelta(string name) =>
            Assert.That(HoleId.Parse(name).Name, Is.EqualTo(name));

        [TestCase("")]
        [TestCase("k3")]
        [TestCase("a")]
        [TestCase("X+3")]
        public void NombreNoValido(string name) =>
            Assert.That(HoleId.TryParse(name, out _), Is.False);

        // ------------------------------------------------------------------ colocación

        private static Circuit NewCircuit() => Circuit.WithImatHat();

        [Test]
        public void PincharUnaResistencia_OcupaDosAgujerosSeparados4()
        {
            var c = NewCircuit();
            var r = new Resistor("R1", 330);
            var p = c.Place(r, "b5", Direction.Right);
            Assert.That(p.Holes.Select(h => h.Name), Is.EqualTo(new[] { "b5", "b9" }));
            Assert.That(c.Breadboard.Occupant(HoleId.Parse("b5")), Is.SameAs(r.GetPin("A")));
            Assert.That(c.Breadboard.Occupant(HoleId.Parse("b9")), Is.SameAs(r.GetPin("B")));
            Assert.That(c.Breadboard.IsFree(HoleId.Parse("b6")), Is.True, "entre las patillas no se ocupa nada");
        }

        [Test]
        public void LaDireccionFijaLaPolaridadDelLed()
        {
            var c = NewCircuit();
            var p = c.Place(new Led("LED1"), "c9", Direction.Left);
            Assert.That(p.Holes.Select(h => h.Name), Is.EqualTo(new[] { "c9", "c8" }), "ánodo en c9, cátodo en c8");
            var q = c.Place(new Led("LED2"), "e20", Direction.Down);
            Assert.That(q.Holes.Select(h => h.Name), Is.EqualTo(new[] { "e20", "f20" }), "puede cruzar el canal central");
        }

        [Test]
        public void PotenciometroConTresPatillasSeguidas()
        {
            var p = NewCircuit().Place(new Potentiometer("RV2", 10_000, 0.5), "h3", Direction.Right);
            Assert.That(p.Holes.Select(h => h.Name), Is.EqualTo(new[] { "h3", "h4", "h5" }));
        }

        [Test]
        public void NoSePuedePincharEnUnAgujeroOcupado()
        {
            var c = NewCircuit();
            c.Place(new Resistor("R1", 330), "b5", Direction.Right);
            var led = new Led("LED1");
            var ex = Assert.Throws<AssemblyException>(() => c.Place(led, "b9", Direction.Right));
            Assert.That(ex.Message, Does.Contain("b9").And.Contain("R1.B"));
            Assert.That(c.ExternalComponents, Has.No.Member(led), "si falla, no se añade nada");
            Assert.That(c.Breadboard.IsFree(HoleId.Parse("b10")), Is.True);
        }

        [Test]
        public void NoSePuedeSalirDeLaPlaca()
        {
            var c = NewCircuit();
            Assert.Throws<AssemblyException>(() => c.Place(new Resistor("R1", 330), "a28", Direction.Right));
            Assert.Throws<AssemblyException>(() => c.Place(new Led("LED1"), "T+4", Direction.Up));
            Assert.That(c.Placements, Is.Empty);
        }

        [Test]
        public void ElServoNoSePincha()
        {
            var ex = Assert.Throws<AssemblyException>(() => NewCircuit().Place(new Servo("M1"), "a1", Direction.Right));
            Assert.That(ex.Message, Does.Contain("no se pincha"));
        }

        [Test]
        public void QuitarUnComponente_LiberaSusAgujeros()
        {
            var c = NewCircuit();
            var r = new Resistor("R1", 330);
            c.Place(r, "b5", Direction.Right);
            int version = c.Version;
            c.Remove(r);
            Assert.That(c.Breadboard.IsFree(HoleId.Parse("b5")) && c.Breadboard.IsFree(HoleId.Parse("b9")), Is.True);
            Assert.That(c.Placements, Is.Empty);
            Assert.That(c.Version, Is.GreaterThan(version));
            c.Place(new Led("LED1"), "b5", Direction.Right);   // el agujero se puede volver a usar
        }

        [Test]
        public void CanPlace_DiceSiCabeSinCambiarNada()
        {
            var c = NewCircuit();
            c.Place(new Resistor("R1", 330), "b5", Direction.Right);
            int version = c.Version;

            Assert.That(c.CanPlace(ComponentTypes.Led, HoleId.Parse("c5"), Direction.Right, null, out var ok), Is.True);
            Assert.That(ok, Is.Null);
            Assert.That(c.CanPlace(ComponentTypes.Led, HoleId.Parse("b8"), Direction.Right, null, out var why), Is.False);
            Assert.That(why, Does.Contain("b9"));
            Assert.That(c.CanPlace(ComponentTypes.Servo, HoleId.Parse("a1"), Direction.Right, null, out _), Is.False);
            Assert.That(c.Version, Is.EqualTo(version));
        }

        [Test]
        public void Mover_LiberaLosAgujerosViejosYOcupaLosNuevos()
        {
            var c = NewCircuit();
            var r = new Resistor("R1", 330);
            c.Place(r, "b5", Direction.Right);
            var p = c.Move(r, HoleId.Parse("b7"), Direction.Right);   // se solapa con su posición anterior: vale
            Assert.That(p.Holes.Select(h => h.Name), Is.EqualTo(new[] { "b7", "b11" }));
            Assert.That(c.Breadboard.IsFree(HoleId.Parse("b5")), Is.True);
            Assert.That(c.Placements, Has.Count.EqualTo(1));

            c.Move(r, HoleId.Parse("b7"), Direction.Down);             // girar: b7 → f7
            Assert.That(c.PlacementOf(r).Holes.Select(h => h.Name), Is.EqualTo(new[] { "b7", "f7" }));
        }

        [Test]
        public void Mover_SiNoCabe_NoCambiaNada()
        {
            var c = NewCircuit();
            var r = new Resistor("R1", 330);
            c.Place(r, "b5", Direction.Right);
            c.AddWire("J7.1", "b20");
            Assert.Throws<AssemblyException>(() => c.Move(r, HoleId.Parse("b16"), Direction.Right));
            Assert.That(c.PlacementOf(r).Holes.Select(h => h.Name), Is.EqualTo(new[] { "b5", "b9" }));
            Assert.That(c.Breadboard.Occupant(HoleId.Parse("b5")), Is.SameAs(r.GetPin("A")));
        }

        [Test]
        public void UnPulsadorExterno_SePulsaDesdeLaFachada()
        {
            var c = NewCircuit();
            var sw = (PushButton)c.Place(new PushButton("SW5"), "e10", Direction.Right).Component;
            var sim = new SimulatorFacade(c);
            sim.PressButton(sw);
            Assert.That(sw.IsPressed, Is.True);
            sim.ReleaseButton(sw);
            Assert.That(sw.IsPressed, Is.False);
        }

        // ------------------------------------------------------------------ cables

        [Test]
        public void CableEntreLaHatYLaProtoboard()
        {
            var c = NewCircuit();
            var w = c.AddWire("J7.1", "a5");
            Assert.That(w.A, Is.SameAs(c.Hat.GetPin("J7.1")));
            Assert.That(c.Breadboard.Occupant(HoleId.Parse("a5")), Is.SameAs(w));
            Assert.That(c.Wires, Has.Count.EqualTo(1));
        }

        [Test]
        public void UnSoloCablePorPinDeLaHat()
        {
            var c = NewCircuit();
            c.AddWire("J7.3", "T-1");
            var ex = Assert.Throws<AssemblyException>(() => c.AddWire("J7.3", "T-2"));
            Assert.That(ex.Message, Does.Contain("J7.3").And.Contain("W1"));
        }

        [Test]
        public void UnCableNoEntraEnUnAgujeroOcupado()
        {
            var c = NewCircuit();
            c.AddWire("J7.1", "a5");
            var ex = Assert.Throws<AssemblyException>(() => c.AddWire("J12.3", "a5"));
            Assert.That(ex.Message, Does.Contain("el cable W1"));
        }

        [Test]
        public void SoloSeCableaALaProtoboardLaHatOComponentesConCable()
        {
            var c = NewCircuit();
            var r = new Resistor("R1", 330);
            c.Place(r, "b5", Direction.Right);
            Assert.Throws<AssemblyException>(() => c.AddWire(r.GetPin("A"), c.Breadboard.Hole("a1")),
                                             "una patilla pinchada ya está en su agujero");
            Assert.Throws<AssemblyException>(() => c.AddWire(c.Pi.GetPin("GPIO14"), c.Breadboard.Hole("a1")),
                                             "los pines de la Pi quedan debajo de la HAT");

            var servo = c.Add(new Servo("M1"));
            c.AddWire(servo.GetPin("SIG"), c.Hat.GetPin("J7.1"));   // el servo sí: tiene sus cables
        }

        [Test]
        public void QuitarUnCable_LiberaSusExtremos()
        {
            var c = NewCircuit();
            var w = c.AddWire("J7.1", "a5");
            c.RemoveWire(w);
            Assert.That(c.Breadboard.IsFree(HoleId.Parse("a5")), Is.True);
            c.AddWire("J7.1", "a6");   // el pin de la HAT vuelve a estar libre
        }

        [Test]
        public void NombreDesconocido()
        {
            Assert.Throws<AssemblyException>(() => NewCircuit().AddWire("J99.1", "a5"));
        }

        // ------------------------------------------------------------------ circuitos resueltos

        private const double Dt = 0.05;

        private static SimulatorFacade Run(Circuit circuit, string program)
        {
            var sim = new SimulatorFacade(circuit);
            Assert.That(sim.LoadProgram(program).Success, Is.True);
            sim.Run();
            for (int i = 0; i < 6; i++) sim.Tick(Dt);
            return sim;
        }

        private const string Gpio14On =
            "from gpiozero import LED\n" +
            "from signal import pause\n" +
            "led = LED(14)\n" +
            "led.on()\n" +
            "pause()\n";

        /// <summary>
        /// Reto 4: GPIO14 (J7.1) → a5 | R 330 Ω b5–b9 | LED ánodo c9, cátodo c10 | d10 → raíl T− | J7.3 (GND) → T−.
        /// </summary>
        [Test]
        public void Reto4_LedConResistencia_EnGpio14()
        {
            var c = NewCircuit();
            c.AddWire("J7.1", "a5");
            c.Place(new Resistor("R1", 330), "b5", Direction.Right);
            var led = c.Place(new Led("LED1"), "c9", Direction.Right).Component as Led;
            c.AddWire("d10", "T-10");
            c.AddWire("J7.3", "T-1");

            var sim = Run(c, Gpio14On);

            // I = (3,3 − 2,0) / (40 Ω del GPIO + 330 Ω + 10 Ω del LED) ≈ 3,42 mA
            double expected = (3.3 - 2.0) / (40 + 330 + 10);
            Assert.That(led.Current, Is.EqualTo(expected).Within(0.02e-3));
            Assert.That(led.IsBurnt, Is.False);
            Assert.That(sim.Faults, Is.Empty);
            Assert.That(sim.State, Is.EqualTo(SimulationState.Running));
        }

        [Test]
        public void Reto4_SinResistencia_QuemaElLed()
        {
            var c = NewCircuit();
            c.AddWire("J7.1", "a5");
            var led = c.Place(new Led("LED1"), "b5", Direction.Right).Component as Led;
            c.AddWire("c6", "T-6");
            c.AddWire("J7.3", "T-1");

            var sim = Run(c, Gpio14On);

            // I = 1,3 V / 50 Ω = 26 mA > 20 mA del LED y > 7 mA del GPIO
            Assert.That(led.IsBurnt, Is.True);
            Assert.That(sim.State, Is.EqualTo(SimulationState.Faulted));
            Assert.That(sim.Faults.Select(f => f.Kind), Has.Member(FaultKind.LedOvercurrent).And.Member(FaultKind.GpioOvercurrent));
        }

        [Test]
        public void LedSinMasa_NoSeEnciende()
        {
            var c = NewCircuit();
            c.AddWire("J7.1", "a5");
            c.Place(new Resistor("R1", 330), "b5", Direction.Right);
            var led = c.Place(new Led("LED1"), "c9", Direction.Right).Component as Led;
            c.AddWire("d10", "T-10");   // el raíl T− no está cableado a GND

            Run(c, Gpio14On);
            Assert.That(led.Current, Is.LessThan(1e-6));
        }

        /// <summary>
        /// Reto 9: 3,3 V (J12.3) → T+ | GND (J12.4) → T− | LDR a20–a21 | T+20 → b20 | R 10 kΩ b21–b25 |
        /// c25 → T− | c21 → CH0 (J1.1). Divisor: V = Vcc · R / (R + R_LDR).
        /// </summary>
        private static (Circuit circuit, Ldr ldr) LdrDivider(string supply, double lux)
        {
            var c = NewCircuit();
            c.AddWire(supply, "T+1");
            c.AddWire("J12.4", "T-1");
            var ldr = (Ldr)c.Place(new Ldr("LDR1", lux), "a20", Direction.Right).Component;
            c.AddWire("T+20", "b20");
            c.Place(new Resistor("R1", 10_000), "b21", Direction.Right);
            c.AddWire("c25", "T-25");
            c.AddWire("c21", "J1.1");
            return (c, ldr);
        }

        [TestCase(10)]
        [TestCase(100)]
        [TestCase(1000)]
        public void Reto9_DivisorConLdr_LlegaAlCanal0(double lux)
        {
            var (c, ldr) = LdrDivider("J12.3", lux);
            var sim = new SimulatorFacade(c);
            sim.Tick(Dt);

            double expected = 3.3 * 10_000 / (10_000 + ldr.Resistance);
            Assert.That(c.Hat.Adc.ChannelVoltage(0), Is.EqualTo(expected).Within(1e-3));
            Assert.That(sim.Faults, Is.Empty);
        }

        [Test]
        public void Reto9_DivisorA5V_DañaElAdc()
        {
            var (c, ldr) = LdrDivider("J12.1", 1000);   // 5 V en lugar de 3,3 V
            var sim = new SimulatorFacade(c);
            sim.Tick(Dt);

            double v = 5.0 * 10_000 / (10_000 + ldr.Resistance);
            Assert.That(v, Is.GreaterThan(3.4), "con mucha luz el divisor pasa de 3,3 V");
            Assert.That(sim.Faults.Select(f => f.Kind), Has.Member(FaultKind.Overvoltage));
        }

        [Test]
        public void CambiarElMontaje_ObligaAReconstruirElCircuito()
        {
            var c = NewCircuit();
            c.AddWire("J7.1", "a5");
            c.Place(new Resistor("R1", 330), "b5", Direction.Right);
            var led = c.Place(new Led("LED1"), "c9", Direction.Right).Component as Led;
            var sim = Run(c, Gpio14On);
            Assert.That(led.Current, Is.LessThan(1e-6), "sin masa todavía");

            c.AddWire("d10", "T-10");
            c.AddWire("J7.3", "T-1");
            sim.Tick(Dt);
            Assert.That(led.Current, Is.GreaterThan(3e-3), "al cerrar el circuito, el LED se enciende sin reiniciar");
        }
    }
}
