using System;
using System.Collections.Generic;

namespace ImatPiSim.Domain.Interpreter
{
    /// <summary>Clases de token del subconjunto de Python (docs/requisitos.md §2.5).</summary>
    public enum TokenType
    {
        // Literales y nombres
        Identifier,
        Integer,
        Float,
        String,

        // Palabras clave
        False, None, True, And, Or, Not, Is, In,
        If, Elif, Else, While, For, Break, Continue, Pass,
        Def, Return, Lambda, Global, Nonlocal,
        Import, From, As,
        Try, Except, Finally, Raise, Class, With, Yield, Del, Assert, Async, Await,

        // Operadores aritméticos y de bits
        Plus, Minus, Star, Slash, DoubleSlash, Percent, DoubleStar,
        LeftShift, RightShift, Ampersand, Pipe, Caret, Tilde,

        // Comparación
        Less, Greater, LessEqual, GreaterEqual, EqualEqual, NotEqual,

        // Asignación
        Assign,
        PlusAssign, MinusAssign, StarAssign, SlashAssign, DoubleSlashAssign, PercentAssign,
        DoubleStarAssign, LeftShiftAssign, RightShiftAssign, AmpersandAssign, PipeAssign, CaretAssign,

        // Delimitadores
        LeftParen, RightParen, LeftBracket, RightBracket, LeftBrace, RightBrace,
        Comma, Colon, Dot, Semicolon, Arrow,

        // Estructura de líneas
        Newline,
        Indent,
        Dedent,
        EndOfFile
    }

    /// <summary>Un token: su clase, el texto original, su valor (si es literal) y su posición.</summary>
    public sealed class Token
    {
        public Token(TokenType type, string lexeme, object value, int line, int column)
        {
            Type = type;
            Lexeme = lexeme;
            Value = value;
            Line = line;
            Column = column;
        }

        public TokenType Type { get; }

        /// <summary>Texto tal cual aparece en el código ("20_000", "'hola'", "if"...).</summary>
        public string Lexeme { get; }

        /// <summary>Valor de un literal: <c>long</c>, <c>double</c> o <c>string</c>; <c>null</c> en otro caso.</summary>
        public object Value { get; }

        /// <summary>Línea (desde 1).</summary>
        public int Line { get; }

        /// <summary>Columna (desde 1).</summary>
        public int Column { get; }

        public override string ToString() =>
            Value != null ? $"{Type}({Lexeme}) @{Line}:{Column}" : $"{Type} @{Line}:{Column}";
    }

    internal static class Keywords
    {
        public static readonly Dictionary<string, TokenType> ByText = new Dictionary<string, TokenType>(StringComparer.Ordinal)
        {
            ["False"] = TokenType.False, ["None"] = TokenType.None, ["True"] = TokenType.True,
            ["and"] = TokenType.And, ["or"] = TokenType.Or, ["not"] = TokenType.Not,
            ["is"] = TokenType.Is, ["in"] = TokenType.In,
            ["if"] = TokenType.If, ["elif"] = TokenType.Elif, ["else"] = TokenType.Else,
            ["while"] = TokenType.While, ["for"] = TokenType.For,
            ["break"] = TokenType.Break, ["continue"] = TokenType.Continue, ["pass"] = TokenType.Pass,
            ["def"] = TokenType.Def, ["return"] = TokenType.Return, ["lambda"] = TokenType.Lambda,
            ["global"] = TokenType.Global, ["nonlocal"] = TokenType.Nonlocal,
            ["import"] = TokenType.Import, ["from"] = TokenType.From, ["as"] = TokenType.As,
            // Reconocidas para poder dar un error claro ("no está soportado") en el parser.
            ["try"] = TokenType.Try, ["except"] = TokenType.Except, ["finally"] = TokenType.Finally,
            ["raise"] = TokenType.Raise, ["class"] = TokenType.Class, ["with"] = TokenType.With,
            ["yield"] = TokenType.Yield, ["del"] = TokenType.Del, ["assert"] = TokenType.Assert,
            ["async"] = TokenType.Async, ["await"] = TokenType.Await,
        };
    }
}
