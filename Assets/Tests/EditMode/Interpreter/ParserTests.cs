using System.Linq;
using ImatPiSim.Domain.Interpreter;
using ImatPiSim.Domain.Interpreter.Ast;
using NUnit.Framework;

namespace ImatPiSim.Domain.Tests.Interpreter
{
    /// <summary>Etapa E7: parser de descenso recursivo, AST (Composite) e impresión (Visitor).</summary>
    public class ParserTests
    {
        private static string E(string expression) => AstPrinter.Print(Parser.ParseExpression(expression));

        private static string P(string program) => AstPrinter.Print(Parser.Parse(program));

        private static ParseException Fails(string program) =>
            Assert.Throws<ParseException>(() => Parser.Parse(program));

        // ------------------------------------------------------------------ precedencia

        [TestCase("2 + 3 * 4", "(+ 2 (* 3 4))")]
        [TestCase("(2 + 3) * 4", "(* (+ 2 3) 4)")]
        [TestCase("10 - 4 - 3", "(- (- 10 4) 3)")]                 // izquierda a derecha
        [TestCase("2 ** 3 ** 2", "(** 2 (** 3 2))")]               // ** asocia por la derecha
        [TestCase("-2 ** 2", "(- (** 2 2))")]                      // como en Python: −(2²) = −4
        [TestCase("1 + 2 << 3", "(<< (+ 1 2) 3)")]                 // + antes que <<
        [TestCase("a & b | c ^ d", "(| (& a b) (^ c d))")]         // & > ^ > |
        [TestCase("(contador >> bit) & 0x1", "(& (>> contador bit) 1)")]   // práctica 3
        [TestCase("not a and b or c", "(or (and (not a) b) c)")]
        [TestCase("x < 5 and y", "(and (cmp x < 5) y)")]
        [TestCase("0 <= v < 1023", "(cmp 0 <= v < 1023)")]       // comparación encadenada
        [TestCase("x not in leds", "(cmp x not-in leds)")]
        [TestCase("a is not None", "(cmp a is-not None)")]
        [TestCase("1 if ok else 0", "(if-expr ok 1 0)")]
        public void Precedencia_YAsociatividad(string expression, string expected)
        {
            Assert.That(E(expression), Is.EqualTo(expected));
        }

        // ------------------------------------------------------------------ expresiones

        [Test]
        public void LlamadasEncadenadas_AtributosEIndices()
        {
            Assert.That(E("GPIO.input(7)"), Is.EqualTo("(call (. GPIO input) 7)"));
            Assert.That(E("leds[i].on()"), Is.EqualTo("(call (. ([] leds i) on))"));
            Assert.That(E("Button(16, pull_up=True)"), Is.EqualTo("(call Button 16 pull_up=True)"));
            Assert.That(E("print('a' 'b')"), Is.EqualTo("(call print 'ab')"));
        }

        [Test]
        public void LambdaConParametroCapturado_DeLaPractica3()
        {
            Assert.That(E("lambda led=led: al_soltar(led=led)"),
                Is.EqualTo("(lambda (led=led) (call al_soltar led=led))"));
            Assert.That(E("lambda: None"), Is.EqualTo("(lambda () None)"));
        }

        [Test]
        public void ListasYTuplas()
        {
            Assert.That(E("[20, 21, 22]"), Is.EqualTo("(list 20 21 22)"));
            Assert.That(E("(7, 19)"), Is.EqualTo("(tuple 7 19)"));
            Assert.That(E("(7,)"), Is.EqualTo("(tuple 7)"));
            Assert.That(E("()"), Is.EqualTo("(tuple)"));
            Assert.That(E("(7)"), Is.EqualTo("7"), "un paréntesis sin coma solo agrupa");
        }

        // ------------------------------------------------------------------ sentencias

        [Test]
        public void IfElifElse_ElElifEsUnIfDentroDelElse()
        {
            var module = Parser.Parse("if x > 0:\n    a = 1\nelif x < 0:\n    a = -1\nelse:\n    a = 0\n");

            var ifStmt = (IfStmt)module.Body.Single();
            Assert.That(ifStmt.ElseBody.Single(), Is.TypeOf<IfStmt>());
            var elif = (IfStmt)ifStmt.ElseBody[0];
            Assert.That(elif.ElseBody.Single(), Is.TypeOf<AssignStmt>());
            Assert.That(ifStmt.Line, Is.EqualTo(1));
            Assert.That(elif.Line, Is.EqualTo(3));
        }

        [Test]
        public void BuclesFor_YWhile_ConBreak()
        {
            Assert.That(P("for pin in range(20, 28):\n    leds.append(LED(pin))\n"),
                Is.EqualTo("for pin in (call range 20 28):\n  (call (. leds append) (call LED pin))"));
            Assert.That(P("while True:\n    if puls_fin.is_pressed:\n        break\n"),
                Is.EqualTo("while True:\n  if (. puls_fin is_pressed):\n    break"));
        }

        [Test]
        public void DefinicionDeFuncion_ConValoresPorDefecto_YReturn()
        {
            Assert.That(P("def parpadear(led, veces=1, t=0.3):\n    return veces * t\n"),
                Is.EqualTo("def parpadear(led veces=1 t=0.3):\n  return (* veces t)"));
        }

        [Test]
        public void Asignaciones_Simples_Multiples_YCompuestas()
        {
            Assert.That(P("a = b = 0\ncontador += 1\nled.value = 0.5\nbits[0] = 1\n"),
                Is.EqualTo("a = b = 0\ncontador += 1\n(. led value) = 0.5\n([] bits 0) = 1"));
        }

        [Test]
        public void CallbacksDeGpiozero_SeAsignanSinLlamar()
        {
            // "sin (), no es llamarla, es asignarla" (Tema 3)
            var stmt = (AssignStmt)Parser.Parse("puls_led.when_pressed = encender\n").Body.Single();

            Assert.That(stmt.Targets.Single(), Is.TypeOf<AttributeExpr>());
            Assert.That(stmt.Value, Is.TypeOf<NameExpr>());
        }

        [Test]
        public void Imports()
        {
            Assert.That(P("from gpiozero import LED, Button\nfrom time import sleep as espera\nimport math\n"),
                Is.EqualTo("from gpiozero import LED, Button\nfrom time import sleep as espera\nimport math"));
        }

        [Test]
        public void SentenciasEnUnaLinea_ConPuntoYComa_YTrasLosDosPuntos()
        {
            Assert.That(P("a = 1; b = 2\nif a: b = 3\n"), Is.EqualTo("a = 1\nb = 2\nif a:\n  b = 3"));
        }

        [Test]
        public void GlobalYNonlocal()
        {
            Assert.That(P("def f():\n    global contador\n    nonlocal x, y\n"),
                Is.EqualTo("def f():\n  global contador\n  nonlocal x, y"));
        }

        [Test]
        public void TryExceptFinally_ElPatronDeLasPracticas()
        {
            const string src =
                "if __name__ == \"__main__\":\n" +
                "    try:\n" +
                "        main()\n" +
                "    except KeyboardInterrupt:\n" +
                "        pass\n" +
                "    finally:\n" +
                "        GPIO.cleanup()\n";

            Assert.That(P(src), Is.EqualTo(
                "if (cmp __name__ == '__main__'):\n" +
                "  try:\n" +
                "    (call main)\n" +
                "  except KeyboardInterrupt:\n" +
                "    pass\n" +
                "  finally:\n" +
                "    (call (. GPIO cleanup))"));

            var tryStmt = (TryStmt)((IfStmt)Parser.Parse(src).Body.Single()).Body.Single();
            Assert.That(tryStmt.Handlers.Single().TypeName, Is.EqualTo("KeyboardInterrupt"));
            Assert.That(tryStmt.FinallyBody, Has.Count.EqualTo(1));
        }

        [Test]
        public void Try_VariosExcept_ConAlias_YSinTipoAlFinal()
        {
            var t = (TryStmt)Parser.Parse(
                "try:\n    x = 1 / d\nexcept ZeroDivisionError as e:\n    print(e)\nexcept:\n    pass\n").Body.Single();

            Assert.That(t.Handlers.Select(h => h.TypeName), Is.EqualTo(new[] { "ZeroDivisionError", null }));
            Assert.That(t.Handlers[0].Alias, Is.EqualTo("e"));
            Assert.That(t.FinallyBody, Is.Empty);
        }

        [Test]
        public void TrySoloConFinally()
        {
            Assert.That(P("try:\n    main()\nfinally:\n    GPIO.cleanup()\n"),
                Is.EqualTo("try:\n  (call main)\nfinally:\n  (call (. GPIO cleanup))"));
        }

        // ------------------------------------------------------------------ programa completo

        [Test]
        public void ProgramaDeLaPractica3_LedMientrasPulso()
        {
            const string src =
                "from gpiozero import LED, Button\n" +
                "\n" +
                "def main() -> None:\n" +
                "    puls_led = Button(7)\n" +
                "    puls_fin = Button(19)\n" +
                "    led_0 = LED(20)\n" +
                "    while True:\n" +
                "        if puls_led.is_pressed == True:\n" +
                "            led_0.on()\n" +
                "        else:\n" +
                "            led_0.off()\n" +
                "        if puls_fin.is_pressed == True:\n" +
                "            break\n" +
                "\n" +
                "if __name__ == \"__main__\":\n" +
                "    main()\n";

            const string expected =
                "from gpiozero import LED, Button\n" +
                "def main():\n" +
                "  puls_led = (call Button 7)\n" +
                "  puls_fin = (call Button 19)\n" +
                "  led_0 = (call LED 20)\n" +
                "  while True:\n" +
                "    if (cmp (. puls_led is_pressed) == True):\n" +
                "      (call (. led_0 on))\n" +
                "    else:\n" +
                "      (call (. led_0 off))\n" +
                "    if (cmp (. puls_fin is_pressed) == True):\n" +
                "      break\n" +
                "if (cmp __name__ == '__main__'):\n" +
                "  (call main)";
            Assert.That(P(src), Is.EqualTo(expected));
        }

        // ------------------------------------------------------------------ errores

        [TestCase("if x\n    y = 1\n", 1, "':'")]
        [TestCase("if x:\ny = 1\n", 2, "bloque sangrado")]
        [TestCase("x = (1 + \n", 1, "")]                           // paréntesis sin cerrar: error del lexer
        [TestCase("led = \n", 1, "sin completar")]
        [TestCase("1 = x\n", 1, "no se puede asignar")]
        [TestCase("a, b = 1, 2\n", 1, "varias variables")]
        [TestCase("f(x=1, 2)\n", 1, "sin nombre")]
        [TestCase("def f(a=1, b):\n    pass\n", 1, "valor por defecto")]
        [TestCase("raise ValueError()\n", 1, "raise")]
        [TestCase("try:\n    main()\n", 1, "al menos un 'except' o un 'finally'")]   // señala el 'try' incompleto
        [TestCase("try:\n    x()\nexcept:\n    pass\nexcept ValueError:\n    pass\n", 5, "sin tipo tiene que ser el último")]
        [TestCase("try:\n    x()\nexcept (A, B):\n    pass\n", 3, "varios tipos")]
        [TestCase("try:\n    x()\nexcept A:\n    pass\nelse:\n    pass\n", 5, "else")]
        [TestCase("except ValueError:\n    pass\n", 1, "sin un 'try'")]
        [TestCase("class Led:\n    pass\n", 1, "clases")]
        [TestCase("bits = [b for b in range(8)]\n", 1, "comprensión")]
        [TestCase("d = {1: 2}\n", 1, "diccionarios")]
        [TestCase("from gpiozero import *\n", 1, "import *")]
        [TestCase("def if():\n    pass\n", 1, "palabra reservada")]
        [TestCase("x = 1 2\n", 1, "final de la línea")]
        public void Errores_ConLineaYMensajeClaro(string src, int line, string fragment)
        {
            var ex = Assert.Catch<ScriptException>(() => Parser.Parse(src));
            Assert.That(ex.Line, Is.EqualTo(line));
            Assert.That(ex.Detail, Does.Contain(fragment));
            TestContext.WriteLine(ex.Message);
        }

        [Test]
        public void Error_MuestraLineaYColumnaEnElMensaje()
        {
            var ex = Fails("x = 1\nif x\n    y = 2\n");
            Assert.That(ex.Message, Does.StartWith("Error de sintaxis en la línea 2, columna 5"));
        }
    }
}
