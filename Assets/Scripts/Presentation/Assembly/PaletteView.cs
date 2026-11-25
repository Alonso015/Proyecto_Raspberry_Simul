using System.Collections.Generic;
using ImatPiSim.Domain.Components;
using UnityEngine;

namespace ImatPiSim.Presentation.Assembly
{
    /// <summary>Una pieza de la paleta: qué tipo crea (por su id) y con qué parámetros.</summary>
    public sealed class PaletteItem
    {
        public PaletteItem(string label, ComponentType type, IReadOnlyDictionary<string, double> parameters = null)
        {
            Label = label;
            Type = type;
            Parameters = parameters;
        }

        public string Label { get; }
        public ComponentType Type { get; }
        public IReadOnlyDictionary<string, double> Parameters { get; }
        public override string ToString() => Label;
    }

    /// <summary>
    /// Franja de piezas entre la HAT y la protoboard. Solo dibuja y dice qué pieza hay bajo el
    /// ratón; arrastrarla es cosa de <see cref="AssemblyController"/>.
    /// </summary>
    /// <remarks>
    /// Las piezas se crean después con <see cref="ComponentFactory"/> a partir de su tipo (patrón
    /// Factory): la paleta no conoce las clases concretas. Las resistencias van en cuatro valores
    /// fijos (decidido el 24/11/2026).
    /// </remarks>
    public sealed class PaletteView : MonoBehaviour, IWorkspaceArea
    {
        public static readonly Vector2 Centre = new Vector2(0, -0.55f);
        private const float Slot = 1.3f;
        private const float SlotSize = 1.16f;

        public static readonly IReadOnlyList<PaletteItem> Items = new[]
        {
            Resistor("220 Ω", 220), Resistor("330 Ω", 330), Resistor("1 kΩ", 1_000), Resistor("10 kΩ", 10_000),
            new PaletteItem("LED", ComponentTypes.Led),
            new PaletteItem("Pulsador", ComponentTypes.PushButton),
            new PaletteItem("Potenc. 10 kΩ", ComponentTypes.Potentiometer),
            new PaletteItem("LDR", ComponentTypes.Ldr)
        };

        private static PaletteItem Resistor(string label, double ohms) =>
            new PaletteItem(label, ComponentTypes.Resistor, new Dictionary<string, double> { ["resistance"] = ohms });

        private readonly List<(PaletteItem item, Rect rect)> _slots = new List<(PaletteItem, Rect)>();

        public Rect Bounds => new Rect((Vector2)transform.position + Centre - new Vector2(Items.Count * Slot, SlotSize) / 2,
                                       new Vector2(Items.Count * Slot, SlotSize));

        private void Awake()
        {
            var root = new GameObject("Paleta").transform;
            root.SetParent(transform, false);
            root.localPosition = Centre;
            float x0 = -(Items.Count - 1) * Slot / 2;
            for (int i = 0; i < Items.Count; i++)
            {
                var item = Items[i];
                var slot = new GameObject(item.Label).transform;
                slot.SetParent(root, false);
                slot.localPosition = new Vector2(x0 + i * Slot, 0);
                SpriteFactory.Shape(slot, "Fondo", SpriteFactory.Square, Vector2.zero, Vector2.one * SlotSize, new Color(0.18f, 0.19f, 0.23f), 0);

                var icon = new GameObject("Icono").transform;
                icon.SetParent(slot, false);
                icon.localPosition = new Vector2(0, 0.15f);
                float span = ComponentArt.Span(item.Type);
                icon.localScale = Vector3.one * Mathf.Min(1f, 0.8f / Mathf.Max(span, 0.01f));
                ComponentArt.Draw(icon, item.Type, span, 1);
                SpriteFactory.Label(slot, item.Label, new Vector2(0, -0.38f), 0.2f, new Color(0.88f, 0.89f, 0.92f));

                var world = (Vector2)slot.position;
                _slots.Add((item, new Rect(world - Vector2.one * SlotSize / 2, Vector2.one * SlotSize)));
            }
        }

        /// <summary>La pieza bajo un punto del mundo, o <c>null</c>.</summary>
        public PaletteItem ItemAt(Vector2 world)
        {
            foreach (var (item, rect) in _slots)
                if (rect.Contains(world)) return item;
            return null;
        }

        /// <summary>Centro de una pieza en el mundo (para las pruebas).</summary>
        public Vector2 PositionOf(PaletteItem item)
        {
            foreach (var (it, rect) in _slots)
                if (it == item) return rect.center;
            return Vector2.zero;
        }
    }
}
