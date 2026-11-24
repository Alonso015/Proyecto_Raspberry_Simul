using ImatPiSim.Domain.Interpreter;
using NUnit.Framework;

namespace ImatPiSim.Domain.Tests.Interpreter
{
    /// <summary>Etapa E15: utilidades de texto del editor de código (líneas y sangría automática).</summary>
    public class SourceTextTests
    {
        [TestCase("", 1)]
        [TestCase("a = 1", 1)]
        [TestCase("a = 1\nb = 2", 2)]
        [TestCase("a = 1\n", 2)]
        public void CountLines(string text, int expected) =>
            Assert.That(SourceText.CountLines(text), Is.EqualTo(expected));

        [Test]
        public void LineAt_DaLaLineaDeCadaCaracter()
        {
            const string text = "ab\ncd\n\nef";
            Assert.That(SourceText.LineAt(text, 0), Is.EqualTo(1));
            Assert.That(SourceText.LineAt(text, 2), Is.EqualTo(1), "el propio \\n pertenece a la línea 1");
            Assert.That(SourceText.LineAt(text, 3), Is.EqualTo(2));
            Assert.That(SourceText.LineAt(text, 7), Is.EqualTo(4));
            Assert.That(SourceText.LineAt(text, 999), Is.EqualTo(4), "fuera de rango → última línea");
        }

        [Test]
        public void LineBefore_EsElTrozoDeLineaAntesDelCursor()
        {
            const string text = "def main():\n    led.on()";
            Assert.That(SourceText.LineBefore(text, 11), Is.EqualTo("def main():"));
            Assert.That(SourceText.LineBefore(text, text.Length), Is.EqualTo("    led.on()"));
            Assert.That(SourceText.LineBefore(text, 16), Is.EqualTo("    "));
            Assert.That(SourceText.LineBefore(text, 0), Is.EqualTo(""));
        }

        [TestCase("led.on()", "")]
        [TestCase("    led.on()", "    ")]
        [TestCase("def main() -> None:", "    ")]
        [TestCase("    while True:", "        ")]
        [TestCase("    if x:   # comentario", "        ")]
        [TestCase("    print('a:')", "    ", Description = "los dos puntos dentro de una cadena no abren bloque")]
        [TestCase("    x = 1  # ojo:", "    ", Description = "ni dentro de un comentario")]
        [TestCase("        break", "    ")]
        [TestCase("    return x + 1", "")]
        [TestCase("    pass", "")]
        [TestCase("    breakpoint = 3", "    ", Description = "una variable que empieza como la palabra clave no cuenta")]
        [TestCase("\tif x:", "\t    ")]
        [TestCase("\t\tbreak", "\t")]
        [TestCase("", "")]
        public void IndentForNewLine_SigueLasReglasDeIdle(string previous, string expected) =>
            Assert.That(SourceText.IndentForNewLine(previous), Is.EqualTo(expected));
    }
}
