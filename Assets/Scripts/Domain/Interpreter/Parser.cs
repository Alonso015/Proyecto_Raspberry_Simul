using System.Collections.Generic;
using ImatPiSim.Domain.Interpreter.Ast;

namespace ImatPiSim.Domain.Interpreter
{
    /// <summary>
    /// Analizador sintáctico de <b>descenso recursivo</b>: convierte los tokens en el árbol
    /// sintáctico (<see cref="ModuleNode"/>).
    /// </summary>
    /// <remarks>
    /// <para>Cada regla de la gramática es un método. La precedencia de los operadores sale de
    /// cómo se llaman entre sí: el método de la suma llama al del producto para leer cada
    /// sumando, así que en <c>2 + 3 * 4</c> el producto queda "más abajo" en el árbol y se
    /// calcula antes. Gramática completa en docs/arquitectura.md (etapa E7).</para>
    /// <para>Construcciones de Python fuera del subconjunto (docs/requisitos.md §2.5) dan un error
    /// que dice "no está soportado", en vez de un error genérico.</para>
    /// </remarks>
    public sealed class Parser
    {
        private readonly IReadOnlyList<Token> _tokens;
        private int _pos;

        private Parser(IReadOnlyList<Token> tokens) => _tokens = tokens;

        /// <summary>Analiza el texto del programa (léxico + sintáctico).</summary>
        /// <exception cref="LexerException">Error léxico.</exception>
        /// <exception cref="ParseException">Error sintáctico.</exception>
        public static ModuleNode Parse(string source) => new Parser(Lexer.Tokenize(source)).ParseModule();

        /// <summary>Analiza una única expresión (útil en pruebas y en la consola).</summary>
        public static Expr ParseExpression(string source)
        {
            var p = new Parser(Lexer.Tokenize(source));
            var e = p.Expression();
            p.Match(TokenType.Newline);
            p.Expect(TokenType.EndOfFile, "se esperaba el final de la expresión");
            return e;
        }

        // ================================================================== sentencias

        private ModuleNode ParseModule()
        {
            var body = new List<Stmt>();
            while (!Check(TokenType.EndOfFile))
            {
                if (Match(TokenType.Newline)) continue;
                body.AddRange(Statement());
            }
            return new ModuleNode(body);
        }

        /// <summary>Una sentencia compuesta, o una línea de sentencias simples separadas por ';'.</summary>
        private IEnumerable<Stmt> Statement()
        {
            switch (Peek.Type)
            {
                case TokenType.If: return new[] { IfStatement() };
                case TokenType.While: return new[] { WhileStatement() };
                case TokenType.For: return new[] { ForStatement() };
                case TokenType.Def: return new[] { FunctionDef() };
                case TokenType.Try: return new[] { TryStatement() };
                case TokenType.Indent: throw Error(Peek, "sangría inesperada");
                default: return SimpleStatementLine();
            }
        }

        private List<Stmt> SimpleStatementLine()
        {
            var list = new List<Stmt> { SmallStatement() };
            while (Match(TokenType.Semicolon))
            {
                if (Check(TokenType.Newline) || Check(TokenType.EndOfFile)) break;
                list.Add(SmallStatement());
            }
            if (!Match(TokenType.Newline) && !Check(TokenType.EndOfFile))
                throw Error(Peek, $"se esperaba el final de la línea y hay '{Describe(Peek)}'");
            return list;
        }

        private Stmt SmallStatement()
        {
            var t = Peek;
            switch (t.Type)
            {
                case TokenType.Pass:
                case TokenType.Break:
                case TokenType.Continue:
                    Advance();
                    return new SimpleStmt(t.Type, t.Line, t.Column);
                case TokenType.Return:
                    Advance();
                    var value = Check(TokenType.Newline) || Check(TokenType.Semicolon) || Check(TokenType.EndOfFile)
                        ? null : ExpressionList();
                    return new ReturnStmt(value, t.Line, t.Column);
                case TokenType.Global:
                case TokenType.Nonlocal:
                    Advance();
                    var names = new List<string> { ExpectName("se esperaba un nombre") };
                    while (Match(TokenType.Comma)) names.Add(ExpectName("se esperaba un nombre"));
                    return new ScopeStmt(t.Type, names, t.Line, t.Column);
                case TokenType.Import:
                    return ImportStatement();
                case TokenType.From:
                    return FromImportStatement();
                default:
                    if (UnsupportedStatement(t, out var message)) throw Error(t, message);
                    return ExpressionStatement();
            }
        }

        private Stmt ExpressionStatement()
        {
            var start = Peek;
            var first = ExpressionList();

            if (IsAugmentedAssign(Peek.Type, out var op))
            {
                var opToken = Advance();
                CheckAssignTarget(first, opToken);
                return new AugAssignStmt(first, op, Expression(), start.Line, start.Column);
            }

            if (!Check(TokenType.Assign)) return new ExprStmt(first, start.Line, start.Column);

            // a = b = valor: todos menos el último son destinos.
            var targets = new List<Expr> { first };
            Expr value = null;
            while (Match(TokenType.Assign))
            {
                value = ExpressionList();
                if (Check(TokenType.Assign)) targets.Add(value);
            }
            foreach (var target in targets) CheckAssignTarget(target, start);
            return new AssignStmt(targets, value, start.Line, start.Column);
        }

        private void CheckAssignTarget(Expr target, Token at)
        {
            if (target is NameExpr || target is AttributeExpr || target is IndexExpr) return;
            if (target is SequenceExpr)
                throw Error(at, "la asignación a varias variables a la vez (a, b = ...) no está soportada");
            throw Error(at, "no se puede asignar un valor a esta expresión");
        }

        private IfStmt IfStatement()
        {
            var t = Advance();   // if / elif
            var condition = Expression();
            var body = Block();
            IReadOnlyList<Stmt> elseBody = new List<Stmt>();
            if (Check(TokenType.Elif)) elseBody = new List<Stmt> { IfStatement() };
            else if (Match(TokenType.Else)) elseBody = Block();
            return new IfStmt(condition, body, elseBody, t.Line, t.Column);
        }

        private WhileStmt WhileStatement()
        {
            var t = Advance();
            var condition = Expression();
            var body = Block();
            if (Check(TokenType.Else)) throw Error(Peek, "el 'else' de un bucle no está soportado");
            return new WhileStmt(condition, body, t.Line, t.Column);
        }

        private ForStmt ForStatement()
        {
            var t = Advance();
            var variable = ExpectName("se esperaba el nombre de la variable del bucle");
            if (Check(TokenType.Comma))
                throw Error(Peek, "recorrer con varias variables a la vez (for a, b in ...) no está soportado");
            Expect(TokenType.In, "se esperaba 'in' en el bucle for");
            var iterable = ExpressionList();
            var body = Block();
            if (Check(TokenType.Else)) throw Error(Peek, "el 'else' de un bucle no está soportado");
            return new ForStmt(variable, iterable, body, t.Line, t.Column);
        }

        /// <summary>
        /// <c>try:</c> bloque, cero o más <c>except [Tipo] [as nombre]:</c> y <c>finally:</c> opcional
        /// (al menos uno de los dos). El <c>except</c> sin tipo tiene que ser el último.
        /// </summary>
        private TryStmt TryStatement()
        {
            var t = Advance();
            var body = Block();
            var handlers = new List<ExceptHandler>();
            while (Check(TokenType.Except))
            {
                var e = Advance();
                if (handlers.Count > 0 && handlers[handlers.Count - 1].TypeName == null)
                    throw Error(e, "un 'except' sin tipo tiene que ser el último");
                string type = null, alias = null;
                if (!Check(TokenType.Colon))
                {
                    if (Check(TokenType.LeftParen))
                        throw Error(Peek, "capturar varios tipos a la vez (except (A, B)) no está soportado: usa un except por tipo");
                    type = ExpectName("se esperaba el tipo de excepción");
                    while (Match(TokenType.Dot)) type += "." + ExpectName("se esperaba un nombre tras '.'");
                    if (Match(TokenType.As)) alias = ExpectName("se esperaba un nombre tras 'as'");
                }
                handlers.Add(new ExceptHandler(type, alias, Block(), e.Line, e.Column));
            }
            if (Check(TokenType.Else)) throw Error(Peek, "el 'else' de un try no está soportado");
            IReadOnlyList<Stmt> finallyBody = new List<Stmt>();
            if (Match(TokenType.Finally)) finallyBody = Block();
            if (handlers.Count == 0 && finallyBody.Count == 0)
                throw Error(t, "un 'try' necesita al menos un 'except' o un 'finally'");
            return new TryStmt(body, handlers, finallyBody, t.Line, t.Column);
        }

        private FunctionDefStmt FunctionDef()
        {
            var t = Advance();
            var name = ExpectName("se esperaba el nombre de la función");
            Expect(TokenType.LeftParen, "se esperaba '(' tras el nombre de la función");
            var parameters = Parameters(TokenType.RightParen);
            Expect(TokenType.RightParen, "se esperaba ')' al final de los parámetros");
            if (Match(TokenType.Arrow)) Expression();   // anotación de retorno: se ignora
            var body = Block();
            return new FunctionDefStmt(name, parameters, body, t.Line, t.Column);
        }

        /// <summary>Parámetros con valor por defecto opcional, hasta <paramref name="end"/>.</summary>
        private List<Parameter> Parameters(TokenType end)
        {
            var list = new List<Parameter>();
            var seen = new HashSet<string>();
            bool defaults = false;
            while (!Check(end))
            {
                if (Check(TokenType.Star) || Check(TokenType.DoubleStar))
                    throw Error(Peek, "los parámetros *args y **kwargs no están soportados");
                var nameToken = Peek;
                var name = ExpectName("se esperaba el nombre de un parámetro");
                if (!seen.Add(name)) throw Error(nameToken, $"el parámetro '{name}' está repetido");
                // Anotación de tipo (def f(x: int)): se ignora. En una lambda ':' cierra los parámetros.
                if (end == TokenType.RightParen && Match(TokenType.Colon)) Expression();
                Expr def = null;
                if (Match(TokenType.Assign))
                {
                    def = Expression();
                    defaults = true;
                }
                else if (defaults)
                    throw Error(nameToken, $"el parámetro '{name}' va tras uno con valor por defecto y no tiene");
                list.Add(new Parameter(name, def));
                if (!Match(TokenType.Comma)) break;
            }
            return list;
        }

        /// <summary>':' seguido de un bloque sangrado, o de sentencias simples en la misma línea.</summary>
        private List<Stmt> Block()
        {
            Expect(TokenType.Colon, "se esperaba ':' al final de la línea");
            if (!Match(TokenType.Newline)) return SimpleStatementLine();   // if x: y = 1
            if (!Match(TokenType.Indent))
                throw Error(Peek, "se esperaba un bloque sangrado después de ':'");
            var body = new List<Stmt>();
            while (!Check(TokenType.Dedent) && !Check(TokenType.EndOfFile))
            {
                if (Match(TokenType.Newline)) continue;
                body.AddRange(Statement());
            }
            Match(TokenType.Dedent);
            return body;
        }

        private ImportStmt ImportStatement()
        {
            var t = Advance();
            var module = DottedName();
            string alias = Match(TokenType.As) ? ExpectName("se esperaba un nombre tras 'as'") : null;
            if (Check(TokenType.Comma)) throw Error(Peek, "importa los módulos de uno en uno, en líneas separadas");
            return new ImportStmt(module, alias, new List<ImportName>(), t.Line, t.Column);
        }

        private ImportStmt FromImportStatement()
        {
            var t = Advance();
            var module = DottedName();
            Expect(TokenType.Import, "se esperaba 'import'");
            if (Check(TokenType.Star))
                throw Error(Peek, $"'from {module} import *' no está soportado: escribe los nombres que uses");
            bool parens = Match(TokenType.LeftParen);
            var names = new List<ImportName>();
            do
            {
                if (parens && Check(TokenType.RightParen)) break;
                var name = ExpectName("se esperaba un nombre que importar");
                string alias = Match(TokenType.As) ? ExpectName("se esperaba un nombre tras 'as'") : null;
                names.Add(new ImportName(name, alias));
            } while (Match(TokenType.Comma));
            if (parens) Expect(TokenType.RightParen, "se esperaba ')'");
            return new ImportStmt(module, null, names, t.Line, t.Column);
        }

        private string DottedName()
        {
            var name = ExpectName("se esperaba el nombre de un módulo");
            while (Match(TokenType.Dot)) name += "." + ExpectName("se esperaba un nombre tras '.'");
            return name;
        }

        // ================================================================== expresiones

        /// <summary>Una expresión, o varias separadas por comas (una tupla sin paréntesis).</summary>
        private Expr ExpressionList()
        {
            var first = Expression();
            if (!Check(TokenType.Comma)) return first;
            var items = new List<Expr> { first };
            while (Match(TokenType.Comma))
            {
                if (!StartsExpression(Peek.Type)) break;
                items.Add(Expression());
            }
            return new SequenceExpr(true, items, first.Line, first.Column);
        }

        // expression := lambda | or_test ['if' or_test 'else' expression]
        private Expr Expression()
        {
            if (Check(TokenType.Lambda)) return Lambda();
            var value = OrTest();
            if (Check(TokenType.If))
            {
                var t = Advance();
                var condition = OrTest();
                Expect(TokenType.Else, "se esperaba 'else' en la expresión condicional");
                var otherwise = Expression();
                return new ConditionalExpr(condition, value, otherwise, t.Line, t.Column);
            }
            return value;
        }

        private Expr Lambda()
        {
            var t = Advance();
            var parameters = Parameters(TokenType.Colon);
            Expect(TokenType.Colon, "se esperaba ':' en la lambda");
            return new LambdaExpr(parameters, Expression(), t.Line, t.Column);
        }

        private Expr OrTest()
        {
            var left = AndTest();
            while (Check(TokenType.Or))
            {
                var t = Advance();
                left = new LogicalExpr(left, TokenType.Or, AndTest(), t.Line, t.Column);
            }
            return left;
        }

        private Expr AndTest()
        {
            var left = NotTest();
            while (Check(TokenType.And))
            {
                var t = Advance();
                left = new LogicalExpr(left, TokenType.And, NotTest(), t.Line, t.Column);
            }
            return left;
        }

        private Expr NotTest()
        {
            if (!Check(TokenType.Not)) return Comparison();
            var t = Advance();
            return new UnaryExpr(TokenType.Not, NotTest(), t.Line, t.Column);
        }

        private Expr Comparison()
        {
            var first = BitOr();
            var ops = new List<CompareOp>();
            var rest = new List<Expr>();
            while (TryCompareOperator(out var op))
            {
                ops.Add(op);
                rest.Add(BitOr());
            }
            return ops.Count == 0 ? first : new CompareExpr(first, ops, rest, first.Line, first.Column);
        }

        private bool TryCompareOperator(out CompareOp op)
        {
            switch (Peek.Type)
            {
                case TokenType.Less: op = CompareOp.Less; break;
                case TokenType.Greater: op = CompareOp.Greater; break;
                case TokenType.LessEqual: op = CompareOp.LessEqual; break;
                case TokenType.GreaterEqual: op = CompareOp.GreaterEqual; break;
                case TokenType.EqualEqual: op = CompareOp.Equal; break;
                case TokenType.NotEqual: op = CompareOp.NotEqual; break;
                case TokenType.In: op = CompareOp.In; break;
                case TokenType.Is:
                    Advance();
                    op = Match(TokenType.Not) ? CompareOp.IsNot : CompareOp.Is;
                    return true;
                case TokenType.Not when PeekAt(1).Type == TokenType.In:
                    Advance();
                    op = CompareOp.NotIn;
                    break;
                default:
                    op = default;
                    return false;
            }
            Advance();
            return true;
        }

        private Expr BitOr() => LeftAssoc(BitXor, TokenType.Pipe);
        private Expr BitXor() => LeftAssoc(BitAnd, TokenType.Caret);
        private Expr BitAnd() => LeftAssoc(Shift, TokenType.Ampersand);
        private Expr Shift() => LeftAssoc(Arith, TokenType.LeftShift, TokenType.RightShift);
        private Expr Arith() => LeftAssoc(Term, TokenType.Plus, TokenType.Minus);
        private Expr Term() => LeftAssoc(Factor, TokenType.Star, TokenType.Slash, TokenType.DoubleSlash, TokenType.Percent);

        /// <summary>Operadores binarios asociativos por la izquierda: a - b - c = (a - b) - c.</summary>
        private Expr LeftAssoc(System.Func<Expr> next, params TokenType[] ops)
        {
            var left = next();
            while (System.Array.IndexOf(ops, Peek.Type) >= 0)
            {
                var t = Advance();
                left = new BinaryExpr(left, t.Type, next(), t.Line, t.Column);
            }
            return left;
        }

        // factor := ('+' | '-' | '~') factor | power
        private Expr Factor()
        {
            if (Check(TokenType.Plus) || Check(TokenType.Minus) || Check(TokenType.Tilde))
            {
                var t = Advance();
                return new UnaryExpr(t.Type, Factor(), t.Line, t.Column);
            }
            return Power();
        }

        // power := primary ['**' factor]   (asociativo por la derecha; -2**2 = -(2**2))
        private Expr Power()
        {
            var basis = Primary();
            if (!Check(TokenType.DoubleStar)) return basis;
            var t = Advance();
            return new BinaryExpr(basis, TokenType.DoubleStar, Factor(), t.Line, t.Column);
        }

        // primary := atom ( '(' args ')' | '.' NAME | '[' expr ']' )*
        private Expr Primary()
        {
            var expr = Atom();
            while (true)
            {
                if (Check(TokenType.LeftParen)) expr = Call(expr);
                else if (Check(TokenType.Dot))
                {
                    var t = Advance();
                    expr = new AttributeExpr(expr, ExpectName("se esperaba un nombre tras '.'"), t.Line, t.Column);
                }
                else if (Check(TokenType.LeftBracket))
                {
                    var t = Advance();
                    if (Check(TokenType.Colon)) throw Error(Peek, "los cortes con ':' (lista[a:b]) no están soportados");
                    var index = Expression();
                    if (Check(TokenType.Colon)) throw Error(Peek, "los cortes con ':' (lista[a:b]) no están soportados");
                    Expect(TokenType.RightBracket, "se esperaba ']'");
                    expr = new IndexExpr(expr, index, t.Line, t.Column);
                }
                else return expr;
            }
        }

        private CallExpr Call(Expr callee)
        {
            var open = Advance();
            var args = new List<Expr>();
            var keywords = new List<KeywordArg>();
            var names = new HashSet<string>();
            while (!Check(TokenType.RightParen))
            {
                if (Check(TokenType.Star) || Check(TokenType.DoubleStar))
                    throw Error(Peek, "los argumentos *args y **kwargs no están soportados");
                if (Check(TokenType.Identifier) && PeekAt(1).Type == TokenType.Assign)
                {
                    var nameToken = Advance();
                    Advance();   // '='
                    if (!names.Add(nameToken.Lexeme))
                        throw Error(nameToken, $"el argumento '{nameToken.Lexeme}' está repetido");
                    keywords.Add(new KeywordArg(nameToken.Lexeme, Expression()));
                }
                else
                {
                    if (keywords.Count > 0)
                        throw Error(Peek, "un argumento sin nombre no puede ir después de uno con nombre");
                    var arg = Expression();
                    if (Check(TokenType.For))
                        throw Error(Peek, "las expresiones generadoras (x for x in ...) no están soportadas");
                    args.Add(arg);
                }
                if (!Match(TokenType.Comma)) break;
            }
            Expect(TokenType.RightParen, "se esperaba ')' al final de la llamada");
            return new CallExpr(callee, args, keywords, open.Line, open.Column);
        }

        private Expr Atom()
        {
            var t = Peek;
            switch (t.Type)
            {
                case TokenType.Identifier:
                    Advance();
                    return new NameExpr(t.Lexeme, t.Line, t.Column);
                case TokenType.Integer:
                case TokenType.Float:
                    Advance();
                    return new NumberExpr(t.Value, t.Line, t.Column);
                case TokenType.String:
                    Advance();
                    var text = (string)t.Value;
                    while (Check(TokenType.String)) text += (string)Advance().Value;   // 'a' 'b' == 'ab'
                    return new StringExpr(text, t.Line, t.Column);
                case TokenType.True:
                case TokenType.False:
                case TokenType.None:
                    Advance();
                    return new ConstantExpr(t.Type, t.Line, t.Column);
                case TokenType.LeftParen:
                    return Parenthesized();
                case TokenType.LeftBracket:
                    return ListLiteral();
                case TokenType.LeftBrace:
                    throw Error(t, "los diccionarios y conjuntos ({...}) no están soportados");
                default:
                    if (UnsupportedStatement(t, out var message)) throw Error(t, message);
                    throw Error(t, t.Type == TokenType.Newline || t.Type == TokenType.EndOfFile
                        ? "la línea termina sin completar la expresión"
                        : $"no se esperaba '{Describe(t)}' aquí");
            }
        }

        // '(' ')' = tupla vacía; '(' e ')' = agrupación; '(' e ',' ... ')' = tupla
        private Expr Parenthesized()
        {
            var open = Advance();
            if (Match(TokenType.RightParen)) return new SequenceExpr(true, new List<Expr>(), open.Line, open.Column);
            var first = Expression();
            if (Check(TokenType.For))
                throw Error(Peek, "las expresiones generadoras (x for x in ...) no están soportadas");
            if (!Check(TokenType.Comma))
            {
                Expect(TokenType.RightParen, "se esperaba ')'");
                return first;
            }
            var items = new List<Expr> { first };
            while (Match(TokenType.Comma) && !Check(TokenType.RightParen)) items.Add(Expression());
            Expect(TokenType.RightParen, "se esperaba ')'");
            return new SequenceExpr(true, items, open.Line, open.Column);
        }

        private Expr ListLiteral()
        {
            var open = Advance();
            var items = new List<Expr>();
            while (!Check(TokenType.RightBracket))
            {
                items.Add(Expression());
                if (Check(TokenType.For))
                    throw Error(Peek, "las listas por comprensión ([x for x in ...]) no están soportadas: usa un bucle for y append");
                if (!Match(TokenType.Comma)) break;
            }
            Expect(TokenType.RightBracket, "se esperaba ']'");
            return new SequenceExpr(false, items, open.Line, open.Column);
        }

        // ================================================================== utilidades

        private static bool UnsupportedStatement(Token t, out string message)
        {
            switch (t.Type)
            {
                case TokenType.Except:
                case TokenType.Finally:
                    message = $"'{t.Lexeme}' sin un 'try' antes, o mal sangrado respecto a su 'try'";
                    return true;
                case TokenType.Raise:
                    message = "lanzar excepciones (raise) no está soportado; sí se pueden capturar con try / except";
                    return true;
                case TokenType.Class: message = "definir clases (class) no está soportado"; return true;
                case TokenType.With: message = "'with' no está soportado"; return true;
                case TokenType.Yield: message = "'yield' (generadores) no está soportado"; return true;
                case TokenType.Del: message = "'del' no está soportado"; return true;
                case TokenType.Assert: message = "'assert' no está soportado"; return true;
                case TokenType.Async:
                case TokenType.Await: message = "'async' / 'await' no están soportados"; return true;
                default: message = null; return false;
            }
        }

        private static bool IsAugmentedAssign(TokenType type, out TokenType op)
        {
            switch (type)
            {
                case TokenType.PlusAssign: op = TokenType.Plus; return true;
                case TokenType.MinusAssign: op = TokenType.Minus; return true;
                case TokenType.StarAssign: op = TokenType.Star; return true;
                case TokenType.SlashAssign: op = TokenType.Slash; return true;
                case TokenType.DoubleSlashAssign: op = TokenType.DoubleSlash; return true;
                case TokenType.PercentAssign: op = TokenType.Percent; return true;
                case TokenType.DoubleStarAssign: op = TokenType.DoubleStar; return true;
                case TokenType.LeftShiftAssign: op = TokenType.LeftShift; return true;
                case TokenType.RightShiftAssign: op = TokenType.RightShift; return true;
                case TokenType.AmpersandAssign: op = TokenType.Ampersand; return true;
                case TokenType.PipeAssign: op = TokenType.Pipe; return true;
                case TokenType.CaretAssign: op = TokenType.Caret; return true;
                default: op = default; return false;
            }
        }

        private static bool StartsExpression(TokenType t) =>
            t == TokenType.Identifier || t == TokenType.Integer || t == TokenType.Float || t == TokenType.String ||
            t == TokenType.True || t == TokenType.False || t == TokenType.None || t == TokenType.LeftParen ||
            t == TokenType.LeftBracket || t == TokenType.Minus || t == TokenType.Plus || t == TokenType.Tilde ||
            t == TokenType.Not || t == TokenType.Lambda;

        private Token Peek => _tokens[_pos];
        private Token PeekAt(int offset) => _tokens[System.Math.Min(_pos + offset, _tokens.Count - 1)];
        private bool Check(TokenType type) => Peek.Type == type;

        private Token Advance()
        {
            var t = _tokens[_pos];
            if (t.Type != TokenType.EndOfFile) _pos++;
            return t;
        }

        private bool Match(TokenType type)
        {
            if (!Check(type)) return false;
            Advance();
            return true;
        }

        private Token Expect(TokenType type, string message)
        {
            if (Check(type)) return Advance();
            throw Error(Peek, message);
        }

        private string ExpectName(string message)
        {
            if (Check(TokenType.Identifier)) return Advance().Lexeme;
            if (Keywords.ByText.ContainsKey(Peek.Lexeme ?? ""))
                throw Error(Peek, $"'{Peek.Lexeme}' es una palabra reservada y no se puede usar como nombre");
            throw Error(Peek, message);
        }

        private static string Describe(Token t)
        {
            switch (t.Type)
            {
                case TokenType.Newline: return "fin de línea";
                case TokenType.EndOfFile: return "fin del programa";
                case TokenType.Indent: return "sangría";
                case TokenType.Dedent: return "fin de bloque";
                default: return t.Lexeme;
            }
        }

        private static ParseException Error(Token at, string message) => new ParseException(message, at.Line, at.Column);
    }
}
