using System.Collections.Generic;
using ImatPiSim.Domain.Components;
using UnityEngine;

namespace ImatPiSim.Presentation
{
    /// <summary>
    /// Dibuja la Raspberry Pi con la IMAT HAT a partir del modelo (LED D1–D8 y pulsadores
    /// SW1–SW4, con su GPIO) y crea una vista por elemento.
    /// </summary>
    /// <remarks>
    /// La disposición es esquemática (formas simples): sigue el orden lógico D1…D8 y SW1…SW4, no
    /// la posición física de la placa. Etiquetas con la correspondencia confirmada en
    /// docs/hardware.md §3.2 y §3.3.
    /// </remarks>
    [RequireComponent(typeof(SimulationRunner))]
    public sealed class BoardView : MonoBehaviour
    {
        private static readonly Color BoardColor = new Color(0.07f, 0.33f, 0.16f);
        private static readonly Color BoardEdge = new Color(0.04f, 0.20f, 0.10f);
        private static readonly Color HeaderColor = new Color(0.10f, 0.10f, 0.10f);
        private static readonly Color Silk = new Color(0.92f, 0.94f, 0.92f);

        private readonly List<LedView> _leds = new List<LedView>();
        private readonly List<ButtonView> _buttons = new List<ButtonView>();

        public IReadOnlyList<LedView> Leds => _leds;
        public IReadOnlyList<ButtonView> Buttons => _buttons;

        private void Start()
        {
            var facade = GetComponent<SimulationRunner>().Facade;
            var hat = facade.Hat;
            var root = new GameObject("IMAT HAT").transform;
            root.SetParent(transform, false);
            root.localPosition = new Vector3(2.2f, 0, 0);

            // Placa y conector de 40 pines
            SpriteFactory.Shape(root, "Borde", SpriteFactory.Square, Vector2.zero, new Vector2(10.4f, 6.6f), BoardEdge, 0);
            SpriteFactory.Shape(root, "Placa", SpriteFactory.Square, Vector2.zero, new Vector2(10.2f, 6.4f), BoardColor, 1);
            SpriteFactory.Shape(root, "Conector 40 pines", SpriteFactory.Square, new Vector2(0, 2.85f), new Vector2(8.6f, 0.45f), HeaderColor, 2);
            SpriteFactory.Label(root, "IMAT HAT  ·  Raspberry Pi 4", new Vector2(0, 2.25f), 0.38f, Silk);

            // LED D1..D8 → GPIO20..27
            for (int n = 1; n <= 8; n++)
            {
                var pos = new Vector2(-4.2f + (n - 1) * 1.2f, 0.9f);
                var go = new GameObject($"D{n} (GPIO{ImatHat.LedGpio[n - 1]})");
                go.transform.SetParent(root, false);
                go.transform.localPosition = pos;
                var glow = SpriteFactory.Shape(go.transform, "Halo", SpriteFactory.Circle, Vector2.zero, Vector2.one * 1.05f, Color.clear, 3);
                var body = SpriteFactory.Shape(go.transform, "LED", SpriteFactory.Circle, Vector2.zero, Vector2.one * 0.55f, Color.black, 4);
                var burnt = SpriteFactory.Label(go.transform, "X", Vector2.zero, 0.45f, new Color(1f, 0.55f, 0.2f), order: 5);
                SpriteFactory.Label(go.transform, $"D{n}\nGPIO{ImatHat.LedGpio[n - 1]}", new Vector2(0, -0.75f), 0.22f, Silk);
                var view = go.AddComponent<LedView>();
                view.Bind(hat.Led(n), n, body, glow, burnt);
                _leds.Add(view);
            }

            // Pulsadores SW1..SW4 → GPIO7, 16, 17, 19
            for (int n = 1; n <= 4; n++)
            {
                var pos = new Vector2(-3.3f + (n - 1) * 2.2f, -1.8f);
                var go = new GameObject($"SW{n} (GPIO{ImatHat.ButtonGpio[n - 1]})");
                go.transform.SetParent(root, false);
                go.transform.localPosition = pos;
                SpriteFactory.Shape(go.transform, "Base", SpriteFactory.Square, Vector2.zero, Vector2.one * 1.0f, new Color(0.75f, 0.75f, 0.72f), 3);
                var cap = SpriteFactory.Shape(go.transform, "Botón", SpriteFactory.Circle, Vector2.zero, Vector2.one * 0.9f, Color.gray, 4);
                SpriteFactory.Label(go.transform, $"SW{n}\nGPIO{ImatHat.ButtonGpio[n - 1]}", new Vector2(0, -0.95f), 0.22f, Silk);
                var collider = go.AddComponent<BoxCollider2D>();
                collider.size = Vector2.one * 1.1f;
                var view = go.AddComponent<ButtonView>();
                view.Bind(GetComponent<SimulationRunner>().Facade, hat.Button(n), n, cap);
                _buttons.Add(view);
            }
        }
    }
}
