using System.Collections.Generic;
using ImatPiSim.Domain.Components;
using ImatPiSim.Domain.Wiring;
using UnityEngine;

namespace ImatPiSim.Presentation.Assembly
{
    /// <summary>
    /// Dibujo provisional de cada tipo de componente (formas simples, hasta el arte de E19). Se
    /// dibuja en horizontal, con las patillas sobre el eje x; quien lo usa lo coloca y lo gira.
    /// </summary>
    public static class ComponentArt
    {
        public static readonly Color LegColor = new Color(0.62f, 0.62f, 0.64f);

        /// <summary>Partes que las vistas animan (LED que se enciende, botón que se hunde).</summary>
        public sealed class Parts
        {
            public SpriteRenderer Body;
            public SpriteRenderer Glow;
            public TextMesh BurntMark;
            public SpriteRenderer Cap;
            public readonly List<SpriteRenderer> All = new List<SpriteRenderer>();
        }

        /// <summary>Ángulo (grados) que gira el dibujo para que las patillas vayan en <paramref name="direction"/>.</summary>
        public static float Angle(Direction direction) => direction switch
        {
            Direction.Right => 0,
            Direction.Down => -90,
            Direction.Left => 180,
            _ => 90
        };

        /// <summary>Longitud entre la primera y la última patilla, en unidades del mundo.</summary>
        public static float Span(ComponentType type) => (type.PinNames.Count - 1) * type.LeadSpacing * BreadboardView.Pitch;

        /// <summary>
        /// Dibuja el componente centrado en <paramref name="parent"/>: patillas en x = −span/2 … +span/2.
        /// </summary>
        /// <param name="baseOrder">Orden de dibujo de lo más bajo (las patillas).</param>
        public static Parts Draw(Transform parent, ComponentType type, float span, int baseOrder, float alpha = 1f)
        {
            var parts = new Parts();
            SpriteRenderer S(string name, Sprite sprite, Vector2 pos, Vector2 size, Color color, int order)
            {
                color.a *= alpha;
                var sr = SpriteFactory.Shape(parent, name, sprite, pos, size, color, baseOrder + order);
                parts.All.Add(sr);
                return sr;
            }

            // Patillas: una barra fina de la primera a la última, y un punto en cada agujero
            S("Patillas", SpriteFactory.Square, Vector2.zero, new Vector2(span, 0.05f), LegColor, 0);
            int n = type.PinNames.Count;
            for (int i = 0; i < n; i++)
            {
                float x = n == 1 ? 0 : -span / 2 + i * span / (n - 1);
                S("Patilla " + type.PinNames[i], SpriteFactory.Circle, new Vector2(x, 0), Vector2.one * 0.12f, LegColor, 1);
            }

            switch (type.Id)
            {
                case "resistor":
                    parts.Body = S("Cuerpo", SpriteFactory.Square, Vector2.zero, new Vector2(Mathf.Max(0.55f, span * 0.55f), 0.2f),
                                   new Color(0.86f, 0.76f, 0.56f), 2);
                    break;
                case "led":
                    parts.Glow = S("Halo", SpriteFactory.Circle, Vector2.zero, Vector2.one * 0.75f, Color.clear, 2);
                    parts.Body = S("LED", SpriteFactory.Circle, Vector2.zero, Vector2.one * 0.34f, new Color(0.30f, 0.06f, 0.05f), 3);
                    // "+" junto al ánodo (primera patilla)
                    var plus = SpriteFactory.Label(parent, "+", new Vector2(-span / 2, 0.24f), 0.2f,
                                                   new Color(0.85f, 0.2f, 0.18f, alpha), order: baseOrder + 4);
                    plus.name = "Ánodo";
                    parts.BurntMark = SpriteFactory.Label(parent, "X", Vector2.zero, 0.3f, new Color(1f, 0.55f, 0.2f, alpha), order: baseOrder + 5);
                    parts.BurntMark.gameObject.SetActive(false);
                    break;
                case "pushbutton":
                    S("Base", SpriteFactory.Square, Vector2.zero, new Vector2(Mathf.Max(0.5f, span + 0.1f), 0.5f), new Color(0.70f, 0.70f, 0.68f), 2);
                    parts.Cap = S("Botón", SpriteFactory.Circle, Vector2.zero, Vector2.one * 0.38f, new Color(0.18f, 0.18f, 0.20f), 3);
                    break;
                case "potentiometer":
                    parts.Body = S("Cuerpo", SpriteFactory.Square, new Vector2(0, 0.12f), new Vector2(span + 0.3f, 0.5f), new Color(0.20f, 0.38f, 0.75f), 2);
                    S("Mando", SpriteFactory.Circle, new Vector2(0, 0.12f), Vector2.one * 0.3f, new Color(0.9f, 0.9f, 0.92f), 3);
                    break;
                case "ldr":
                    parts.Body = S("Cuerpo", SpriteFactory.Circle, Vector2.zero, Vector2.one * 0.36f, new Color(0.85f, 0.55f, 0.20f), 2);
                    S("Pista", SpriteFactory.Square, Vector2.zero, new Vector2(0.22f, 0.05f), new Color(0.45f, 0.20f, 0.10f), 3);
                    break;
                default:
                    parts.Body = S("Cuerpo", SpriteFactory.Square, Vector2.zero, new Vector2(span + 0.2f, 0.3f), Color.gray, 2);
                    break;
            }
            return parts;
        }
    }
}
