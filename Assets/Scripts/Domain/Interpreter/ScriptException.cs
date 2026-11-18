using System;

namespace ImatPiSim.Domain.Interpreter
{
    /// <summary>
    /// Error en el programa del jugador, con su posición. Base común de los errores léxicos,
    /// sintácticos y de ejecución, para que la interfaz los muestre igual (línea marcada + mensaje).
    /// </summary>
    public abstract class ScriptException : Exception
    {
        protected ScriptException(string kind, string message, int line, int column)
            : base($"{kind} en la línea {line}, columna {column}: {message}")
        {
            Line = line;
            Column = column;
            Detail = message;
        }

        public int Line { get; }
        public int Column { get; }

        /// <summary>El mensaje sin la posición.</summary>
        public string Detail { get; }
    }

    /// <summary>Error de análisis léxico (carácter no válido, cadena sin cerrar, sangría...).</summary>
    public sealed class LexerException : ScriptException
    {
        public LexerException(string message, int line, int column)
            : base("Error léxico", message, line, column) { }
    }

    /// <summary>Error sintáctico (estructura del programa incorrecta o no soportada).</summary>
    public sealed class ParseException : ScriptException
    {
        public ParseException(string message, int line, int column)
            : base("Error de sintaxis", message, line, column) { }
    }
}
