using System.Collections.Generic;

namespace ImatPiSim.Domain.Interpreter.Ast
{
    // =====================================================================================
    // Árbol sintáctico (AST) del programa del jugador.
    //
    // Patrón Composite: el programa es un árbol de nodos; los nodos compuestos (un `if`, una
    // suma, una llamada) contienen otros nodos, y los simples (un número, un nombre) son hojas.
    // Todos se tratan igual a través de Node.
    //
    // Patrón Visitor: cada operación sobre el árbol (imprimirlo, comprobarlo, ejecutarlo) es un
    // IAstVisitor<T> distinto; los nodos no cambian al añadir operaciones nuevas.
    // =====================================================================================

    /// <summary>Nodo del árbol, con la posición del código del que procede.</summary>
    public abstract class Node
    {
        protected Node(int line, int column)
        {
            Line = line;
            Column = column;
        }

        public int Line { get; }
        public int Column { get; }

        public abstract T Accept<T>(IAstVisitor<T> visitor);
    }

    /// <summary>Expresión: produce un valor.</summary>
    public abstract class Expr : Node
    {
        protected Expr(int line, int column) : base(line, column) { }
    }

    /// <summary>Sentencia: hace algo (asignar, repetir, definir...).</summary>
    public abstract class Stmt : Node
    {
        protected Stmt(int line, int column) : base(line, column) { }
    }

    // ------------------------------------------------------------------ expresiones (hojas)

    /// <summary>Número entero (<c>long</c>) o decimal (<c>double</c>).</summary>
    public sealed class NumberExpr : Expr
    {
        public NumberExpr(object value, int line, int column) : base(line, column) => Value = value;
        public object Value { get; }
        public override T Accept<T>(IAstVisitor<T> v) => v.VisitNumber(this);
    }

    public sealed class StringExpr : Expr
    {
        public StringExpr(string value, int line, int column) : base(line, column) => Value = value;
        public string Value { get; }
        public override T Accept<T>(IAstVisitor<T> v) => v.VisitString(this);
    }

    /// <summary><c>True</c>, <c>False</c> o <c>None</c>.</summary>
    public sealed class ConstantExpr : Expr
    {
        public ConstantExpr(TokenType kind, int line, int column) : base(line, column) => Kind = kind;
        public TokenType Kind { get; }
        public override T Accept<T>(IAstVisitor<T> v) => v.VisitConstant(this);
    }

    /// <summary>Nombre de variable, función o clase (<c>led</c>, <c>LED</c>, <c>sleep</c>).</summary>
    public sealed class NameExpr : Expr
    {
        public NameExpr(string name, int line, int column) : base(line, column) => Name = name;
        public string Name { get; }
        public override T Accept<T>(IAstVisitor<T> v) => v.VisitName(this);
    }

    // ------------------------------------------------------------------ expresiones compuestas

    /// <summary><c>-x</c>, <c>+x</c>, <c>~x</c>, <c>not x</c>.</summary>
    public sealed class UnaryExpr : Expr
    {
        public UnaryExpr(TokenType op, Expr operand, int line, int column) : base(line, column)
        {
            Operator = op;
            Operand = operand;
        }
        public TokenType Operator { get; }
        public Expr Operand { get; }
        public override T Accept<T>(IAstVisitor<T> v) => v.VisitUnary(this);
    }

    /// <summary>Operación aritmética o de bits: <c>a + b</c>, <c>c &gt;&gt; bit</c>...</summary>
    public sealed class BinaryExpr : Expr
    {
        public BinaryExpr(Expr left, TokenType op, Expr right, int line, int column) : base(line, column)
        {
            Left = left;
            Operator = op;
            Right = right;
        }
        public Expr Left { get; }
        public TokenType Operator { get; }
        public Expr Right { get; }
        public override T Accept<T>(IAstVisitor<T> v) => v.VisitBinary(this);
    }

    /// <summary><c>a and b</c> / <c>a or b</c> (evaluación en cortocircuito).</summary>
    public sealed class LogicalExpr : Expr
    {
        public LogicalExpr(Expr left, TokenType op, Expr right, int line, int column) : base(line, column)
        {
            Left = left;
            Operator = op;
            Right = right;
        }
        public Expr Left { get; }
        public TokenType Operator { get; }
        public Expr Right { get; }
        public override T Accept<T>(IAstVisitor<T> v) => v.VisitLogical(this);
    }

    /// <summary>Operadores de comparación de una cadena.</summary>
    public enum CompareOp { Less, Greater, LessEqual, GreaterEqual, Equal, NotEqual, In, NotIn, Is, IsNot }

    /// <summary>
    /// Comparación, posiblemente encadenada como en Python: <c>0 &lt;= x &lt; 10</c> equivale a
    /// <c>0 &lt;= x and x &lt; 10</c> evaluando <c>x</c> una sola vez.
    /// </summary>
    public sealed class CompareExpr : Expr
    {
        public CompareExpr(Expr first, IReadOnlyList<CompareOp> ops, IReadOnlyList<Expr> rest, int line, int column)
            : base(line, column)
        {
            First = first;
            Operators = ops;
            Operands = rest;
        }
        public Expr First { get; }
        public IReadOnlyList<CompareOp> Operators { get; }
        public IReadOnlyList<Expr> Operands { get; }
        public override T Accept<T>(IAstVisitor<T> v) => v.VisitCompare(this);
    }

    /// <summary>Argumento con nombre en una llamada: <c>pull_up=True</c>.</summary>
    public sealed class KeywordArg
    {
        public KeywordArg(string name, Expr value)
        {
            Name = name;
            Value = value;
        }
        public string Name { get; }
        public Expr Value { get; }
    }

    /// <summary>Llamada: <c>LED(20)</c>, <c>led.on()</c>, <c>Button(16, pull_up=True)</c>.</summary>
    public sealed class CallExpr : Expr
    {
        public CallExpr(Expr callee, IReadOnlyList<Expr> args, IReadOnlyList<KeywordArg> keywords, int line, int column)
            : base(line, column)
        {
            Callee = callee;
            Arguments = args;
            Keywords = keywords;
        }
        public Expr Callee { get; }
        public IReadOnlyList<Expr> Arguments { get; }
        public IReadOnlyList<KeywordArg> Keywords { get; }
        public override T Accept<T>(IAstVisitor<T> v) => v.VisitCall(this);
    }

    /// <summary>Acceso a atributo: <c>button.is_pressed</c>, <c>GPIO.HIGH</c>.</summary>
    public sealed class AttributeExpr : Expr
    {
        public AttributeExpr(Expr target, string name, int line, int column) : base(line, column)
        {
            Target = target;
            Name = name;
        }
        public Expr Target { get; }
        public string Name { get; }
        public override T Accept<T>(IAstVisitor<T> v) => v.VisitAttribute(this);
    }

    /// <summary>Indexación: <c>leds[i]</c>.</summary>
    public sealed class IndexExpr : Expr
    {
        public IndexExpr(Expr target, Expr index, int line, int column) : base(line, column)
        {
            Target = target;
            Index = index;
        }
        public Expr Target { get; }
        public Expr Index { get; }
        public override T Accept<T>(IAstVisitor<T> v) => v.VisitIndex(this);
    }

    /// <summary>Lista <c>[a, b]</c> o tupla <c>(a, b)</c>.</summary>
    public sealed class SequenceExpr : Expr
    {
        public SequenceExpr(bool isTuple, IReadOnlyList<Expr> items, int line, int column) : base(line, column)
        {
            IsTuple = isTuple;
            Items = items;
        }
        public bool IsTuple { get; }
        public IReadOnlyList<Expr> Items { get; }
        public override T Accept<T>(IAstVisitor<T> v) => v.VisitSequence(this);
    }

    /// <summary>Parámetro de una función o lambda, con valor por defecto opcional.</summary>
    public sealed class Parameter
    {
        public Parameter(string name, Expr defaultValue)
        {
            Name = name;
            Default = defaultValue;
        }
        public string Name { get; }
        public Expr Default { get; }
    }

    /// <summary><c>lambda led=led: al_soltar(led=led)</c>.</summary>
    public sealed class LambdaExpr : Expr
    {
        public LambdaExpr(IReadOnlyList<Parameter> parameters, Expr body, int line, int column) : base(line, column)
        {
            Parameters = parameters;
            Body = body;
        }
        public IReadOnlyList<Parameter> Parameters { get; }
        public Expr Body { get; }
        public override T Accept<T>(IAstVisitor<T> v) => v.VisitLambda(this);
    }

    /// <summary>Expresión condicional: <c>a if cond else b</c>.</summary>
    public sealed class ConditionalExpr : Expr
    {
        public ConditionalExpr(Expr condition, Expr whenTrue, Expr whenFalse, int line, int column) : base(line, column)
        {
            Condition = condition;
            WhenTrue = whenTrue;
            WhenFalse = whenFalse;
        }
        public Expr Condition { get; }
        public Expr WhenTrue { get; }
        public Expr WhenFalse { get; }
        public override T Accept<T>(IAstVisitor<T> v) => v.VisitConditional(this);
    }

    // ------------------------------------------------------------------ sentencias

    /// <summary>Programa completo.</summary>
    public sealed class ModuleNode : Node
    {
        public ModuleNode(IReadOnlyList<Stmt> body) : base(1, 1) => Body = body;
        public IReadOnlyList<Stmt> Body { get; }
        public override T Accept<T>(IAstVisitor<T> v) => v.VisitModule(this);
    }

    /// <summary>Una expresión usada como sentencia: <c>led.on()</c>.</summary>
    public sealed class ExprStmt : Stmt
    {
        public ExprStmt(Expr expression, int line, int column) : base(line, column) => Expression = expression;
        public Expr Expression { get; }
        public override T Accept<T>(IAstVisitor<T> v) => v.VisitExprStmt(this);
    }

    /// <summary><c>a = b = valor</c> (uno o varios destinos: nombre, atributo o índice).</summary>
    public sealed class AssignStmt : Stmt
    {
        public AssignStmt(IReadOnlyList<Expr> targets, Expr value, int line, int column) : base(line, column)
        {
            Targets = targets;
            Value = value;
        }
        public IReadOnlyList<Expr> Targets { get; }
        public Expr Value { get; }
        public override T Accept<T>(IAstVisitor<T> v) => v.VisitAssign(this);
    }

    /// <summary><c>contador += 1</c>. <see cref="Operator"/> es el operador binario (<c>Plus</c>...).</summary>
    public sealed class AugAssignStmt : Stmt
    {
        public AugAssignStmt(Expr target, TokenType op, Expr value, int line, int column) : base(line, column)
        {
            Target = target;
            Operator = op;
            Value = value;
        }
        public Expr Target { get; }
        public TokenType Operator { get; }
        public Expr Value { get; }
        public override T Accept<T>(IAstVisitor<T> v) => v.VisitAugAssign(this);
    }

    /// <summary><c>if / elif / else</c>. Un <c>elif</c> es un <c>IfStmt</c> dentro de <see cref="ElseBody"/>.</summary>
    public sealed class IfStmt : Stmt
    {
        public IfStmt(Expr condition, IReadOnlyList<Stmt> body, IReadOnlyList<Stmt> elseBody, int line, int column)
            : base(line, column)
        {
            Condition = condition;
            Body = body;
            ElseBody = elseBody;
        }
        public Expr Condition { get; }
        public IReadOnlyList<Stmt> Body { get; }

        /// <summary>Vacío si no hay <c>else</c>.</summary>
        public IReadOnlyList<Stmt> ElseBody { get; }

        public override T Accept<T>(IAstVisitor<T> v) => v.VisitIf(this);
    }

    public sealed class WhileStmt : Stmt
    {
        public WhileStmt(Expr condition, IReadOnlyList<Stmt> body, int line, int column) : base(line, column)
        {
            Condition = condition;
            Body = body;
        }
        public Expr Condition { get; }
        public IReadOnlyList<Stmt> Body { get; }
        public override T Accept<T>(IAstVisitor<T> v) => v.VisitWhile(this);
    }

    /// <summary><c>for nombre in iterable:</c> (el destino es un único nombre).</summary>
    public sealed class ForStmt : Stmt
    {
        public ForStmt(string variable, Expr iterable, IReadOnlyList<Stmt> body, int line, int column) : base(line, column)
        {
            Variable = variable;
            Iterable = iterable;
            Body = body;
        }
        public string Variable { get; }
        public Expr Iterable { get; }
        public IReadOnlyList<Stmt> Body { get; }
        public override T Accept<T>(IAstVisitor<T> v) => v.VisitFor(this);
    }

    /// <summary><c>break</c>, <c>continue</c> o <c>pass</c>.</summary>
    public sealed class SimpleStmt : Stmt
    {
        public SimpleStmt(TokenType kind, int line, int column) : base(line, column) => Kind = kind;
        public TokenType Kind { get; }
        public override T Accept<T>(IAstVisitor<T> v) => v.VisitSimple(this);
    }

    public sealed class ReturnStmt : Stmt
    {
        public ReturnStmt(Expr value, int line, int column) : base(line, column) => Value = value;

        /// <summary><c>null</c> en un <c>return</c> sin valor.</summary>
        public Expr Value { get; }

        public override T Accept<T>(IAstVisitor<T> v) => v.VisitReturn(this);
    }

    public sealed class FunctionDefStmt : Stmt
    {
        public FunctionDefStmt(string name, IReadOnlyList<Parameter> parameters, IReadOnlyList<Stmt> body, int line, int column)
            : base(line, column)
        {
            Name = name;
            Parameters = parameters;
            Body = body;
        }
        public string Name { get; }
        public IReadOnlyList<Parameter> Parameters { get; }
        public IReadOnlyList<Stmt> Body { get; }
        public override T Accept<T>(IAstVisitor<T> v) => v.VisitFunctionDef(this);
    }

    /// <summary><c>global x, y</c> o <c>nonlocal x</c>.</summary>
    public sealed class ScopeStmt : Stmt
    {
        public ScopeStmt(TokenType kind, IReadOnlyList<string> names, int line, int column) : base(line, column)
        {
            Kind = kind;
            Names = names;
        }
        public TokenType Kind { get; }
        public IReadOnlyList<string> Names { get; }
        public override T Accept<T>(IAstVisitor<T> v) => v.VisitScope(this);
    }

    /// <summary>Nombre importado con alias opcional: <c>LED</c>, <c>sleep as espera</c>.</summary>
    public sealed class ImportName
    {
        public ImportName(string name, string alias)
        {
            Name = name;
            Alias = alias;
        }
        public string Name { get; }
        public string Alias { get; }
        public string BoundName => Alias ?? Name;
    }

    /// <summary>
    /// <c>import modulo [as alias]</c> (<see cref="Names"/> vacío) o
    /// <c>from modulo import a, b as c</c>.
    /// </summary>
    public sealed class ImportStmt : Stmt
    {
        public ImportStmt(string module, string moduleAlias, IReadOnlyList<ImportName> names, int line, int column)
            : base(line, column)
        {
            Module = module;
            ModuleAlias = moduleAlias;
            Names = names;
        }
        public string Module { get; }
        public string ModuleAlias { get; }
        public IReadOnlyList<ImportName> Names { get; }
        public bool IsFrom => Names.Count > 0;
        public override T Accept<T>(IAstVisitor<T> v) => v.VisitImport(this);
    }

    /// <summary>Un <c>except [Tipo] [as nombre]:</c> de un <see cref="TryStmt"/>.</summary>
    public sealed class ExceptHandler
    {
        public ExceptHandler(string typeName, string alias, IReadOnlyList<Stmt> body, int line, int column)
        {
            TypeName = typeName;
            Alias = alias;
            Body = body;
            Line = line;
            Column = column;
        }

        /// <summary>Tipo capturado (<c>KeyboardInterrupt</c>, <c>Exception</c>...); <c>null</c> = cualquiera.</summary>
        public string TypeName { get; }

        /// <summary>Nombre con el que se guarda la excepción (<c>except Exception as e</c>); puede ser <c>null</c>.</summary>
        public string Alias { get; }

        public IReadOnlyList<Stmt> Body { get; }
        public int Line { get; }
        public int Column { get; }
    }

    /// <summary>
    /// <c>try / except / finally</c> básico (decidido el 18/11/2026). Permite el patrón de las
    /// prácticas: <c>try: main() except KeyboardInterrupt: ... finally: GPIO.cleanup()</c>.
    /// </summary>
    public sealed class TryStmt : Stmt
    {
        public TryStmt(IReadOnlyList<Stmt> body, IReadOnlyList<ExceptHandler> handlers, IReadOnlyList<Stmt> finallyBody,
                       int line, int column) : base(line, column)
        {
            Body = body;
            Handlers = handlers;
            FinallyBody = finallyBody;
        }
        public IReadOnlyList<Stmt> Body { get; }
        public IReadOnlyList<ExceptHandler> Handlers { get; }

        /// <summary>Vacío si no hay <c>finally</c>.</summary>
        public IReadOnlyList<Stmt> FinallyBody { get; }

        public override T Accept<T>(IAstVisitor<T> v) => v.VisitTry(this);
    }

    // ------------------------------------------------------------------ Visitor

    /// <summary>Una operación sobre el árbol (patrón Visitor): un método por clase de nodo.</summary>
    public interface IAstVisitor<out T>
    {
        T VisitModule(ModuleNode node);

        T VisitNumber(NumberExpr node);
        T VisitString(StringExpr node);
        T VisitConstant(ConstantExpr node);
        T VisitName(NameExpr node);
        T VisitUnary(UnaryExpr node);
        T VisitBinary(BinaryExpr node);
        T VisitLogical(LogicalExpr node);
        T VisitCompare(CompareExpr node);
        T VisitCall(CallExpr node);
        T VisitAttribute(AttributeExpr node);
        T VisitIndex(IndexExpr node);
        T VisitSequence(SequenceExpr node);
        T VisitLambda(LambdaExpr node);
        T VisitConditional(ConditionalExpr node);

        T VisitExprStmt(ExprStmt node);
        T VisitAssign(AssignStmt node);
        T VisitAugAssign(AugAssignStmt node);
        T VisitIf(IfStmt node);
        T VisitWhile(WhileStmt node);
        T VisitFor(ForStmt node);
        T VisitSimple(SimpleStmt node);
        T VisitReturn(ReturnStmt node);
        T VisitFunctionDef(FunctionDefStmt node);
        T VisitScope(ScopeStmt node);
        T VisitImport(ImportStmt node);
        T VisitTry(TryStmt node);
    }
}
