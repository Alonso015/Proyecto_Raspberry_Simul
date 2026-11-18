using System.Linq;
using ImatPiSim.Domain.Interpreter;
using NUnit.Framework;
using T = ImatPiSim.Domain.Interpreter.TokenType;

namespace ImatPiSim.Domain.Tests.Interpreter
{
    /// <summary>Etapa E6: analizador léxico del subconjunto de Python.</summary>
    public class LexerTests
    {
        private static TokenType[] Types(string src) => Lexer.Tokenize(src).Select(t => t.Type).ToArray();

        private static LexerException Fails(string src) => Assert.Throws<LexerException>(() => Lexer.Tokenize(src));

        // ------------------------------------------------------------------ básico

        [Test]
        public void Asignacion_ConLlamada()
        {
            var tokens = Lexer.Tokenize("led = LED(20)\n");

            Assert.That(tokens.Select(t => t.Type), Is.EqualTo(new[]
                { T.Identifier, T.Assign, T.Identifier, T.LeftParen, T.Integer, T.RightParen, T.Newline, T.EndOfFile }));
            Assert.That(tokens[0].Lexeme, Is.EqualTo("led"));
            Assert.That(tokens[4].Value, Is.EqualTo(20L));
        }

        [Test]
        public void PalabrasClave_YNombresQueSeLesParecen()
        {
            Assert.That(Types("if_ = True"), Is.EqualTo(new[] { T.Identifier, T.Assign, T.True, T.Newline, T.EndOfFile }));
            Assert.That(Types("not x and y or None"), Is.EqualTo(new[]
                { T.Not, T.Identifier, T.And, T.Identifier, T.Or, T.None, T.Newline, T.EndOfFile }));
        }

        [Test]
        public void SinSaltoFinal_SeAnadeNewlineYEof()
        {
            Assert.That(Types("x = 1"), Is.EqualTo(new[] { T.Identifier, T.Assign, T.Integer, T.Newline, T.EndOfFile }));
            Assert.That(Types(""), Is.EqualTo(new[] { T.EndOfFile }));
        }

        [Test]
        public void PosicionDeCadaToken_LineaYColumna()
        {
            var t = Lexer.Tokenize("a = 1\nbb  = 22\n");
            var bb = t.First(x => x.Lexeme == "bb");
            var n22 = t.First(x => x.Lexeme == "22");
            Assert.That((bb.Line, bb.Column), Is.EqualTo((2, 1)));
            Assert.That((n22.Line, n22.Column), Is.EqualTo((2, 7)));
        }

        // ------------------------------------------------------------------ sangría

        [Test]
        public void BucleDeScan_ConIfElseAnidado_GeneraIndentYDedent()
        {
            // Fragmento del ejemplo RPi.GPIO del Tema 3.
            const string src =
                "while True:\n" +
                "    if GPIO.input(7) == GPIO.LOW:\n" +
                "        GPIO.output(20, GPIO.HIGH)\n" +
                "    else:\n" +
                "        GPIO.output(20, GPIO.LOW)\n";
            var structure = Types(src).Where(t => t == T.Indent || t == T.Dedent || t == T.Newline || t == T.While
                                               || t == T.If || t == T.Else || t == T.EndOfFile).ToArray();

            Assert.That(structure, Is.EqualTo(new[]
            {
                T.While, T.Newline,
                T.Indent, T.If, T.Newline,
                T.Indent, T.Newline,            // GPIO.output(20, GPIO.HIGH)
                T.Dedent, T.Else, T.Newline,
                T.Indent, T.Newline,            // GPIO.output(20, GPIO.LOW)
                T.Dedent, T.Dedent, T.EndOfFile   // se cierran los dos niveles al final
            }));
        }

        [Test]
        public void LineasEnBlancoYComentarios_NoCuentanParaLaSangria()
        {
            const string src =
                "if x:\n" +
                "    y = 1\n" +
                "\n" +
                "        # comentario con otra sangría\n" +
                "    z = 2  # comentario al final\n";
            var types = Types(src);

            Assert.That(types.Count(t => t == T.Indent), Is.EqualTo(1));
            Assert.That(types.Count(t => t == T.Dedent), Is.EqualTo(1));
            Assert.That(types.Count(t => t == T.Newline), Is.EqualTo(3));
        }

        [Test]
        public void Tabuladores_FuncionanComoSangria()
        {
            Assert.That(Types("if x:\n\ty = 1\n").Count(t => t == T.Indent), Is.EqualTo(1));
        }

        [Test]
        public void FinalesDeLineaDeWindows_SeAceptan()
        {
            Assert.That(Types("if x:\r\n    y = 1\r\n"), Is.EqualTo(Types("if x:\n    y = 1\n")));
        }

        [Test]
        public void DentroDeParentesis_LosSaltosNoTerminanLaSentencia()
        {
            const string src = "leds = LEDBoard(20, 21,\n                22, 23)\nx = 1\n";
            var types = Types(src);

            Assert.That(types.Count(t => t == T.Newline), Is.EqualTo(2));
            Assert.That(types.Count(t => t == T.Indent), Is.EqualTo(0));
        }

        [Test]
        public void BarraInvertidaAlFinal_UneLineas()
        {
            Assert.That(Types("x = 1 + \\\n    2\n").Count(t => t == T.Newline), Is.EqualTo(1));
        }

        // ------------------------------------------------------------------ números

        [TestCase("20_000", 20000L)]   // como en la práctica 4: hz=20_000
        [TestCase("0x1F", 31L)]
        [TestCase("0b101", 5L)]
        [TestCase("0o17", 15L)]
        [TestCase("0", 0L)]
        [TestCase("000", 0L)]
        public void Enteros(string text, long value)
        {
            var t = Lexer.Tokenize(text)[0];
            Assert.That(t.Type, Is.EqualTo(T.Integer));
            Assert.That(t.Value, Is.EqualTo(value));
        }

        [TestCase("2.5", 2.5)]
        [TestCase(".5", 0.5)]
        [TestCase("1e-3", 0.001)]
        [TestCase("0.0005", 0.0005)]   // MinPulse del servo
        [TestCase("1_000.25", 1000.25)]
        [TestCase("3.", 3.0)]
        public void Decimales(string text, double value)
        {
            var t = Lexer.Tokenize(text)[0];
            Assert.That(t.Type, Is.EqualTo(T.Float));
            Assert.That(t.Value, Is.EqualTo(value));
        }

        // ------------------------------------------------------------------ cadenas

        [Test]
        public void Cadenas_ConComillasSimplesODobles_YEscapes()
        {
            var t = Lexer.Tokenize("print('hola', \"it's\", 'a\\nb', r'a\\nb')");
            var strings = t.Where(x => x.Type == T.String).Select(x => (string)x.Value).ToArray();

            Assert.That(strings, Is.EqualTo(new[] { "hola", "it's", "a\nb", "a\\nb" }));
        }

        [Test]
        public void CadenaTriple_PuedeOcuparVariasLineas()
        {
            var t = Lexer.Tokenize("\"\"\"Programa\nde prueba\"\"\"\nx = 1\n");

            Assert.That(t[0].Type, Is.EqualTo(T.String));
            Assert.That(t[0].Value, Is.EqualTo("Programa\nde prueba"));
            Assert.That(t.First(x => x.Lexeme == "x").Line, Is.EqualTo(3));
        }

        // ------------------------------------------------------------------ operadores

        [Test]
        public void Operadores_SeTomaElMasLargoPosible()
        {
            Assert.That(Types("a <<= b ** c // d != e >= f"), Is.EqualTo(new[]
            {
                T.Identifier, T.LeftShiftAssign, T.Identifier, T.DoubleStar, T.Identifier, T.DoubleSlash,
                T.Identifier, T.NotEqual, T.Identifier, T.GreaterEqual, T.Identifier, T.Newline, T.EndOfFile
            }));
        }

        [Test]
        public void ExpresionDeBitsDeLaPractica3()
        {
            var types = Types("bits = [(contador >> bit) & 0x1 for bit in range(8)]");

            Assert.That(types, Is.EqualTo(new[]
            {
                T.Identifier, T.Assign, T.LeftBracket, T.LeftParen, T.Identifier, T.RightShift, T.Identifier, T.RightParen,
                T.Ampersand, T.Integer, T.For, T.Identifier, T.In, T.Identifier, T.LeftParen, T.Integer, T.RightParen,
                T.RightBracket, T.Newline, T.EndOfFile
            }));
        }

        [Test]
        public void LambdaDeLaPractica3()
        {
            var types = Types("button.when_released = lambda led=led: al_soltar(led=led)");

            Assert.That(types.Take(6), Is.EqualTo(new[] { T.Identifier, T.Dot, T.Identifier, T.Assign, T.Lambda, T.Identifier }));
            Assert.That(types, Does.Contain(T.Colon));
        }

        // ------------------------------------------------------------------ errores

        [Test]
        public void Error_DedentQueNoCoincideConNingunNivel()
        {
            var ex = Fails("if x:\n        y = 1\n    z = 2\n");
            Assert.That(ex.Line, Is.EqualTo(3));
            Assert.That(ex.Detail, Does.Contain("sangría"));
        }

        [Test]
        public void Error_SangriaEnLaPrimeraLinea()
        {
            var ex = Fails("    x = 1\n");
            Assert.That(ex.Detail, Does.Contain("sangría inesperada"));
        }

        [Test]
        public void Error_CadenaSinCerrar_IndicaDondeEmpieza()
        {
            var ex = Fails("x = 1\nprint('hola)\n");
            Assert.That((ex.Line, ex.Column), Is.EqualTo((2, 7)));
        }

        [Test]
        public void Error_ParentesisSinCerrar_IndicaElDeApertura()
        {
            var ex = Fails("led = LED(20\nx = 1\n");
            Assert.That((ex.Line, ex.Column), Is.EqualTo((1, 10)));
            Assert.That(ex.Detail, Does.Contain("'('"));
        }

        [TestCase("x = 3)", "sin su '('")]
        [TestCase("x = [1, 2)", "no cierra")]
        [TestCase("x = 5 $ 2", "carácter no válido '$'")]
        [TestCase("if !x:", "'not'")]
        [TestCase("print(f'{x}')", "f-strings")]
        [TestCase("x = 007", "ceros a la izquierda")]
        [TestCase("x = 1__0", "'_'")]
        [TestCase("x = 3abc", "número mal formado")]
        [TestCase("x = 0x", "faltan dígitos")]
        public void Errores_ConMensajeClaro(string src, string fragment)
        {
            var ex = Fails(src);
            Assert.That(ex.Detail, Does.Contain(fragment));
            Assert.That(ex.Message, Does.StartWith("Línea 1, columna"));
        }
    }
}
