using System;
using System.Collections.Generic;

namespace ImatPiSim.Domain.Interpreter.Runtime
{
    // =====================================================================================
    // Valores de Python en el intérprete:
    //   int → long      float → double      bool → bool      str → string      None → null
    //   list → PyList   tuple → PyTuple      range → PyRange   funciones → IPyCallable
    // =====================================================================================

    /// <summary>
    /// Lo que un paso de ejecución comunica al planificador: "he hecho una unidad de trabajo"
    /// (<see cref="Step"/>) o "duérmeme" (<see cref="Sleep"/>).
    /// </summary>
    public sealed class Signal
    {
        private Signal(bool isSleep, double seconds)
        {
            IsSleep = isSleep;
            Seconds = seconds;
        }

        /// <summary>Una unidad del presupuesto de instrucciones por tick.</summary>
        public static readonly Signal Step = new Signal(false, 0);

        /// <summary>Suspender la tarea <paramref name="seconds"/> segundos de tiempo simulado.</summary>
        public static Signal Sleep(double seconds) => new Signal(true, seconds);

        public bool IsSleep { get; }
        public double Seconds { get; }
    }

    /// <summary>Hueco donde un paso de ejecución deja su resultado (los iteradores no devuelven valores).</summary>
    public sealed class Ref
    {
        public object Value;
    }

    /// <summary>Objeto con atributos (<c>led.on</c>, <c>button.is_pressed</c>, <c>lista.append</c>...).</summary>
    public interface IPyObject
    {
        /// <summary>Nombre del tipo, como lo diría Python (<c>LED</c>, <c>list</c>...).</summary>
        string TypeName { get; }

        /// <exception cref="PyException">AttributeError si no existe.</exception>
        object GetAttribute(string name);

        /// <exception cref="PyException">AttributeError si no se puede asignar.</exception>
        void SetAttribute(string name, object value);
    }

    /// <summary>Objeto que admite <c>obj[i]</c>, <c>len(obj)</c> y <c>for x in obj</c> (p. ej. <c>LEDBoard</c>).</summary>
    public interface IPyIndexable
    {
        int Count { get; }
        object GetItem(int index);
    }

    /// <summary>Algo que se puede llamar con <c>(...)</c>: funciones del jugador, integradas, clases de la API.</summary>
    public interface IPyCallable
    {
        string Name { get; }

        /// <summary>
        /// Ejecuta la llamada paso a paso. Puede pausarse (p. ej. <c>sleep</c>) devolviendo señales;
        /// deja el valor de retorno en <paramref name="result"/>.
        /// </summary>
        IEnumerable<Signal> Call(ScriptRuntime runtime, object[] args, IReadOnlyDictionary<string, object> kwargs, Ref result);
    }

    /// <summary>Función nativa (en C#) que no se pausa nunca.</summary>
    public sealed class NativeFunction : IPyCallable
    {
        private readonly Func<object[], IReadOnlyDictionary<string, object>, object> _body;

        public NativeFunction(string name, Func<object[], IReadOnlyDictionary<string, object>, object> body)
        {
            Name = name;
            _body = body;
        }

        /// <summary>Atajo para funciones sin argumentos con nombre.</summary>
        public NativeFunction(string name, Func<object[], object> body)
            : this(name, (args, kw) =>
            {
                if (kw.Count > 0) throw PyException.TypeError($"{name}() no acepta argumentos con nombre");
                return body(args);
            }) { }

        public string Name { get; }

        public IEnumerable<Signal> Call(ScriptRuntime runtime, object[] args, IReadOnlyDictionary<string, object> kwargs, Ref result)
        {
            result.Value = _body(args, kwargs);
            yield break;
        }

        public override string ToString() => $"<función integrada {Name}>";
    }

    /// <summary>Función nativa que puede pausarse (como <c>sleep</c> o <c>pause</c>).</summary>
    public sealed class NativeCoroutine : IPyCallable
    {
        public delegate IEnumerable<Signal> Body(ScriptRuntime runtime, object[] args, IReadOnlyDictionary<string, object> kwargs, Ref result);

        private readonly Body _body;

        public NativeCoroutine(string name, Body body)
        {
            Name = name;
            _body = body;
        }

        public string Name { get; }

        public IEnumerable<Signal> Call(ScriptRuntime runtime, object[] args, IReadOnlyDictionary<string, object> kwargs, Ref result) =>
            _body(runtime, args, kwargs, result);

        public override string ToString() => $"<función integrada {Name}>";
    }

    /// <summary>Lista de Python (mutable).</summary>
    public sealed class PyList : IPyObject
    {
        public PyList() => Items = new List<object>();
        public PyList(IEnumerable<object> items) => Items = new List<object>(items);

        public List<object> Items { get; }
        public string TypeName => "list";

        public object GetAttribute(string name)
        {
            switch (name)
            {
                case "append": return new NativeFunction("append", a => { Args.Exactly("append", a, 1); Items.Add(a[0]); return null; });
                case "insert":
                    return new NativeFunction("insert", a =>
                    {
                        Args.Exactly("insert", a, 2);
                        int i = (int)Math.Max(0, Math.Min(Items.Count, PyOps.NormalizeIndex(PyOps.ToIndex(a[0]), Items.Count, clamp: true)));
                        Items.Insert(i, a[1]);
                        return null;
                    });
                case "pop":
                    return new NativeFunction("pop", a =>
                    {
                        if (Items.Count == 0) throw PyException.IndexError("pop de una lista vacía");
                        int i = a.Length == 0 ? Items.Count - 1 : PyOps.NormalizeIndex(PyOps.ToIndex(a[0]), Items.Count);
                        var v = Items[i];
                        Items.RemoveAt(i);
                        return v;
                    });
                case "remove":
                    return new NativeFunction("remove", a =>
                    {
                        Args.Exactly("remove", a, 1);
                        int i = Items.FindIndex(x => PyOps.AreEqual(x, a[0]));
                        if (i < 0) throw PyException.ValueError("el valor no está en la lista");
                        Items.RemoveAt(i);
                        return null;
                    });
                case "clear": return new NativeFunction("clear", a => { Items.Clear(); return null; });
                case "index":
                    return new NativeFunction("index", a =>
                    {
                        Args.Exactly("index", a, 1);
                        int i = Items.FindIndex(x => PyOps.AreEqual(x, a[0]));
                        if (i < 0) throw PyException.ValueError($"{PyOps.Repr(a[0])} no está en la lista");
                        return (long)i;
                    });
                case "count": return new NativeFunction("count", a => { Args.Exactly("count", a, 1); return (long)Items.FindAll(x => PyOps.AreEqual(x, a[0])).Count; });
                case "reverse": return new NativeFunction("reverse", a => { Items.Reverse(); return null; });
                default: throw PyException.AttributeError(TypeName, name);
            }
        }

        public void SetAttribute(string name, object value) => throw PyException.AttributeError(TypeName, name);
    }

    /// <summary>Tupla de Python (inmutable).</summary>
    public sealed class PyTuple
    {
        public PyTuple(IReadOnlyList<object> items) => Items = items;
        public IReadOnlyList<object> Items { get; }
    }

    /// <summary><c>range(inicio, fin, paso)</c>, sin crear la lista entera.</summary>
    public sealed class PyRange
    {
        public PyRange(long start, long stop, long step)
        {
            if (step == 0) throw PyException.ValueError("el paso de range() no puede ser 0");
            Start = start;
            Stop = stop;
            StepValue = step;
        }

        public long Start { get; }
        public long Stop { get; }
        public long StepValue { get; }

        public long Count => StepValue > 0
            ? Math.Max(0, (Stop - Start + StepValue - 1) / StepValue)
            : Math.Max(0, (Start - Stop - StepValue - 1) / -StepValue);

        public IEnumerable<object> Values()
        {
            for (long i = 0; i < Count; i++) yield return Start + i * StepValue;
        }
    }

    /// <summary>Módulo importable (<c>time</c>, <c>gpiozero</c>...): un diccionario de nombres.</summary>
    public sealed class PyModule : IPyObject
    {
        private readonly Dictionary<string, object> _members = new Dictionary<string, object>();

        public PyModule(string name) => Name = name;

        public string Name { get; }
        public string TypeName => "module";

        public PyModule Add(string name, object value)
        {
            _members[name] = value;
            return this;
        }

        public bool TryGet(string name, out object value) => _members.TryGetValue(name, out value);

        public object GetAttribute(string name) =>
            _members.TryGetValue(name, out var v) ? v : throw PyException.AttributeError("módulo '" + Name + "'", name);

        public void SetAttribute(string name, object value) => _members[name] = value;
    }

    /// <summary>Valor de una excepción capturada con <c>except ... as e</c>.</summary>
    public sealed class PyExceptionValue : IPyObject
    {
        public PyExceptionValue(PyException exception) => Exception = exception;
        public PyException Exception { get; }
        public string TypeName => Exception.PythonType;
        public object GetAttribute(string name) => throw PyException.AttributeError(TypeName, name);
        public void SetAttribute(string name, object value) => throw PyException.AttributeError(TypeName, name);
        public override string ToString() => Exception.PythonMessage;
    }

    /// <summary>Comprobaciones de número de argumentos para funciones nativas.</summary>
    public static class Args
    {
        public static void Exactly(string fn, object[] args, int n)
        {
            if (args.Length != n)
                throw PyException.TypeError($"{fn}() necesita {n} argumento{(n == 1 ? "" : "s")} y ha recibido {args.Length}");
        }

        public static void Between(string fn, object[] args, int min, int max)
        {
            if (args.Length < min || args.Length > max)
                throw PyException.TypeError($"{fn}() necesita entre {min} y {max} argumentos y ha recibido {args.Length}");
        }
    }
}
