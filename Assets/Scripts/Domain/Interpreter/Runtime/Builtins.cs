using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ImatPiSim.Domain.Interpreter.Runtime
{
    /// <summary>Funciones integradas de Python y módulos estándar simulados (<c>time</c>, <c>signal</c>).</summary>
    internal static class Builtins
    {
        public static Dictionary<string, object> Create(ScriptRuntime rt)
        {
            var b = new Dictionary<string, object>();
            void Add(IPyCallable fn) => b[fn.Name] = fn;

            Add(new NativeFunction("print", (args, kw) =>
            {
                string sep = kw.TryGetValue("sep", out var s) ? PyOps.Str(s) : " ";
                string end = kw.TryGetValue("end", out var e) ? PyOps.Str(e) : "\n";
                foreach (var k in kw.Keys)
                    if (k != "sep" && k != "end") throw PyException.TypeError($"print() no tiene el parámetro '{k}'");
                rt.Write(string.Join(sep, args.Select(PyOps.Str)) + end);
                return null;
            }));

            Add(new NativeFunction("range", args =>
            {
                Args.Between("range", args, 1, 3);
                long[] v = args.Select(a => PyOps.ToLong(a, "range()")).ToArray();
                return v.Length == 1 ? new PyRange(0, v[0], 1)
                     : v.Length == 2 ? new PyRange(v[0], v[1], 1)
                     : new PyRange(v[0], v[1], v[2]);
            }));

            Add(new NativeFunction("len", args => { Args.Exactly("len", args, 1); return PyOps.Length(args[0]); }));

            Add(new NativeFunction("int", args =>
            {
                if (args.Length == 0) return 0L;
                Args.Exactly("int", args, 1);
                switch (args[0])
                {
                    case bool x: return x ? 1L : 0L;
                    case long l: return l;
                    case double d:
                        if (double.IsNaN(d) || double.IsInfinity(d)) throw PyException.ValueError("no se puede convertir a entero");
                        return (long)Math.Truncate(d);
                    case string str when long.TryParse(str.Trim().Replace("_", ""), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var p):
                        return p;
                    case string str:
                        throw PyException.ValueError($"'{str}' no es un número entero válido");
                    default:
                        throw PyException.TypeError($"int() no admite {PyOps.TypeName(args[0])}");
                }
            }));

            Add(new NativeFunction("float", args =>
            {
                if (args.Length == 0) return 0.0;
                Args.Exactly("float", args, 1);
                if (args[0] is string str)
                {
                    if (double.TryParse(str.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
                    throw PyException.ValueError($"'{str}' no es un número válido");
                }
                return PyOps.ToDouble(args[0], "float()");
            }));

            Add(new NativeFunction("str", args => args.Length == 0 ? "" : PyOps.Str(args[0])));
            Add(new NativeFunction("bool", args => args.Length != 0 && PyOps.IsTruthy(args[0])));

            Add(new NativeFunction("abs", args =>
            {
                Args.Exactly("abs", args, 1);
                if (args[0] is double d) return Math.Abs(d);
                long l = PyOps.ToLong(args[0], "abs()");
                return l < 0 ? -l : l;
            }));

            Add(new NativeFunction("min", args => Extreme("min", args, -1)));
            Add(new NativeFunction("max", args => Extreme("max", args, +1)));

            Add(new NativeFunction("round", args =>
            {
                Args.Between("round", args, 1, 2);
                double x = PyOps.ToDouble(args[0], "round()");
                if (args.Length == 1) return (long)Math.Round(x, MidpointRounding.ToEven);   // como Python 3
                int nd = (int)PyOps.ToLong(args[1], "round()");
                return Math.Round(x, Math.Max(0, Math.Min(15, nd)), MidpointRounding.ToEven);
            }));

            Add(new NativeFunction("sum", args =>
            {
                Args.Exactly("sum", args, 1);
                object total = 0L;
                foreach (var v in PyOps.Iterate(args[0])) total = PyOps.Binary(TokenType.Plus, total, v);
                return total;
            }));

            Add(new NativeFunction("list", args => new PyList(args.Length == 0 ? Enumerable.Empty<object>() : PyOps.Iterate(args[0]))));
            Add(new NativeFunction("tuple", args => new PyTuple(args.Length == 0 ? new object[0] : PyOps.Iterate(args[0]).ToArray())));
            Add(new NativeFunction("reversed", args =>
            {
                Args.Exactly("reversed", args, 1);
                return new PyList(PyOps.Iterate(args[0]).Reverse());
            }));

            return b;
        }

        private static object Extreme(string name, object[] args, int sign)
        {
            var items = args.Length == 1 ? PyOps.Iterate(args[0]).ToList() : args.ToList();
            if (items.Count == 0) throw PyException.ValueError($"{name}() de una secuencia vacía");
            var best = items[0];
            foreach (var v in items.Skip(1))
                if (PyOps.Compare(sign > 0 ? Ast.CompareOp.Greater : Ast.CompareOp.Less, v, best)) best = v;
            return best;
        }

        /// <summary>Módulo <c>time</c>: <c>sleep</c> pausa la tarea en tiempo simulado (no bloquea Unity).</summary>
        public static PyModule TimeModule(ScriptRuntime rt)
        {
            var m = new PyModule("time");
            m.Add("sleep", new NativeCoroutine("sleep", Sleep));
            m.Add("time", new NativeFunction("time", a => rt.Time));
            m.Add("monotonic", new NativeFunction("monotonic", a => rt.Time));
            return m;
        }

        private static IEnumerable<Signal> Sleep(ScriptRuntime rt, object[] args, IReadOnlyDictionary<string, object> kw, Ref result)
        {
            Args.Exactly("sleep", args, 1);
            double seconds = PyOps.ToDouble(args[0], "sleep()");
            if (seconds < 0) throw PyException.ValueError("sleep() no admite un tiempo negativo");
            yield return Signal.Sleep(seconds);
            rt.CheckInterrupt();   // si se pulsó Parar durante la espera, KeyboardInterrupt aquí
            result.Value = null;
        }

        /// <summary>Módulo <c>signal</c>: <c>pause()</c> espera para siempre (hasta pulsar Parar), como en el Tema 3.</summary>
        public static PyModule SignalModule()
        {
            var m = new PyModule("signal");
            m.Add("pause", new NativeCoroutine("pause", Pause));
            return m;
        }

        private static IEnumerable<Signal> Pause(ScriptRuntime rt, object[] args, IReadOnlyDictionary<string, object> kw, Ref result)
        {
            Args.Exactly("pause", args, 0);
            while (true)
            {
                yield return Signal.Sleep(double.PositiveInfinity);
                rt.CheckInterrupt();
            }
        }
    }
}
