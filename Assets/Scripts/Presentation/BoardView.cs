using System.Collections.Generic;
using ImatPiSim.Domain.Components;
using UnityEngine;
using DomainPin = ImatPiSim.Domain.Components.Pin;

namespace ImatPiSim.Presentation
{
    /// <summary>
    /// Dibuja la Raspberry Pi con la IMAT HAT a partir del modelo: LED D1–D8, pulsadores SW1–SW4
    /// y los conectores a los que se pueden llevar cables (J11, J3, J2, J1, J7–J10, J12).
    /// </summary>
    /// <remarks>
    /// La disposición es esquemática (formas simples): sigue el orden lógico D1…D8 y SW1…SW4, no
    /// la posición física de la placa. Etiquetas con la correspondencia confirmada en
    /// docs/hardware.md §3.2–3.7. Cada pin de conector sabe a qué <see cref="DomainPin"/> del
    /// dominio corresponde: el montaje (E16) los usa como extremos de cable.
    /// </remarks>
    [RequireComponent(typeof(SimulationRunner))]
    public sealed class BoardView : MonoBehaviour, IWorkspaceArea
    {
        /// <summary>Centro de la HAT en el mundo.</summary>
        public static readonly Vector2 Centre = new Vector2(0, 4.3f);

        private static readonly Vector2 BoardSize = new Vector2(10.4f, 8.2f);
        private const float PinPitch = 0.40f;
        private const float PinSize = 0.22f;
        private const float GroupGap = 0.52f;

        private static readonly Color BoardColor = new Color(0.07f, 0.33f, 0.16f);
        private static readonly Color BoardEdge = new Color(0.04f, 0.20f, 0.10f);
        private static readonly Color HeaderColor = new Color(0.10f, 0.10f, 0.10f);
        private static readonly Color Silk = new Color(0.92f, 0.94f, 0.92f);

        private readonly List<LedView> _leds = new List<LedView>();
        private readonly List<ButtonView> _buttons = new List<ButtonView>();
        private readonly Dictionary<DomainPin, Vector2> _connectorPins = new Dictionary<DomainPin, Vector2>();
        private readonly Dictionary<DomainPin, string> _pinInfo = new Dictionary<DomainPin, string>();

        public IReadOnlyList<LedView> Leds => _leds;
        public IReadOnlyList<ButtonView> Buttons => _buttons;

        /// <summary>Rectángulo que ocupa la placa en el mundo (para encuadrar la cámara).</summary>
        public Rect Bounds => new Rect((Vector2)transform.position + Centre - BoardSize / 2, BoardSize);

        /// <summary>Pines de los conectores y su posición en el mundo.</summary>
        public IReadOnlyDictionary<DomainPin, Vector2> ConnectorPins => _connectorPins;

        public bool IsBuilt { get; private set; }

        private void Start()
        {
            var facade = GetComponent<SimulationRunner>().Facade;
            var hat = facade.Hat;
            var root = new GameObject("IMAT HAT").transform;
            root.SetParent(transform, false);
            root.localPosition = Centre;

            // Placa y conector de 40 pines
            SpriteFactory.Shape(root, "Borde", SpriteFactory.Square, Vector2.zero, BoardSize, BoardEdge, 0);
            SpriteFactory.Shape(root, "Placa", SpriteFactory.Square, Vector2.zero, BoardSize - new Vector2(0.2f, 0.2f), BoardColor, 1);
            SpriteFactory.Shape(root, "Conector 40 pines", SpriteFactory.Square, new Vector2(0, 3.65f), new Vector2(8.6f, 0.45f), HeaderColor, 2);
            SpriteFactory.Label(root, "IMAT HAT  ·  Raspberry Pi 4", new Vector2(0, 3.0f), 0.38f, Silk);

            // LED D1..D8 → GPIO20..27
            for (int n = 1; n <= 8; n++)
            {
                var pos = new Vector2(-4.2f + (n - 1) * 1.2f, 1.7f);
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
                var pos = new Vector2(-3.3f + (n - 1) * 2.2f, -0.45f);
                var go = new GameObject($"SW{n} (GPIO{ImatHat.ButtonGpio[n - 1]})");
                go.transform.SetParent(root, false);
                go.transform.localPosition = pos;
                SpriteFactory.Shape(go.transform, "Base", SpriteFactory.Square, Vector2.zero, Vector2.one * 1.0f, new Color(0.75f, 0.75f, 0.72f), 3);
                var cap = SpriteFactory.Shape(go.transform, "Botón", SpriteFactory.Circle, Vector2.zero, Vector2.one * 0.9f, Color.gray, 4);
                SpriteFactory.Label(go.transform, $"SW{n}\nGPIO{ImatHat.ButtonGpio[n - 1]}", new Vector2(0, -0.95f), 0.22f, Silk);
                var collider = go.AddComponent<BoxCollider2D>();
                collider.size = Vector2.one * 1.1f;
                var view = go.AddComponent<ButtonView>();
                view.Bind(facade, hat.Button(n), n, cap);
                _buttons.Add(view);
            }

            // Conectores (docs/hardware.md §3): dos filas en la parte de abajo
            var rowA = new[] { Group("J11", 8), Group("J3", 4), Group("J2", 4) };
            var rowB = new[] { Group("J1", 8), Group("J7", 3), Group("J8", 3), Group("J9", 3), Group("J10", 3), Group("J12", 4) };
            DrawRow(root, hat, rowA, -2.45f);
            DrawRow(root, hat, rowB, -3.45f);
            IsBuilt = true;
        }

        private static (string name, int count) Group(string name, int count) => (name, count);

        private void DrawRow(Transform root, ImatHat hat, (string name, int count)[] groups, float y)
        {
            float width = -GroupGap;
            foreach (var g in groups) width += (g.count - 1) * PinPitch + GroupGap;
            float x = -width / 2;
            foreach (var (name, count) in groups)
            {
                float groupWidth = (count - 1) * PinPitch;
                SpriteFactory.Label(root, name, new Vector2(x + groupWidth / 2, y + 0.32f), 0.2f, Silk);
                SpriteFactory.Shape(root, name, SpriteFactory.Square, new Vector2(x + groupWidth / 2, y),
                                    new Vector2(groupWidth + PinSize + 0.12f, PinSize + 0.12f), HeaderColor, 2);
                for (int i = 0; i < count; i++)
                {
                    var pin = hat.GetPin($"{name}.{i + 1}");
                    var local = new Vector2(x + i * PinPitch, y);
                    SpriteFactory.Shape(root, pin.Name, SpriteFactory.Square, local, Vector2.one * PinSize, PinColor(pin.Name), 3);
                    var color = PinColor(pin.Name);
                    SpriteFactory.Label(root, PinLabel(pin.Name), local + new Vector2(0, -0.27f), 0.13f,
                                        color == GndColor || color == SignalColor ? Silk : color);
                    _connectorPins[pin] = root.TransformPoint(local);
                    _pinInfo[pin] = PinFunction(pin.Name);
                }
                x += groupWidth + GroupGap;
            }
        }

        /// <summary>El pin de conector que hay en <paramref name="world"/> (con algo de margen), o <c>null</c>.</summary>
        public DomainPin ConnectorAt(Vector2 world, float radius = 0.17f)
        {
            DomainPin best = null;
            float bestDistance = radius;
            foreach (var kv in _connectorPins)
            {
                float d = Vector2.Distance(kv.Value, world);
                if (d <= bestDistance) { best = kv.Key; bestDistance = d; }
            }
            return best;
        }

        /// <summary>Texto para el jugador: "J7.1 · GPIO14 (servo 1, señal)".</summary>
        public string Describe(DomainPin pin) => _pinInfo.TryGetValue(pin, out var info) ? $"{pin.Name} · {info}" : pin.Name;

        // Función de cada pin según el esquema (docs/hardware.md §3; ImatHat la cablea igual).
        private static string PinFunction(string name)
        {
            var parts = name.Split('.');
            int n = int.Parse(parts[1]);
            switch (parts[0])
            {
                case "J11": return $"GPIO{ImatHat.LedGpio[n - 1]} (D{n})";
                case "J3": return $"GPIO{ImatHat.ButtonGpio[n - 1]} (SW{n})";
                case "J2": return n == 1 ? "3,3 V" : n == 2 ? "GPIO3 (SDA)" : n == 3 ? "GPIO2 (SCL)" : "GND";
                case "J1": return n == 8 ? "CH7 del MCP3008 (si JP3 está en J1)" : $"CH{n - 1} del MCP3008";
                case "J12": return n == 1 ? "5 V" : n == 3 ? "3,3 V" : "GND";
                default:
                    int servo = int.Parse(parts[0].Substring(1)) - 7;
                    return n == 1 ? $"GPIO{ImatHat.ServoGpio[servo]} (servo {servo + 1}, señal)" : n == 2 ? "5 V (servo)" : "GND (servo)";
            }
        }

        private static readonly Color GndColor = new Color(0.15f, 0.15f, 0.15f);
        private static readonly Color V5Color = new Color(0.95f, 0.30f, 0.25f);
        private static readonly Color V33Color = new Color(1.00f, 0.62f, 0.20f);
        private static readonly Color SignalColor = new Color(0.88f, 0.80f, 0.35f);

        private static Color PinColor(string name)
        {
            string f = PinFunction(name);
            if (f.StartsWith("GND")) return GndColor;
            if (f.StartsWith("5 V")) return V5Color;
            if (f.StartsWith("3,3 V")) return V33Color;
            return SignalColor;
        }

        /// <summary>
        /// Rótulo corto de un pin de alimentación ("3V3", "5V", "GND"), o <c>null</c> si es una señal.
        /// Son los que da el esquema (J12, J2.1/J2.4, pines 2 y 3 de J7–J10): no se añade ninguno.
        /// </summary>
        /// <summary>
        /// Rótulo corto que se dibuja bajo cada pin: la alimentación ("3V3", "5V", "GND") o la señal
        /// ("GP20", "SDA", "CH0"). Sale de la misma tabla que <see cref="Describe"/> (esquema rev. v1.1).
        /// </summary>
        public static string PinLabel(string pinName)
        {
            string power = PowerLabel(pinName);
            if (power != null) return power;
            string f = PinFunction(pinName);
            if (f.Contains("SDA")) return "SDA";
            if (f.Contains("SCL")) return "SCL";
            if (f.StartsWith("CH")) return f.Substring(0, 3);
            return "GP" + f.Substring(4, f.IndexOf(' ') - 4);   // "GPIO14 (...)" → "GP14"
        }

        public static string PowerLabel(string pinName)
        {
            string f = PinFunction(pinName);
            if (f.StartsWith("GND")) return "GND";
            if (f.StartsWith("5 V")) return "5V";
            if (f.StartsWith("3,3 V")) return "3V3";
            return null;
        }
    }

    /// <summary>Parte de la escena que la cámara tiene que mostrar entera (la HAT, la protoboard...).</summary>
    public interface IWorkspaceArea
    {
        Rect Bounds { get; }
    }
}
