using System.Collections.Generic;
using System.Linq;
using ImatPiSim.Domain.Interpreter.Runtime;

namespace ImatPiSim.Domain.Api
{
    /// <summary>
    /// <c>OutputDevice</c> y <c>LED</c> de gpiozero: salida digital.
    /// </summary>
    /// <remarks>
    /// Código → pin: <c>led.on()</c> escribe HIGH (3,3 V) en el GPIO si <c>active_high</c> es
    /// <c>True</c>. Pin → circuito: el modelo eléctrico calcula la corriente por la resistencia y
    /// el LED en la siguiente resolución. Lo que se observa (el LED encendido) sale de esa corriente.
    /// </remarks>
    internal class GzDigitalOutput : GzDevice
    {
        private readonly IPin _pin;
        private readonly bool _activeHigh;
        private bool _active;
        private ScriptRuntime.BackgroundTask _blink;

        public GzDigitalOutput(GpioZeroContext ctx, string typeName, object[] args, IReadOnlyDictionary<string, object> kw)
            : base(ctx, typeName)
        {
            var p = Params.Bind(typeName, args, kw,
                ("pin", Params.Required), ("active_high", true), ("initial_value", false));
            _activeHigh = p.Bool("active_high");
            int bcm = PinNumbers.Parse(p["pin"]);
            _pin = ctx.Pins.Reserve(bcm, $"{typeName}({bcm})");
            _pin.SetFunction(PinFunction.Output);
            Set(p.Bool("initial_value"));

            Method("on", a => { StopBlink(); Set(true); return null; });
            Method("off", a => { StopBlink(); Set(false); return null; });
            Method("toggle", a => { StopBlink(); Set(!_active); return null; });
            Property("value", () => _active ? 1L : 0L, v => { StopBlink(); Set(PyOps.IsTruthy(v)); });
            Property("is_active", () => _active);
            Property("active_high", () => _activeHigh);
            Property("pin", () => "GPIO" + _pin.Number);
            if (typeName == "LED")
            {
                Property("is_lit", () => _active);
                Coroutine("blink", Blink);
            }
        }

        public int PinNumber => _pin.Number;
        public bool IsActive => _active;

        /// <summary>Cambia el estado lógico; el nivel del pin depende de <c>active_high</c>.</summary>
        public void Set(bool active)
        {
            _active = active;
            _pin.Write(active == _activeHigh);
        }

        public void StopBlink()
        {
            _blink?.Cancel();
            _blink = null;
        }

        /// <summary>
        /// <c>blink(on_time=1, off_time=1, n=None, background=True)</c>. Por defecto 1 s encendido y
        /// 1 s apagado (0,5 Hz, como dice el Tema 3) en una tarea de fondo; con
        /// <c>background=False</c> el programa espera a que termine.
        /// </summary>
        private IEnumerable<Signal> Blink(ScriptRuntime rt, object[] args, IReadOnlyDictionary<string, object> kw, Ref result)
        {
            var p = Params.Bind("blink", args, kw, ("on_time", 1L), ("off_time", 1L), ("n", null), ("background", true));
            double on = p.Double("on_time"), off = p.Double("off_time");
            if (on < 0 || off < 0) throw PyException.ValueError("los tiempos de blink() no pueden ser negativos");
            long? n = p["n"] == null ? (long?)null : PyOps.ToLong(p["n"], "n");
            StopBlink();
            var steps = BlinkLoop(rt, on, off, n);
            if (p.Bool("background")) _blink = rt.StartBackground($"{TypeName}.blink", steps);
            else foreach (var s in steps) yield return s;
            result.Value = null;
        }

        private IEnumerable<Signal> BlinkLoop(ScriptRuntime rt, double on, double off, long? n)
        {
            for (long i = 0; n == null || i < n; i++)
            {
                Set(true);
                yield return Signal.Sleep(on);
                Set(false);
                yield return Signal.Sleep(off);
            }
        }

        protected override void OnClose()
        {
            StopBlink();
            Ctx.Pins.Release(_pin);
        }

        public override string ToString() =>
            $"<gpiozero.{TypeName} object on pin GPIO{_pin.Number}, active_high={(_activeHigh ? "True" : "False")}, is_active={(_active ? "True" : "False")}>";
    }

    /// <summary>
    /// <c>PWMOutputDevice</c> y <c>PWMLED</c>: <c>value</c> entre 0 y 1 es el factor de servicio.
    /// </summary>
    /// <remarks>
    /// Un LED con PWM a 100 Hz (frecuencia por defecto de gpiozero) parpadea demasiado rápido
    /// para el ojo, que percibe el brillo medio: <c>value = 0.25</c> → un cuarto del brillo.
    /// </remarks>
    internal sealed class GzPwmOutput : GzDevice
    {
        private readonly IPin _pin;
        private readonly bool _activeHigh;
        private double _value;
        private double _frequency;

        public GzPwmOutput(GpioZeroContext ctx, string typeName, object[] args, IReadOnlyDictionary<string, object> kw)
            : base(ctx, typeName)
        {
            var p = Params.Bind(typeName, args, kw,
                ("pin", Params.Required), ("active_high", true), ("initial_value", 0L), ("frequency", 100L));
            _activeHigh = p.Bool("active_high");
            _frequency = p.Double("frequency");
            if (!(_frequency > 0)) throw PyException.ValueError("la frecuencia debe ser positiva");
            int bcm = PinNumbers.Parse(p["pin"]);
            _pin = ctx.Pins.Reserve(bcm, $"{typeName}({bcm})");
            SetValue(p.Double("initial_value"));

            Method("on", a => { SetValue(1); return null; });
            Method("off", a => { SetValue(0); return null; });
            Method("toggle", a => { SetValue(1 - _value); return null; });
            Property("value", () => _value, v => SetValue(PyOps.ToDouble(v, "value")));
            Property("is_active", () => _value > 0);
            Property("frequency", () => _frequency, v =>
            {
                double f = PyOps.ToDouble(v, "frequency");
                if (!(f > 0)) throw PyException.ValueError("la frecuencia debe ser positiva");
                _frequency = f;
                SetValue(_value);
            });
            Property("pin", () => "GPIO" + _pin.Number);
            if (typeName == "PWMLED") Property("is_lit", () => _value > 0);
        }

        private void SetValue(double v)
        {
            if (double.IsNaN(v) || v < 0 || v > 1)
                throw new PyException("OutputDeviceBadValue", $"el valor de {TypeName} debe estar entre 0 y 1, no {PyOps.Str(v)}");
            _value = v;
            _pin.SetPwm(_frequency, _activeHigh ? v : 1 - v);
        }

        protected override void OnClose() => Ctx.Pins.Release(_pin);
    }

    /// <summary>
    /// <c>LEDBoard(20, 21, ...)</c>: varios LED que se manejan juntos. Admite <c>board[i]</c>,
    /// <c>len(board)</c>, <c>for led in board</c> y <c>board.value = (1, 0, ...)</c>.
    /// </summary>
    internal sealed class GzLedBoard : GzDevice, IPyIndexable
    {
        private readonly List<GzDevice> _leds = new List<GzDevice>();

        public GzLedBoard(GpioZeroContext ctx, object[] args, IReadOnlyDictionary<string, object> kw)
            : base(ctx, "LEDBoard")
        {
            var options = Params.Bind("LEDBoard", new object[0], kw,
                ("pwm", false), ("active_high", true), ("initial_value", false));
            if (args.Length == 0) throw PyException.TypeError("LEDBoard() necesita al menos un pin");
            bool pwm = options.Bool("pwm");
            try
            {
                foreach (var pin in args)
                {
                    var ledKw = new Dictionary<string, object> { ["active_high"] = options["active_high"] };
                    _leds.Add(pwm
                        ? (GzDevice)new GzPwmOutput(ctx, "PWMLED", new[] { pin }, ledKw)
                        : new GzDigitalOutput(ctx, "LED", new[] { pin }, ledKw));
                }
            }
            catch
            {
                foreach (var l in _leds) l.Close();   // si un pin falla, no deja los otros reservados
                throw;
            }
            if (options.Bool("initial_value")) SetAll(true);

            Method("on", a => { ForIndices(a, true); return null; });
            Method("off", a => { ForIndices(a, false); return null; });
            Method("toggle", a =>
            {
                foreach (var i in Indices(a)) SetLed(i, !IsOn(i));
                return null;
            });
            Property("value", () => new PyTuple(_leds.Select((l, i) => Value(i)).ToArray()), SetValues);
            Property("is_active", () => Enumerable.Range(0, _leds.Count).Any(IsOn));
            Property("is_lit", () => Enumerable.Range(0, _leds.Count).Any(IsOn));
            Property("leds", () => new PyTuple(_leds.Cast<object>().ToArray()));
        }

        public int Count => _leds.Count;
        public object GetItem(int index) => _leds[index];

        private IEnumerable<int> Indices(object[] a) =>
            a.Length == 0 ? Enumerable.Range(0, _leds.Count) : a.Select(x => PyOps.NormalizeIndex(PyOps.ToIndex(x), _leds.Count));

        private void ForIndices(object[] a, bool on)
        {
            foreach (var i in Indices(a)) SetLed(i, on);
        }

        private void SetAll(bool on)
        {
            for (int i = 0; i < _leds.Count; i++) SetLed(i, on);
        }

        private void SetLed(int i, bool on)
        {
            if (_leds[i] is GzDigitalOutput d)
            {
                d.StopBlink();
                d.Set(on);
            }
            else _leds[i].SetAttribute("value", on ? 1.0 : 0.0);
        }

        private bool IsOn(int i) => PyOps.IsTruthy(_leds[i].GetAttribute("is_active"));

        private object Value(int i) => _leds[i].GetAttribute("value");

        private void SetValues(object v)
        {
            var items = PyOps.Iterate(v).ToList();
            if (items.Count != _leds.Count)
                throw PyException.ValueError($"LEDBoard tiene {_leds.Count} LED y se le han dado {items.Count} valores");
            for (int i = 0; i < items.Count; i++)
            {
                if (_leds[i] is GzDigitalOutput) SetLed(i, PyOps.IsTruthy(items[i]));
                else _leds[i].SetAttribute("value", items[i]);
            }
        }

        protected override void OnClose()
        {
            foreach (var l in _leds) l.Close();
        }
    }
}
