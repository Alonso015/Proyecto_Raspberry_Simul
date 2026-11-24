using System;
using System.Collections.Generic;
using System.Linq;
using ImatPiSim.Domain.Components;
using ImatPiSim.Domain.Interpreter.Runtime;

namespace ImatPiSim.Domain.Api
{
    /// <summary>Lo que necesitan los dispositivos de la API: el intérprete, los pines y el ADC de la HAT.</summary>
    public sealed class GpioZeroContext
    {
        public GpioZeroContext(ScriptRuntime runtime, IPinFactory pins, Mcp3008 adc)
        {
            Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            Pins = pins ?? throw new ArgumentNullException(nameof(pins));
            Adc = adc;
        }

        public ScriptRuntime Runtime { get; }
        public IPinFactory Pins { get; }

        /// <summary>MCP3008 de la IMAT HAT (<c>null</c> si no hay HAT).</summary>
        public Mcp3008 Adc { get; }
    }

    /// <summary>Clase de Python que crea objetos al llamarla: <c>LED(20)</c>.</summary>
    internal sealed class PyClass : IPyCallable
    {
        private readonly Func<object[], IReadOnlyDictionary<string, object>, object> _construct;

        public PyClass(string name, Func<object[], IReadOnlyDictionary<string, object>, object> construct)
        {
            Name = name;
            _construct = construct;
        }

        public string Name { get; }

        public IEnumerable<Signal> Call(ScriptRuntime runtime, object[] args, IReadOnlyDictionary<string, object> kwargs, Ref result)
        {
            result.Value = _construct(args, kwargs);
            yield break;
        }

        public override string ToString() => $"<class 'gpiozero.{Name}'>";
    }

    /// <summary>
    /// Enlaza los argumentos de una llamada con sus parámetros, como Python: posicionales en orden,
    /// con nombre por nombre y valores por defecto para el resto.
    /// </summary>
    internal sealed class Params
    {
        public static readonly object Required = new object();

        private readonly Dictionary<string, object> _values = new Dictionary<string, object>();

        private Params() { }

        public static Params Bind(string fn, object[] args, IReadOnlyDictionary<string, object> kwargs,
                                  params (string name, object defaultValue)[] spec)
        {
            var p = new Params();
            if (args.Length > spec.Length)
                throw PyException.TypeError($"{fn}() admite como mucho {spec.Length} argumentos y ha recibido {args.Length}");
            for (int i = 0; i < args.Length; i++) p._values[spec[i].name] = args[i];
            foreach (var kv in kwargs)
            {
                if (kv.Key == "pin_factory") continue;   // se acepta y se ignora: el simulador usa siempre la suya
                if (!spec.Any(s => s.name == kv.Key)) throw PyException.TypeError($"{fn}() no tiene el parámetro '{kv.Key}'");
                if (p._values.ContainsKey(kv.Key)) throw PyException.TypeError($"{fn}() ha recibido dos valores para '{kv.Key}'");
                p._values[kv.Key] = kv.Value;
            }
            foreach (var (name, def) in spec)
            {
                if (p._values.ContainsKey(name)) continue;
                if (def == Required) throw PyException.TypeError($"{fn}() necesita el argumento '{name}'");
                p._values[name] = def;
            }
            return p;
        }

        public object this[string name] => _values[name];
        public double Double(string name) => PyOps.ToDouble(_values[name], name);
        public bool Bool(string name) => PyOps.IsTruthy(_values[name]);
    }

    /// <summary>Conversión de la forma de indicar un pin en gpiozero a número BCM.</summary>
    internal static class PinNumbers
    {
        /// <summary>Pin físico del conector → GPIO (BCM), según docs/hardware.md §1.3.</summary>
        private static readonly Dictionary<int, int> BoardToBcm = new Dictionary<int, int>
        {
            [3] = 2, [5] = 3, [7] = 4, [8] = 14, [10] = 15, [11] = 17, [12] = 18, [13] = 27, [15] = 22,
            [16] = 23, [18] = 24, [19] = 10, [21] = 9, [22] = 25, [23] = 11, [24] = 8, [26] = 7, [29] = 5,
            [31] = 6, [32] = 12, [33] = 13, [35] = 19, [36] = 16, [37] = 26, [38] = 20, [40] = 21,
        };

        /// <summary>Admite 17, "17", "GPIO17", "BCM17" y "BOARD11" (pin físico).</summary>
        public static int Parse(object pin)
        {
            switch (pin)
            {
                case long l when l >= 0 && l < RaspberryPi.GpioCount:
                    return (int)l;
                case string s:
                    var t = s.Trim().ToUpperInvariant();
                    if (t.StartsWith("BOARD") && int.TryParse(t.Substring(5), out var phys))
                        return BoardToBcm.TryGetValue(phys, out var b) ? b : throw PinErrors.Invalid(pin);
                    if (t.StartsWith("GPIO")) t = t.Substring(4);
                    else if (t.StartsWith("BCM")) t = t.Substring(3);
                    if (int.TryParse(t, out var n) && n >= 0 && n < RaspberryPi.GpioCount) return n;
                    throw PinErrors.Invalid(pin);
                default:
                    throw PinErrors.Invalid(pin);
            }
        }
    }

    /// <summary>
    /// Base de los dispositivos de gpiozero: atributos (propiedades y métodos) declarados en una
    /// tabla, y <c>close()</c>.
    /// </summary>
    internal abstract class GzDevice : IPyObject
    {
        private readonly Dictionary<string, Func<object>> _getters = new Dictionary<string, Func<object>>();
        private readonly Dictionary<string, Action<object>> _setters = new Dictionary<string, Action<object>>();

        protected GzDevice(GpioZeroContext ctx, string typeName)
        {
            Ctx = ctx;
            TypeName = typeName;
            Method("close", a => { Close(); return null; });
            Property("closed", () => IsClosed);
        }

        protected GpioZeroContext Ctx { get; }
        public string TypeName { get; }
        public bool IsClosed { get; private set; }

        protected void Property(string name, Func<object> get, Action<object> set = null)
        {
            _getters[name] = get;
            if (set != null) _setters[name] = set;
        }

        protected void Method(string name, Func<object[], object> body) =>
            _getters[name] = () => new NativeFunction(name, body);

        protected void Method(string name, Func<object[], IReadOnlyDictionary<string, object>, object> body) =>
            _getters[name] = () => new NativeFunction(name, body);

        protected void Coroutine(string name, NativeCoroutine.Body body) =>
            _getters[name] = () => new NativeCoroutine(name, body);

        public object GetAttribute(string name)
        {
            if (IsClosed && name != "closed" && name != "close")
                throw new PyException("GPIODeviceClosed", $"el dispositivo {TypeName} está cerrado (se llamó a close())");
            if (_getters.TryGetValue(name, out var get)) return get();
            throw PyException.AttributeError(TypeName, name);
        }

        public void SetAttribute(string name, object value)
        {
            if (IsClosed) throw new PyException("GPIODeviceClosed", $"el dispositivo {TypeName} está cerrado");
            if (_setters.TryGetValue(name, out var set))
            {
                set(value);
                return;
            }
            if (_getters.ContainsKey(name))
                throw new PyException("AttributeError", $"el atributo '{name}' de {TypeName} es de solo lectura");
            throw PyException.AttributeError(TypeName, name);
        }

        public void Close()
        {
            if (IsClosed) return;
            OnClose();
            IsClosed = true;
        }

        protected abstract void OnClose();

        static GzDevice()
        {
            PyException.RegisterType("GPIODeviceClosed", "Exception");
        }
    }
}
