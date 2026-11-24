using System.Collections.Generic;
using ImatPiSim.Domain.Interpreter.Ast;

namespace ImatPiSim.Domain.Interpreter.Runtime
{
    /// <summary>Qué nombres son locales, globales o de una función que contiene a esta.</summary>
    /// <remarks>
    /// Regla de Python: un nombre al que se asigna dentro de una función es local de esa función
    /// en todo su cuerpo, salvo que se declare <c>global</c> o <c>nonlocal</c>. Por eso
    /// <c>contador += 1</c> dentro de una función, sin <c>global contador</c>, da
    /// <c>UnboundLocalError</c>: un error típico que el simulador reproduce.
    /// </remarks>
    public sealed class Scope
    {
        public static readonly Scope Module = new Scope(true, new HashSet<string>(), new HashSet<string>(), new HashSet<string>());

        public Scope(bool isModule, HashSet<string> locals, HashSet<string> globals, HashSet<string> nonlocals)
        {
            IsModule = isModule;
            Locals = locals;
            Globals = globals;
            Nonlocals = nonlocals;
        }

        public bool IsModule { get; }
        public HashSet<string> Locals { get; }
        public HashSet<string> Globals { get; }
        public HashSet<string> Nonlocals { get; }

        /// <summary>Calcula el ámbito de una función a partir de sus parámetros y su cuerpo.</summary>
        public static Scope ForFunction(IEnumerable<Parameter> parameters, IEnumerable<Stmt> body)
        {
            var assigned = new HashSet<string>();
            var globals = new HashSet<string>();
            var nonlocals = new HashSet<string>();
            foreach (var p in parameters) assigned.Add(p.Name);
            Collect(body, assigned, globals, nonlocals);
            assigned.ExceptWith(globals);
            assigned.ExceptWith(nonlocals);
            return new Scope(false, assigned, globals, nonlocals);
        }

        /// <summary>Ámbito de una lambda: solo sus parámetros son locales.</summary>
        public static Scope ForLambda(IEnumerable<Parameter> parameters)
        {
            var locals = new HashSet<string>();
            foreach (var p in parameters) locals.Add(p.Name);
            return new Scope(false, locals, new HashSet<string>(), new HashSet<string>());
        }

        private static void Collect(IEnumerable<Stmt> body, HashSet<string> assigned, HashSet<string> globals, HashSet<string> nonlocals)
        {
            foreach (var s in body)
            {
                switch (s)
                {
                    case AssignStmt a:
                        foreach (var t in a.Targets) if (t is NameExpr n) assigned.Add(n.Name);
                        break;
                    case AugAssignStmt a when a.Target is NameExpr n:
                        assigned.Add(n.Name);
                        break;
                    case ForStmt f:
                        assigned.Add(f.Variable);
                        Collect(f.Body, assigned, globals, nonlocals);
                        break;
                    case WhileStmt w:
                        Collect(w.Body, assigned, globals, nonlocals);
                        break;
                    case IfStmt i:
                        Collect(i.Body, assigned, globals, nonlocals);
                        Collect(i.ElseBody, assigned, globals, nonlocals);
                        break;
                    case TryStmt t:
                        Collect(t.Body, assigned, globals, nonlocals);
                        foreach (var h in t.Handlers)
                        {
                            if (h.Alias != null) assigned.Add(h.Alias);
                            Collect(h.Body, assigned, globals, nonlocals);
                        }
                        Collect(t.FinallyBody, assigned, globals, nonlocals);
                        break;
                    case FunctionDefStmt d:
                        assigned.Add(d.Name);   // su cuerpo es otro ámbito
                        break;
                    case ImportStmt im:
                        if (im.IsFrom) foreach (var n in im.Names) assigned.Add(n.BoundName);
                        else assigned.Add(im.ModuleAlias ?? im.Module.Split('.')[0]);
                        break;
                    case ScopeStmt sc:
                        foreach (var n in sc.Names) (sc.Kind == TokenType.Global ? globals : nonlocals).Add(n);
                        break;
                }
            }
        }
    }

    /// <summary>Cómo debe continuar la ejecución tras una sentencia.</summary>
    public enum Flow
    {
        Normal,
        Break,
        Continue,
        Return
    }

    /// <summary>
    /// Marco de ejecución: las variables de una llamada a función (o del módulo) y un enlace al
    /// marco donde se definió la función (para los cierres, <c>nonlocal</c> y lambdas).
    /// </summary>
    public sealed class Frame
    {
        public Frame(Scope scope, Frame enclosing, Dictionary<string, object> globals)
        {
            Scope = scope;
            Enclosing = enclosing;
            Globals = globals;
            Vars = scope.IsModule ? globals : new Dictionary<string, object>();
        }

        public Scope Scope { get; }

        /// <summary>Marco de la función que contiene a esta (<c>null</c> en el módulo).</summary>
        public Frame Enclosing { get; }

        public Dictionary<string, object> Globals { get; }
        public Dictionary<string, object> Vars { get; }

        public Flow Flow { get; set; }
        public object ReturnValue { get; set; }
    }
}
