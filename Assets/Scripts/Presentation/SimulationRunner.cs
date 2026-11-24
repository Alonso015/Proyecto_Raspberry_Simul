using System.Linq;
using ImatPiSim.Domain.Simulation;
using UnityEngine;

namespace ImatPiSim.Presentation
{
    /// <summary>
    /// Puente entre Unity y el dominio: crea el simulador (a través de su fachada) y lo hace
    /// avanzar en cada frame con el tiempo real transcurrido.
    /// </summary>
    /// <remarks>
    /// Es el único MonoBehaviour que conoce <see cref="SimulatorFacade"/> como "dueño". Las
    /// vistas solo leen el modelo y le pasan las acciones del ratón: no contienen lógica de
    /// simulación. Etapa E14: el programa es fijo (se edita en el Inspector); el editor de código
    /// dentro del juego llega en E15.
    /// </remarks>
    public sealed class SimulationRunner : MonoBehaviour
    {
        /// <summary>Paso máximo por frame: si un frame tarda mucho, no se salta medio programa.</summary>
        private const float MaxStep = 0.1f;

        [TextArea(10, 40)]
        [Tooltip("Programa en Python (subconjunto) que se ejecuta al darle a Play.")]
        public string program =
            "from gpiozero import LED, Button\n" +
            "\n" +
            "# Tema 3: el LED D1 (GPIO20) se enciende mientras se pulsa SW1 (GPIO7).\n" +
            "# SW4 (GPIO19) termina el programa.\n" +
            "def main() -> None:\n" +
            "    puls_led = Button(7)\n" +
            "    puls_fin = Button(19)\n" +
            "    led_0 = LED(20)\n" +
            "    while True:\n" +
            "        if puls_led.is_pressed:\n" +
            "            led_0.on()\n" +
            "        else:\n" +
            "            led_0.off()\n" +
            "        if puls_fin.is_pressed:\n" +
            "            print('Fin del programa')\n" +
            "            break\n" +
            "\n" +
            "if __name__ == \"__main__\":\n" +
            "    try:\n" +
            "        main()\n" +
            "    except KeyboardInterrupt:\n" +
            "        print('Programa detenido')\n";

        [Tooltip("Ejecutar el programa automáticamente al darle a Play.")]
        public bool runOnStart = true;

        [Tooltip("Mostrar el panel de estado (provisional, hasta la UI de E15).")]
        public bool showStatusPanel = true;

        public SimulatorFacade Facade { get; private set; }

        private string _loadError;

        private void Awake()
        {
            Facade = SimulatorFacade.WithImatHat();
        }

        private void Start()
        {
            if (runOnStart) LoadAndRun();
        }

        public void LoadAndRun()
        {
            var result = Facade.LoadProgram(program);
            _loadError = result.Success ? null : result.Error.Message;
            if (result.Success) Facade.Run();
        }

        private void Update()
        {
            Facade.Tick(Mathf.Min(Time.deltaTime, MaxStep));
        }

        // ------------------------------------------------------------------ panel provisional

        private void OnGUI()
        {
            if (!showStatusPanel || Facade == null) return;
            GUILayout.BeginArea(new Rect(10, 10, 430, Screen.height - 20), GUI.skin.box);
            GUILayout.Label($"<b>Estado:</b> {Facade.State}   ·   t = {Facade.Time:0.0} s   ·   línea {Facade.CurrentLine}",
                            Rich());
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Ejecutar")) LoadAndRun();
            if (GUILayout.Button("Parar (Ctrl+C)")) Facade.Stop();
            if (GUILayout.Button("Reiniciar")) Facade.Reset();
            GUILayout.EndHorizontal();
            GUILayout.Label("Pulsa SW1–SW4 con el ratón (mantén el clic para mantenerlo pulsado).");

            if (_loadError != null) GUILayout.Label($"<color=#ff6060>{_loadError}</color>", Rich());
            if (Facade.State == SimulationState.ProgramError)
                GUILayout.Label($"<color=#ff6060>{Facade.ProgramError.Message}</color>", Rich());
            foreach (var f in Facade.Faults) GUILayout.Label($"<color=#ffb040>⚠ {f.Message}</color>", Rich());

            GUILayout.Label("<b>Salida (print):</b>", Rich());
            foreach (var line in Facade.Output.Skip(System.Math.Max(0, Facade.Output.Count - 8)))
                GUILayout.Label(line);
            GUILayout.EndArea();
        }

        private static GUIStyle Rich() => new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
    }
}
