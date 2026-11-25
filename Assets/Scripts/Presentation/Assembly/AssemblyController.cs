using System.Collections.Generic;
using System.Linq;
using ImatPiSim.Domain.Components;
using ImatPiSim.Domain.Simulation;
using ImatPiSim.Domain.Wiring;
using ImatPiSim.Domain.Wiring.Routing;
using ImatPiSim.Presentation.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using DomainComponent = ImatPiSim.Domain.Components.Component;
using DomainPin = ImatPiSim.Domain.Components.Pin;

namespace ImatPiSim.Presentation.Assembly
{
    /// <summary>
    /// Montaje con el ratón: arrastrar piezas de la paleta a la protoboard, moverlas, girarlas,
    /// borrarlas y tender cables. Traduce el ratón a operaciones del dominio
    /// (<see cref="Circuit.Place"/>, <see cref="Circuit.Move"/>, <see cref="Circuit.RouteWire"/>...)
    /// y mantiene una vista por cada componente y cable.
    /// </summary>
    /// <remarks>
    /// <para><b>Controles</b>:</para>
    /// <list type="bullet">
    /// <item>arrastrar una pieza de la paleta → colocarla; <b>R</b> la gira mientras se arrastra;</item>
    /// <item>arrastrar un componente → moverlo;</item>
    /// <item>clic en un agujero o en un pin de la HAT, y clic en el otro extremo → cable;</item>
    /// <item>clic derecho → borrar; <b>Esc</b> → cancelar.</item>
    /// </list>
    /// <para>Con el programa en marcha, el montaje queda bloqueado (decidido el 24/11/2026). Toda la
    /// validación la hace el dominio: aquí solo se muestran sus mensajes. Los métodos
    /// <c>Try…</c> son los mismos que usa el ratón; las pruebas los llaman directamente.</para>
    /// </remarks>
    [RequireComponent(typeof(SimulationRunner), typeof(BoardView), typeof(BreadboardView))]
    [RequireComponent(typeof(PaletteView))]
    [DefaultExecutionOrder(50)]
    public sealed class AssemblyController : MonoBehaviour
    {
        private enum Mode { Idle, PlacingNew, Moving, Wiring }

        private const float MessageSeconds = 4f;
        private static readonly Color Valid = new Color(0.25f, 0.85f, 0.35f);
        private static readonly Color Invalid = new Color(0.95f, 0.25f, 0.20f);

        private readonly ComponentFactory _factory = ComponentFactory.CreateDefault();
        private readonly Dictionary<DomainComponent, PlacedComponentView> _components = new Dictionary<DomainComponent, PlacedComponentView>();
        private readonly Dictionary<Wire, WireView> _wires = new Dictionary<Wire, WireView>();
        private readonly Dictionary<string, int> _counters = new Dictionary<string, int>();

        private SimulatorFacade _facade;
        private BoardView _board;
        private BreadboardView _breadboard;
        private PaletteView _palette;
        private Transform _root;
        private TextMeshProUGUI _hint;
        private float _messageUntil;
        private int _wireIndex;

        // Estado de la interacción en curso
        private Mode _mode = Mode.Idle;
        private PaletteItem _dragItem;
        private PlacedComponentView _dragView;
        private Direction _dragDirection;
        private (int dr, int dc) _grab;
        private Transform _ghost;
        private (HoleId? start, Direction dir) _ghostKey;
        private DomainPin _wireFrom;
        private LineRenderer _rubber;

        /// <summary>Algoritmo de trazado (Strategy): el A* por defecto.</summary>
        public IRouter Router { get; set; } = new AStarRouter();

        public Circuit Circuit => _facade.Circuit;

        /// <summary>El montaje no se puede cambiar con el programa en marcha.</summary>
        public bool IsLocked => _facade.IsRunning;

        /// <summary>Último mensaje mostrado (un error de montaje, normalmente).</summary>
        public string LastMessage { get; private set; }

        public IReadOnlyCollection<PlacedComponentView> ComponentViews => _components.Values;
        public IReadOnlyCollection<WireView> WireViews => _wires.Values;
        public BreadboardView Breadboard => _breadboard;
        public BoardView Board => _board;

        private void Start()
        {
            _facade = GetComponent<SimulationRunner>().Facade;
            _board = GetComponent<BoardView>();
            _breadboard = GetComponent<BreadboardView>();
            _palette = GetComponent<PaletteView>();
            _root = new GameObject("Montaje").transform;
            _root.SetParent(transform, false);
            CreateHint();
        }

        private void CreateHint()
        {
            var canvas = GetComponentInChildren<Canvas>();
            if (canvas == null) return;
            var rt = UiFactory.Rect(canvas.transform, "Ayuda del montaje", new Vector2(EditorPanel.WidthFraction, 0), new Vector2(1, 0),
                                    new Vector2(16, 6), new Vector2(-16, 46));
            _hint = UiFactory.Text(rt, "", 20, UiFactory.TextColor, TextAlignmentOptions.BottomLeft);
            _hint.richText = true;
        }

        // ================================================================== operaciones (ratón y pruebas)

        /// <summary>Crea una pieza de la paleta y la pincha en la protoboard.</summary>
        public bool TryPlace(PaletteItem item, HoleId start, Direction direction)
        {
            if (Locked()) return false;
            if (!Circuit.CanPlace(item.Type, start, direction, null, out var reason)) return Fail(reason);
            var component = _factory.Create(item.Type.Id, NextId(item.Type), item.Parameters);
            return Do(() => Show(Circuit.Place(component, start, direction)), $"{component} colocado.");
        }

        /// <summary>Mueve o gira un componente ya pinchado.</summary>
        public bool TryMove(PlacedComponentView view, HoleId start, Direction direction)
        {
            if (Locked()) return false;
            var component = view.Component;
            return Do(() =>
            {
                var placement = Circuit.Move(component, start, direction);
                Destroy(view.gameObject);
                _components.Remove(component);
                Show(placement);
            }, $"{component.Id} movido.");
        }

        public bool Delete(PlacedComponentView view)
        {
            if (Locked() || view == null) return false;
            var component = view.Component;
            Circuit.Remove(component);
            _components.Remove(component);
            Destroy(view.gameObject);
            return Message($"{component.Id} retirado.");
        }

        public bool Delete(WireView view)
        {
            if (Locked() || view == null) return false;
            Circuit.RemoveWire(view.Wire);
            _wires.Remove(view.Wire);
            Destroy(view.gameObject);
            return Message($"Cable {view.Wire.Id} retirado.");
        }

        /// <summary>
        /// Tiende un cable. Entre dos agujeros, con el recorrido de <see cref="Router"/> (A*);
        /// si un extremo es un pin de la HAT, es un cable Dupont al aire (línea recta).
        /// </summary>
        public bool TryWire(DomainPin a, DomainPin b)
        {
            if (Locked()) return false;
            var ha = Circuit.Breadboard.HoleOf(a);
            var hb = Circuit.Breadboard.HoleOf(b);
            return Do(() =>
            {
                var wire = ha.HasValue && hb.HasValue
                    ? Circuit.RouteWire(ha.Value.Name, hb.Value.Name, Router)
                    : Circuit.AddWire(a, b);
                Show(wire);
            }, "Cable tendido.");
        }

        /// <summary>Posición en el mundo de un extremo de cable (agujero o pin de la HAT).</summary>
        public Vector3 EndpointPosition(DomainPin pin)
        {
            var hole = Circuit.Breadboard.HoleOf(pin);
            if (hole.HasValue) return _breadboard.HoleToWorld(hole.Value);
            return _board.ConnectorPins.TryGetValue(pin, out var p) ? (Vector3)p : Vector3.zero;
        }

        /// <summary>El extremo de cable bajo un punto: un agujero o un pin de conector de la HAT.</summary>
        public DomainPin EndpointAt(Vector2 world)
        {
            var hole = _breadboard.WorldToHole(world);
            if (hole.HasValue) return Circuit.Breadboard.Hole(hole.Value);
            return _board.ConnectorAt(world);
        }

        private void Show(Placement placement)
        {
            var view = PlacedComponentView.Create(_root, placement, _breadboard, _facade);
            _components[placement.Component] = view;
        }

        private void Show(Wire wire)
        {
            var points = wire.Route.Count > 0
                ? wire.Route.Select(h => (Vector3)_breadboard.HoleToWorld(h)).ToList()
                : new List<Vector3> { EndpointPosition(wire.A), EndpointPosition(wire.B) };
            _wires[wire] = WireView.Create(_root, wire, points, _wireIndex++);
        }

        private string NextId(ComponentType type)
        {
            string prefix = type.Id switch
            {
                "resistor" => "R", "led" => "LED", "pushbutton" => "SW", "potentiometer" => "RV", "ldr" => "LDR", _ => "X"
            };
            // SW1–SW4 y RV1 ya existen en la HAT: los externos siguen la numeración.
            int n = _counters.TryGetValue(prefix, out var c) ? c : prefix == "SW" ? 5 : prefix == "RV" ? 2 : 1;
            while (Circuit.Components.Any(x => x.Id == prefix + n)) n++;
            _counters[prefix] = n + 1;
            return prefix + n;
        }

        private bool Locked() => IsLocked && !Fail("Montaje bloqueado: pulsa Parar para cambiarlo.");

        private bool Do(System.Action action, string ok)
        {
            try
            {
                action();
                return Message(ok);
            }
            catch (AssemblyException ex)
            {
                return Fail(ex.Message);
            }
        }

        private bool Fail(string message)
        {
            LastMessage = message;
            _messageUntil = Time.time + MessageSeconds;
            if (_hint != null) _hint.text = $"<color=#ff7b6b>{message}</color>";
            return false;
        }

        private bool Message(string message)
        {
            LastMessage = message;
            _messageUntil = Time.time + 1.5f;
            if (_hint != null) _hint.text = message;
            return true;
        }

        // ================================================================== ratón y teclado

        private void Update()
        {
            if (_facade == null) return;
            var cam = Camera.main;
            if (cam == null) return;
            Vector2 mouse = cam.ScreenToWorldPoint(Input.mousePosition);
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

            if (IsLocked)
            {
                if (_mode != Mode.Idle) Cancel();
                Hint("Montaje bloqueado mientras se ejecuta el programa (pulsa Parar para cambiarlo).");
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape)) Cancel();

            switch (_mode)
            {
                case Mode.PlacingNew:
                case Mode.Moving:
                    UpdateDrag(mouse);
                    return;
                case Mode.Wiring:
                    UpdateWiring(mouse, overUi);
                    return;
            }

            // Reposo
            if (overUi) return;
            var hitComponent = Pick<PlacedComponentView>(mouse);
            var hitWire = hitComponent == null ? Pick<WireView>(mouse) : null;
            var item = _palette.ItemAt(mouse);

            if (Input.GetMouseButtonDown(1))
            {
                if (hitComponent != null) Delete(hitComponent);
                else if (hitWire != null) Delete(hitWire);
                return;
            }
            if (Input.GetMouseButtonDown(0))
            {
                if (item != null) BeginDrag(item, null, Direction.Right, mouse);
                else if (hitComponent != null) BeginDrag(null, hitComponent, hitComponent.Placement.Direction, mouse);
                else
                {
                    var end = EndpointAt(mouse);
                    if (end != null) BeginWiring(end);
                }
                return;
            }

            // Ayuda según lo que hay bajo el ratón
            if (item != null) Hint($"Arrastra para colocar: <b>{item.Label}</b> (R gira)");
            else if (hitComponent != null) Hint($"<b>{hitComponent.Component}</b> · arrastra para mover · clic derecho para quitar");
            else if (hitWire != null) Hint($"Cable {hitWire.Wire.A.FullName} — {hitWire.Wire.B.FullName} · clic derecho para quitar");
            else Hint(DescribePoint(mouse));
        }

        private string DescribePoint(Vector2 mouse)
        {
            var hole = _breadboard.WorldToHole(mouse);
            if (hole.HasValue)
            {
                var occupant = Circuit.Breadboard.Occupant(hole.Value);
                string strip = hole.Value.IsRail ? $"raíl {hole.Value.Name.Substring(0, 2)}" :
                    hole.Value.Row <= BreadboardRow.E ? $"tira a–e de la columna {hole.Value.Column}" : $"tira f–j de la columna {hole.Value.Column}";
                return $"<b>{hole.Value}</b> · {strip} · " +
                       (occupant == null ? "libre: clic para empezar un cable" : "ocupado por " + Domain.Wiring.Breadboard.Describe(occupant));
            }
            var pin = _board.ConnectorAt(mouse);
            if (pin != null) return $"<b>{_board.Describe(pin)}</b> · clic para empezar un cable";
            return "";
        }

        private void Hint(string text)
        {
            if (_hint == null || Time.time < _messageUntil) return;
            if (_hint.text != text) _hint.text = text;
        }

        private T Pick<T>(Vector2 world) where T : MonoBehaviour
        {
            foreach (var col in Physics2D.OverlapPointAll(world))
            {
                var t = col.GetComponent<T>();
                if (t != null) return t;
            }
            return null;
        }

        // ------------------------------------------------------------------ arrastrar (colocar o mover)

        private void BeginDrag(PaletteItem item, PlacedComponentView view, Direction direction, Vector2 mouse)
        {
            _mode = item != null ? Mode.PlacingNew : Mode.Moving;
            _dragItem = item;
            _dragView = view;
            _dragDirection = direction;
            _grab = (0, 0);
            if (view != null)
            {
                // Se agarra por el agujero bajo el ratón: el componente se mueve con ese desfase
                var under = _breadboard.WorldToHole(mouse) ?? view.Placement.Start;
                _grab = (under.GridRow - view.Placement.Start.GridRow, under.Column - view.Placement.Start.Column);
            }
            _ghostKey = (null, direction);
        }

        private void UpdateDrag(Vector2 mouse)
        {
            if (Input.GetKeyDown(KeyCode.R))
            {
                _dragDirection = (Direction)(((int)_dragDirection + 1) % 4);
                _grab = (0, 0);
            }
            if (Input.GetMouseButtonDown(1)) { Cancel(); return; }

            var type = _dragItem?.Type ?? _dragView.Component.Type;
            var under = _breadboard.WorldToHole(mouse);
            HoleId? start = null;
            if (under.HasValue)
            {
                int row = under.Value.GridRow - _grab.dr, col = under.Value.Column - _grab.dc;
                if (row >= 0 && row < Domain.Wiring.Breadboard.RowCount) start = new HoleId((BreadboardRow)row, col);
            }

            bool valid = false;
            string reason = "Suelta la pieza sobre la protoboard.";
            if (start.HasValue)
                valid = Circuit.CanPlace(type, start.Value, _dragDirection, _dragView?.Component, out reason);
            UpdateGhost(type, start, mouse, valid);
            Hint(valid ? $"Suelta para colocar en <b>{start}</b> (R gira, clic derecho cancela)" : $"<color=#ff7b6b>{reason}</color>");

            if (Input.GetMouseButtonUp(0))
            {
                var mode = _mode;
                var item = _dragItem;
                var view = _dragView;
                var dir = _dragDirection;
                ClearDrag();
                if (!start.HasValue) { if (mode == Mode.PlacingNew) Fail("Suelta la pieza sobre la protoboard."); return; }
                if (mode == Mode.PlacingNew) TryPlace(item, start.Value, dir);
                else if (start.Value != view.Placement.Start || dir != view.Placement.Direction) TryMove(view, start.Value, dir);
            }
        }

        /// <summary>Vista previa semitransparente y agujeros resaltados en verde (cabe) o rojo (no cabe).</summary>
        private void UpdateGhost(ComponentType type, HoleId? start, Vector2 mouse, bool valid)
        {
            if (_ghost == null || _ghostKey.start != start || _ghostKey.dir != _dragDirection)
            {
                if (_ghost != null) Destroy(_ghost.gameObject);
                _ghost = new GameObject("Vista previa").transform;
                _ghost.SetParent(_root, false);
                _ghost.rotation = Quaternion.Euler(0, 0, ComponentArt.Angle(_dragDirection));
                ComponentArt.Draw(_ghost, type, ComponentArt.Span(type), 20, 0.6f);
                _ghostKey = (start, _dragDirection);
                if (start.HasValue)
                {
                    var holes = Placement.HolesFor(type, start.Value, _dragDirection);
                    _breadboard.Highlight(holes.Where(h => Circuit.Breadboard.Contains(h)), valid ? Valid : Invalid);
                }
                else _breadboard.Highlight(null, null);
            }

            if (start.HasValue)
            {
                var holes = Placement.HolesFor(type, start.Value, _dragDirection);
                var first = _breadboard.HoleToWorld(holes[0]);
                var dir = ComponentArt.Angle(_dragDirection) * Mathf.Deg2Rad;
                var axis = new Vector2(Mathf.Cos(dir), Mathf.Sin(dir));
                _ghost.position = first + axis * ComponentArt.Span(type) / 2;
            }
            else _ghost.position = mouse;
        }

        private void ClearDrag()
        {
            _mode = Mode.Idle;
            _dragItem = null;
            _dragView = null;
            if (_ghost != null) Destroy(_ghost.gameObject);
            _ghost = null;
            _breadboard.Highlight(null, null);
        }

        // ------------------------------------------------------------------ cables

        private void BeginWiring(DomainPin from)
        {
            _mode = Mode.Wiring;
            _wireFrom = from;
            var go = new GameObject("Cable en curso");
            go.transform.SetParent(_root, false);
            _rubber = go.AddComponent<LineRenderer>();
            _rubber.positionCount = 2;
            _rubber.widthMultiplier = WireView.Width * 0.8f;
            _rubber.sharedMaterial = WireView.LineMaterial;
            _rubber.startColor = _rubber.endColor = new Color(1f, 1f, 1f, 0.7f);
            _rubber.sortingOrder = 30;
        }

        private void UpdateWiring(Vector2 mouse, bool overUi)
        {
            _rubber.SetPosition(0, EndpointPosition(_wireFrom));
            _rubber.SetPosition(1, (Vector3)mouse);
            var target = EndpointAt(mouse);
            string name = Circuit.Breadboard.HoleOf(_wireFrom)?.Name ?? _wireFrom.Name;
            Hint($"Cable desde <b>{name}</b>: clic en el otro extremo (Esc o clic derecho cancelan)" +
                 (target != null && target != _wireFrom ? $" → {Circuit.Breadboard.HoleOf(target)?.Name ?? _board.Describe(target)}" : ""));

            if (Input.GetMouseButtonDown(1)) { Cancel(); return; }
            if (Input.GetMouseButtonDown(0) && !overUi && target != null && target != _wireFrom)
            {
                var from = _wireFrom;
                ClearWiring();
                TryWire(from, target);
            }
        }

        private void ClearWiring()
        {
            _mode = Mode.Idle;
            _wireFrom = null;
            if (_rubber != null) Destroy(_rubber.gameObject);
            _rubber = null;
        }

        private void Cancel()
        {
            ClearDrag();
            ClearWiring();
        }
    }
}
