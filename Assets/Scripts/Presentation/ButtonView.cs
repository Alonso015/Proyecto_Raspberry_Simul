using ImatPiSim.Domain.Components;
using ImatPiSim.Domain.Simulation;
using UnityEngine;
using DomainComponent = ImatPiSim.Domain.Components.Component;

namespace ImatPiSim.Presentation
{
    /// <summary>
    /// Vista de un pulsador de la HAT: mientras se mantiene el clic, está pulsado. Pasa la acción
    /// a la fachada y se redibuja cuando el <see cref="PushButton"/> del dominio avisa (Observer).
    /// </summary>
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class ButtonView : MonoBehaviour
    {
        private static readonly Color Released = new Color(0.18f, 0.18f, 0.20f);
        private static readonly Color Pressed = new Color(0.45f, 0.75f, 1.00f);

        private SimulatorFacade _facade;
        private PushButton _button;
        private SpriteRenderer _cap;

        /// <summary>Número del pulsador en la HAT (1..4).</summary>
        public int Number { get; private set; }

        public void Bind(SimulatorFacade facade, PushButton button, int number, SpriteRenderer cap)
        {
            _facade = facade;
            _button = button;
            Number = number;
            _cap = cap;
            _button.StateChanged += OnStateChanged;
            Refresh();
        }

        private void OnDestroy()
        {
            if (_button != null) _button.StateChanged -= OnStateChanged;
        }

        // Unity llama a estos métodos al hacer clic sobre el BoxCollider2D (Input Manager clásico).
        private void OnMouseDown() => Press();
        private void OnMouseUp() => Release();

        /// <summary>Pulsa el botón (lo mismo que hace el ratón; público para las pruebas).</summary>
        public void Press() => _facade.PressButton(Number);

        public void Release() => _facade.ReleaseButton(Number);

        private void OnStateChanged(DomainComponent _) => Refresh();

        private void Refresh()
        {
            if (_cap == null) return;
            _cap.color = _button.IsPressed ? Pressed : Released;
            _cap.transform.localScale = Vector3.one * (_button.IsPressed ? 0.82f : 0.9f);
        }
    }
}
