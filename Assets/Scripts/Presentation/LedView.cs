using ImatPiSim.Domain.Components;
using UnityEngine;
using DomainComponent = ImatPiSim.Domain.Components.Component;

namespace ImatPiSim.Presentation
{
    /// <summary>
    /// Vista de un LED de la HAT. Observa al <see cref="Led"/> del dominio (evento
    /// <c>StateChanged</c>, patrón Observer) y solo traduce su estado a color.
    /// </summary>
    /// <remarks>
    /// El brillo visible se normaliza al de un LED de la HAT en continua (≈ 4 mA, brillo ≈ 0,2):
    /// así un LED de la HAT encendido se ve "lleno" y uno con PWM al 25 % se ve a un cuarto.
    /// </remarks>
    public sealed class LedView : MonoBehaviour
    {
        private const float FullBrightness = 0.2f;

        private static readonly Color OffColor = new Color(0.30f, 0.06f, 0.05f);
        private static readonly Color OnColor = new Color(1.00f, 0.18f, 0.12f);
        private static readonly Color BurntColor = new Color(0.12f, 0.12f, 0.12f);

        private Led _led;
        private SpriteRenderer _body;
        private SpriteRenderer _glow;
        private TextMesh _burntMark;

        /// <summary>Número del LED en la HAT (1..8), para pruebas y depuración.</summary>
        public int Number { get; private set; }

        /// <summary>Intensidad mostrada (0..1).</summary>
        public float VisibleLevel { get; private set; }

        public void Bind(Led led, int number, SpriteRenderer body, SpriteRenderer glow, TextMesh burntMark)
        {
            _led = led;
            Number = number;
            _body = body;
            _glow = glow;
            _burntMark = burntMark;
            _led.StateChanged += OnStateChanged;
            Refresh();
        }

        private void OnDestroy()
        {
            if (_led != null) _led.StateChanged -= OnStateChanged;
        }

        private void OnStateChanged(DomainComponent _) => Refresh();

        private void Refresh()
        {
            if (_body == null) return;
            if (_led.IsBurnt)
            {
                VisibleLevel = 0;
                _body.color = BurntColor;
                _glow.color = Color.clear;
                _burntMark.gameObject.SetActive(true);
                return;
            }
            _burntMark.gameObject.SetActive(false);
            VisibleLevel = Mathf.Clamp01((float)_led.Brightness / FullBrightness);
            _body.color = Color.Lerp(OffColor, OnColor, VisibleLevel);
            _glow.color = new Color(1f, 0.25f, 0.15f, 0.45f * VisibleLevel);
        }
    }
}
