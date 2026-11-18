using System.Collections.Generic;

namespace ImatPiSim.Domain.Electrical
{
    /// <summary>
    /// Cualquier cosa que participa en el circuito: resistencias, LEDs, pines GPIO, raíles...
    /// </summary>
    /// <remarks>
    /// El solver no sabe qué es cada elemento: le pide que "estampe" su contribución a la
    /// matriz (<see cref="Stamp"/>) y, tras resolver, le avisa para que calcule su propio
    /// estado a partir de las tensiones (<see cref="OnSolved"/>).
    /// </remarks>
    public interface IElectricalElement
    {
        IReadOnlyList<Terminal> Terminals { get; }

        /// <summary>Añade la contribución del elemento al sistema G·V = I.</summary>
        void Stamp(IStampContext ctx);

        /// <summary>Recibe la solución para actualizar corrientes y estado internos.</summary>
        void OnSolved(ISolution solution);
    }

    /// <summary>
    /// Elemento cuyo modelo depende del punto de trabajo (p. ej. un diodo, que conduce o no
    /// según la tensión). Se modela como lineal a trozos: en cada "región" es lineal.
    /// </summary>
    /// <remarks>
    /// El solver supone una región para cada elemento, resuelve y pregunta si la suposición
    /// era coherente. Si alguno cambia de región, vuelve a estampar y resolver.
    /// </remarks>
    public interface INonlinearElement : IElectricalElement
    {
        /// <summary>
        /// Comprueba la región supuesta frente a la solución y la corrige si hace falta.
        /// </summary>
        /// <returns><c>true</c> si ha cambiado de región (hay que volver a resolver).</returns>
        bool UpdateRegion(ISolution solution);
    }

    /// <summary>
    /// Lo que un elemento puede escribir en el sistema de ecuaciones nodales.
    /// </summary>
    public interface IStampContext
    {
        /// <summary>
        /// Conductancia <paramref name="g"/> (en siemens, 1/Ω) entre los nodos
        /// <paramref name="a"/> y <paramref name="b"/>.
        /// </summary>
        void AddConductance(Node a, Node b, double g);

        /// <summary>
        /// Conductancia <paramref name="g"/> entre el nodo <paramref name="a"/> y una fuente de
        /// tensión ideal interna de <paramref name="voltage"/> voltios (sin nodo propio).
        /// </summary>
        /// <remarks>
        /// Sirve para el equivalente de Norton de una salida GPIO (fuente + resistencia interna)
        /// y para las resistencias de pull-up / pull-down internas del chip.
        /// </remarks>
        void AddConductanceTo(Node a, double g, double voltage);

        /// <summary>
        /// Fuente de corriente de <paramref name="amps"/> amperios que entra en el nodo
        /// <paramref name="into"/> (negativa si sale).
        /// </summary>
        void AddCurrentSource(Node into, double amps);
    }

    /// <summary>Resultado de resolver un circuito.</summary>
    public interface ISolution
    {
        /// <summary>Tensión del nodo respecto a GND (0 V).</summary>
        double VoltageAt(Node node);
    }

    /// <summary>
    /// Elemento que impone una tensión fija a su terminal (raíles de 3,3 V, 5 V y GND).
    /// </summary>
    public interface IVoltageReference
    {
        Terminal Terminal { get; }
        double Voltage { get; }
    }
}
