using System.Collections.Generic;

namespace ImatPiSim.Domain.Electrical
{
    /// <summary>
    /// Estructura union-find (conjuntos disjuntos) con compresión de caminos y unión por rango.
    /// </summary>
    /// <remarks>
    /// Se usa para agrupar terminales en nodos: si A está unido a B y B a C, los tres acaban
    /// en el mismo conjunto aunque A y C nunca se hayan unido directamente. Cada operación
    /// cuesta casi O(1) amortizado, así que formar los nodos es prácticamente lineal en el
    /// número de conexiones.
    /// </remarks>
    public sealed class DisjointSet<T>
    {
        private readonly Dictionary<T, T> _parent = new Dictionary<T, T>();
        private readonly Dictionary<T, int> _rank = new Dictionary<T, int>();

        /// <summary>Añade <paramref name="item"/> como conjunto propio si aún no existe.</summary>
        public void Add(T item)
        {
            if (_parent.ContainsKey(item)) return;
            _parent[item] = item;
            _rank[item] = 0;
        }

        public bool Contains(T item) => _parent.ContainsKey(item);

        /// <summary>Representante del conjunto de <paramref name="item"/> (lo añade si no existe).</summary>
        public T Find(T item)
        {
            Add(item);
            var root = item;
            while (!EqualityComparer<T>.Default.Equals(_parent[root], root))
                root = _parent[root];

            // Compresión de caminos: todos los recorridos apuntan directamente a la raíz.
            var current = item;
            while (!EqualityComparer<T>.Default.Equals(current, root))
            {
                var next = _parent[current];
                _parent[current] = root;
                current = next;
            }
            return root;
        }

        /// <summary>Une los conjuntos de <paramref name="a"/> y <paramref name="b"/>.</summary>
        /// <returns><c>true</c> si estaban separados.</returns>
        public bool Union(T a, T b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (EqualityComparer<T>.Default.Equals(ra, rb)) return false;

            // Unión por rango: el árbol más bajo cuelga del más alto, para mantenerlos planos.
            if (_rank[ra] < _rank[rb]) (ra, rb) = (rb, ra);
            _parent[rb] = ra;
            if (_rank[ra] == _rank[rb]) _rank[ra]++;
            return true;
        }

        public bool AreConnected(T a, T b) =>
            EqualityComparer<T>.Default.Equals(Find(a), Find(b));

        /// <summary>Todos los elementos registrados.</summary>
        public IEnumerable<T> Items => _parent.Keys;
    }
}
