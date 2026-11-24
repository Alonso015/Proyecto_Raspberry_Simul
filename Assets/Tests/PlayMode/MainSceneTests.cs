using System;
using System.Collections;
using System.Linq;
using ImatPiSim.Domain.Simulation;
using ImatPiSim.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ImatPiSim.PlayMode.Tests
{
    /// <summary>
    /// Etapa E14: la escena principal arranca, dibuja la HAT y reacciona a los pulsadores.
    /// Se ejecutan en modo Play (Test Runner → PlayMode).
    /// </summary>
    public class MainSceneTests
    {
        private SimulationRunner _runner;
        private BoardView _board;

        [UnitySetUp]
        public IEnumerator CargarEscena()
        {
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                "Assets/Scenes/Main.unity", new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync("Main");
#endif
            yield return null;   // Awake y Start
            yield return null;
            _runner = UnityEngine.Object.FindFirstObjectByType<SimulationRunner>();
            _board = UnityEngine.Object.FindFirstObjectByType<BoardView>();
        }

        /// <summary>Deja pasar frames hasta que se cumpla la condición (o falla tras 2 s reales).</summary>
        private static IEnumerator Until(Func<bool> condition, string what)
        {
            float limit = Time.realtimeSinceStartup + 2f;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > limit) Assert.Fail("No se cumplió a tiempo: " + what);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator LaEscena_DibujaOchoLedsYCuatroPulsadores_YElProgramaArranca()
        {
            Assert.That(_runner, Is.Not.Null, "falta el SimulationRunner en la escena");
            Assert.That(_board.Leds, Has.Count.EqualTo(8));
            Assert.That(_board.Buttons, Has.Count.EqualTo(4));
            yield return Until(() => _runner.Facade.State == SimulationState.Running, "programa en marcha");
            Assert.That(_board.Leds.All(l => l.VisibleLevel == 0), Is.True, "todos apagados al empezar");
        }

        [UnityTest]
        public IEnumerator PulsarSw1_EnciendeD1_YSoltarlo_LoApaga()
        {
            var sw1 = _board.Buttons.Single(b => b.Number == 1);
            var d1 = _board.Leds.Single(l => l.Number == 1);

            sw1.Press();   // lo mismo que hace OnMouseDown
            yield return Until(() => d1.VisibleLevel > 0.5f, "D1 encendido");
            Assert.That(_board.Leds.Where(l => l.Number != 1).All(l => l.VisibleLevel == 0), Is.True);

            sw1.Release();
            yield return Until(() => d1.VisibleLevel == 0, "D1 apagado");
        }

        [UnityTest]
        public IEnumerator PulsarSw4_TerminaElPrograma()
        {
            _board.Buttons.Single(b => b.Number == 4).Press();
            yield return Until(() => _runner.Facade.State == SimulationState.Finished, "programa terminado");
            Assert.That(_runner.Facade.Output, Does.Contain("Fin del programa"));
        }
    }
}
