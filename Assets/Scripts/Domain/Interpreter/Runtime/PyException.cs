using System;
using System.Collections.Generic;

namespace ImatPiSim.Domain.Interpreter.Runtime
{
    /// <summary>
    /// Excepción de Python durante la ejecución (ZeroDivisionError, NameError...). Viaja por el
    /// intérprete como excepción de C#; un <c>try / except</c> del jugador puede capturarla.
    /// </summary>
    public sealed class PyException : Exception
    {
        /// <summary>Jerarquía de tipos: hijo → padre (como en Python).</summary>
        private static readonly Dictionary<string, string> Parent = new Dictionary<string, string>
        {
            ["Exception"] = "BaseException",
            ["KeyboardInterrupt"] = "BaseException",
            ["ArithmeticError"] = "Exception",
            ["ZeroDivisionError"] = "ArithmeticError",
            ["OverflowError"] = "ArithmeticError",
            ["LookupError"] = "Exception",
            ["IndexError"] = "LookupError",
            ["KeyError"] = "LookupError",
            ["NameError"] = "Exception",
            ["UnboundLocalError"] = "NameError",
            ["TypeError"] = "Exception",
            ["ValueError"] = "Exception",
            ["AttributeError"] = "Exception",
            ["ImportError"] = "Exception",
            ["RuntimeError"] = "Exception",
            ["RecursionError"] = "RuntimeError",
        };

        public PyException(string pythonType, string message) : base(pythonType + ": " + message)
        {
            PythonType = pythonType;
            PythonMessage = message;
        }

        public string PythonType { get; }
        public string PythonMessage { get; }

        /// <summary>Posición del código donde se produjo (0 = sin asignar todavía).</summary>
        public int Line { get; private set; }
        public int Column { get; private set; }

        /// <summary>Asigna la posición si aún no la tiene (la más interna es la buena).</summary>
        public PyException At(int line, int column)
        {
            if (Line == 0)
            {
                Line = line;
                Column = column;
            }
            return this;
        }

        public static bool IsKnownType(string name) => name == "BaseException" || Parent.ContainsKey(name);

        /// <summary>¿Captura un <c>except <paramref name="handlerType"/></c> esta excepción?</summary>
        public bool IsInstanceOf(string handlerType)
        {
            for (var t = PythonType; t != null; t = Parent.TryGetValue(t, out var p) ? p : null)
                if (t == handlerType) return true;
            return false;
        }

        public static PyException TypeError(string m) => new PyException("TypeError", m);
        public static PyException ValueError(string m) => new PyException("ValueError", m);
        public static PyException IndexError(string m) => new PyException("IndexError", m);
        public static PyException NameError(string name) =>
            new PyException("NameError", $"el nombre '{name}' no está definido");
        public static PyException UnboundLocal(string name) =>
            new PyException("UnboundLocalError", $"la variable local '{name}' se usa antes de darle un valor");
        public static PyException AttributeError(string type, string name) =>
            new PyException("AttributeError", $"'{type}' no tiene el atributo '{name}'");
        public static PyException ZeroDivision() => new PyException("ZeroDivisionError", "división entre cero");
        public static PyException KeyboardInterrupt() => new PyException("KeyboardInterrupt", "programa detenido");
    }

    /// <summary>Error de ejecución no capturado, tal como se le muestra al jugador.</summary>
    public sealed class ScriptRuntimeException : ScriptException
    {
        public ScriptRuntimeException(PyException inner)
            : base("Error de ejecución", inner.PythonType + ": " + inner.PythonMessage, inner.Line, inner.Column)
        {
            PythonType = inner.PythonType;
        }

        public string PythonType { get; }
    }
}
