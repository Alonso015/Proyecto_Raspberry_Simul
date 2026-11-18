using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ImatPiSim.Domain.Interpreter.Ast;

namespace ImatPiSim.Domain.Interpreter
{
    /// <summary>
    /// Visitor que convierte el árbol en texto, para verlo y para los tests.
    /// </summary>
    /// <remarks>
    /// Las expresiones se escriben en forma prefija con paréntesis, que deja ver la estructura
    /// del árbol sin ambigüedad: <c>2 + 3 * 4</c> → <c>(+ 2 (* 3 4))</c>. Las sentencias se
    /// escriben una por línea, con dos espacios de sangría por nivel.
    /// </remarks>
    public sealed class AstPrinter : IAstVisitor<string>
    {
        private int _indent;

        public static string Print(Node node) => node.Accept(new AstPrinter());

        // ------------------------------------------------------------------ expresiones

        public string VisitNumber(NumberExpr n) =>
            n.Value is double d ? d.ToString("R", CultureInfo.InvariantCulture) : n.Value.ToString();

        public string VisitString(StringExpr n) => "'" + n.Value.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n") + "'";

        public string VisitConstant(ConstantExpr n) => n.Kind.ToString();

        public string VisitName(NameExpr n) => n.Name;

        public string VisitUnary(UnaryExpr n) => $"({Op(n.Operator)} {n.Operand.Accept(this)})";

        public string VisitBinary(BinaryExpr n) => $"({Op(n.Operator)} {n.Left.Accept(this)} {n.Right.Accept(this)})";

        public string VisitLogical(LogicalExpr n) => $"({Op(n.Operator)} {n.Left.Accept(this)} {n.Right.Accept(this)})";

        public string VisitCompare(CompareExpr n)
        {
            var sb = new StringBuilder("(cmp ").Append(n.First.Accept(this));
            for (int i = 0; i < n.Operators.Count; i++)
                sb.Append(' ').Append(Cmp(n.Operators[i])).Append(' ').Append(n.Operands[i].Accept(this));
            return sb.Append(')').ToString();
        }

        public string VisitCall(CallExpr n)
        {
            var parts = new List<string> { n.Callee.Accept(this) };
            parts.AddRange(n.Arguments.Select(a => a.Accept(this)));
            parts.AddRange(n.Keywords.Select(k => k.Name + "=" + k.Value.Accept(this)));
            return "(call " + string.Join(" ", parts) + ")";
        }

        public string VisitAttribute(AttributeExpr n) => $"(. {n.Target.Accept(this)} {n.Name})";

        public string VisitIndex(IndexExpr n) => $"([] {n.Target.Accept(this)} {n.Index.Accept(this)})";

        public string VisitSequence(SequenceExpr n) =>
            (n.IsTuple ? "(tuple" : "(list") + string.Concat(n.Items.Select(i => " " + i.Accept(this))) + ")";

        public string VisitLambda(LambdaExpr n) => $"(lambda ({Params(n.Parameters)}) {n.Body.Accept(this)})";

        public string VisitConditional(ConditionalExpr n) =>
            $"(if-expr {n.Condition.Accept(this)} {n.WhenTrue.Accept(this)} {n.WhenFalse.Accept(this)})";

        // ------------------------------------------------------------------ sentencias

        public string VisitModule(ModuleNode n) => Lines(n.Body).TrimEnd('\n');

        public string VisitExprStmt(ExprStmt n) => Line(n.Expression.Accept(this));

        public string VisitAssign(AssignStmt n) =>
            Line(string.Join(" = ", n.Targets.Select(t => t.Accept(this))) + " = " + n.Value.Accept(this));

        public string VisitAugAssign(AugAssignStmt n) =>
            Line($"{n.Target.Accept(this)} {Op(n.Operator)}= {n.Value.Accept(this)}");

        public string VisitIf(IfStmt n)
        {
            var sb = new StringBuilder(Line("if " + n.Condition.Accept(this) + ":"));
            sb.Append(Block(n.Body));
            if (n.ElseBody.Count > 0) sb.Append(Line("else:")).Append(Block(n.ElseBody));
            return sb.ToString();
        }

        public string VisitWhile(WhileStmt n) => Line("while " + n.Condition.Accept(this) + ":") + Block(n.Body);

        public string VisitFor(ForStmt n) =>
            Line($"for {n.Variable} in {n.Iterable.Accept(this)}:") + Block(n.Body);

        public string VisitSimple(SimpleStmt n) => Line(n.Kind.ToString().ToLowerInvariant());

        public string VisitReturn(ReturnStmt n) => Line(n.Value == null ? "return" : "return " + n.Value.Accept(this));

        public string VisitFunctionDef(FunctionDefStmt n) =>
            Line($"def {n.Name}({Params(n.Parameters)}):") + Block(n.Body);

        public string VisitScope(ScopeStmt n) => Line(n.Kind.ToString().ToLowerInvariant() + " " + string.Join(", ", n.Names));

        public string VisitImport(ImportStmt n) => n.IsFrom
            ? Line($"from {n.Module} import " + string.Join(", ", n.Names.Select(x => x.Alias == null ? x.Name : x.Name + " as " + x.Alias)))
            : Line($"import {n.Module}" + (n.ModuleAlias == null ? "" : " as " + n.ModuleAlias));

        public string VisitTry(TryStmt n)
        {
            var sb = new StringBuilder(Line("try:")).Append(Block(n.Body));
            foreach (var h in n.Handlers)
            {
                string head = "except" + (h.TypeName == null ? "" : " " + h.TypeName) + (h.Alias == null ? "" : " as " + h.Alias);
                sb.Append(Line(head + ":")).Append(Block(h.Body));
            }
            if (n.FinallyBody.Count > 0) sb.Append(Line("finally:")).Append(Block(n.FinallyBody));
            return sb.ToString();
        }

        // ------------------------------------------------------------------ utilidades

        private string Line(string text) => new string(' ', _indent * 2) + text + "\n";

        private string Lines(IEnumerable<Stmt> stmts) => string.Concat(stmts.Select(s => s.Accept(this)));

        private string Block(IEnumerable<Stmt> stmts)
        {
            _indent++;
            var text = Lines(stmts);
            _indent--;
            return text;
        }

        private string Params(IEnumerable<Parameter> ps) =>
            string.Join(" ", ps.Select(p => p.Default == null ? p.Name : p.Name + "=" + p.Default.Accept(this)));

        private static string Op(TokenType t)
        {
            switch (t)
            {
                case TokenType.Plus: return "+";
                case TokenType.Minus: return "-";
                case TokenType.Star: return "*";
                case TokenType.Slash: return "/";
                case TokenType.DoubleSlash: return "//";
                case TokenType.Percent: return "%";
                case TokenType.DoubleStar: return "**";
                case TokenType.LeftShift: return "<<";
                case TokenType.RightShift: return ">>";
                case TokenType.Ampersand: return "&";
                case TokenType.Pipe: return "|";
                case TokenType.Caret: return "^";
                case TokenType.Tilde: return "~";
                case TokenType.Not: return "not";
                case TokenType.And: return "and";
                case TokenType.Or: return "or";
                default: return t.ToString();
            }
        }

        private static string Cmp(CompareOp op)
        {
            switch (op)
            {
                case CompareOp.Less: return "<";
                case CompareOp.Greater: return ">";
                case CompareOp.LessEqual: return "<=";
                case CompareOp.GreaterEqual: return ">=";
                case CompareOp.Equal: return "==";
                case CompareOp.NotEqual: return "!=";
                case CompareOp.In: return "in";
                case CompareOp.NotIn: return "not-in";
                case CompareOp.Is: return "is";
                default: return "is-not";
            }
        }
    }
}
