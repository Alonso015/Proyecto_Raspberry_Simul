using ImatPiSim.Domain.Interpreter;
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
    /// simulación. El campo <see cref="program"/> es el texto inicial del editor
    /// (<see cref="Ui.EditorPanel"/>), que lo sustituye al pulsar Ejecutar.
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
            "# Tema 3: el LED D1 (GPIO20) se enciende mientras\n" +
            "# se pulsa SW1 (GPIO7). SW4 (GPIO19) termina.\n" +
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

        public SimulatorFacade Facade { get; private set; }

        /// <summary>Error de la última carga (léxico o sintáctico), o <c>null</c> si el programa se cargó bien.</summary>
        public ScriptException LoadError { get; private set; }

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
            LoadError = result.Error;
            if (result.Success) Facade.Run();
        }

        private void Update()
        {
            Facade.Tick(Mathf.Min(Time.deltaTime, MaxStep));
        }
    }
}
