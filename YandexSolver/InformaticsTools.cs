using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace YandexSolver
{
    public static class InformaticsTools
    {
        private const string Digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        public static string ConvertNumberBases(string input, int fromBase)
        {
            string raw = (input ?? string.Empty).Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(raw))
            {
                throw new ArgumentException("Введите число.");
            }
            if (fromBase < 2 || fromBase > 36)
            {
                throw new ArgumentException("Основание должно быть от 2 до 36.");
            }

            bool negative = false;
            if (raw.StartsWith("-"))
            {
                negative = true;
                raw = raw.Substring(1);
            }
            if (string.IsNullOrEmpty(raw))
            {
                throw new ArgumentException("Некорректное число.");
            }

            ulong value = 0;
            foreach (char ch in raw)
            {
                int d = Digits.IndexOf(ch);
                if (d < 0 || d >= fromBase)
                {
                    throw new ArgumentException(string.Format("Символ '{0}' недопустим для основания {1}.", ch, fromBase));
                }
                checked
                {
                    value = value * (ulong)fromBase + (ulong)d;
                }
            }

            string sign = negative && value > 0 ? "-" : string.Empty;
            return string.Format(
                "BIN (2):  {0}{1}\r\nOCT (8):  {0}{2}\r\nDEC (10): {0}{3}\r\nHEX (16): {0}{4}",
                sign,
                ToBase(value, 2),
                ToBase(value, 8),
                value.ToString(CultureInfo.InvariantCulture),
                ToBase(value, 16));
        }

        private static string ToBase(ulong value, int toBase)
        {
            if (value == 0)
            {
                return "0";
            }
            var sb = new StringBuilder();
            while (value > 0)
            {
                sb.Insert(0, Digits[(int)(value % (ulong)toBase)]);
                value /= (ulong)toBase;
            }
            return sb.ToString();
        }

        public static string FormatBitsSummary(double totalBits)
        {
            double bytes = totalBits / 8.0;
            double kb = bytes / 1024.0;
            double mb = kb / 1024.0;
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:0.##} бит | {1:0.##} байт | {2:0.####} Кбайт | {3:0.####} Мбайт",
                totalBits,
                bytes,
                kb,
                mb);
        }

        public static string BuildTruthTable(string expression)
        {
            string expr = (expression ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(expr))
            {
                throw new ArgumentException("Введите логическое выражение.");
            }

            List<Token> tokens = Tokenize(expr);
            var varsSet = new SortedSet<string>(StringComparer.Ordinal);
            foreach (Token t in tokens)
            {
                if (t.Kind == TokenKind.Var)
                {
                    varsSet.Add(t.Text);
                }
            }

            if (varsSet.Count == 0)
            {
                throw new ArgumentException("Добавьте хотя бы одну переменную (A, B, C...).");
            }
            if (varsSet.Count > 5)
            {
                throw new ArgumentException("Максимум 5 переменных для таблицы истинности.");
            }

            var vars = new List<string>(varsSet);
            var sb = new StringBuilder();
            foreach (string v in vars)
            {
                sb.Append(v).Append(" | ");
            }
            sb.AppendLine("F");
            sb.AppendLine(new string('-', vars.Count * 4 + 3));

            int rows = 1 << vars.Count;
            var env = new Dictionary<string, bool>(StringComparer.Ordinal);
            for (int mask = 0; mask < rows; mask++)
            {
                for (int i = 0; i < vars.Count; i++)
                {
                    bool bit = ((mask >> (vars.Count - 1 - i)) & 1) == 1;
                    env[vars[i]] = bit;
                    sb.Append(bit ? "1" : "0").Append(" | ");
                }

                var parser = new BoolExprParser(tokens, env);
                bool res = parser.Parse();
                sb.AppendLine(res ? "1" : "0");
            }

            return sb.ToString();
        }

        private enum TokenKind
        {
            Var,
            Const,
            Not,
            And,
            Or,
            Xor,
            Imp,
            Eq,
            LParen,
            RParen
        }

        private sealed class Token
        {
            public TokenKind Kind { get; set; }
            public string Text { get; set; }
            public bool BoolValue { get; set; }
        }

        private static List<Token> Tokenize(string expr)
        {
            var list = new List<Token>();
            int i = 0;
            while (i < expr.Length)
            {
                char c = expr[i];
                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }
                if (c == '(') { list.Add(new Token { Kind = TokenKind.LParen }); i++; continue; }
                if (c == ')') { list.Add(new Token { Kind = TokenKind.RParen }); i++; continue; }
                if (c == '!' || c == '~' || c == '¬') { list.Add(new Token { Kind = TokenKind.Not }); i++; continue; }
                if (c == '*' || c == '∧') { list.Add(new Token { Kind = TokenKind.And }); i++; continue; }
                if (c == '+' || c == '∨') { list.Add(new Token { Kind = TokenKind.Or }); i++; continue; }
                if (c == '^' || c == '⊕') { list.Add(new Token { Kind = TokenKind.Xor }); i++; continue; }
                if (c == '&')
                {
                    i += (i + 1 < expr.Length && expr[i + 1] == '&') ? 2 : 1;
                    list.Add(new Token { Kind = TokenKind.And });
                    continue;
                }
                if (c == '|')
                {
                    i += (i + 1 < expr.Length && expr[i + 1] == '|') ? 2 : 1;
                    list.Add(new Token { Kind = TokenKind.Or });
                    continue;
                }
                if (c == '-' && i + 1 < expr.Length && expr[i + 1] == '>')
                {
                    list.Add(new Token { Kind = TokenKind.Imp });
                    i += 2;
                    continue;
                }
                if (c == '=' && i + 1 < expr.Length && expr[i + 1] == '>')
                {
                    list.Add(new Token { Kind = TokenKind.Imp });
                    i += 2;
                    continue;
                }
                if (c == '=' && i + 1 < expr.Length && expr[i + 1] == '=')
                {
                    list.Add(new Token { Kind = TokenKind.Eq });
                    i += 2;
                    continue;
                }
                if (c == '=')
                {
                    list.Add(new Token { Kind = TokenKind.Eq });
                    i++;
                    continue;
                }
                if (c == '<' && i + 2 < expr.Length && expr[i + 1] == '-' && expr[i + 2] == '>')
                {
                    list.Add(new Token { Kind = TokenKind.Eq });
                    i += 3;
                    continue;
                }
                if (c == '0' || c == '1')
                {
                    list.Add(new Token { Kind = TokenKind.Const, BoolValue = c == '1' });
                    i++;
                    continue;
                }
                if (char.IsLetter(c))
                {
                    int start = i;
                    while (i < expr.Length && char.IsLetterOrDigit(expr[i]))
                    {
                        i++;
                    }
                    string word = expr.Substring(start, i - start);
                    string lower = word.ToLowerInvariant();
                    if (lower == "not") list.Add(new Token { Kind = TokenKind.Not });
                    else if (lower == "and") list.Add(new Token { Kind = TokenKind.And });
                    else if (lower == "or") list.Add(new Token { Kind = TokenKind.Or });
                    else if (lower == "xor") list.Add(new Token { Kind = TokenKind.Xor });
                    else if (lower == "true") list.Add(new Token { Kind = TokenKind.Const, BoolValue = true });
                    else if (lower == "false") list.Add(new Token { Kind = TokenKind.Const, BoolValue = false });
                    else list.Add(new Token { Kind = TokenKind.Var, Text = word.ToUpperInvariant() });
                    continue;
                }

                throw new ArgumentException("Неизвестный символ в выражении: '" + c + "'");
            }
            return list;
        }

        private sealed class BoolExprParser
        {
            private readonly List<Token> _tokens;
            private readonly Dictionary<string, bool> _env;
            private int _pos;

            public BoolExprParser(List<Token> tokens, Dictionary<string, bool> env)
            {
                _tokens = tokens;
                _env = env;
            }

            public bool Parse()
            {
                bool val = ParseEq();
                if (_pos < _tokens.Count)
                {
                    throw new ArgumentException("Лишние символы в выражении.");
                }
                return val;
            }

            private bool ParseEq()
            {
                bool left = ParseImp();
                while (_pos < _tokens.Count && _tokens[_pos].Kind == TokenKind.Eq)
                {
                    _pos++;
                    bool right = ParseImp();
                    left = (left == right);
                }
                return left;
            }

            private bool ParseImp()
            {
                bool left = ParseOr();
                if (_pos < _tokens.Count && _tokens[_pos].Kind == TokenKind.Imp)
                {
                    _pos++;
                    bool right = ParseImp();
                    return !left || right;
                }
                return left;
            }

            private bool ParseOr()
            {
                bool left = ParseXor();
                while (_pos < _tokens.Count && _tokens[_pos].Kind == TokenKind.Or)
                {
                    _pos++;
                    bool right = ParseXor();
                    left = left || right;
                }
                return left;
            }

            private bool ParseXor()
            {
                bool left = ParseAnd();
                while (_pos < _tokens.Count && _tokens[_pos].Kind == TokenKind.Xor)
                {
                    _pos++;
                    bool right = ParseAnd();
                    left = left ^ right;
                }
                return left;
            }

            private bool ParseAnd()
            {
                bool left = ParseNot();
                while (_pos < _tokens.Count && _tokens[_pos].Kind == TokenKind.And)
                {
                    _pos++;
                    bool right = ParseNot();
                    left = left && right;
                }
                return left;
            }

            private bool ParseNot()
            {
                if (_pos < _tokens.Count && _tokens[_pos].Kind == TokenKind.Not)
                {
                    _pos++;
                    return !ParseNot();
                }
                return ParsePrimary();
            }

            private bool ParsePrimary()
            {
                if (_pos >= _tokens.Count)
                {
                    throw new ArgumentException("Неожиданный конец выражения.");
                }
                Token t = _tokens[_pos++];
                if (t.Kind == TokenKind.Var)
                {
                    return _env[t.Text];
                }
                if (t.Kind == TokenKind.Const)
                {
                    return t.BoolValue;
                }
                if (t.Kind == TokenKind.LParen)
                {
                    bool inner = ParseEq();
                    if (_pos >= _tokens.Count || _tokens[_pos].Kind != TokenKind.RParen)
                    {
                        throw new ArgumentException("Пропущена закрывающая скобка ')'.");
                    }
                    _pos++;
                    return inner;
                }
                throw new ArgumentException("Неожиданный токен в выражении.");
            }
        }
    }
}
