using System.Linq;
using ImatPiSim.Domain.Interpreter;
using ImatPiSim.Domain.Interpreter.Runtime;
using NUnit.Framework;

namespace ImatPiSim.Domain.Tests.Interpreter
{
    /// <summary>Etapa E8: ejecución cooperativa (iteradores), tiempo simulado, Parar y callbacks.</summary>
    public class RuntimeTests
    {
        private const double Dt = 0.1;   // 100 ms por tick en las pruebas

        private static ScriptRuntime Load(string src)
        {
            var rt = new ScriptRuntime();
            rt.Load(src);
            rt.Start();
            return rt;
        }

        /// <summary>Carga, arranca y da ticks hasta que termine (o hasta <paramref name="maxTicks"/>).</summary>
        private static ScriptRuntime Run(string src, int maxTicks = 100)
        {
            var rt = Load(src);
            for (int i = 0; i < maxTicks && rt.IsRunning; i++) rt.Tick(Dt);
            return rt;
        }

        private static string Out(ScriptRuntime rt) => string.Join("\n", rt.Output);

        // ------------------------------------------------------------------ lenguaje

        [Test]
        public void Aritmetica_ComoEnPython()
        {
            var rt = Run("print(2 + 3 * 4, 7 // 2, -7 // 2, 7 % 3, -7 % 3, 2 ** 10, 1 / 2, True + 1)");

            Assert.That(rt.State, Is.EqualTo(RuntimeState.Finished));
            Assert.That(Out(rt), Is.EqualTo("14 3 -4 1 2 1024 0.5 2"));
        }

        [Test]
        public void ConversionATexto_ComoPrint()
        {
            var rt = Run("print(0.1 + 0.2, 2.0, True, None, 'a', [1, 'b', 2.5], (7,), 1e-5)");
            Assert.That(Out(rt), Is.EqualTo("0.30000000000000004 2.0 True None a [1, 'b', 2.5] (7,) 1e-05"));
        }

        [TestCase(0.1, "0.1")]
        [TestCase(0.3 * 3, "0.8999999999999999")]
        [TestCase(2.0, "2.0")]
        [TestCase(-1.5, "-1.5")]
        [TestCase(1e-5, "1e-05")]
        [TestCase(0.0001, "0.0001")]
        [TestCase(123456.789, "123456.789")]
        [TestCase(1e16, "1e+16")]
        [TestCase(1.5e16, "1.5e+16")]
        [TestCase(1e15, "1000000000000000.0")]
        [TestCase(0.0, "0.0")]
        [TestCase(3.3 / 1023, "0.0032258064516129032")]
        public void FormatoDeDecimales_ComoPython_IgualEnUnityYEnDotnet(double value, string expected)
        {
            Assert.That(PyOps.FormatFloat(value), Is.EqualTo(expected));
        }

        [Test]
        public void BitsDeUnContador_Practica3()
        {
            var rt = Run(
                "contador = 6\n" +
                "bits = []\n" +
                "for bit in range(8):\n" +
                "    bits.append((contador >> bit) & 0x1)\n" +
                "print(bits)\n");

            Assert.That(Out(rt), Is.EqualTo("[0, 1, 1, 0, 0, 0, 0, 0]"));
        }

        [Test]
        public void Control_IfElifElse_WhileBreakContinue()
        {
            var rt = Run(
                "def signo(x):\n" +
                "    if x > 0:\n        return 'positivo'\n" +
                "    elif x < 0:\n        return 'negativo'\n" +
                "    else:\n        return 'cero'\n" +
                "print(signo(5), signo(-2), signo(0))\n" +
                "i = 0\n" +
                "pares = []\n" +
                "while True:\n" +
                "    i += 1\n" +
                "    if i > 10:\n        break\n" +
                "    if i % 2:\n        continue\n" +
                "    pares.append(i)\n" +
                "print(pares)\n");

            Assert.That(Out(rt), Is.EqualTo("positivo negativo cero\n[2, 4, 6, 8, 10]"));
        }

        [Test]
        public void Funciones_ValoresPorDefecto_ArgumentosConNombre_YRecursion()
        {
            var rt = Run(
                "def parpadeo(veces=1, duracion=0.3):\n    return veces * duracion\n" +
                "def fact(n):\n    return 1 if n <= 1 else n * fact(n - 1)\n" +
                "print(parpadeo(), parpadeo(3), parpadeo(duracion=0.5, veces=2), fact(10))\n");

            Assert.That(Out(rt), Is.EqualTo("0.3 0.8999999999999999 1.0 3628800"));
        }

        [Test]
        public void Cierres_ConNonlocal_ComoEnElExamen()
        {
            var rt = Run(
                "def main():\n" +
                "    hum = 0.5\n" +
                "    def aum():\n" +
                "        nonlocal hum\n" +
                "        if hum < 1:\n            hum += 0.25\n" +
                "    aum()\n    aum()\n    aum()\n" +
                "    print(hum)\n" +
                "main()\n");

            Assert.That(Out(rt), Is.EqualTo("1.0"));
        }

        [Test]
        public void Global_ModificaLaVariableDelModulo()
        {
            var rt = Run("n = 0\ndef sumar():\n    global n\n    n += 1\nsumar()\nsumar()\nprint(n)\n");
            Assert.That(Out(rt), Is.EqualTo("2"));
        }

        [Test]
        public void SinGlobal_AsignarDentroDeUnaFuncion_EsUnboundLocalError()
        {
            // El error típico: falta 'global contador'.
            var rt = Run("contador = 0\ndef sumar():\n    contador += 1\nsumar()\n");

            Assert.That(rt.State, Is.EqualTo(RuntimeState.Error));
            Assert.That(rt.Error.PythonType, Is.EqualTo("UnboundLocalError"));
            Assert.That(rt.Error.Line, Is.EqualTo(3));
        }

        [Test]
        public void Lambda_ConParametroCapturado_DeLaPractica3()
        {
            var rt = Run(
                "def al_soltar(led):\n    print('soltado', led)\n" +
                "led = 20\n" +
                "cb = lambda led=led: al_soltar(led=led)\n" +
                "led = 99\n" +
                "cb()\n");

            Assert.That(Out(rt), Is.EqualTo("soltado 20"), "el valor por defecto se captura al crear la lambda");
        }

        // ------------------------------------------------------------------ ejecución cooperativa

        [Test]
        public void Sleep_AvanzaConElTiempoSimulado()
        {
            var rt = Load("from time import sleep\nt = 0\nwhile True:\n    t += 1\n    sleep(0.5)\n");

            for (int i = 0; i < 10; i++) rt.Tick(Dt);   // 1,0 s simulado
            // tick 1 (0,1 s): t = 1 y duerme hasta 0,6 s · tick 6 (0,6 s): t = 2 y duerme hasta 1,1 s
            Assert.That(rt.GetGlobal("t"), Is.EqualTo(2L));

            rt.Tick(Dt);                                 // 1,1 s
            Assert.That(rt.GetGlobal("t"), Is.EqualTo(3L));
            Assert.That(rt.State, Is.EqualTo(RuntimeState.Running));
        }

        [Test]
        public void BucleInfinitoSinSleep_NoBloquea_SoloGastaSuPresupuesto()
        {
            var rt = Load("x = 0\nwhile True:\n    x += 1\n");

            rt.Tick(Dt);   // si se colgara, el test no terminaría

            Assert.That(rt.State, Is.EqualTo(RuntimeState.Running));
            Assert.That(rt.StepsLastTick, Is.EqualTo(rt.StepsPerTick));
            long x1 = (long)rt.GetGlobal("x");
            Assert.That(x1, Is.GreaterThan(0).And.LessThan(rt.StepsPerTick));
            rt.Tick(Dt);
            Assert.That((long)rt.GetGlobal("x"), Is.GreaterThan(x1), "y en el siguiente tick sigue por donde iba");
        }

        [Test]
        public void TimeTime_DevuelveElTiempoSimulado()
        {
            var rt = Load("import time\nfrom time import sleep\ninicio = time.time()\nsleep(1)\nduracion = time.time() - inicio\n");
            for (int i = 0; i < 30 && rt.IsRunning; i++) rt.Tick(Dt);

            Assert.That((double)rt.GetGlobal("duracion"), Is.EqualTo(1.0).Within(1e-6));
        }

        // ------------------------------------------------------------------ Parar = Ctrl+C

        private const string PatronDeLasPracticas =
            "from time import sleep\n" +
            "def main():\n" +
            "    while True:\n" +
            "        sleep(1)\n" +
            "if __name__ == '__main__':\n" +
            "    try:\n" +
            "        main()\n" +
            "    except KeyboardInterrupt:\n" +
            "        print('parado')\n" +
            "    finally:\n" +
            "        print('pines liberados')\n";

        [Test]
        public void Parar_LanzaKeyboardInterrupt_YSeEjecutanExceptYFinally()
        {
            var rt = Load(PatronDeLasPracticas);
            rt.Tick(Dt);
            rt.Tick(Dt);

            rt.Stop();
            rt.Tick(Dt);

            Assert.That(rt.Output, Is.EqualTo(new[] { "parado", "pines liberados" }));
            Assert.That(rt.State, Is.EqualTo(RuntimeState.Finished), "el programa atrapa la interrupción y termina bien");
        }

        [Test]
        public void Parar_SinTry_DejaElProgramaDetenido()
        {
            var rt = Load("from signal import pause\npause()\n");
            rt.Tick(Dt);

            rt.Stop();
            rt.Tick(Dt);

            Assert.That(rt.State, Is.EqualTo(RuntimeState.Stopped));
        }

        [Test]
        public void PararDosVeces_TerminaAunqueElProgramaIgnoreLaInterrupcion()
        {
            var rt = Load(
                "while True:\n" +
                "    try:\n" +
                "        x = 1\n" +
                "    except KeyboardInterrupt:\n" +
                "        pass\n");
            rt.Tick(Dt);
            rt.Stop();
            rt.Tick(Dt);
            Assume.That(rt.State, Is.EqualTo(RuntimeState.Running));

            rt.Stop();

            Assert.That(rt.State, Is.EqualTo(RuntimeState.Stopped));
        }

        // ------------------------------------------------------------------ callbacks

        [Test]
        public void CallbackEncolado_SeEjecutaMientrasElPrincipalDuerme()
        {
            var rt = Load(
                "from time import sleep\n" +
                "pulsaciones = 0\n" +
                "def al_pulsar():\n    global pulsaciones\n    pulsaciones += 1\n" +
                "while True:\n    sleep(10)\n");
            rt.Tick(Dt);

            var callback = (IPyCallable)rt.GetGlobal("al_pulsar");
            rt.EnqueueCall(callback);
            rt.Tick(Dt);
            rt.EnqueueCall(callback);
            rt.Tick(Dt);

            Assert.That(rt.GetGlobal("pulsaciones"), Is.EqualTo(2L));
            Assert.That(rt.State, Is.EqualTo(RuntimeState.Running));
        }

        // ------------------------------------------------------------------ excepciones

        [Test]
        public void ErrorDeEjecucion_IndicaTipoYLinea()
        {
            var rt = Run("x = 1\ny = x / 0\nprint('no llega')\n");

            Assert.That(rt.State, Is.EqualTo(RuntimeState.Error));
            Assert.That(rt.Error.PythonType, Is.EqualTo("ZeroDivisionError"));
            Assert.That(rt.Error.Line, Is.EqualTo(2));
            Assert.That(rt.Error.Message, Does.StartWith("Error de ejecución en la línea 2"));
            Assert.That(rt.Output, Is.Empty);
        }

        [TestCase("print(nada)", "NameError", "'nada' no está definido")]
        [TestCase("print('LED ' + 20)", "TypeError", "str(numero)")]
        [TestCase("x = [1, 2]\nprint(x[5])", "IndexError", "fuera de rango")]
        [TestCase("x = 5\nx.on()", "AttributeError", "'int' no tiene el atributo 'on'")]
        [TestCase("f = 3\nf()", "TypeError", "no se puede llamar")]
        [TestCase("def f(a):\n    pass\nf(1, 2)", "TypeError", "admite 1 argumento")]
        [TestCase("import RPi.GPIO as GPIO", "ImportError", "usa gpiozero")]
        [TestCase("import numpy", "ImportError", "no está disponible")]
        [TestCase("def f():\n    return f()\nf()", "RecursionError", "demasiadas llamadas")]
        [TestCase("print(int('hola'))", "ValueError", "no es un número entero")]
        public void ErroresDeEjecucion_ConMensajeClaro(string src, string type, string fragment)
        {
            var rt = Run(src);

            Assert.That(rt.State, Is.EqualTo(RuntimeState.Error));
            Assert.That(rt.Error.PythonType, Is.EqualTo(type));
            Assert.That(rt.Error.Detail, Does.Contain(fragment));
        }

        [Test]
        public void ExceptConAlias_YExceptionCapturaLosErroresNormales()
        {
            var rt = Run(
                "try:\n    x = 1 / 0\nexcept ZeroDivisionError as e:\n    print('capturado:', e)\n" +
                "try:\n    lista = []\n    lista.pop()\nexcept Exception:\n    print('también')\n");

            Assert.That(rt.Output, Is.EqualTo(new[] { "capturado: división entre cero", "también" }));
            Assert.That(rt.State, Is.EqualTo(RuntimeState.Finished));
        }

        [Test]
        public void ExceptDeOtroTipo_NoCaptura_PeroElFinallySeEjecuta()
        {
            var rt = Run("try:\n    x = 1 / 0\nexcept IndexError:\n    print('no')\nfinally:\n    print('finally')\n");

            Assert.That(rt.Output, Is.EqualTo(new[] { "finally" }));
            Assert.That(rt.Error.PythonType, Is.EqualTo("ZeroDivisionError"));
        }

        [Test]
        public void Finally_SeEjecutaAunqueHayaReturn()
        {
            var rt = Run("def f():\n    try:\n        return 1\n    finally:\n        print('limpio')\nprint(f())\n");
            Assert.That(rt.Output, Is.EqualTo(new[] { "limpio", "1" }));
        }

        // ------------------------------------------------------------------ errores al cargar

        [TestCase("return 5", "'return' fuera de una función")]
        [TestCase("break", "fuera de un bucle")]
        [TestCase("nonlocal x", "dentro de una función")]
        [TestCase("try:\n    pass\nexcept Pepito:\n    pass", "desconocido")]
        public void ErroresAlCargar(string src, string fragment)
        {
            var rt = new ScriptRuntime();
            var ex = Assert.Catch<ScriptException>(() => rt.Load(src));
            Assert.That(ex.Detail, Does.Contain(fragment));
        }
    }
}
