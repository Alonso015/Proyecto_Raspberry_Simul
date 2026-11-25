using System.Collections;
using ImatPiSim.Domain.Simulation;
using ImatPiSim.Presentation;
using ImatPiSim.Presentation.Ui;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ImatPiSim.PlayMode.Tests
{
    /// <summary>
    /// Clics que pasan por el mismo camino que el ratón real: el StandaloneInputModule lee el
    /// ratón en cada frame (aquí uno simulado con <see cref="BaseInput"/>), lanza el rayo y
    /// envía los eventos a la interfaz.
    /// </summary>
    public class RealMouseTests
    {
        /// <summary>Ratón simulado: el módulo de entrada lo lee en lugar del de verdad.</summary>
        private sealed class FakeMouse : BaseInput
        {
            public Vector2 Position;
            public bool Held;
            private bool _down, _up;

            public void Press() { Held = true; _down = true; }
            public void Release() { Held = false; _up = true; }

            /// <summary>Los flancos (Down/Up) duran un solo frame, como en Input.</summary>
            public void EndFrame() { _down = false; _up = false; }

            public override bool mousePresent => true;
            public override Vector2 mousePosition => Position;
            public override Vector2 mouseScrollDelta => Vector2.zero;
            public override bool GetMouseButtonDown(int button) => button == 0 && _down;
            public override bool GetMouseButtonUp(int button) => button == 0 && _up;
            public override bool GetMouseButton(int button) => button == 0 && Held;
            public override bool touchSupported => false;
            public override int touchCount => 0;
        }

        private EditorPanel _panel;
        private SimulationRunner _runner;
        private FakeMouse _mouse;

        [UnitySetUp]
        public IEnumerator CargarEscena()
        {
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                "Assets/Scenes/Main.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            yield return null;
            _panel = Object.FindFirstObjectByType<EditorPanel>();
            _runner = Object.FindFirstObjectByType<SimulationRunner>();
            var module = Object.FindFirstObjectByType<StandaloneInputModule>();
            Assert.That(module, Is.Not.Null, "falta el módulo de entrada");
            _mouse = module.gameObject.AddComponent<FakeMouse>();
            module.inputOverride = _mouse;
            yield return null;
        }

        private IEnumerator Click(Vector2 screen)
        {
            _mouse.Position = screen;
            yield return null;               // el módulo ve el ratón encima
            _mouse.EndFrame();
            _mouse.Press();
            yield return null;               // pulsar
            _mouse.EndFrame();
            _mouse.Release();
            yield return null;               // soltar → clic
            _mouse.EndFrame();
            yield return null;
            yield return null;
        }

        private static Vector2 ScreenCentre(Component c)
        {
            var rt = (RectTransform)c.transform;
            return RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
        }

        [UnityTest]
        public IEnumerator ClicRealEnElEditor_PermiteEscribir()
        {
            yield return Click(ScreenCentre(_panel.Editor.Input));
            TestContext.Out.WriteLine("seleccionado: " + EventSystem.current.currentSelectedGameObject?.name);
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(_panel.Editor.Input.gameObject));
            Assert.That(_panel.Editor.Input.isFocused, Is.True);
        }

        [UnityTest]
        public IEnumerator ElEditor_TieneConQueDibujarCursorYSeleccion()
        {
            // TMP_InputField dibuja el cursor y la selección con un hijo "Caret" (TMP_SelectionCaret).
            yield return null;
            var caret = _panel.Editor.Input.textViewport.GetComponentInChildren<TMPro.TMP_SelectionCaret>(true);
            Assert.That(caret, Is.Not.Null, "sin Caret no se ve dónde se hace clic ni lo seleccionado");
        }

        [UnityTest]
        public IEnumerator ClicRealYArrastre_SeleccionaTexto()
        {
            var input = _panel.Editor.Input;
            var text = input.textComponent;
            yield return null;
            // Del primer carácter de la línea 1 al último de la línea 2, arrastrando con el botón pulsado
            text.ForceMeshUpdate();
            var info = text.textInfo;
            Vector2 Screen(int charIndex, bool right)
            {
                var ch = info.characterInfo[charIndex];
                var local = new Vector3(right ? ch.topRight.x : ch.bottomLeft.x + 1, (ch.ascender + ch.descender) / 2, 0);
                return RectTransformUtility.WorldToScreenPoint(null, text.transform.TransformPoint(local));
            }
            int endOfLine1 = info.lineInfo[0].lastVisibleCharacterIndex;
            _mouse.Position = Screen(0, false);
            yield return null;
            _mouse.EndFrame();
            _mouse.Press();
            yield return null;
            _mouse.EndFrame();
            for (int i = 1; i <= 5; i++)
            {
                _mouse.Position = Vector2.Lerp(Screen(0, false), Screen(endOfLine1, true), i / 5f);
                yield return null;
            }
            _mouse.Release();
            yield return null;
            _mouse.EndFrame();
            yield return null;

            int a = input.selectionStringAnchorPosition, b = input.selectionStringFocusPosition;
            TestContext.Out.WriteLine($"selección: {a}–{b}");
            Assert.That(System.Math.Abs(b - a), Is.GreaterThan(5), "arrastrar selecciona texto");
        }

        [UnityTest]
        public IEnumerator ClicRealEnParar_DetieneElPrograma()
        {
            Assume.That(_runner.Facade.IsRunning, "el programa de ejemplo arranca solo");
            yield return Click(ScreenCentre(_panel.StopButton));
            yield return null;
            // El programa de ejemplo captura el KeyboardInterrupt (try/except) y termina normalmente.
            Assert.That(_runner.Facade.IsRunning, Is.False);
            Assert.That(_runner.Facade.Output, Has.Member("Programa detenido"));
        }
    }
}
