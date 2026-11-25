using System;
using System.Collections.Generic;
using System.Linq;
using ImatPiSim.Domain.Challenges;
using ImatPiSim.Domain.Components;
using ImatPiSim.Domain.Electrical;
using ImatPiSim.Domain.Simulation;
using ImatPiSim.Domain.Wiring;
using ImatPiSim.Domain.Wiring.Routing;
using NUnit.Framework;

namespace ImatPiSim.Domain.Tests.Challenges
{
    /// <summary>Etapa E11: máquina de estados del juego (patrón State) y reinicio limpio.</summary>
    public class GameTests
    {
        private const string Reto1 =
            "from gpiozero import LED\nfrom signal import pause\nLED(20).on()\npause()\n";

        private const string Gpio14On =
            "from gpiozero import LED\nfrom signal import pause\nLED(14).on()\npause()\n";

        private static Game AtBuilding(int number = 1)
        {
            var game = new Game();
            game.Play();
            game.Choose(number);
            return game;
        }

        [Test]
        public void EmpiezaEnElMenu_YSoloAdmiteJugar()
        {
            var game = new Game();
            Assert.That(game.Screen, Is.EqualTo(GameScreen.Menu));
            Assert.Throws<InvalidOperationException>(() => game.Check());
            Assert.Throws<InvalidOperationException>(() => game.Choose(1));
            Assert.Throws<InvalidOperationException>(() => game.Retry());
            game.Play();
            Assert.That(game.Screen, Is.EqualTo(GameScreen.Selection));
        }

        [Test]
        public void SoloElPrimerRetoEstaDesbloqueado()
        {
            var game = new Game();
            game.Play();
            Assert.That(game.IsUnlocked(1), Is.True);
            Assert.That(game.IsUnlocked(2), Is.False);
            var ex = Assert.Throws<InvalidOperationException>(() => game.Choose(2));
            Assert.That(ex.Message, Does.Contain("bloqueado"));
        }

        [Test]
        public void Ganar_DesbloqueaElSiguiente_YNextLoAbre()
        {
            var game = AtBuilding(1);
            var screens = new List<GameScreen>();
            game.ScreenChanged += screens.Add;

            game.Program = Reto1;
            var result = game.Check();
            Assert.That(result.Passed, Is.True, result.Message);
            Assert.That(game.Screen, Is.EqualTo(GameScreen.Victory));
            Assert.That(game.IsCompleted(1) && game.IsUnlocked(2), Is.True);

            game.Next();
            Assert.That(game.Screen, Is.EqualTo(GameScreen.Building));
            Assert.That(game.Current.Number, Is.EqualTo(2));
            Assert.That(game.Program, Is.Empty, "cada reto empieza con el programa vacío");
            Assert.That(screens, Is.EqualTo(new[] { GameScreen.Victory, GameScreen.Building }));
        }

        [Test]
        public void Perder_LlevaADerrota_ConElMotivo()
        {
            var game = AtBuilding(1);
            game.Program = Reto1.Replace("LED(20)", "LED(21)");
            var result = game.Check();
            Assert.That(game.Screen, Is.EqualTo(GameScreen.Defeat));
            Assert.That(result.Kind, Is.EqualTo(ResultKind.WrongBehaviour));
            Assert.That(game.LastResult, Is.SameAs(result));
            Assert.Throws<InvalidOperationException>(() => game.Next(), "solo se pasa al siguiente ganando");
        }

        [Test]
        public void Reintentar_ConservaMontajeYPrograma_EnUnCircuitoLimpio()
        {
            // Reto 4 desbloqueado a mano: el progreso no importa aquí.
            var game = new Game(new Challenge[] { ChallengeCatalog.ByNumber(4) });
            game.Play();
            game.Choose(4);

            // El jugador monta un LED sin resistencia y lo prueba: se quema en SU circuito.
            var c = game.Facade.Circuit;
            c.AddWire("J7.1", "a5");
            c.Place(new Led("LED1"), "b5", Direction.Right);
            c.AddWire("e6", "T-6");
            c.AddWire("J7.3", "T-1");
            game.Program = Gpio14On;
            Assert.That(game.Facade.LoadProgram(game.Program).Success, Is.True);
            game.Facade.Run();
            for (int i = 0; i < 5; i++) game.Facade.Tick(0.05);
            Assert.That(((Led)c.ExternalComponents.Single()).IsBurnt, Is.True);

            var result = game.Check();
            Assert.That(result.Kind, Is.EqualTo(ResultKind.ElectricalFault));

            var oldFacade = game.Facade;
            game.Retry();
            Assert.That(game.Screen, Is.EqualTo(GameScreen.Building));
            Assert.That(game.Facade, Is.Not.SameAs(oldFacade), "circuito nuevo");
            Assert.That(game.Program, Is.EqualTo(Gpio14On), "conserva el programa");
            var led = game.Facade.Circuit.ExternalComponents.OfType<Led>().Single();
            Assert.That(led.IsBurnt, Is.False, "sin estado eléctrico residual: el LED vuelve a estar sano");
            Assert.That(game.Facade.Circuit.Wires, Has.Count.EqualTo(3), "conserva el montaje");
            Assert.That(game.Facade.Circuit.PlacementOf(led).Start.Name, Is.EqualTo("b5"));
            Assert.That(game.Facade.State, Is.EqualTo(SimulationState.Idle));
        }

        [Test]
        public void EmpezarDeCero_VuelveAlMontajeDePartida()
        {
            var game = new Game(new Challenge[] { ChallengeCatalog.ByNumber(8) });
            game.Play();
            game.Choose(8);
            Assert.That(game.Facade.Circuit.ExternalComponents.OfType<Servo>().Count(), Is.EqualTo(1), "el servo viene montado");
            game.Facade.Circuit.AddWire("J12.3", "T+1");
            game.Program = "print('hola')";

            game.Restart();
            Assert.That(game.Facade.Circuit.Wires, Has.Count.EqualTo(3), "solo los 3 cables del servo");
            Assert.That(game.Program, Is.Empty);
        }

        [Test]
        public void VolverAlMenu_DesdeCualquierPantalla_LoDejaTodoLimpio()
        {
            var game = AtBuilding(1);
            game.Program = Reto1;
            game.Facade.LoadProgram(Reto1);
            game.Facade.Run();
            game.Facade.Tick(0.1);

            game.BackToMenu();
            Assert.That(game.Screen, Is.EqualTo(GameScreen.Menu));
            Assert.That(game.Facade, Is.Null);
            Assert.That(game.Current, Is.Null);
            game.BackToMenu();   // en el menú no hace nada
        }

        [Test]
        public void VolverALaSeleccion_DesdeVictoria()
        {
            var game = AtBuilding(1);
            game.Program = Reto1;
            game.Check();
            game.BackToSelection();
            Assert.That(game.Screen, Is.EqualTo(GameScreen.Selection));
            game.Choose(2);
            Assert.That(game.Current.Number, Is.EqualTo(2));
        }
    }

    /// <summary>Etapa E11: copia del montaje del jugador (lo que usa el verificador).</summary>
    public class AssemblyPlanTests
    {
        [Test]
        public void CapturarYConstruir_DaElMismoMontaje()
        {
            var c = Circuit.WithImatHat();
            c.Pi.SpiEnabled = true;
            c.AddWire("J7.1", "a5");
            c.Place(new Resistor("R1", 330), "b5", Direction.Right);
            c.Place(new Led("LED1"), "c9", Direction.Down);
            var routed = c.RouteWire("d20", "d25", new AStarRouter());
            c.Place(new Potentiometer("RV2", 10_000, 0.3), "h3", Direction.Right);
            var servo = c.Add(new Servo("M1", 30));
            c.AddWire(servo.GetPin("SIG"), c.Hat.GetPin("J8.1"));

            var plan = AssemblyPlan.Capture(c);
            var copy = plan.Build();

            Assert.That(copy, Is.Not.SameAs(c));
            Assert.That(copy.Pi.SpiEnabled, Is.True);
            Assert.That(copy.ExternalComponents.Select(x => x.Id), Is.EqualTo(new[] { "R1", "LED1", "RV2", "M1" }));
            Assert.That(((Resistor)copy.ExternalComponents[0]).Resistance, Is.EqualTo(330));
            Assert.That(((Potentiometer)copy.ExternalComponents[2]).Position, Is.EqualTo(0.3));
            Assert.That(((Servo)copy.ExternalComponents[3]).InitialAngle, Is.EqualTo(30));
            Assert.That(copy.PlacementOf(copy.ExternalComponents[1]).Holes.Select(h => h.Name), Is.EqualTo(new[] { "c9", "d9" }));
            Assert.That(copy.Wires.Select(w => copy.NameOf(w.A) + "-" + copy.NameOf(w.B)),
                        Is.EqualTo(new[] { "J7.1-a5", "d20-d25", "M1.SIG-J8.1" }));
            Assert.That(copy.Wires[1].Route, Is.EqualTo(routed.Route), "el recorrido del A* se copia tal cual");
        }

        [Test]
        public void ElNombreDeUnPin_IdaYVuelta()
        {
            var c = Circuit.WithImatHat();
            var servo = c.Add(new Servo("M1"));
            foreach (var pin in new[] { c.Breadboard.Hole("T-3"), c.Hat.GetPin("J12.3"), servo.GetPin("GND") })
                Assert.That(c.PinByName(c.NameOf(pin)), Is.SameAs(pin));
        }
    }

    /// <summary>Etapa E11: cortocircuito a través de un componente (> 1 A), decidido el 25/11/2026.</summary>
    public class ComponentShortCircuitTests
    {
        private static IReadOnlyList<Fault> Solve(Circuit c)
        {
            var sim = new SimulatorFacade(c);
            sim.Tick(0.01);
            return sim.Faults;
        }

        [Test]
        public void PulsadorEntre3V3YGnd_AlPulsarlo()
        {
            var c = Circuit.WithImatHat();
            c.AddWire("J12.3", "a10");
            var sw = (PushButton)c.Place(new PushButton("SW5"), "c10", Direction.Right).Component;
            c.AddWire("e12", "J12.4");
            var sim = new SimulatorFacade(c);
            sim.Tick(0.01);
            Assert.That(sim.Faults, Is.Empty, "suelto no pasa nada");

            sim.PressButton(sw);
            sim.Tick(0.01);
            var fault = sim.Faults.Single();
            Assert.That(fault.Kind, Is.EqualTo(FaultKind.ShortCircuit));
            Assert.That(fault.Value, Is.EqualTo(3.3 / PushButton.DefaultContactResistance).Within(0.5));
            Assert.That(fault.Message, Does.Contain("SW5").And.Contain("33 A"));
        }

        [Test]
        public void PotenciometroAlExtremo_ConElCursorAMasa()
        {
            // Extremos entre 3,3 V y GND y el cursor también a GND: con el mando al extremo de 3,3 V,
            // la pista que queda entre 3,3 V y el cursor mide casi 0 Ω.
            var c = Circuit.WithImatHat();
            c.AddWire("J12.3", "a3");
            var pot = (Potentiometer)c.Place(new Potentiometer("RV2", 10_000, 1.0), "c3", Direction.Right).Component;
            c.AddWire("e4", "J12.4");
            c.AddWire("e5", "J12.2");
            var faults = Solve(c);
            Assert.That(faults.Select(f => f.Kind), Has.Member(FaultKind.ShortCircuit));
            Assert.That(faults.Single().SourceId, Is.EqualTo("RV2"));
        }

        [Test]
        public void UnaResistenciaNormalEntreRailes_NoEsCortocircuito()
        {
            var c = Circuit.WithImatHat();
            c.AddWire("J12.3", "a10");
            c.Place(new Resistor("R1", 220), "c10", Direction.Right);   // 15 mA
            c.AddWire("e14", "J12.4");
            Assert.That(Solve(c), Is.Empty);
        }
    }
}
