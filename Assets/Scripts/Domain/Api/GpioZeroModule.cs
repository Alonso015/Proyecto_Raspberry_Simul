using ImatPiSim.Domain.Components;
using ImatPiSim.Domain.Interpreter.Runtime;

namespace ImatPiSim.Domain.Api
{
    /// <summary>
    /// Registra en el intérprete el módulo <c>gpiozero</c> simulado y <c>gpiozero.pins.pigpio</c>.
    /// </summary>
    /// <remarks>
    /// Clases disponibles: <c>LED</c>, <c>PWMLED</c>, <c>LEDBoard</c>, <c>OutputDevice</c>,
    /// <c>PWMOutputDevice</c>, <c>Button</c>, <c>InputDevice</c>, <c>Servo</c>,
    /// <c>AngularServo</c> y <c>MCP3008</c> (docs/requisitos.md §2.5). Cualquier otra da
    /// <c>ImportError</c> al importarla.
    /// </remarks>
    public static class GpioZeroModule
    {
        public static void Register(ScriptRuntime runtime, IPinFactory pins, Mcp3008 adc = null)
        {
            PinErrors.EnsureRegistered();
            PyException.RegisterType("PinInvalidState", "GPIOZeroError");
            var ctx = new GpioZeroContext(runtime, pins, adc);

            var gz = new PyModule("gpiozero");
            gz.Add("LED", new PyClass("LED", (a, k) => new GzDigitalOutput(ctx, "LED", a, k)));
            gz.Add("OutputDevice", new PyClass("OutputDevice", (a, k) => new GzDigitalOutput(ctx, "OutputDevice", a, k)));
            gz.Add("DigitalOutputDevice", new PyClass("DigitalOutputDevice", (a, k) => new GzDigitalOutput(ctx, "DigitalOutputDevice", a, k)));
            gz.Add("PWMLED", new PyClass("PWMLED", (a, k) => new GzPwmOutput(ctx, "PWMLED", a, k)));
            gz.Add("PWMOutputDevice", new PyClass("PWMOutputDevice", (a, k) => new GzPwmOutput(ctx, "PWMOutputDevice", a, k)));
            gz.Add("LEDBoard", new PyClass("LEDBoard", (a, k) => new GzLedBoard(ctx, a, k)));
            gz.Add("Button", new PyClass("Button", (a, k) => new GzDigitalInput(ctx, "Button", a, k)));
            gz.Add("InputDevice", new PyClass("InputDevice", (a, k) => new GzDigitalInput(ctx, "InputDevice", a, k)));
            gz.Add("Servo", new PyClass("Servo", (a, k) => new GzServo(ctx, "Servo", a, k)));
            gz.Add("AngularServo", new PyClass("AngularServo", (a, k) => new GzServo(ctx, "AngularServo", a, k)));
            gz.Add("MCP3008", new PyClass("MCP3008", (a, k) => new GzMcp3008(ctx, a, k)));
            runtime.RegisterModule(gz);

            // pin_factory=PiGPIOFactory() se acepta (práctica 4) aunque el simulador no lo necesite:
            // aquí no hay jitter de temporizadores por software.
            var pigpio = new PyModule("gpiozero.pins.pigpio");
            pigpio.Add("PiGPIOFactory", new PyClass("PiGPIOFactory", (a, k) => new PyModule("PiGPIOFactory")));
            runtime.RegisterModule(pigpio);
        }
    }
}
