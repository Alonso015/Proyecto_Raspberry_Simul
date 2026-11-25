using ImatPiSim.Domain.Components;
using ImatPiSim.Domain.Simulation;
using UnityEngine;
using DomainComponent = ImatPiSim.Domain.Components.Component;

namespace ImatPiSim.Presentation
{
    /// <summary>
    /// Vista de un pulsador (de la HAT o pinchado en la protoboard): mientras se mantiene el clic,
    /// está pulsado. Pasa la acción a la fachada y se redibuja cuando el <see cref="PushButton"/>
    /// del dominio avisa (Observer).
    /// </summary>
    /// <remarks>
    /// Un pulsador externo solo se pulsa con el programa en marcha: parado, el clic es para
    /// moverlo (montaje, E16).
    /// </remarks>
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class ButtonView : MonoBehaviour
    {
        private static readonly Color Released = new Color(0.18f, 0.18f, 0.20f);
        private static readonly Color Pressed = new Color(0.45f, 0.75f, 1.00f);

        private SimulatorFacade _facade;
        private PushButton _button;
        private SpriteRenderer _cap;
        private Vector3 _capScale;

        /// <summary>Número del pulsador en la HAT (1..4), o 0 si es externo.</summary>
        public int Number { get; private set; }

        public bool IsExternal => Number == 0;

        public void Bind(SimulatorFacade facade, PushButton button, int number, SpriteRenderer cap)
        {
            _facade = facade;
            _button = button;
            Number = number;
            _cap = cap;
            _capScale = cap.transform.localScale;   // tamaño de suelto: al pulsar se hunde un poco
            _button.StateChanged += OnStateChanged;
            Refresh();
        }

        private void OnDestroy()
        {
            if (_button != null) _button.StateChanged -= OnStateChanged;
        }

        // Unity llama a estos métodos al hacer clic sobre el BoxCollider2D (Input Manager clásico).
        private void OnMouseDown()
        {
            if (!IsExternal || _facade.IsRunning) Press();
        }

        private void OnMouseUp()
        {
            if (_button.IsPressed) Release();
        }

        /// <summary>Pulsa el botón (lo mismo que hace el ratón; público para las pruebas).</summary>
        public void Press() => _facade.PressButton(_button);

        public void Release() => _facade.ReleaseButton(_button);

        private void OnStateChanged(DomainComponent _) => Refresh();

        private void Refresh()
        {
            if (_cap == null) return;
            _cap.color = _button.IsPressed ? Pressed : Released;
            _cap.transform.localScale = _capScale * (_button.IsPressed ? 0.91f : 1f);
        }
    }
}
