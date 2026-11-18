using System;
using System.Collections.Generic;

namespace ImatPiSim.Domain.Components
{
    /// <summary>Crea componentes a partir del identificador de su tipo (patrón <b>Factory</b>).</summary>
    public interface IComponentFactory
    {
        /// <param name="typeId">Id del tipo, p. ej. "resistor".</param>
        /// <param name="id">Id de la instancia, p. ej. "R7".</param>
        /// <param name="parameters">Parámetros que sustituyen a los por defecto del tipo.</param>
        Component Create(string typeId, string id, IReadOnlyDictionary<string, double> parameters = null);

        bool CanCreate(string typeId);
    }

    /// <summary>
    /// Fábrica con un registro de creadores por tipo. Quien pide un componente (la paleta de
    /// la UI, la definición de un reto) solo conoce el id del tipo, no la clase concreta, y
    /// añadir un componente nuevo es registrar un creador más.
    /// </summary>
    public sealed class ComponentFactory : IComponentFactory
    {
        /// <summary>Recibe el tipo, el id y los parámetros ya combinados con los por defecto.</summary>
        public delegate Component Creator(ComponentType type, string id, IReadOnlyDictionary<string, double> parameters);

        private readonly Dictionary<string, (ComponentType type, Creator creator)> _registry =
            new Dictionary<string, (ComponentType, Creator)>();

        public void Register(ComponentType type, Creator creator)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            _registry[type.Id] = (type, creator ?? throw new ArgumentNullException(nameof(creator)));
        }

        public bool CanCreate(string typeId) => typeId != null && _registry.ContainsKey(typeId);

        public IEnumerable<ComponentType> RegisteredTypes
        {
            get { foreach (var entry in _registry.Values) yield return entry.type; }
        }

        public Component Create(string typeId, string id, IReadOnlyDictionary<string, double> parameters = null)
        {
            if (!CanCreate(typeId))
                throw new ArgumentException($"Tipo de componente desconocido: '{typeId}'.", nameof(typeId));
            var (type, creator) = _registry[typeId];

            var merged = new Dictionary<string, double>();
            foreach (var kv in type.DefaultParameters) merged[kv.Key] = kv.Value;
            if (parameters != null)
                foreach (var kv in parameters)
                {
                    if (!type.DefaultParameters.ContainsKey(kv.Key))
                        throw new ArgumentException($"El tipo '{typeId}' no tiene el parámetro '{kv.Key}'.", nameof(parameters));
                    merged[kv.Key] = kv.Value;
                }
            return creator(type, id, merged);
        }

        /// <summary>Fábrica con los componentes sueltos que el jugador puede colocar.</summary>
        public static ComponentFactory CreateDefault()
        {
            var f = new ComponentFactory();
            f.Register(ComponentTypes.Resistor, (t, id, p) => new Resistor(id, p["resistance"]));
            f.Register(ComponentTypes.Led, (t, id, p) =>
                new Led(id, p["forwardVoltage"], p["seriesResistance"], p["maxCurrent"]));
            f.Register(ComponentTypes.PushButton, (t, id, p) => new PushButton(id));
            f.Register(ComponentTypes.Potentiometer, (t, id, p) =>
                new Potentiometer(id, p["resistance"], p["position"]));
            f.Register(ComponentTypes.Ldr, (t, id, p) => new Ldr(id, p["illuminance"]));
            return f;
        }
    }
}
