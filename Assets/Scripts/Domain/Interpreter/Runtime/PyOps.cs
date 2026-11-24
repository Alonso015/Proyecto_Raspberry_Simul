using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ImatPiSim.Domain.Interpreter.Ast;

namespace ImatPiSim.Domain.Interpreter.Runtime
{
    /// <summary>
    /// Semántica de los operadores y conversiones de Python sobre los valores del intérprete.
    /// </summary>
    /// <remarks>
    /// Se replican los detalles que un estudiante se encuentra en las prácticas: <c>/</c> siempre
    /// da decimal; <c>//</c> y <c>%</c> redondean hacia −∞ (−7 // 2 = −4); <c>True</c> vale 1 en
    /// aritmética; los operadores de bits solo admiten enteros.
    /// </remarks>
    public static class PyOps
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ------------------------------------------------------------------ tipos y verdad

        public static string TypeName(object v)
        {
            switch (v)
            {
                case null: return "NoneType";
                case bool _: return "bool";
                case long _: return "int";
                case double _: return "float";
                case string _: return "str";
                case PyTuple _: return "tuple";
                case PyRange _: return "range";
                case IPyObject o: return o.TypeName;
                case IPyCallable _: return "function";
                default: return v.GetType().Name;
            }
        }

        /// <summary>Valor de verdad de Python: 0, 0.0, '', [], () y None son falsos.</summary>
        public static bool IsTruthy(object v)
        {
            switch (v)
            {
                case null: return false;
                case bool b: return b;
                case long l: return l != 0;
                case double d: return d != 0;
                case string s: return s.Length > 0;
                case PyList l: return l.Items.Count > 0;
                case PyTuple t: return t.Items.Count > 0;
                case PyRange r: return r.Count > 0;
                default: return true;
            }
        }

        private static bool IsInt(object v) => v is long || v is bool;
        private static bool IsNumber(object v) => v is long || v is bool || v is double;
        private static long AsLong(object v) => v is bool b ? (b ? 1L : 0L) : (long)v;
        private static double AsDouble(object v) => v is double d ? d : AsLong(v);

        public static double ToDouble(object v, string what = "el valor")
        {
            if (IsNumber(v)) return AsDouble(v);
            throw PyException.TypeError($"{what} debe ser un número, no {TypeName(v)}");
        }

        public static long ToLong(object v, string what = "el valor")
        {
            if (IsInt(v)) return AsLong(v);
            throw PyException.TypeError($"{what} debe ser un entero, no {TypeName(v)}");
        }

        // ------------------------------------------------------------------ operadores

        public static object Unary(TokenType op, object v)
        {
            switch (op)
            {
                case TokenType.Not: return !IsTruthy(v);
                case TokenType.Minus:
                    if (v is double d) return -d;
                    if (IsInt(v)) return Checked(() => -AsLong(v));
                    break;
                case TokenType.Plus:
                    if (v is double) return v;
                    if (IsInt(v)) return AsLong(v);
                    break;
                case TokenType.Tilde:
                    if (IsInt(v)) return ~AsLong(v);
                    break;
            }
            throw PyException.TypeError($"operador {Symbol(op)} no válido para {TypeName(v)}");
        }

        public static object Binary(TokenType op, object a, object b)
        {
            // Concatenación y repetición de cadenas y listas
            if (op == TokenType.Plus)
            {
                if (a is string sa && b is string sb) return sa + sb;
                if (a is PyList la && b is PyList lb) return new PyList(la.Items.Concat(lb.Items));
                if (a is PyTuple ta && b is PyTuple tb) return new PyTuple(ta.Items.Concat(tb.Items).ToArray());
            }
            if (op == TokenType.Star)
            {
                if (a is string s && IsInt(b)) return Repeat(s, AsLong(b));
                if (IsInt(a) && b is string s2) return Repeat(s2, AsLong(a));
                if (a is PyList l && IsInt(b)) return new PyList(Enumerable.Repeat(l.Items, (int)Math.Max(0, AsLong(b))).SelectMany(x => x));
            }
            if (op == TokenType.Percent && a is string)
                throw PyException.TypeError("el formato con % no está soportado: usa str() y +");

            if (!IsNumber(a) || !IsNumber(b))
                throw PyException.TypeError(
                    $"operador {Symbol(op)} no válido entre {TypeName(a)} y {TypeName(b)}" +
                    (op == TokenType.Plus && (a is string || b is string) ? " (para unir un número a un texto usa str(numero))" : ""));

            bool ints = IsInt(a) && IsInt(b);
            switch (op)
            {
                case TokenType.Plus: return ints ? Checked(() => AsLong(a) + AsLong(b)) : (object)(AsDouble(a) + AsDouble(b));
                case TokenType.Minus: return ints ? Checked(() => AsLong(a) - AsLong(b)) : (object)(AsDouble(a) - AsDouble(b));
                case TokenType.Star: return ints ? Checked(() => AsLong(a) * AsLong(b)) : (object)(AsDouble(a) * AsDouble(b));
                case TokenType.Slash:
                    if (AsDouble(b) == 0) throw PyException.ZeroDivision();
                    return AsDouble(a) / AsDouble(b);
                case TokenType.DoubleSlash:
                    if (AsDouble(b) == 0) throw PyException.ZeroDivision();
                    if (ints) return FloorDiv(AsLong(a), AsLong(b));
                    return Math.Floor(AsDouble(a) / AsDouble(b));
                case TokenType.Percent:
                    if (AsDouble(b) == 0) throw PyException.ZeroDivision();
                    if (ints) return AsLong(a) - AsLong(b) * FloorDiv(AsLong(a), AsLong(b));
                    return AsDouble(a) - AsDouble(b) * Math.Floor(AsDouble(a) / AsDouble(b));
                case TokenType.DoubleStar:
                    if (ints && AsLong(b) >= 0) return IntPow(AsLong(a), AsLong(b));
                    if (AsDouble(a) == 0 && AsDouble(b) < 0) throw PyException.ZeroDivision();
                    return Math.Pow(AsDouble(a), AsDouble(b));
            }

            // Operadores de bits: solo enteros
            if (!ints)
                throw PyException.TypeError($"operador {Symbol(op)} solo admite enteros, no {TypeName(a)} y {TypeName(b)}");
            long x = AsLong(a), y = AsLong(b);
            switch (op)
            {
                case TokenType.Ampersand: return x & y;
                case TokenType.Pipe: return x | y;
                case TokenType.Caret: return x ^ y;
                case TokenType.LeftShift:
                    if (y < 0) throw PyException.ValueError("desplazamiento negativo");
                    if (y >= 63) return x == 0 ? 0L : throw new PyException("OverflowError", "entero demasiado grande para el simulador");
                    return Checked(() => x * (1L << (int)y));
                case TokenType.RightShift:
                    if (y < 0) throw PyException.ValueError("desplazamiento negativo");
                    return y >= 63 ? (x < 0 ? -1L : 0L) : x >> (int)y;
            }
            throw PyException.TypeError($"operador {Symbol(op)} no soportado");
        }

        private static long FloorDiv(long a, long b)
        {
            long q = a / b;
            if ((a % b != 0) && ((a < 0) != (b < 0))) q--;
            return q;
        }

        private static object IntPow(long b, long e)
        {
            long result = 1;
            try
            {
                checked
                {
                    while (e > 0)
                    {
                        if ((e & 1) == 1) result *= b;
                        e >>= 1;
                        if (e > 0) b *= b;
                    }
                }
            }
            catch (OverflowException)
            {
                throw new PyException("OverflowError", "entero demasiado grande para el simulador");
            }
            return result;
        }

        private static object Checked(Func<long> f)
        {
            try { return checked(f()); }
            catch (OverflowException) { throw new PyException("OverflowError", "entero demasiado grande para el simulador"); }
        }

        private static string Repeat(string s, long n) =>
            n <= 0 ? "" : new StringBuilder(s.Length * (int)n).Insert(0, s, (int)n).ToString();

        // ------------------------------------------------------------------ comparaciones

        public static bool Compare(CompareOp op, object a, object b)
        {
            switch (op)
            {
                case CompareOp.Equal: return AreEqual(a, b);
                case CompareOp.NotEqual: return !AreEqual(a, b);
                case CompareOp.Is: return IsSame(a, b);
                case CompareOp.IsNot: return !IsSame(a, b);
                case CompareOp.In: return Contains(b, a);
                case CompareOp.NotIn: return !Contains(b, a);
            }
            int c;
            if (IsNumber(a) && IsNumber(b)) c = AsDouble(a).CompareTo(AsDouble(b));
            else if (a is string sa && b is string sb) c = string.CompareOrdinal(sa, sb);
            else throw PyException.TypeError($"no se puede comparar {TypeName(a)} con {TypeName(b)} usando {OpText(op)}");
            switch (op)
            {
                case CompareOp.Less: return c < 0;
                case CompareOp.Greater: return c > 0;
                case CompareOp.LessEqual: return c <= 0;
                default: return c >= 0;
            }
        }

        public static bool AreEqual(object a, object b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (IsNumber(a) && IsNumber(b)) return AsDouble(a) == AsDouble(b);
            if (a is string sa && b is string sb) return sa == sb;
            if (a is PyList la && b is PyList lb) return SequenceEqual(la.Items, lb.Items);
            if (a is PyTuple ta && b is PyTuple tb) return SequenceEqual(ta.Items, tb.Items);
            return ReferenceEquals(a, b);
        }

        private static bool SequenceEqual(IReadOnlyList<object> a, IReadOnlyList<object> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (!AreEqual(a[i], b[i])) return false;
            return true;
        }

        private static bool IsSame(object a, object b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (a is bool ba && b is bool bb) return ba == bb;
            return ReferenceEquals(a, b);
        }

        private static bool Contains(object container, object item)
        {
            if (container is string s)
            {
                if (!(item is string sub)) throw PyException.TypeError("'in' sobre un texto necesita otro texto");
                return s.Contains(sub);
            }
            if (container is PyRange r && IsInt(item))
            {
                long v = AsLong(item);
                return r.Values().Any(x => (long)x == v);
            }
            return Iterate(container).Any(x => AreEqual(x, item));
        }

        // ------------------------------------------------------------------ secuencias

        /// <summary>Recorre una lista, tupla, range o texto (para <c>for</c>, <c>in</c>, <c>list()</c>...).</summary>
        public static IEnumerable<object> Iterate(object v)
        {
            switch (v)
            {
                case PyList l: return l.Items.ToArray();   // copia: modificar la lista dentro del for no rompe el recorrido
                case PyTuple t: return t.Items;
                case PyRange r: return r.Values();
                case IPyIndexable ix: return Enumerable.Range(0, ix.Count).Select(ix.GetItem).ToArray();
                case string s: return s.Select(c => (object)c.ToString());
                case IEnumerable<object> e when !(v is IPyObject): return e;
                default: throw PyException.TypeError($"'{TypeName(v)}' no se puede recorrer");
            }
        }

        public static long Length(object v)
        {
            switch (v)
            {
                case string s: return s.Length;
                case PyList l: return l.Items.Count;
                case PyTuple t: return t.Items.Count;
                case PyRange r: return r.Count;
                case IPyIndexable ix: return ix.Count;
                default: throw PyException.TypeError($"'{TypeName(v)}' no tiene longitud");
            }
        }

        public static int ToIndex(object v)
        {
            if (!IsInt(v)) throw PyException.TypeError($"los índices deben ser enteros, no {TypeName(v)}");
            long l = AsLong(v);
            if (l > int.MaxValue || l < int.MinValue) throw PyException.IndexError("índice fuera de rango");
            return (int)l;
        }

        /// <summary>Convierte un índice de Python (que puede ser negativo) en posición 0..n-1.</summary>
        public static int NormalizeIndex(int index, int count, bool clamp = false)
        {
            int i = index < 0 ? index + count : index;
            if (clamp) return Math.Max(0, Math.Min(count, i));
            if (i < 0 || i >= count) throw PyException.IndexError("índice fuera de rango");
            return i;
        }

        public static object GetIndex(object target, object index)
        {
            switch (target)
            {
                case PyList l: return l.Items[NormalizeIndex(ToIndex(index), l.Items.Count)];
                case PyTuple t: return t.Items[NormalizeIndex(ToIndex(index), t.Items.Count)];
                case string s: return s[NormalizeIndex(ToIndex(index), s.Length)].ToString();
                case PyRange r:
                    return r.Start + NormalizeIndex(ToIndex(index), (int)r.Count) * r.StepValue;
                case IPyIndexable ix: return ix.GetItem(NormalizeIndex(ToIndex(index), ix.Count));
                default: throw PyException.TypeError($"'{TypeName(target)}' no admite índices");
            }
        }

        public static void SetIndex(object target, object index, object value)
        {
            if (target is PyList l)
            {
                l.Items[NormalizeIndex(ToIndex(index), l.Items.Count)] = value;
                return;
            }
            throw PyException.TypeError($"'{TypeName(target)}' no admite asignar por índice" +
                                        (target is PyTuple || target is string ? " (es inmutable)" : ""));
        }

        // ------------------------------------------------------------------ atributos

        public static object GetAttribute(object target, string name)
        {
            switch (target)
            {
                case IPyObject o: return o.GetAttribute(name);
                case string s:
                    switch (name)
                    {
                        case "upper": return new NativeFunction("upper", a => s.ToUpperInvariant());
                        case "lower": return new NativeFunction("lower", a => s.ToLowerInvariant());
                        case "strip": return new NativeFunction("strip", a => s.Trim());
                    }
                    break;
            }
            throw PyException.AttributeError(TypeName(target), name);
        }

        public static void SetAttribute(object target, string name, object value)
        {
            if (target is IPyObject o)
            {
                o.SetAttribute(name, value);
                return;
            }
            throw PyException.AttributeError(TypeName(target), name);
        }

        // ------------------------------------------------------------------ texto

        /// <summary>Como <c>str(v)</c>: lo que muestra <c>print</c>.</summary>
        public static string Str(object v) => v is string s ? s : Repr(v);

        /// <summary>Como <c>repr(v)</c>: los textos dentro de listas salen entre comillas.</summary>
        public static string Repr(object v)
        {
            switch (v)
            {
                case null: return "None";
                case bool b: return b ? "True" : "False";
                case long l: return l.ToString(Inv);
                case double d: return FormatFloat(d);
                case string s: return "'" + s.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n") + "'";
                case PyList l: return "[" + string.Join(", ", l.Items.Select(Repr)) + "]";
                case PyTuple t: return t.Items.Count == 1 ? "(" + Repr(t.Items[0]) + ",)" : "(" + string.Join(", ", t.Items.Select(Repr)) + ")";
                case PyRange r: return r.StepValue == 1 ? $"range({r.Start}, {r.Stop})" : $"range({r.Start}, {r.Stop}, {r.StepValue})";
                case PyExceptionValue e: return e.ToString();
                case IPyCallable c: return c.ToString();
                default: return v.ToString();
            }
        }

        public static string FormatFloat(double d)
        {
            if (double.IsNaN(d)) return "nan";
            if (double.IsPositiveInfinity(d)) return "inf";
            if (double.IsNegativeInfinity(d)) return "-inf";
            if (d == 0) return 1 / d < 0 ? "-0.0" : "0.0";

            // Python escribe la representación MÁS CORTA que, leída de vuelta, da el mismo double.
            // No se usa ToString("R") porque el Mono de Unity y .NET no dan el mismo resultado
            // (0.89999999999999991 frente a 0.8999999999999999): se buscan los dígitos a mano.
            string sci = null;
            for (int p = 1; p <= 17; p++)
            {
                sci = d.ToString("E" + (p - 1), Inv);   // p dígitos significativos: "-8.99E-001"
                if (double.Parse(sci, NumberStyles.Float, Inv) == d) break;
            }

            bool negative = sci[0] == '-';
            if (negative) sci = sci.Substring(1);
            int ePos = sci.IndexOf('E');
            string digits = sci.Substring(0, ePos).Replace(".", "").TrimEnd('0');
            if (digits.Length == 0) digits = "0";
            int exp = int.Parse(sci.Substring(ePos + 1), NumberStyles.AllowLeadingSign, Inv);

            string body;
            if (exp < -4 || exp >= 16)
            {
                // Notación científica, como Python: 1e-05, 1.5e+16
                body = digits.Substring(0, 1) + (digits.Length > 1 ? "." + digits.Substring(1) : "") +
                       "e" + (exp < 0 ? "-" : "+") + Math.Abs(exp).ToString("00", Inv);
            }
            else if (exp >= 0)
            {
                string intPart = digits.Length > exp + 1 ? digits.Substring(0, exp + 1) : digits.PadRight(exp + 1, '0');
                string frac = digits.Length > exp + 1 ? digits.Substring(exp + 1) : "0";
                body = intPart + "." + frac;
            }
            else
            {
                body = "0." + new string('0', -exp - 1) + digits;
            }
            return (negative ? "-" : "") + body;
        }

        private static string Symbol(TokenType op)
        {
            switch (op)
            {
                case TokenType.Plus: return "+";
                case TokenType.Minus: return "-";
                case TokenType.Star: return "*";
                case TokenType.Slash: return "/";
                case TokenType.DoubleSlash: return "//";
                case TokenType.Percent: return "%";
                case TokenType.DoubleStar: return "**";
                case TokenType.Ampersand: return "&";
                case TokenType.Pipe: return "|";
                case TokenType.Caret: return "^";
                case TokenType.LeftShift: return "<<";
                case TokenType.RightShift: return ">>";
                case TokenType.Tilde: return "~";
                case TokenType.Not: return "not";
                default: return op.ToString();
            }
        }

        private static string OpText(CompareOp op)
        {
            switch (op)
            {
                case CompareOp.Less: return "<";
                case CompareOp.Greater: return ">";
                case CompareOp.LessEqual: return "<=";
                default: return ">=";
            }
        }
    }
}
