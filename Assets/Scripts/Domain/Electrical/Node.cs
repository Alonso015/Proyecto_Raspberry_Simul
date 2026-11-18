namespace ImatPiSim.Domain.Electrical
{
    /// <summary>
    /// Nodo eléctrico: un conjunto de terminales unidos entre sí (por cables, tiras de la
    /// protoboard o pistas de la placa), que por tanto están a la misma tensión.
    /// </summary>
    /// <remarks>
    /// Los nodos los crea <see cref="NetlistBuilder"/>; el resto del código solo los consulta.
    /// </remarks>
    public sealed class Node
    {
        internal Node(int index, double? fixedVoltage, string name)
        {
            Index = index;
            FixedVoltage = fixedVoltage;
            Name = name;
        }

        /// <summary>Posición del nodo dentro de su <see cref="Netlist"/> (0..N-1).</summary>
        public int Index { get; }

        /// <summary>
        /// Tensión impuesta por un raíl (3,3 V, 5 V, GND) o <c>null</c> si es una incógnita
        /// que calcula el solver.
        /// </summary>
        public double? FixedVoltage { get; }

        /// <summary>Nombre para depuración: el de un terminal o raíl del nodo.</summary>
        public string Name { get; }

        public bool IsFixed => FixedVoltage.HasValue;

        public override string ToString() =>
            IsFixed ? $"Node#{Index}({Name}, {FixedVoltage} V fijos)" : $"Node#{Index}({Name})";
    }
}
