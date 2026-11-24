using System.Collections.Generic;
using System.Linq;
using ImatPiSim.Domain.Components;
using ImatPiSim.Domain.Electrical;
using ImatPiSim.Domain.Simulation;
using NUnit.Framework;

namespace ImatPiSim.Domain.Tests.Simulation
{
    /// <summary>Etapa E10: motor de simulación y fachada.</summary>
    public class SimulationEngineTests
    {
        private const double Dt = 0.05;   // ~20 ticks por segundo

        private const string LedMientrasPulso =
            "from gpiozero import LED, Button\n" +
            "puls_led = Button(7)\n" +
            "puls_fin = Button(19)\n" +
            "led_0 = LED(20)\n" +
            "while True:\n" +
            "    if puls_led.is_pressed:\n" +
            "        led_0.on()\n" +
            "    else:\n" +
            "        led_0.off()\n" +
            "    if puls_fin.is_pressed:\n" +
            "        break\n";

        private static void Advance(SimulatorFacade sim, double seconds)
        {
            int n = (int)System.Math.Round(seconds / Dt);
            for (int i = 0; i < n; i++) sim.Tick(Dt);
        }

        [Test]
        public void LedMientrasPulso_ConPulsacionesDurante2Segundos()
        {
            var sim = SimulatorFacade.WithImatHat();
            Assert.That(sim.LoadProgram(LedMientrasPulso).Success, Is.True);
            sim.Run();

            var registro = new List<string>();
            sim.Ticked += () => registro.Add(sim.LedBrightness(1) > 0 ? "1" : "0");

            Advance(sim, 0.5);
            sim.PressButton(1);
            Advance(sim, 0.7);
            sim.ReleaseButton(1);
            Advance(sim, 0.5);
            Assert.That(sim.LedBrightness(1), Is.EqualTo(0));

            // Del registro: apagado → encendido mientras se pulsa → apagado, sin parpadeos.
            string patron = string.Concat(registro);
            Assert.That(patron, Does.Match("^0+1+0+$"), patron);
            Assert.That(patron.Count(c => c == '1'), Is.InRange(12, 14), "≈ 0,7 s pulsado a 20 ticks/s");

            sim.PressButton(4);
            Advance(sim, 0.3);
            Assert.That(sim.State, Is.EqualTo(SimulationState.Finished));
        }

        [Test]
        public void AlTerminarElPrograma_SeLiberanLosPines_YElLedSeApaga()
        {
            // Como en la Pi: gpiozero libera los pines al salir de Python.
            var sim = SimulatorFacade.WithImatHat();
            sim.LoadProgram("from gpiozero import LED\nfrom time import sleep\nled = LED(20)\nled.on()\nsleep(0.5)\n");
            sim.Run();

            Advance(sim, 0.2);
            Assert.That(sim.LedBrightness(1), Is.GreaterThan(0));
            Advance(sim, 0.5);
            Assert.That(sim.State, Is.EqualTo(SimulationState.Finished));
            Assert.That(sim.LedBrightness(1), Is.EqualTo(0));
        }

        [Test]
        public void ElCircuitoSoloSeResuelve_CuandoAlgoCambia()
        {
            var sim = SimulatorFacade.WithImatHat();
            sim.LoadProgram("from gpiozero import LED\nfrom signal import pause\nLED(20).on()\npause()\n");
            sim.Run();
            Advance(sim, 0.5);
            int tras = sim.Engine.SolveCount;

            Advance(sim, 1.0);   // 20 ticks sin cambios
            Assert.That(sim.Engine.SolveCount, Is.EqualTo(tras));

            sim.PressButton(2);
            sim.Tick(Dt);
            Assert.That(sim.Engine.SolveCount, Is.EqualTo(tras + 1));
        }

        [Test]
        public void UnFallo_DetieneLaSimulacion_YEmiteElEvento()
        {
            // LED externo sin resistencia en el conector de servo J7 (GPIO14 → LED → GND)
            var circuit = Circuit.WithImatHat();
            var led = circuit.Add(new Led("Dext"));
            circuit.Connect(circuit.Hat.GetPin("J7.1"), led.GetPin("A"));
            circuit.Connect(led.GetPin("K"), circuit.Hat.GetPin("J7.3"));
            var sim = new SimulatorFacade(circuit);
            var avisos = new List<Fault>();
            sim.FaultDetected += avisos.Add;

            sim.LoadProgram("from gpiozero import LED\nfrom signal import pause\nLED(14).on()\npause()\n");
            sim.Run();
            Advance(sim, 0.2);

            Assert.That(sim.State, Is.EqualTo(SimulationState.Faulted));
            Assert.That(avisos.Select(f => f.Kind), Is.EquivalentTo(new[] { FaultKind.LedOvercurrent, FaultKind.GpioOvercurrent }));
            Assert.That(led.IsBurnt, Is.True);
            Assert.That(sim.Engine.Runtime.IsRunning, Is.False, "el programa se detiene");

            sim.Reset();
            Assert.That(led.IsBurnt, Is.False);
            Assert.That(sim.Faults, Is.Empty);
            Assert.That(sim.State, Is.EqualTo(SimulationState.Idle));
        }

        [Test]
        public void ConflictoDePines_SpiActivoYPulsadorSw1_EsUnFallo()
        {
            var sim = SimulatorFacade.WithImatHat();
            sim.SetSpiEnabled(true);
            sim.LoadProgram("from gpiozero import Button\nfrom signal import pause\nb = Button(7)\npause()\n");
            sim.Run();
            Advance(sim, 0.2);

            Assert.That(sim.State, Is.EqualTo(SimulationState.Faulted));
            Assert.That(sim.Faults.Single().Kind, Is.EqualTo(FaultKind.PinConflict));
        }

        [Test]
        public void ErrorDePython_DejaElEstadoProgramError()
        {
            var sim = SimulatorFacade.WithImatHat();
            sim.LoadProgram("x = 1\ny = x / 0\n");
            sim.Run();
            sim.Tick(Dt);

            Assert.That(sim.State, Is.EqualTo(SimulationState.ProgramError));
            Assert.That(sim.ProgramError.Line, Is.EqualTo(2));
        }

        [Test]
        public void ErrorDeSintaxis_SeDevuelveAlCargar()
        {
            var sim = SimulatorFacade.WithImatHat();
            var r = sim.LoadProgram("if x\n    y = 1\n");

            Assert.That(r.Success, Is.False);
            Assert.That(r.Error.Line, Is.EqualTo(1));
        }

        [Test]
        public void Parar_EjecutaElFinally_YAvisaDelCambioDeEstado()
        {
            var sim = SimulatorFacade.WithImatHat();
            var estados = new List<SimulationState>();
            var salida = new List<string>();
            sim.StateChanged += estados.Add;
            sim.OutputLine += salida.Add;
            sim.LoadProgram("from signal import pause\ntry:\n    pause()\nfinally:\n    print('adiós')\n");
            sim.Run();
            Advance(sim, 0.2);

            sim.Stop();
            sim.Tick(Dt);

            Assert.That(salida, Is.EqualTo(new[] { "adiós" }));
            Assert.That(estados.Last(), Is.EqualTo(SimulationState.Stopped));
        }

        [Test]
        public void ServoExterno_GiraConElTiempo()
        {
            var circuit = Circuit.WithImatHat();
            var servo = circuit.Add(new Servo("Servo", initialAngle: 0));
            circuit.Connect(servo.GetPin("SIG"), circuit.Hat.GetPin("J7.1"));
            circuit.Connect(servo.GetPin("VCC"), circuit.Hat.GetPin("J7.2"));
            circuit.Connect(servo.GetPin("GND"), circuit.Hat.GetPin("J7.3"));
            var sim = new SimulatorFacade(circuit);
            sim.LoadProgram("from gpiozero import AngularServo\nfrom signal import pause\n" +
                            "s = AngularServo(14, min_pulse_width=0.0005, max_pulse_width=0.0025)\ns.angle = 0\npause()\n");
            sim.Run();

            sim.Tick(Dt);   // en el mismo tick: el programa escribe el PWM → circuito → el servo ya gira
            Assert.That(servo.TargetAngle, Is.EqualTo(90).Within(1e-9));
            Assert.That(servo.Angle, Is.EqualTo(30).Within(1e-9), "600 °/s · 0,05 s = 30° por tick");
            Advance(sim, 0.5);
            Assert.That(servo.Angle, Is.EqualTo(90).Within(1e-9));
        }

        [Test]
        public void PotenciometroDesdeLaFachada_LlegaAlProgramaPorElAdc()
        {
            var sim = SimulatorFacade.WithImatHat();
            sim.SetSpiEnabled(true);
            sim.LoadProgram("from gpiozero import MCP3008\nfrom time import sleep\npot = MCP3008(7)\n" +
                            "while True:\n    v = pot.value\n    sleep(0.1)\n");
            sim.SetPotentiometer(0.25);
            sim.Run();
            Advance(sim, 0.5);

            Assert.That((double)sim.Engine.Runtime.GetGlobal("v"), Is.EqualTo(0.25).Within(2.0 / 1023));
        }
    }
}
