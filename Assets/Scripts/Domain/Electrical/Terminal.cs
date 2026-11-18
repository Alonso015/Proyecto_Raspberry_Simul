using System;

namespace ImatPiSim.Domain.Electrical
{
    /// <summary>
    /// Punto de conexión de un elemento eléctrico (una patilla de una resistencia, el ánodo
    /// de un LED, un pin de la Raspberry Pi...).
    /// </summary>
    /// <remarks>
    /// Un terminal no sabe con qué está conectado. Al construir la netlist, cada terminal
    /// queda asociado al <see cref="Node"/> que comparte con los terminales unidos a él.
    /// </remarks>
    public sealed class Terminal
    {
        public Terminal(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("El terminal necesita un nombre.", nameof(name));
            Name = name;
        }

        public string Name { get; }

        /// <summary>
        /// Nodo al que pertenece tras <see cref="NetlistBuilder.Build"/>; <c>null</c> antes.
        /// </summary>
        public Node Node { get; internal set; }

        public override string ToString() => Name;
    }
}
