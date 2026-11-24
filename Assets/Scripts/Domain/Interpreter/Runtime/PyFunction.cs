using System.Collections.Generic;
using System.Linq;

namespace ImatPiSim.Domain.Interpreter.Runtime
{
    /// <summary>Paso ejecutable compilado a partir de un nodo del árbol.</summary>
    public delegate IEnumerable<Signal> Executable(Frame frame, Ref result);

    /// <summary>Función definida por el jugador con <c>def</c> o <c>lambda</c>.</summary>
    public sealed class PyFunction : IPyCallable
    {
        private readonly string[] _paramNames;
        private readonly object[] _defaults;   // null = sin valor por defecto (se marca con NoDefault)
        private readonly Executable _body;
        private readonly bool _isLambda;

        internal static readonly object NoDefault = new object();

        internal PyFunction(string name, string[] paramNames, object[] defaults, Scope scope, Executable body,
                            Frame closure, bool isLambda)
        {
            Name = name;
            _paramNames = paramNames;
            _defaults = defaults;
            Scope = scope;
            _body = body;
            Closure = closure;
            _isLambda = isLambda;
        }

        public string Name { get; }
        public Scope Scope { get; }

        /// <summary>Marco donde se definió (para leer variables de la función que la contiene).</summary>
        public Frame Closure { get; }

        public int ParameterCount => _paramNames.Length;

        /// <summary>Parámetros sin valor por defecto.</summary>
        public int RequiredParameterCount => _defaults.Count(d => d == NoDefault);

        public IEnumerable<Signal> Call(ScriptRuntime runtime, object[] args, IReadOnlyDictionary<string, object> kwargs, Ref result)
        {
            var frame = new Frame(Scope, Closure.Scope.IsModule ? null : Closure, Closure.Globals);
            Bind(frame, args, kwargs);

            runtime.EnterCall(Name);
            try
            {
                if (_isLambda)
                {
                    foreach (var s in _body(frame, result)) yield return s;
                }
                else
                {
                    foreach (var s in _body(frame, null)) yield return s;
                    result.Value = frame.Flow == Flow.Return ? frame.ReturnValue : null;
                }
            }
            finally
            {
                runtime.ExitCall();
            }
        }

        private void Bind(Frame frame, object[] args, IReadOnlyDictionary<string, object> kwargs)
        {
            if (args.Length > _paramNames.Length)
                throw PyException.TypeError(
                    $"{Name}() admite {_paramNames.Length} argumento{(_paramNames.Length == 1 ? "" : "s")} y ha recibido {args.Length}");
            for (int i = 0; i < args.Length; i++) frame.Vars[_paramNames[i]] = args[i];
            foreach (var kv in kwargs)
            {
                int i = System.Array.IndexOf(_paramNames, kv.Key);
                if (i < 0) throw PyException.TypeError($"{Name}() no tiene el parámetro '{kv.Key}'");
                if (i < args.Length) throw PyException.TypeError($"{Name}() ha recibido dos valores para '{kv.Key}'");
                frame.Vars[kv.Key] = kv.Value;
            }
            for (int i = 0; i < _paramNames.Length; i++)
            {
                if (frame.Vars.ContainsKey(_paramNames[i])) continue;
                if (_defaults[i] == NoDefault)
                    throw PyException.TypeError($"{Name}() necesita el argumento '{_paramNames[i]}'");
                frame.Vars[_paramNames[i]] = _defaults[i];
            }
        }

        public override string ToString() =>
            _isLambda ? "<lambda>" : $"<función {Name}({string.Join(", ", _paramNames.AsEnumerable())})>";
    }
}
