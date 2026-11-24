using System.Collections.Generic;
using System.Linq;
using ImatPiSim.Domain.Interpreter.Ast;

namespace ImatPiSim.Domain.Interpreter.Runtime
{
    /// <summary>
    /// Visitor que convierte cada nodo del árbol en un <see cref="Executable"/>: un iterador de C#
    /// que ejecuta el nodo paso a paso y puede pausarse en mitad (decisión E8, opción A).
    /// </summary>
    /// <remarks>
    /// <para>Es el patrón <b>Interpreter</b> sobre el Composite del árbol: cada nodo sabe
    /// ejecutarse, y los compuestos ejecutan a sus hijos. La compilación se hace una sola vez al
    /// cargar el programa; luego solo se ejecutan los delegados.</para>
    /// <para><b>Pausas.</b> Cada sentencia emite <see cref="Signal.Step"/> (consume presupuesto) y
    /// las funciones como <c>sleep</c> emiten <see cref="Signal.Sleep"/>. Un iterador que se pausa
    /// recuerda exactamente dónde estaba; al reanudarlo sigue desde ahí, aunque sea dentro de un
    /// bucle dentro de una función dentro de una expresión.</para>
    /// <para><b>try / except en iteradores.</b> C# no permite <c>yield</c> dentro de un <c>try</c>
    /// con <c>catch</c>. Por eso el cuerpo protegido se recorre a mano: el <c>try/catch</c> envuelve
    /// solo la llamada a <c>MoveNext()</c> y el <c>yield</c> queda fuera.</para>
    /// </remarks>
    public sealed class Compiler : IAstVisitor<Executable>
    {
        private readonly ScriptRuntime _rt;
        private int _functionDepth;
        private int _loopDepth;

        public Compiler(ScriptRuntime runtime) => _rt = runtime;

        public Executable Compile(ModuleNode module) => module.Accept(this);

        private Executable C(Node n) => n.Accept(this);

        // ================================================================== utilidades de iteración

        /// <summary>Ejecuta un paso y deja su resultado en <paramref name="r"/> (para expresiones).</summary>
        private static IEnumerable<Signal> Run(Executable e, Frame f, Ref r) => e(f, r);

        private static ParseException CompileError(Node n, string message) =>
            new ParseException(message, n.Line, n.Column);

        // ================================================================== sentencias

        public Executable VisitModule(ModuleNode n)
        {
            var body = Block(n.Body);
            return (f, r) => body(f, r);
        }

        /// <summary>Secuencia de sentencias; se corta si una pide break, continue o return.</summary>
        private Executable Block(IReadOnlyList<Stmt> stmts)
        {
            var compiled = stmts.Select(s => Statement(s)).ToArray();
            return (f, r) => RunBlock(compiled, f);
        }

        private static IEnumerable<Signal> RunBlock(Executable[] stmts, Frame f)
        {
            foreach (var s in stmts)
            {
                foreach (var sig in s(f, null)) yield return sig;
                if (f.Flow != Flow.Normal) yield break;
            }
        }

        /// <summary>
        /// Envuelve una sentencia: marca su línea, atiende la petición de parada (KeyboardInterrupt)
        /// y consume una unidad de presupuesto antes de ejecutarla.
        /// </summary>
        private Executable Statement(Stmt s)
        {
            var inner = C(s);
            int line = s.Line, col = s.Column;
            return (f, r) => RunStatement(inner, f, line, col);
        }

        private IEnumerable<Signal> RunStatement(Executable inner, Frame f, int line, int col)
        {
            _rt.BeginStatement(line, col);
            yield return Signal.Step;
            _rt.BeginStatement(line, col);   // tras una pausa, volver a comprobar si hay que parar
            foreach (var sig in inner(f, null)) yield return sig;
        }

        public Executable VisitExprStmt(ExprStmt n)
        {
            var e = C(n.Expression);
            return (f, r) => e(f, new Ref());
        }

        public Executable VisitAssign(AssignStmt n)
        {
            var value = C(n.Value);
            var targets = n.Targets.Select(Assigner).ToArray();
            return (f, r) => RunAssign(value, targets, f);
        }

        private static IEnumerable<Signal> RunAssign(Executable value, AssignTarget[] targets, Frame f)
        {
            var v = new Ref();
            foreach (var s in value(f, v)) yield return s;
            foreach (var t in targets)
                foreach (var s in t(f, v.Value)) yield return s;
        }

        private delegate IEnumerable<Signal> AssignTarget(Frame f, object value);

        private AssignTarget Assigner(Expr target)
        {
            switch (target)
            {
                case NameExpr name:
                    return (f, v) => AssignName(name, f, v);
                case AttributeExpr a:
                {
                    var obj = C(a.Target);
                    return (f, v) => AssignAttribute(obj, a, f, v);
                }
                case IndexExpr ix:
                {
                    var obj = C(ix.Target);
                    var index = C(ix.Index);
                    return (f, v) => AssignIndex(obj, index, ix, f, v);
                }
                default:
                    throw CompileError(target, "no se puede asignar un valor a esta expresión");
            }
        }

        private IEnumerable<Signal> AssignName(NameExpr name, Frame f, object v)
        {
            Guard(name, () => _rt.StoreName(f, name.Name, v));
            yield break;
        }

        private static IEnumerable<Signal> AssignAttribute(Executable obj, AttributeExpr a, Frame f, object v)
        {
            var o = new Ref();
            foreach (var s in obj(f, o)) yield return s;
            Guard(a, () => PyOps.SetAttribute(o.Value, a.Name, v));
        }

        private static IEnumerable<Signal> AssignIndex(Executable obj, Executable index, IndexExpr ix, Frame f, object v)
        {
            var o = new Ref();
            foreach (var s in obj(f, o)) yield return s;
            var i = new Ref();
            foreach (var s in index(f, i)) yield return s;
            Guard(ix, () => PyOps.SetIndex(o.Value, i.Value, v));
        }

        public Executable VisitAugAssign(AugAssignStmt n)
        {
            var value = C(n.Value);
            var op = n.Operator;
            switch (n.Target)
            {
                case NameExpr name:
                    return (f, r) => AugName(name, op, value, f, n);
                case AttributeExpr a:
                {
                    var obj = C(a.Target);
                    return (f, r) => AugAttribute(obj, a, op, value, f, n);
                }
                case IndexExpr ix:
                {
                    var obj = C(ix.Target);
                    var index = C(ix.Index);
                    return (f, r) => AugIndex(obj, index, op, value, f, n);
                }
                default:
                    throw CompileError(n, "no se puede asignar un valor a esta expresión");
            }
        }

        private IEnumerable<Signal> AugName(NameExpr name, TokenType op, Executable value, Frame f, Node at)
        {
            object current = Guard(name, () => _rt.LoadName(f, name.Name));
            var v = new Ref();
            foreach (var s in value(f, v)) yield return s;
            var result = Guard(at, () => PyOps.Binary(op, current, v.Value));
            Guard(name, () => _rt.StoreName(f, name.Name, result));
        }

        private static IEnumerable<Signal> AugAttribute(Executable obj, AttributeExpr a, TokenType op, Executable value, Frame f, Node at)
        {
            var o = new Ref();
            foreach (var s in obj(f, o)) yield return s;
            var current = Guard(a, () => PyOps.GetAttribute(o.Value, a.Name));
            var v = new Ref();
            foreach (var s in value(f, v)) yield return s;
            var result = Guard(at, () => PyOps.Binary(op, current, v.Value));
            Guard(a, () => PyOps.SetAttribute(o.Value, a.Name, result));
        }

        private static IEnumerable<Signal> AugIndex(Executable obj, Executable index, TokenType op, Executable value, Frame f, Node at)
        {
            var o = new Ref();
            foreach (var s in obj(f, o)) yield return s;
            var i = new Ref();
            foreach (var s in index(f, i)) yield return s;
            var current = Guard(at, () => PyOps.GetIndex(o.Value, i.Value));
            var v = new Ref();
            foreach (var s in value(f, v)) yield return s;
            var result = Guard(at, () => PyOps.Binary(op, current, v.Value));
            Guard(at, () => PyOps.SetIndex(o.Value, i.Value, result));
        }

        public Executable VisitIf(IfStmt n)
        {
            var cond = C(n.Condition);
            var body = Block(n.Body);
            var elseBody = Block(n.ElseBody);
            return (f, r) => RunIf(cond, body, elseBody, f);
        }

        private static IEnumerable<Signal> RunIf(Executable cond, Executable body, Executable elseBody, Frame f)
        {
            var c = new Ref();
            foreach (var s in cond(f, c)) yield return s;
            foreach (var s in (PyOps.IsTruthy(c.Value) ? body : elseBody)(f, null)) yield return s;
        }

        public Executable VisitWhile(WhileStmt n)
        {
            var cond = C(n.Condition);
            _loopDepth++;
            var body = Block(n.Body);
            _loopDepth--;
            return (f, r) => RunWhile(cond, body, f);
        }

        private static IEnumerable<Signal> RunWhile(Executable cond, Executable body, Frame f)
        {
            while (true)
            {
                yield return Signal.Step;   // cada vuelta cuesta presupuesto, aunque el cuerpo sea vacío
                var c = new Ref();
                foreach (var s in cond(f, c)) yield return s;
                if (!PyOps.IsTruthy(c.Value)) yield break;
                foreach (var s in body(f, null)) yield return s;
                if (LoopShouldExit(f)) yield break;
            }
        }

        /// <summary>Atiende break/continue tras el cuerpo de un bucle. <c>true</c> = salir del bucle.</summary>
        private static bool LoopShouldExit(Frame f)
        {
            switch (f.Flow)
            {
                case Flow.Break:
                    f.Flow = Flow.Normal;
                    return true;
                case Flow.Continue:
                    f.Flow = Flow.Normal;
                    return false;
                case Flow.Return:
                    return true;   // el return sigue subiendo
                default:
                    return false;
            }
        }

        public Executable VisitFor(ForStmt n)
        {
            var iterable = C(n.Iterable);
            _loopDepth++;
            var body = Block(n.Body);
            _loopDepth--;
            return (f, r) => RunFor(n, iterable, body, f);
        }

        private IEnumerable<Signal> RunFor(ForStmt n, Executable iterable, Executable body, Frame f)
        {
            var it = new Ref();
            foreach (var s in iterable(f, it)) yield return s;
            var items = Guard(n, () => PyOps.Iterate(it.Value));
            foreach (var item in items)
            {
                yield return Signal.Step;
                Guard(n, () => _rt.StoreName(f, n.Variable, item));
                foreach (var s in body(f, null)) yield return s;
                if (LoopShouldExit(f)) yield break;
            }
        }

        public Executable VisitSimple(SimpleStmt n)
        {
            switch (n.Kind)
            {
                case TokenType.Pass:
                    return (f, r) => Enumerable.Empty<Signal>();
                case TokenType.Break:
                case TokenType.Continue:
                    if (_loopDepth == 0)
                        throw CompileError(n, $"'{n.Kind.ToString().ToLowerInvariant()}' fuera de un bucle");
                    var flow = n.Kind == TokenType.Break ? Flow.Break : Flow.Continue;
                    return (f, r) => SetFlow(f, flow);
                default:
                    throw CompileError(n, "sentencia no soportada");
            }
        }

        private static IEnumerable<Signal> SetFlow(Frame f, Flow flow)
        {
            f.Flow = flow;
            yield break;
        }

        public Executable VisitReturn(ReturnStmt n)
        {
            if (_functionDepth == 0) throw CompileError(n, "'return' fuera de una función");
            var value = n.Value == null ? null : C(n.Value);
            return (f, r) => RunReturn(value, f);
        }

        private static IEnumerable<Signal> RunReturn(Executable value, Frame f)
        {
            object v = null;
            if (value != null)
            {
                var r = new Ref();
                foreach (var s in value(f, r)) yield return s;
                v = r.Value;
            }
            f.ReturnValue = v;
            f.Flow = Flow.Return;
        }

        public Executable VisitFunctionDef(FunctionDefStmt n)
        {
            var defaults = n.Parameters.Select(p => p.Default == null ? null : C(p.Default)).ToArray();
            var names = n.Parameters.Select(p => p.Name).ToArray();
            var scope = Scope.ForFunction(n.Parameters, n.Body);

            int savedLoops = _loopDepth;
            _loopDepth = 0;
            _functionDepth++;
            var body = Block(n.Body);
            _functionDepth--;
            _loopDepth = savedLoops;

            return (f, r) => DefineFunction(n, names, defaults, scope, body, f);
        }

        private IEnumerable<Signal> DefineFunction(FunctionDefStmt n, string[] names, Executable[] defaults, Scope scope,
                                                   Executable body, Frame f)
        {
            var values = new object[defaults.Length];
            for (int i = 0; i < defaults.Length; i++)
            {
                if (defaults[i] == null)
                {
                    values[i] = PyFunction.NoDefault;
                    continue;
                }
                var v = new Ref();
                foreach (var s in defaults[i](f, v)) yield return s;   // se evalúan al definir, como en Python
                values[i] = v.Value;
            }
            var fn = new PyFunction(n.Name, names, values, scope, body, f, isLambda: false);
            Guard(n, () => _rt.StoreName(f, n.Name, fn));
        }

        public Executable VisitScope(ScopeStmt n)
        {
            if (_functionDepth == 0 && n.Kind == TokenType.Nonlocal)
                throw CompileError(n, "'nonlocal' solo puede usarse dentro de una función");
            return (f, r) => Enumerable.Empty<Signal>();   // se resuelve al analizar el ámbito
        }

        public Executable VisitImport(ImportStmt n) => (f, r) => RunImport(n, f);

        private IEnumerable<Signal> RunImport(ImportStmt n, Frame f)
        {
            Guard(n, () =>
            {
                var module = _rt.ImportModule(n.Module);
                if (!n.IsFrom)
                {
                    if (n.Module.Contains(".") && n.ModuleAlias == null)
                        throw new PyException("ImportError", $"usa 'import {n.Module} as nombre'");
                    _rt.StoreName(f, n.ModuleAlias ?? n.Module, module);
                    return;
                }
                foreach (var name in n.Names)
                {
                    if (!module.TryGet(name.Name, out var value))
                        throw new PyException("ImportError", $"no se puede importar '{name.Name}' de '{n.Module}'");
                    _rt.StoreName(f, name.BoundName, value);
                }
            });
            yield break;
        }

        public Executable VisitTry(TryStmt n)
        {
            var body = Block(n.Body);
            var handlers = n.Handlers.Select(h => (handler: h, body: Block(h.Body))).ToArray();
            var finallyBody = Block(n.FinallyBody);
            foreach (var h in n.Handlers)
                if (h.TypeName != null && !PyException.IsKnownType(h.TypeName))
                    throw CompileError(n, $"tipo de excepción desconocido: '{h.TypeName}'");
            return (f, r) => RunTry(body, handlers, finallyBody, f);
        }

        private IEnumerable<Signal> RunTry(Executable body, (ExceptHandler handler, Executable body)[] handlers,
                                           Executable finallyBody, Frame f)
        {
            PyException pending = null;

            // 1. Cuerpo del try, capturando las excepciones de Python que salgan de él.
            foreach (var s in Protected(body(f, null), ex => pending = ex)) yield return s;

            // 2. Si hubo excepción, el primer except que encaje la trata (y puede lanzar otra).
            if (pending != null)
            {
                var caught = pending;
                foreach (var (handler, handlerBody) in handlers)
                {
                    if (handler.TypeName != null && !caught.IsInstanceOf(handler.TypeName)) continue;
                    pending = null;
                    if (handler.Alias != null) _rt.StoreName(f, handler.Alias, new PyExceptionValue(caught));
                    foreach (var s in Protected(handlerBody(f, null), ex => pending = ex)) yield return s;
                    break;
                }
            }

            // 3. El finally se ejecuta siempre: con o sin excepción, y también tras break/return.
            if (finallyBody != null)
            {
                var savedFlow = f.Flow;
                var savedReturn = f.ReturnValue;
                f.Flow = Flow.Normal;
                foreach (var s in finallyBody(f, null)) yield return s;
                if (f.Flow == Flow.Normal)
                {
                    f.Flow = savedFlow;
                    f.ReturnValue = savedReturn;
                }
                else pending = null;   // un return/break dentro del finally descarta la excepción
            }

            if (pending != null) throw pending;
        }

        /// <summary>
        /// Recorre un iterador capturando las <see cref="PyException"/> que lance. El
        /// <c>try/catch</c> rodea solo a <c>MoveNext()</c>, porque C# no permite <c>yield</c> dentro.
        /// </summary>
        private static IEnumerable<Signal> Protected(IEnumerable<Signal> steps, System.Action<PyException> onError)
        {
            using (var it = steps.GetEnumerator())
            {
                while (true)
                {
                    bool has;
                    try
                    {
                        has = it.MoveNext();
                    }
                    catch (PyException ex)
                    {
                        onError(ex);
                        yield break;
                    }
                    if (!has) yield break;
                    yield return it.Current;
                }
            }
        }

        // ================================================================== expresiones

        public Executable VisitNumber(NumberExpr n)
        {
            var v = n.Value;
            return (f, r) => Constant(r, v);
        }

        public Executable VisitString(StringExpr n)
        {
            var v = n.Value;
            return (f, r) => Constant(r, v);
        }

        public Executable VisitConstant(ConstantExpr n)
        {
            object v = n.Kind == TokenType.True ? true : n.Kind == TokenType.False ? (object)false : null;
            return (f, r) => Constant(r, v);
        }

        private static IEnumerable<Signal> Constant(Ref r, object v)
        {
            r.Value = v;
            yield break;
        }

        public Executable VisitName(NameExpr n) => (f, r) => LoadName(n, f, r);

        private IEnumerable<Signal> LoadName(NameExpr n, Frame f, Ref r)
        {
            r.Value = Guard(n, () => _rt.LoadName(f, n.Name));
            yield break;
        }

        public Executable VisitUnary(UnaryExpr n)
        {
            var operand = C(n.Operand);
            return (f, r) => RunUnary(n, operand, f, r);
        }

        private static IEnumerable<Signal> RunUnary(UnaryExpr n, Executable operand, Frame f, Ref r)
        {
            var v = new Ref();
            foreach (var s in operand(f, v)) yield return s;
            r.Value = Guard(n, () => PyOps.Unary(n.Operator, v.Value));
        }

        public Executable VisitBinary(BinaryExpr n)
        {
            var left = C(n.Left);
            var right = C(n.Right);
            return (f, r) => RunBinary(n, left, right, f, r);
        }

        private static IEnumerable<Signal> RunBinary(BinaryExpr n, Executable left, Executable right, Frame f, Ref r)
        {
            var a = new Ref();
            foreach (var s in left(f, a)) yield return s;
            var b = new Ref();
            foreach (var s in right(f, b)) yield return s;
            r.Value = Guard(n, () => PyOps.Binary(n.Operator, a.Value, b.Value));
        }

        public Executable VisitLogical(LogicalExpr n)
        {
            var left = C(n.Left);
            var right = C(n.Right);
            bool isAnd = n.Operator == TokenType.And;
            return (f, r) => RunLogical(isAnd, left, right, f, r);
        }

        /// <summary>
        /// Cortocircuito: <c>a and b</c> no evalúa b si a es falso, y devuelve el operando (no un bool),
        /// como en Python.
        /// </summary>
        private static IEnumerable<Signal> RunLogical(bool isAnd, Executable left, Executable right, Frame f, Ref r)
        {
            foreach (var s in left(f, r)) yield return s;
            if (PyOps.IsTruthy(r.Value) != isAnd) yield break;
            foreach (var s in right(f, r)) yield return s;
        }

        public Executable VisitCompare(CompareExpr n)
        {
            var first = C(n.First);
            var rest = n.Operands.Select(C).ToArray();
            return (f, r) => RunCompare(n, first, rest, f, r);
        }

        private static IEnumerable<Signal> RunCompare(CompareExpr n, Executable first, Executable[] rest, Frame f, Ref r)
        {
            var left = new Ref();
            foreach (var s in first(f, left)) yield return s;
            for (int i = 0; i < rest.Length; i++)
            {
                var right = new Ref();
                foreach (var s in rest[i](f, right)) yield return s;
                var op = n.Operators[i];
                bool ok = Guard(n, () => PyOps.Compare(op, left.Value, right.Value));
                if (!ok)
                {
                    r.Value = false;
                    yield break;
                }
                left = right;
            }
            r.Value = true;
        }

        public Executable VisitCall(CallExpr n)
        {
            var callee = C(n.Callee);
            var args = n.Arguments.Select(C).ToArray();
            var kwargs = n.Keywords.Select(k => (name: k.Name, value: C(k.Value))).ToArray();
            return (f, r) => RunCall(n, callee, args, kwargs, f, r);
        }

        private IEnumerable<Signal> RunCall(CallExpr n, Executable callee, Executable[] args,
                                            (string name, Executable value)[] kwargs, Frame f, Ref r)
        {
            var c = new Ref();
            foreach (var s in callee(f, c)) yield return s;
            var values = new object[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                var v = new Ref();
                foreach (var s in args[i](f, v)) yield return s;
                values[i] = v.Value;
            }
            var kw = new Dictionary<string, object>();
            foreach (var (name, value) in kwargs)
            {
                var v = new Ref();
                foreach (var s in value(f, v)) yield return s;
                kw[name] = v.Value;
            }
            if (!(c.Value is IPyCallable callable))
                throw PyException.TypeError($"'{PyOps.TypeName(c.Value)}' no se puede llamar").At(n.Line, n.Column);

            yield return Signal.Step;
            foreach (var s in _rt.Invoke(callable, values, kw, r, n.Line, n.Column)) yield return s;
        }

        public Executable VisitAttribute(AttributeExpr n)
        {
            var target = C(n.Target);
            return (f, r) => RunAttribute(n, target, f, r);
        }

        private static IEnumerable<Signal> RunAttribute(AttributeExpr n, Executable target, Frame f, Ref r)
        {
            var o = new Ref();
            foreach (var s in target(f, o)) yield return s;
            r.Value = Guard(n, () => PyOps.GetAttribute(o.Value, n.Name));
        }

        public Executable VisitIndex(IndexExpr n)
        {
            var target = C(n.Target);
            var index = C(n.Index);
            return (f, r) => RunIndex(n, target, index, f, r);
        }

        private static IEnumerable<Signal> RunIndex(IndexExpr n, Executable target, Executable index, Frame f, Ref r)
        {
            var o = new Ref();
            foreach (var s in target(f, o)) yield return s;
            var i = new Ref();
            foreach (var s in index(f, i)) yield return s;
            r.Value = Guard(n, () => PyOps.GetIndex(o.Value, i.Value));
        }

        public Executable VisitSequence(SequenceExpr n)
        {
            var items = n.Items.Select(C).ToArray();
            bool tuple = n.IsTuple;
            return (f, r) => RunSequence(tuple, items, f, r);
        }

        private static IEnumerable<Signal> RunSequence(bool tuple, Executable[] items, Frame f, Ref r)
        {
            var values = new object[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                var v = new Ref();
                foreach (var s in items[i](f, v)) yield return s;
                values[i] = v.Value;
            }
            r.Value = tuple ? (object)new PyTuple(values) : new PyList(values);
        }

        public Executable VisitLambda(LambdaExpr n)
        {
            var defaults = n.Parameters.Select(p => p.Default == null ? null : C(p.Default)).ToArray();
            var names = n.Parameters.Select(p => p.Name).ToArray();
            var scope = Scope.ForLambda(n.Parameters);
            _functionDepth++;
            var body = C(n.Body);
            _functionDepth--;
            return (f, r) => MakeLambda(names, defaults, scope, body, f, r);
        }

        private static IEnumerable<Signal> MakeLambda(string[] names, Executable[] defaults, Scope scope, Executable body,
                                                      Frame f, Ref r)
        {
            var values = new object[defaults.Length];
            for (int i = 0; i < defaults.Length; i++)
            {
                if (defaults[i] == null)
                {
                    values[i] = PyFunction.NoDefault;
                    continue;
                }
                var v = new Ref();
                foreach (var s in defaults[i](f, v)) yield return s;
                values[i] = v.Value;
            }
            r.Value = new PyFunction("<lambda>", names, values, scope, body, f, isLambda: true);
        }

        public Executable VisitConditional(ConditionalExpr n)
        {
            var cond = C(n.Condition);
            var yes = C(n.WhenTrue);
            var no = C(n.WhenFalse);
            return (f, r) => RunConditional(cond, yes, no, f, r);
        }

        private static IEnumerable<Signal> RunConditional(Executable cond, Executable yes, Executable no, Frame f, Ref r)
        {
            var c = new Ref();
            foreach (var s in cond(f, c)) yield return s;
            foreach (var s in (PyOps.IsTruthy(c.Value) ? yes : no)(f, r)) yield return s;
        }

        // ================================================================== errores con posición

        /// <summary>Ejecuta código que no se pausa y, si lanza una PyException, le pone la posición del nodo.</summary>
        private static T Guard<T>(Node n, System.Func<T> f)
        {
            try { return f(); }
            catch (PyException ex) { throw ex.At(n.Line, n.Column); }
        }

        private static void Guard(Node n, System.Action a)
        {
            try { a(); }
            catch (PyException ex) { throw ex.At(n.Line, n.Column); }
        }
    }
}
