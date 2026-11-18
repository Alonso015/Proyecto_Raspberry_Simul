using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ImatPiSim.Domain.Interpreter
{
    /// <summary>
    /// Analizador léxico del subconjunto de Python: convierte el texto del programa en tokens.
    /// </summary>
    /// <remarks>
    /// <para><b>Sangría.</b> En Python la sangría es parte de la gramática. Al empezar cada línea
    /// lógica se mide su sangría y se compara con una pila de niveles: si es mayor se emite
    /// <see cref="TokenType.Indent"/>; si es menor, un <see cref="TokenType.Dedent"/> por cada nivel
    /// que se cierra (y debe coincidir con uno abierto antes). Las líneas en blanco o solo con
    /// comentario no cuentan.</para>
    /// <para><b>Líneas lógicas.</b> Dentro de paréntesis, corchetes o llaves los saltos de línea no
    /// terminan la sentencia; tampoco una barra invertida al final de línea.</para>
    /// <para>Los tabuladores avanzan hasta el siguiente múltiplo de 8, como en Python.</para>
    /// </remarks>
    public sealed class Lexer
    {
        private static readonly (string text, TokenType type)[] Operators =
        {
            // Más largos primero (maximal munch): "<<=" antes que "<<" antes que "<".
            ("**=", TokenType.DoubleStarAssign), ("//=", TokenType.DoubleSlashAssign),
            ("<<=", TokenType.LeftShiftAssign), (">>=", TokenType.RightShiftAssign),
            ("**", TokenType.DoubleStar), ("//", TokenType.DoubleSlash),
            ("<<", TokenType.LeftShift), (">>", TokenType.RightShift),
            ("<=", TokenType.LessEqual), (">=", TokenType.GreaterEqual),
            ("==", TokenType.EqualEqual), ("!=", TokenType.NotEqual),
            ("+=", TokenType.PlusAssign), ("-=", TokenType.MinusAssign),
            ("*=", TokenType.StarAssign), ("/=", TokenType.SlashAssign),
            ("%=", TokenType.PercentAssign), ("&=", TokenType.AmpersandAssign),
            ("|=", TokenType.PipeAssign), ("^=", TokenType.CaretAssign),
            ("->", TokenType.Arrow),
            ("+", TokenType.Plus), ("-", TokenType.Minus), ("*", TokenType.Star),
            ("/", TokenType.Slash), ("%", TokenType.Percent),
            ("&", TokenType.Ampersand), ("|", TokenType.Pipe), ("^", TokenType.Caret), ("~", TokenType.Tilde),
            ("<", TokenType.Less), (">", TokenType.Greater), ("=", TokenType.Assign),
            ("(", TokenType.LeftParen), (")", TokenType.RightParen),
            ("[", TokenType.LeftBracket), ("]", TokenType.RightBracket),
            ("{", TokenType.LeftBrace), ("}", TokenType.RightBrace),
            (",", TokenType.Comma), (":", TokenType.Colon), (".", TokenType.Dot), (";", TokenType.Semicolon),
        };

        private readonly string _src;
        private readonly List<Token> _tokens = new List<Token>();
        private readonly Stack<int> _indents = new Stack<int>();
        private readonly Stack<(char open, int line, int column)> _brackets = new Stack<(char, int, int)>();
        private int _pos;
        private int _line = 1;
        private int _lineStart;
        private bool _atLineStart = true;

        private Lexer(string source)
        {
            // Normaliza los finales de línea de Windows y Mac antiguo a '\n'.
            _src = (source ?? throw new ArgumentNullException(nameof(source)))
                .Replace("\r\n", "\n").Replace('\r', '\n');
            _indents.Push(0);
        }

        /// <summary>Convierte el programa en la lista de tokens, terminada en <see cref="TokenType.EndOfFile"/>.</summary>
        /// <exception cref="LexerException">Si hay un error léxico (con línea y columna).</exception>
        public static IReadOnlyList<Token> Tokenize(string source) => new Lexer(source).Run();

        private int Column => _pos - _lineStart + 1;
        private char Peek(int offset = 0) => _pos + offset < _src.Length ? _src[_pos + offset] : '\0';
        private bool AtEnd => _pos >= _src.Length;

        private List<Token> Run()
        {
            while (!AtEnd)
            {
                if (_atLineStart && _brackets.Count == 0)
                {
                    if (HandleIndentation()) continue;   // línea en blanco o de comentario
                }

                char c = Peek();
                if (c == ' ' || c == '\t') { _pos++; continue; }
                if (c == '#') { SkipComment(); continue; }
                if (c == '\\' && Peek(1) == '\n') { _pos += 2; NewLine(); continue; }   // continuación explícita
                if (c == '\\') throw Error("la barra invertida solo puede ir al final de una línea");
                if (c == '\n') { HandleNewline(); continue; }
                if (char.IsDigit(c) || (c == '.' && char.IsDigit(Peek(1)))) { LexNumber(); continue; }
                if (IsIdentifierStart(c)) { LexIdentifierOrString(); continue; }
                if (c == '\'' || c == '"') { LexString(raw: false, startPos: _pos); continue; }
                if (TryLexOperator()) continue;
                if (c == '!') throw Error("'!' no existe en Python: usa 'not' para negar o '!=' para comparar");
                throw Error($"carácter no válido '{c}'");
            }

            if (_brackets.Count > 0)
            {
                var (open, line, column) = _brackets.Peek();
                throw new LexerException($"falta cerrar '{open}'", line, column);
            }
            if (_tokens.Count > 0 && _tokens[_tokens.Count - 1].Type != TokenType.Newline)
                Add(TokenType.Newline, "", null, _line, Column);
            while (_indents.Peek() > 0)
            {
                _indents.Pop();
                Add(TokenType.Dedent, "", null, _line, Column);
            }
            Add(TokenType.EndOfFile, "", null, _line, Column);
            return _tokens;
        }

        // ------------------------------------------------------------------ líneas y sangría

        /// <returns><c>true</c> si la línea está en blanco o solo tiene un comentario.</returns>
        private bool HandleIndentation()
        {
            int width = 0;
            while (!AtEnd && (Peek() == ' ' || Peek() == '\t'))
            {
                width = Peek() == '\t' ? (width / 8 + 1) * 8 : width + 1;
                _pos++;
            }
            if (AtEnd) return true;
            if (Peek() == '\n') { _pos++; NewLine(); return true; }
            if (Peek() == '#') { SkipComment(); if (!AtEnd) { _pos++; NewLine(); } return true; }

            _atLineStart = false;
            int current = _indents.Peek();
            if (width > current)
            {
                if (_tokens.Count == 0 || _tokens[_tokens.Count - 1].Type != TokenType.Newline)
                    throw Error("sangría inesperada");
                _indents.Push(width);
                Add(TokenType.Indent, "", null, _line, 1);
            }
            else if (width < current)
            {
                while (_indents.Peek() > width)
                {
                    _indents.Pop();
                    Add(TokenType.Dedent, "", null, _line, 1);
                }
                if (_indents.Peek() != width)
                    throw Error("la sangría no coincide con ningún nivel anterior");
            }
            return false;
        }

        private void HandleNewline()
        {
            int col = Column;
            _pos++;
            if (_brackets.Count == 0)
            {
                // Solo se emite si la línea tenía algo (evita NEWLINE dobles).
                if (_tokens.Count > 0 && _tokens[_tokens.Count - 1].Type != TokenType.Newline)
                    Add(TokenType.Newline, "\\n", null, _line, col);
                _atLineStart = true;
            }
            NewLine();
        }

        private void NewLine()
        {
            _line++;
            _lineStart = _pos;
        }

        private void SkipComment()
        {
            while (!AtEnd && Peek() != '\n') _pos++;
        }

        // ------------------------------------------------------------------ números

        private void LexNumber()
        {
            int start = _pos, startCol = Column;

            if (Peek() == '0' && "xXbBoO".IndexOf(Peek(1)) >= 0 && Peek(1) != '\0')
            {
                char kind = char.ToLowerInvariant(Peek(1));
                _pos += 2;
                int radix = kind == 'x' ? 16 : kind == 'b' ? 2 : 8;
                string digits = ReadDigits(ch => DigitValue(ch) >= 0 && DigitValue(ch) < radix, allowLeadingUnderscore: true);
                if (digits.Length == 0) throw Error($"faltan dígitos tras '0{kind}'", startCol);
                CheckNotFollowedByName(startCol);
                long value = 0;
                foreach (char ch in digits)
                {
                    try { value = checked(value * radix + DigitValue(ch)); }
                    catch (OverflowException) { throw Error("número demasiado grande", startCol); }
                }
                Add(TokenType.Integer, _src.Substring(start, _pos - start), value, _line, startCol);
                return;
            }

            string intPart = ReadDigits(char.IsDigit, allowLeadingUnderscore: false);
            bool isFloat = false;
            string fracPart = "", expPart = "";
            if (Peek() == '.' && !IsIdentifierStart(Peek(1)))
            {
                isFloat = true;
                _pos++;
                fracPart = ReadDigits(char.IsDigit, allowLeadingUnderscore: false);
            }
            if (Peek() == 'e' || Peek() == 'E')
            {
                int save = _pos;
                _pos++;
                string sign = "";
                if (Peek() == '+' || Peek() == '-') { sign = Peek().ToString(); _pos++; }
                string exp = ReadDigits(char.IsDigit, allowLeadingUnderscore: false);
                if (exp.Length == 0) throw Error("exponente sin dígitos", startCol);
                expPart = "e" + sign + exp;
                isFloat = true;
                _ = save;
            }
            CheckNotFollowedByName(startCol);

            string lexeme = _src.Substring(start, _pos - start);
            if (isFloat)
            {
                string text = (intPart.Length == 0 ? "0" : intPart) + "." + (fracPart.Length == 0 ? "0" : fracPart) + expPart;
                double d = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
                Add(TokenType.Float, lexeme, d, _line, startCol);
            }
            else
            {
                if (intPart.Length > 1 && intPart[0] == '0' && intPart.TrimStart('0').Length > 0)
                    throw Error("no se permiten ceros a la izquierda en un entero (usa 0o para octal)", startCol);
                if (!long.TryParse(intPart, NumberStyles.None, CultureInfo.InvariantCulture, out var l))
                    throw Error("número demasiado grande", startCol);
                Add(TokenType.Integer, lexeme, l, _line, startCol);
            }
        }

        /// <summary>Lee dígitos admitiendo '_' solo entre dígitos (como "20_000").</summary>
        private string ReadDigits(Func<char, bool> isDigit, bool allowLeadingUnderscore)
        {
            var sb = new StringBuilder();
            bool lastWasUnderscore = false, any = false;
            while (!AtEnd)
            {
                char ch = Peek();
                if (ch == '_')
                {
                    if (lastWasUnderscore || (!any && !allowLeadingUnderscore) || !isDigit(Peek(1)))
                        throw Error("'_' solo puede ir entre dos dígitos");
                    lastWasUnderscore = true;
                    _pos++;
                    continue;
                }
                if (!isDigit(ch)) break;
                sb.Append(ch);
                any = true;
                lastWasUnderscore = false;
                _pos++;
            }
            return sb.ToString();
        }

        private void CheckNotFollowedByName(int startCol)
        {
            if (IsIdentifierPart(Peek()))
                throw Error("número mal formado: un nombre no puede empezar por un dígito", startCol);
        }

        private static int DigitValue(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        // ------------------------------------------------------------------ nombres y cadenas

        private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';
        private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '_';

        private void LexIdentifierOrString()
        {
            int start = _pos, startCol = Column;
            while (!AtEnd && IsIdentifierPart(Peek())) _pos++;
            string text = _src.Substring(start, _pos - start);

            // Prefijos de cadena: r'...' (sin escapes). f-strings y bytes no están soportados.
            if (Peek() == '\'' || Peek() == '"')
            {
                string prefix = text.ToLowerInvariant();
                if (prefix == "r") { LexString(raw: true, startPos: start); return; }
                if (prefix.IndexOf('f') >= 0)
                    throw Error("las f-strings no están soportadas; usa str() y + para componer el texto", startCol);
                if (prefix.IndexOf('b') >= 0 || prefix == "u" || prefix == "rb" || prefix == "br")
                    throw Error($"las cadenas con prefijo '{text}' no están soportadas", startCol);
            }

            Add(Keywords.ByText.TryGetValue(text, out var kw) ? kw : TokenType.Identifier, text, null, _line, startCol);
        }

        private void LexString(bool raw, int startPos)
        {
            int startLine = _line, startCol = startPos - _lineStart + 1;
            char quote = Peek();
            bool triple = Peek(1) == quote && Peek(2) == quote;
            _pos += triple ? 3 : 1;
            var sb = new StringBuilder();

            while (true)
            {
                if (AtEnd) throw new LexerException("cadena sin cerrar", startLine, startCol);
                char c = Peek();
                if (triple && c == quote && Peek(1) == quote && Peek(2) == quote) { _pos += 3; break; }
                if (!triple && c == quote) { _pos++; break; }
                if (c == '\n')
                {
                    if (!triple) throw new LexerException("cadena sin cerrar antes del final de la línea", startLine, startCol);
                    sb.Append('\n');
                    _pos++;
                    NewLine();
                    continue;
                }
                if (c == '\\' && !raw)
                {
                    char e = Peek(1);
                    _pos += 2;
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case '0': sb.Append('\0'); break;
                        case '\\': sb.Append('\\'); break;
                        case '\'': sb.Append('\''); break;
                        case '"': sb.Append('"'); break;
                        case '\n': NewLine(); break;   // la barra al final de línea une las líneas
                        case '\0': throw new LexerException("cadena sin cerrar", startLine, startCol);
                        default: sb.Append('\\').Append(e); break;   // como Python: se conserva
                    }
                    continue;
                }
                sb.Append(c);
                _pos++;
            }
            Add(TokenType.String, _src.Substring(startPos, _pos - startPos), sb.ToString(), startLine, startCol);
        }

        // ------------------------------------------------------------------ operadores

        private bool TryLexOperator()
        {
            foreach (var (text, type) in Operators)
            {
                if (string.CompareOrdinal(_src, _pos, text, 0, text.Length) != 0) continue;
                int col = Column;
                char c = text[0];
                if (text.Length == 1 && (c == '(' || c == '[' || c == '{'))
                    _brackets.Push((c, _line, col));
                else if (text.Length == 1 && (c == ')' || c == ']' || c == '}'))
                {
                    char expected = c == ')' ? '(' : c == ']' ? '[' : '{';
                    if (_brackets.Count == 0) throw Error($"'{c}' sin su '{expected}' de apertura");
                    if (_brackets.Peek().open != expected)
                        throw Error($"'{c}' no cierra el '{_brackets.Peek().open}' abierto en la línea {_brackets.Peek().line}");
                    _brackets.Pop();
                }
                _pos += text.Length;
                Add(type, text, null, _line, col);
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ utilidades

        private void Add(TokenType type, string lexeme, object value, int line, int column) =>
            _tokens.Add(new Token(type, lexeme, value, line, column));

        private LexerException Error(string message, int? column = null) =>
            new LexerException(message, _line, column ?? Column);
    }
}
