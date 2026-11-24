using System;
using System.Collections;
using System.Linq;
using ImatPiSim.Domain.Simulation;
using ImatPiSim.Presentation;
using ImatPiSim.Presentation.Ui;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ImatPiSim.PlayMode.Tests
{
    /// <summary>
    /// Etapa E15: el editor de código del juego. Se escribe un programa, se pulsa Ejecutar y se
    /// comprueba el efecto en la placa, en la consola y en las marcas de línea.
    /// </summary>
    public class CodeEditorTests
    {
        private SimulationRunner _runner;
        private BoardView _board;
        private EditorPanel _panel;

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
            _board = UnityEngine.Object.FindFirstObjectByType<BoardView>();
            _panel = UnityEngine.Object.FindFirstObjectByType<EditorPanel>();
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

        /// <summary>Escribe el programa en el editor y pulsa el botón Ejecutar.</summary>
        private void WriteAndRun(string program)
        {
            _panel.Editor.Text = program;
            _panel.RunButton.onClick.Invoke();
        }

        [UnityTest]
        public IEnumerator ElEditor_MuestraElProgramaInicialConNumerosDeLinea()
        {
            Assert.That(_panel, Is.Not.Null, "falta el EditorPanel en la escena");
            Assert.That(_panel.Editor.Text, Is.EqualTo(_runner.program));
            yield return null;
            Assert.That(_panel.Editor.GutterText, Does.StartWith("1\n2\n3\n"));
        }

        [UnityTest]
        public IEnumerator UnaLineaLarga_NoSeParte_YLosNumerosCoinciden()
        {
            string program = "x = 1\n# " + new string('a', 300) + "\ny = 2\n";
            _panel.Editor.Text = program;
            yield return null;
            yield return null;
            Assert.That(_panel.Editor.Input.textComponent.textInfo.lineCount,
                        Is.EqualTo(ImatPiSim.Domain.Interpreter.SourceText.CountLines(program)),
                        "cada línea del programa debe ser una sola línea dibujada");
        }

        [UnityTest]
        public IEnumerator Reto1_EscritoEnElEditor_EnciendeD1()
        {
            WriteAndRun("from gpiozero import LED\n" +
                        "from signal import pause\n" +
                        "\n" +
                        "led = LED(20)\n" +
                        "led.on()\n" +
                        "pause()\n");
            var d1 = _board.Leds.Single(l => l.Number == 1);
            yield return Until(() => d1.VisibleLevel > 0.5f, "D1 encendido");
            Assert.That(_runner.Facade.State, Is.EqualTo(SimulationState.Running));
            Assert.That(_board.Leds.Where(l => l.Number != 1).All(l => l.VisibleLevel == 0), Is.True);
            Assert.That(_panel.Editor.ExecutingLine, Is.EqualTo(6), "el programa espera en pause()");
        }

        [UnityTest]
        public IEnumerator ErrorDeSintaxis_SeMuestraEnLaConsola_YSeMarcaSuLinea()
        {
            WriteAndRun("from gpiozero import LED\n" +
                        "led = LED(20)\n" +
                        "if True\n" +
                        "    led.on()\n");
            yield return null;
            Assert.That(_runner.Facade.State, Is.Not.EqualTo(SimulationState.Running), "un programa con errores no arranca");
            Assert.That(_panel.Editor.ErrorLine, Is.EqualTo(3));
            Assert.That(_panel.Editor.ErrorMarkVisible, Is.True);
            Assert.That(_panel.Console.PlainText, Does.Contain("línea 3"));
        }

        [UnityTest]
        public IEnumerator ErrorAlEjecutar_MarcaLaLineaQueFalla()
        {
            WriteAndRun("print('antes')\n" +
                        "x = 1 / 0\n");
            yield return Until(() => _runner.Facade.State == SimulationState.ProgramError, "error de ejecución");
            yield return null;
            Assert.That(_panel.Editor.ErrorLine, Is.EqualTo(2));
            Assert.That(_panel.Console.PlainText, Does.Contain("antes"));
            Assert.That(_panel.Console.PlainText, Does.Contain("ZeroDivisionError"));
        }

        [UnityTest]
        public IEnumerator EditarElTexto_QuitaLaMarcaDeError()
        {
            WriteAndRun("if True\n    pass\n");
            yield return null;
            Assert.That(_panel.Editor.ErrorLine, Is.EqualTo(1));

            var input = _panel.Editor.Input;
            input.stringPosition = "if True".Length;
            input.ProcessEvent(new Event { type = EventType.KeyDown, character = ':' });
            yield return null;
            Assert.That(input.text, Does.StartWith("if True:"));
            Assert.That(_panel.Editor.ErrorLine, Is.Null);
            Assert.That(_panel.Editor.ErrorMarkVisible, Is.False);
        }

        [UnityTest]
        public IEnumerator Enter_TrasDosPuntos_SangraLaLineaNueva()
        {
            var input = _panel.Editor.Input;
            _panel.Editor.Text = "def main():";
            yield return null;   // TextMeshPro coloca los caracteres antes de mover el cursor
            input.stringPosition = input.text.Length;
            input.ProcessEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.Return, character = '\n' });
            yield return null;
            Assert.That(input.text, Is.EqualTo("def main():\n    "));
            Assert.That(input.stringPosition, Is.EqualTo(input.text.Length), "el cursor queda tras la sangría");
        }

        [UnityTest]
        public IEnumerator Parar_DetieneElPrograma()
        {
            WriteAndRun("from time import sleep\n" +
                        "while True:\n" +
                        "    sleep(0.1)\n");
            yield return Until(() => _runner.Facade.State == SimulationState.Running, "en marcha");
            _panel.StopButton.onClick.Invoke();
            yield return Until(() => _runner.Facade.State == SimulationState.Stopped, "detenido");
            Assert.That(_panel.Editor.ExecutingLine, Is.Null, "sin programa en marcha no se marca ninguna línea");
        }

        [UnityTest]
        public IEnumerator LaPlaca_QuedaFueraDelPanel()
        {
            yield return null;
            var cam = Camera.main;
            var b = _board.Bounds;
            float left = cam.WorldToViewportPoint(new Vector3(b.xMin, b.center.y)).x;
            float right = cam.WorldToViewportPoint(new Vector3(b.xMax, b.center.y)).x;
            Assert.That(left, Is.GreaterThanOrEqualTo(EditorPanel.WidthFraction), "la placa no debe quedar tapada por el panel");
            Assert.That(right, Is.LessThanOrEqualTo(1f));
        }
    }
}
