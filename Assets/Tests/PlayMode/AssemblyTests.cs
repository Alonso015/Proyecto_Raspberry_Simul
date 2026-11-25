using System;
using System.Collections;
using System.Linq;
using ImatPiSim.Domain.Components;
using ImatPiSim.Domain.Simulation;
using ImatPiSim.Domain.Wiring;
using ImatPiSim.Presentation;
using ImatPiSim.Presentation.Assembly;
using ImatPiSim.Presentation.Ui;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ImatPiSim.PlayMode.Tests
{
    /// <summary>Etapa E16: montaje en la protoboard desde la escena.</summary>
    public class AssemblyTests
    {
        private SimulationRunner _runner;
        private EditorPanel _panel;
        private AssemblyController _assembly;

        private const string Gpio14On =
            "from gpiozero import LED\n" +
            "from signal import pause\n" +
            "led = LED(14)\n" +
            "led.on()\n" +
            "pause()\n";

        [UnitySetUp]
        public IEnumerator CargarEscena()
        {
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                "Assets/Scenes/Main.unity", new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync("Main");
#endif
            yield return null;
            yield return null;
            _runner = UnityEngine.Object.FindFirstObjectByType<SimulationRunner>();
            _panel = UnityEngine.Object.FindFirstObjectByType<EditorPanel>();
            _assembly = UnityEngine.Object.FindFirstObjectByType<AssemblyController>();
            _runner.Facade.Reset();   // el programa de ejemplo arranca solo: se para para poder montar
            yield return null;
        }

        private static IEnumerator Until(Func<bool> condition, string what)
        {
            float limit = Time.realtimeSinceStartup + 2f;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > limit) Assert.Fail("No se cumplió a tiempo: " + what);
                yield return null;
            }
        }

        private static HoleId H(string name) => HoleId.Parse(name);
        private static PaletteItem Item(string label) => PaletteView.Items.Single(i => i.Label == label);
        private Domain.Components.Pin Hole(string name) => _assembly.Circuit.Breadboard.Hole(name);
        private Domain.Components.Pin HatPin(string name) => _assembly.Circuit.Hat.GetPin(name);

        // ------------------------------------------------------------------ del ratón a la rejilla

        [Test]
        public void DelMundoALaRejilla_YVuelta()
        {
            var view = _assembly.Breadboard;
            foreach (var hole in _assembly.Circuit.Breadboard.Holes)
                Assert.That(view.WorldToHole(view.HoleToWorld(hole)), Is.EqualTo((HoleId?)hole), hole.Name);

            var c12 = view.HoleToWorld(H("c12"));
            Assert.That(view.WorldToHole(c12 + new Vector2(0.1f, -0.1f)), Is.EqualTo((HoleId?)H("c12")), "un poco desviado sigue siendo c12");
            Assert.That(view.WorldToHole(c12 + new Vector2(0, 5f)), Is.Null, "fuera de la placa no hay agujero");
        }

        [Test]
        public void LaPaleta_TieneOchoPiezas()
        {
            var palette = UnityEngine.Object.FindFirstObjectByType<PaletteView>();
            Assert.That(PaletteView.Items.Count, Is.EqualTo(8));
            foreach (var item in PaletteView.Items)
                Assert.That(palette.ItemAt(palette.PositionOf(item)), Is.SameAs(item));
        }

        [Test]
        public void LosConectoresDeLaHat_SeEncuentranPorSuPosicion()
        {
            var board = _assembly.Board;
            var j71 = HatPin("J7.1");
            Assert.That(board.ConnectorAt(board.ConnectorPins[j71]), Is.SameAs(j71));
            Assert.That(board.Describe(j71), Does.Contain("GPIO14"));
            Assert.That(board.ConnectorPins, Has.Count.EqualTo(8 + 4 + 4 + 8 + 4 * 3 + 4));
        }

        [TestCase("J12.1", "5V")]
        [TestCase("J12.2", "GND")]
        [TestCase("J12.3", "3V3")]
        [TestCase("J12.4", "GND")]
        [TestCase("J2.1", "3V3")]
        [TestCase("J2.4", "GND")]
        [TestCase("J7.2", "5V")]
        [TestCase("J10.3", "GND")]
        [TestCase("J7.1", null)]
        [TestCase("J1.1", null)]
        public void LosPinesDeAlimentacion_LlevanRotulo(string pin, string label) =>
            Assert.That(BoardView.PowerLabel(pin), Is.EqualTo(label));

        [TestCase("J11.1", "GP20")]
        [TestCase("J11.8", "GP27")]
        [TestCase("J3.1", "GP7")]
        [TestCase("J3.4", "GP19")]
        [TestCase("J2.2", "SDA")]
        [TestCase("J2.3", "SCL")]
        [TestCase("J1.1", "CH0")]
        [TestCase("J1.8", "CH7")]
        [TestCase("J7.1", "GP14")]
        [TestCase("J10.1", "GP23")]
        [TestCase("J12.3", "3V3")]
        public void CadaPin_LlevaSuRotulo(string pin, string label) =>
            Assert.That(BoardView.PinLabel(pin), Is.EqualTo(label));

        [Test]
        public void ElRotulo_CorrespondeALaTensionReal()
        {
            // Lo que dice el rótulo es lo que hay en el circuito: el pin está en el raíl de esa tensión.
            var hat = _assembly.Circuit.Hat;
            var pi = _assembly.Circuit.Pi;
            Assert.That(hat.GetPin("J12.3").Terminal, Is.SameAs(pi.GetPin("3V3").Terminal));
            Assert.That(hat.GetPin("J2.1").Terminal, Is.SameAs(pi.GetPin("3V3").Terminal));
            Assert.That(hat.GetPin("J12.1").Terminal, Is.SameAs(pi.GetPin("5V").Terminal));
        }

        // ------------------------------------------------------------------ colocar, mover, borrar

        [Test]
        public void ColocarUnaResistencia_CreaSuVistaEntreSusAgujeros()
        {
            Assert.That(_assembly.TryPlace(Item("330 Ω"), H("b5"), Direction.Right), Is.True, _assembly.LastMessage);
            var view = _assembly.ComponentViews.Single();
            Assert.That(view.Component, Is.TypeOf<Resistor>());
            Assert.That(((Resistor)view.Component).Resistance, Is.EqualTo(330));
            var middle = (_assembly.Breadboard.HoleToWorld(H("b5")) + _assembly.Breadboard.HoleToWorld(H("b9"))) / 2;
            Assert.That(Vector2.Distance(view.transform.position, middle), Is.LessThan(1e-4f));
        }

        [Test]
        public void AgujeroOcupado_SeExplica()
        {
            _assembly.TryPlace(Item("330 Ω"), H("b5"), Direction.Right);
            Assert.That(_assembly.TryPlace(Item("LED"), H("b9"), Direction.Right), Is.False);
            Assert.That(_assembly.LastMessage, Does.Contain("b9").And.Contain("ocupado"));
            Assert.That(_assembly.ComponentViews, Has.Count.EqualTo(1));
        }

        [Test]
        public void MoverGirarYQuitar()
        {
            _assembly.TryPlace(Item("1 kΩ"), H("b5"), Direction.Right);
            var view = _assembly.ComponentViews.Single();
            Assert.That(_assembly.TryMove(view, H("a20"), Direction.Down), Is.True, _assembly.LastMessage);
            var moved = _assembly.ComponentViews.Single();
            Assert.That(moved.Placement.Holes.Select(h => h.Name), Is.EqualTo(new[] { "a20", "e20" }));
            Assert.That(_assembly.Circuit.Breadboard.IsFree(H("b5")), Is.True);

            Assert.That(_assembly.Delete(moved), Is.True);
            Assert.That(_assembly.ComponentViews, Is.Empty);
            Assert.That(_assembly.Circuit.Placements, Is.Empty);
        }

        [UnityTest]
        public IEnumerator ConElProgramaEnMarcha_ElMontajeSeBloquea()
        {
            _panel.Run();
            yield return Until(() => _runner.Facade.IsRunning, "programa en marcha");
            Assert.That(_assembly.TryPlace(Item("LED"), H("a1"), Direction.Right), Is.False);
            Assert.That(_assembly.LastMessage, Does.Contain("bloqueado"));
            _runner.Facade.Stop();
            yield return Until(() => !_runner.Facade.IsRunning, "parado");
            Assert.That(_assembly.TryPlace(Item("LED"), H("a1"), Direction.Right), Is.True);
        }

        // ------------------------------------------------------------------ cables

        [Test]
        public void CableEntreAgujeros_SigueElRecorridoDelAStar()
        {
            _assembly.TryPlace(Item("330 Ω"), H("d5"), Direction.Right);
            Assert.That(_assembly.TryWire(Hole("d4"), Hole("d10")), Is.True, _assembly.LastMessage);
            var wire = _assembly.WireViews.Single();
            Assert.That(wire.Wire.Route, Has.Count.GreaterThan(2), "rodea la resistencia");
            Assert.That(wire.Points, Has.Count.EqualTo(wire.Wire.Route.Count));
            Assert.That(Vector3.Distance(wire.Points[0], _assembly.Breadboard.HoleToWorld(H("d4"))), Is.LessThan(1e-4f));
        }

        [Test]
        public void CableALaHat_EsUnaLineaRecta()
        {
            Assert.That(_assembly.TryWire(HatPin("J7.1"), Hole("a5")), Is.True, _assembly.LastMessage);
            var wire = _assembly.WireViews.Single();
            Assert.That(wire.Wire.Route, Is.Empty);
            Assert.That(wire.Points, Has.Count.EqualTo(2));
            Assert.That(_assembly.TryWire(HatPin("J7.1"), Hole("a6")), Is.False, "un solo cable por pin de la HAT");
        }

        // ------------------------------------------------------------------ montar y ejecutar

        private void MountReto4(bool withResistor)
        {
            Assert.That(_assembly.TryWire(HatPin("J7.1"), Hole("a5")), Is.True, _assembly.LastMessage);
            if (withResistor)
            {
                Assert.That(_assembly.TryPlace(Item("330 Ω"), H("b5"), Direction.Right), Is.True, _assembly.LastMessage);
                Assert.That(_assembly.TryPlace(Item("LED"), H("c9"), Direction.Right), Is.True, _assembly.LastMessage);   // ánodo c9, cátodo c10
                Assert.That(_assembly.TryWire(Hole("e10"), Hole("T-10")), Is.True, _assembly.LastMessage);              // trazado con A*
            }
            else
            {
                Assert.That(_assembly.TryPlace(Item("LED"), H("b5"), Direction.Right), Is.True, _assembly.LastMessage);
                Assert.That(_assembly.TryWire(Hole("e6"), Hole("T-6")), Is.True, _assembly.LastMessage);
            }
            Assert.That(_assembly.TryWire(HatPin("J7.3"), Hole("T-1")), Is.True, _assembly.LastMessage);
        }

        [UnityTest]
        public IEnumerator Reto4_MontadoEnLaEscena_EnciendeElLed()
        {
            MountReto4(withResistor: true);
            var led = _assembly.ComponentViews.Single(v => v.Led != null);
            _panel.Editor.Text = Gpio14On;
            _panel.Run();
            yield return Until(() => led.Led.VisibleLevel > 0.5f, "LED externo encendido");
            Assert.That(((Led)led.Component).Current, Is.EqualTo(1.3 / 380).Within(0.05e-3));
            Assert.That(_runner.Facade.Faults, Is.Empty);
        }

        [UnityTest]
        public IEnumerator Reto4_SinResistencia_QuemaElLed()
        {
            MountReto4(withResistor: false);
            var led = _assembly.ComponentViews.Single(v => v.Led != null);
            _panel.Editor.Text = Gpio14On;
            _panel.Run();
            yield return Until(() => _runner.Facade.State == SimulationState.Faulted, "fallo eléctrico");
            Assert.That(((Led)led.Component).IsBurnt, Is.True);
            Assert.That(led.Led.VisibleLevel, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator UnPulsadorExterno_SePulsaConElProgramaEnMarcha()
        {
            _assembly.TryPlace(Item("Pulsador"), H("e10"), Direction.Right);
            var view = _assembly.ComponentViews.Single();
            _panel.Run();
            yield return Until(() => _runner.Facade.IsRunning, "en marcha");
            view.Button.Press();
            Assert.That(((PushButton)view.Component).IsPressed, Is.True);
            view.Button.Release();
            Assert.That(((PushButton)view.Component).IsPressed, Is.False);
        }

        [UnityTest]
        public IEnumerator LaProtoboard_SeVeEnteraFueraDelPanel()
        {
            yield return null;
            var cam = Camera.main;
            foreach (var area in new IWorkspaceArea[] { _assembly.Board, _assembly.Breadboard })
            {
                var b = area.Bounds;
                var min = cam.WorldToViewportPoint(b.min);
                var max = cam.WorldToViewportPoint(b.max);
                Assert.That(min.x, Is.GreaterThanOrEqualTo(EditorPanel.WidthFraction - 1e-3f));
                Assert.That(max.x, Is.LessThanOrEqualTo(1f + 1e-3f));
                Assert.That(min.y, Is.GreaterThanOrEqualTo(-1e-3f));
                Assert.That(max.y, Is.LessThanOrEqualTo(1f + 1e-3f));
            }
        }
    }
}
