using System.Collections.Generic;
using ImatPiSim.Domain.Interpreter.Runtime;

namespace ImatPiSim.Domain.Api
{
    /// <summary>
    /// <c>InputDevice</c> y <c>Button</c> de gpiozero: entrada digital, con callbacks en Button.
    /// </summary>
    /// <remarks>
    /// <para><b>Activo a nivel bajo o alto.</b> Con <c>pull_up=True</c> (por defecto en Button) se
    /// activa la pull-up interna: en reposo el pin lee HIGH y el pulsador, que une el pin a GND,
    /// lo pone a LOW al pulsarlo. Por eso "pulsado" = LOW. Con <c>pull_up=False</c> se usa la
    /// pull-down y "pulsado" = HIGH. Con <c>pull_up=None</c> no hay resistencia y hay que decir
    /// con <c>active_state</c> qué nivel significa "activo".</para>
    /// <para><b>Callbacks.</b> Al detectar un flanco (tras resolver el circuito), el dispositivo
    /// encola <c>when_pressed</c> o <c>when_released</c> en el intérprete; se ejecutan como
    /// tareas aparte, intercaladas con el programa principal. Igual que en gpiozero, la función
    /// recibe el dispositivo si tiene un parámetro obligatorio, y nada si no tiene ninguno.</para>
    /// <para>Aproximación: no se simulan los rebotes del pulsador (y <c>bounce_time</c> se ignora).</para>
    /// </remarks>
    internal sealed class GzDigitalInput : GzDevice
    {
        private readonly IPin _pin;
        private readonly bool _activeWhenHigh;
        private IPyCallable _whenActivated;
        private IPyCallable _whenDeactivated;

        public GzDigitalInput(GpioZeroContext ctx, string typeName, object[] args, IReadOnlyDictionary<string, object> kw)
            : base(ctx, typeName)
        {
            bool isButton = typeName == "Button";
            var p = isButton
                ? Params.Bind(typeName, args, kw, ("pin", Params.Required), ("pull_up", true), ("active_state", null),
                              ("bounce_time", null), ("hold_time", 1L), ("hold_repeat", false))
                : Params.Bind(typeName, args, kw, ("pin", Params.Required), ("pull_up", false), ("active_state", null));

            var pullUp = p["pull_up"];
            PinPull pull;
            if (pullUp == null)
            {
                if (p["active_state"] == null)
                    throw new PyException("PinInvalidState", "con pull_up=None hay que indicar active_state=True o False");
                pull = PinPull.None;
                _activeWhenHigh = PyOps.IsTruthy(p["active_state"]);
            }
            else
            {
                if (p["active_state"] != null)
                    throw new PyException("PinInvalidState", "active_state solo se puede indicar con pull_up=None");
                bool up = PyOps.IsTruthy(pullUp);
                pull = up ? PinPull.Up : PinPull.Down;
                _activeWhenHigh = !up;   // pull-up → activo a nivel bajo
            }

            int bcm = PinNumbers.Parse(p["pin"]);
            _pin = ctx.Pins.Reserve(bcm, $"{typeName}({bcm})");
            _pin.SetPull(pull);
            _pin.SetFunction(PinFunction.Input);
            _pin.Edge += OnEdge;

            Property("value", () => IsActive ? 1L : 0L);
            Property("is_active", () => IsActive);
            Property("pull_up", () => pullUp);
            Property("pin", () => "GPIO" + _pin.Number);
            if (isButton)
            {
                Property("is_pressed", () => IsActive);
                Property("when_pressed", () => _whenActivated, v => _whenActivated = AsCallback(v, "when_pressed"));
                Property("when_released", () => _whenDeactivated, v => _whenDeactivated = AsCallback(v, "when_released"));
                Property("when_activated", () => _whenActivated, v => _whenActivated = AsCallback(v, "when_activated"));
                Property("when_deactivated", () => _whenDeactivated, v => _whenDeactivated = AsCallback(v, "when_deactivated"));
                Coroutine("wait_for_press", (rt, a, k, r) => WaitFor(true, rt, a, k, r));
                Coroutine("wait_for_release", (rt, a, k, r) => WaitFor(false, rt, a, k, r));
                Coroutine("wait_for_active", (rt, a, k, r) => WaitFor(true, rt, a, k, r));
                Coroutine("wait_for_inactive", (rt, a, k, r) => WaitFor(false, rt, a, k, r));
            }
        }

        /// <summary>Lectura del pin (de la última resolución del circuito) traducida a "activo".</summary>
        public bool IsActive => _pin.Read() == _activeWhenHigh;

        private IPyCallable AsCallback(object v, string name)
        {
            if (v == null) return null;
            if (v is IPyCallable c) return c;
            throw PyException.TypeError(
                $"{name} necesita una función, no {PyOps.TypeName(v)} (¿has puesto paréntesis? se asigna sin llamarla: {name} = funcion)");
        }

        private void OnEdge(bool level)
        {
            bool active = level == _activeWhenHigh;
            var callback = active ? _whenActivated : _whenDeactivated;
            if (callback == null) return;
            bool passDevice = callback is PyFunction f && f.RequiredParameterCount >= 1;
            Ctx.Runtime.EnqueueCall(callback, passDevice ? new object[] { this } : new object[0]);
        }

        /// <summary>
        /// <c>wait_for_press(timeout=None)</c>: espera (sin bloquear Unity) a que el pulsador esté
        /// pulsado. Devuelve <c>True</c> si ocurrió y <c>False</c> si se agotó el tiempo.
        /// </summary>
        private IEnumerable<Signal> WaitFor(bool active, ScriptRuntime rt, object[] args, IReadOnlyDictionary<string, object> kw, Ref result)
        {
            var p = Params.Bind("wait_for_press", args, kw, ("timeout", null));
            double? timeout = p["timeout"] == null ? (double?)null : p.Double("timeout");
            double start = rt.Time;
            while (IsActive != active)
            {
                if (timeout.HasValue && rt.Time - start >= timeout.Value)
                {
                    result.Value = false;
                    yield break;
                }
                yield return Signal.Sleep(0);   // se vuelve a mirar en el siguiente tick
                rt.CheckInterrupt();
            }
            result.Value = true;
        }

        protected override void OnClose()
        {
            _pin.Edge -= OnEdge;
            Ctx.Pins.Release(_pin);
        }

        public override string ToString() =>
            $"<gpiozero.{TypeName} object on pin GPIO{_pin.Number}, is_active={(IsActive ? "True" : "False")}>";
    }
}
