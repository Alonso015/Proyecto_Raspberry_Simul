using System.Collections.Generic;
using System.Linq;
using ImatPiSim.Domain.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ImatPiSim.Presentation.Ui
{
    /// <summary>
    /// Panel izquierdo de la pantalla: botones Ejecutar / Parar / Reiniciar, estado, editor de
    /// código y consola. Pasa las órdenes del jugador a <see cref="SimulationRunner"/> y, en
    /// cada frame, copia el estado de la simulación en las vistas.
    /// </summary>
    /// <remarks>
    /// <para>Recorrido de "Ejecutar": texto del editor → <see cref="SimulationRunner.LoadAndRun"/>
    /// → <see cref="SimulatorFacade.LoadProgram"/> (lexer, parser y compilador) → si hay error, se
    /// marca su línea; si no, <see cref="SimulatorFacade.Run"/> y el programa avanza en cada
    /// <c>Tick</c>.</para>
    /// <para>También encuadra la cámara: la placa se dibuja en la parte de la pantalla que el
    /// panel deja libre (ver <see cref="FrameCamera"/>).</para>
    /// <para>Se ejecuta después de <see cref="SimulationRunner"/> en cada frame
    /// (<see cref="DefaultExecutionOrderAttribute"/>): así lo que muestra es el resultado del tick
    /// de ese mismo frame, no del anterior.</para>
    /// </remarks>
    [RequireComponent(typeof(SimulationRunner))]
    [DefaultExecutionOrder(100)]
    public sealed class EditorPanel : MonoBehaviour
    {
        /// <summary>Fracción del ancho de la pantalla que ocupa el panel.</summary>
        public const float WidthFraction = 0.4f;

        private const float ToolbarHeight = 64;
        private const float StatusHeight = 40;
        private const float ConsoleFraction = 0.28f;

        private SimulationRunner _runner;
        private TextMeshProUGUI _status;

        public CodeEditorView Editor { get; private set; }
        public ConsoleView Console { get; private set; }
        public Button RunButton { get; private set; }
        public Button StopButton { get; private set; }
        public Button ResetButton { get; private set; }

        /// <summary>Texto de la línea de estado (para las pruebas).</summary>
        public string StatusText => _status.text;

        private void Awake()
        {
            _runner = GetComponent<SimulationRunner>();
            Build();
        }

        private void Build()
        {
            var canvas = UiFactory.CreateCanvas("Interfaz", transform);
            var panel = UiFactory.Rect(canvas.transform, "Panel del programa", Vector2.zero, new Vector2(WidthFraction, 1));
            UiFactory.Background(panel, UiFactory.PanelColor, raycastTarget: true);

            // Barra de botones
            var bar = UiFactory.Rect(panel, "Botones", new Vector2(0, 1), Vector2.one,
                                     new Vector2(12, -ToolbarHeight), new Vector2(-12, -10));
            RunButton = UiFactory.Button(bar, "Ejecutar", "Ejecutar", new Vector2(0, 0), new Vector2(1 / 3f, 1),
                                         Vector2.zero, new Vector2(-6, 0), new Color(0.18f, 0.55f, 0.30f));
            StopButton = UiFactory.Button(bar, "Parar", "Parar (Ctrl+C)", new Vector2(1 / 3f, 0), new Vector2(2 / 3f, 1),
                                          new Vector2(3, 0), new Vector2(-3, 0), new Color(0.65f, 0.25f, 0.22f));
            ResetButton = UiFactory.Button(bar, "Reiniciar", "Reiniciar", new Vector2(2 / 3f, 0), new Vector2(1, 1),
                                           new Vector2(6, 0), Vector2.zero, new Color(0.30f, 0.33f, 0.40f));
            RunButton.onClick.AddListener(Run);
            StopButton.onClick.AddListener(() => _runner.Facade.Stop());
            ResetButton.onClick.AddListener(() => _runner.Facade.Reset());

            // Estado
            var status = UiFactory.Rect(panel, "Estado", new Vector2(0, 1), Vector2.one,
                                        new Vector2(14, -ToolbarHeight - StatusHeight), new Vector2(-14, -ToolbarHeight - 4));
            _status = UiFactory.Text(status, "", 22, UiFactory.TextColor, TextAlignmentOptions.MidlineLeft);
            _status.richText = true;

            // Editor
            var editorArea = UiFactory.Rect(panel, "Editor", new Vector2(0, ConsoleFraction), Vector2.one,
                                            new Vector2(12, 40), new Vector2(-12, -ToolbarHeight - StatusHeight - 6));
            Editor = CodeEditorView.Create(editorArea, _runner.program);

            // Consola
            var consoleTitle = UiFactory.Rect(panel, "Título de la consola", new Vector2(0, ConsoleFraction), new Vector2(1, ConsoleFraction),
                                              new Vector2(14, 4), new Vector2(-14, 36));
            UiFactory.Text(consoleTitle, "Salida", 20, UiFactory.DimTextColor, TextAlignmentOptions.BottomLeft);
            var consoleArea = UiFactory.Rect(panel, "Consola", Vector2.zero, new Vector2(1, ConsoleFraction),
                                             new Vector2(12, 12), new Vector2(-12, 0));
            Console = ConsoleView.Create(consoleArea);
        }

        /// <summary>Ejecuta el texto del editor (lo que hace el botón Ejecutar).</summary>
        public void Run()
        {
            _runner.program = Editor.Text;
            _runner.LoadAndRun();
            Editor.ErrorLine = _runner.LoadError?.Line;
        }

        private void Update()
        {
            var facade = _runner.Facade;

            // Ctrl+C con el editor sin foco, como en el terminal de la Raspberry
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (ctrl && Input.GetKeyDown(KeyCode.C) && !Editor.Input.isFocused) facade.Stop();

            var error = _runner.LoadError ?? (facade.State == SimulationState.ProgramError ? facade.ProgramError : null);
            if (error != null && Editor.ErrorLine == null && !_errorShown) Editor.ErrorLine = error.Line;
            _errorShown = error != null;
            Editor.ExecutingLine = facade.State == SimulationState.Running && facade.CurrentLine > 0 ? facade.CurrentLine : (int?)null;

            _status.text = $"<b>{StateName(facade.State)}</b>   ·   t = {facade.Time:0.0} s" +
                           (facade.State == SimulationState.Running ? $"   ·   línea {facade.CurrentLine}" : "");

            _errors.Clear();
            if (error != null) _errors.Add(error.Message);
            _warnings.Clear();
            _warnings.AddRange(facade.Faults.Select(f => f.Message));
            Console.Show(facade.Output, _errors, _warnings);
        }

        // La marca de error se pone una vez por error: si el jugador edita, desaparece y no vuelve.
        private bool _errorShown;
        private readonly List<string> _errors = new List<string>();
        private readonly List<string> _warnings = new List<string>();

        private void LateUpdate() => FrameCamera(Camera.main, GetComponent<BoardView>());

        /// <summary>
        /// Coloca la cámara para que la placa quepa entera en la zona derecha de la pantalla (la que
        /// no tapa el panel), sea cual sea la relación de aspecto de la ventana.
        /// </summary>
        public static void FrameCamera(Camera camera, BoardView board, float margin = 0.6f)
        {
            if (camera == null || board == null || !camera.orthographic) return;
            var b = board.Bounds;
            float aspect = Mathf.Max(0.1f, camera.aspect);
            float free = 1 - WidthFraction;
            float size = Mathf.Max((b.height + 2 * margin) / 2, (b.width + 2 * margin) / (2 * aspect * free));
            camera.orthographicSize = size;
            float visibleWidth = 2 * size * aspect;
            // El centro de la zona libre está a WidthFraction/2 del centro de la pantalla.
            var p = camera.transform.position;
            camera.transform.position = new Vector3(b.center.x - WidthFraction / 2 * visibleWidth, b.center.y, p.z);
        }

        private static string StateName(SimulationState state) => state switch
        {
            SimulationState.Idle => "Listo",
            SimulationState.Running => "<color=#7ddc8a>En ejecución</color>",
            SimulationState.Finished => "Programa terminado",
            SimulationState.Stopped => "Detenido (KeyboardInterrupt)",
            SimulationState.ProgramError => "<color=#ff6b6b>Error en el programa</color>",
            SimulationState.Faulted => "<color=#ffb347>Fallo eléctrico</color>",
            _ => state.ToString()
        };
    }
}
