using System;

namespace ImatPiSim.Domain.Interpreter
{
    /// <summary>
    /// Utilidades sobre el texto de un programa que necesita el editor de código: contar
    /// líneas, saber en qué línea está un carácter y calcular la sangría automática.
    /// </summary>
    /// <remarks>
    /// Está en el dominio (sin Unity) porque son reglas del lenguaje, no de la interfaz: qué
    /// sangría lleva la línea siguiente depende de la sintaxis de Python. Así se prueban con
    /// <c>dotnet test</c> igual que el lexer.
    /// </remarks>
    public static class SourceText
    {
        /// <summary>Sangría que se añade tras una línea que abre bloque (<c>if …:</c>, <c>def …:</c>).</summary>
        public const string IndentUnit = "    ";

        /// <summary>Palabras tras las que el bloque termina: la línea siguiente sale un nivel.</summary>
        private static readonly string[] BlockEnders = { "return", "pass", "break", "continue", "raise" };

        /// <summary>Número de líneas del texto (un texto vacío tiene una línea).</summary>
        public static int CountLines(string text)
        {
            if (string.IsNullOrEmpty(text)) return 1;
            int n = 1;
            foreach (char c in text) if (c == '\n') n++;
            return n;
        }

        /// <summary>Línea (empezando en 1) en la que está el carácter de posición <paramref name="index"/>.</summary>
        public static int LineAt(string text, int index)
        {
            text ??= "";
            index = Math.Max(0, Math.Min(index, text.Length));
            int line = 1;
            for (int i = 0; i < index; i++) if (text[i] == '\n') line++;
            return line;
        }

        /// <summary>Texto de la línea actual desde su inicio hasta <paramref name="index"/> (sin incluirlo).</summary>
        public static string LineBefore(string text, int index)
        {
            text ??= "";
            index = Math.Max(0, Math.Min(index, text.Length));
            int start = index == 0 ? 0 : text.LastIndexOf('\n', index - 1) + 1;
            return text.Substring(start, index - start);
        }

        /// <summary>
        /// Sangría de la línea nueva que se crea al pulsar Enter al final de <paramref name="previousLine"/>:
        /// la misma que tenía, un nivel más si la línea termina en <c>:</c> (abre bloque) y un nivel
        /// menos si empieza por <c>return</c>, <c>pass</c>, <c>break</c>, <c>continue</c> o <c>raise</c>.
        /// </summary>
        /// <remarks>Es lo que hace IDLE, el editor que trae Python. Los comentarios no cuentan.</remarks>
        public static string IndentForNewLine(string previousLine)
        {
            previousLine ??= "";
            int k = 0;
            while (k < previousLine.Length && (previousLine[k] == ' ' || previousLine[k] == '\t')) k++;
            string indent = previousLine.Substring(0, k);
            string code = StripComment(previousLine.Substring(k)).TrimEnd();

            if (code.EndsWith(":")) return indent + IndentUnit;
            if (StartsWithKeyword(code)) return Dedent(indent);
            return indent;
        }

        /// <summary>Quita un nivel de sangría (4 espacios o un tabulador) del final.</summary>
        private static string Dedent(string indent)
        {
            if (indent.EndsWith("\t")) return indent.Substring(0, indent.Length - 1);
            int spaces = 0;
            while (spaces < IndentUnit.Length && spaces < indent.Length && indent[indent.Length - 1 - spaces] == ' ') spaces++;
            return indent.Substring(0, indent.Length - spaces);
        }

        private static bool StartsWithKeyword(string code)
        {
            foreach (var word in BlockEnders)
            {
                if (!code.StartsWith(word, StringComparison.Ordinal)) continue;
                if (code.Length == word.Length) return true;
                char next = code[word.Length];
                if (!char.IsLetterOrDigit(next) && next != '_') return true;
            }
            return false;
        }

        /// <summary>Elimina un comentario <c># …</c> que no esté dentro de una cadena.</summary>
        private static string StripComment(string code)
        {
            char quote = '\0';
            for (int i = 0; i < code.Length; i++)
            {
                char c = code[i];
                if (quote != '\0')
                {
                    if (c == '\\') i++;
                    else if (c == quote) quote = '\0';
                }
                else if (c == '\'' || c == '"') quote = c;
                else if (c == '#') return code.Substring(0, i);
            }
            return code;
        }
    }
}
