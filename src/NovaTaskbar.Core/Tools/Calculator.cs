using NovaTaskbar.Core.Localization;
using System.Globalization;

namespace NovaTaskbar.Core.Tools;

public enum AngleMode { Degrees, Radians }

/// <summary>
/// Calculadora segura (analisador próprio, nada de eval): + − × ÷ % ^, parênteses, menos unário, fatorial (!),
/// decimais com vírgula ou ponto, constantes π/e, funções científicas (sin, cos, tan, asin, acos, atan, sinh, cosh,
/// tanh, √/sqrt, ∛/cbrt, ln, log, log2, exp, abs, round, floor, ceil) em graus ou radianos e multiplicação implícita
/// (2π, 3(1+2), 2√9). Ideias de recursos do app Unitto; código próprio.
/// </summary>
public static class Calculator
{
    public static bool TryEvaluate(string? expression, out double result) => TryEvaluate(expression, AngleMode.Degrees, out result);

    public static bool TryEvaluate(string? expression, AngleMode angles, out double result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(expression) || expression.Length > 256) return false;
        try
        {
            var p = new Parser(expression, angles);
            result = p.ParseExpression();
            p.SkipSpaces();
            return p.AtEnd && double.IsFinite(result);
        }
        catch (FormatException) { return false; }
        catch (OverflowException) { return false; }
    }

    /// <summary>Resultado formatado em pt-BR (até 10 casas, sem zeros à direita).</summary>
    public static string Format(double v) =>
        Math.Abs(v) >= 1e15 || (Math.Abs(v) < 1e-9 && v != 0)
            ? v.ToString("0.######E+0", L.Culture)
            : Math.Round(v, 10).ToString("#,0.##########", L.Culture);

    private static readonly string[] Functions =
        ["asin", "acos", "atan", "sinh", "cosh", "tanh", "sin", "cos", "tan", "sqrt", "cbrt", "log2", "log", "ln", "exp", "abs", "round", "floor", "ceil", "√", "∛"];

    private sealed class Parser(string s, AngleMode angles)
    {
        private int _i;
        public bool AtEnd => _i >= s.Length;

        public void SkipSpaces() { while (!AtEnd && char.IsWhiteSpace(s[_i])) _i++; }

        private bool Eat(params char[] cs)
        {
            SkipSpaces();
            if (!AtEnd && cs.Contains(s[_i])) { _i++; return true; }
            return false;
        }

        public double ParseExpression()
        {
            double v = ParseTerm();
            while (true)
            {
                if (Eat('+')) v += ParseTerm();
                else if (Eat('-', '−')) v -= ParseTerm();
                else return v;
            }
        }

        private double ParseTerm()
        {
            double v = ParseFactor();
            while (true)
            {
                if (Eat('*', '×', 'x', 'X')) v *= ParseFactor();
                else if (Eat('/', '÷')) { double d = ParseFactor(); if (d == 0) throw new FormatException("divisão por zero"); v /= d; }
                else if (Eat('%')) v = v / 100.0 * (AtEndOfFactor() ? 1 : ParseFactor());
                else if (StartsPrimary()) v *= ParseFactor(); // multiplicação implícita: 2π, 3(4), 2√9
                else return v;
            }
        }

        /// <summary>O próximo token começa um valor (número, "(", constante ou função)?</summary>
        private bool StartsPrimary()
        {
            SkipSpaces();
            if (AtEnd) return false;
            char c = s[_i];
            if (c is '(' or 'π' or '√' or '∛' || char.IsDigit(c)) return true;
            if (!char.IsLetter(c) || c is 'x' or 'X') return false;
            int save = _i;
            bool ok = Functions.Any(f => PeekWord(f)) || PeekWord("pi") || PeekWord("e");
            _i = save;
            return ok;
        }

        private bool PeekWord(string w)
        {
            if (string.Compare(s, _i, w, 0, w.Length, StringComparison.OrdinalIgnoreCase) != 0) return false;
            int end = _i + w.Length;
            return w is "√" or "∛" || end >= s.Length || !char.IsLetter(s[end]);
        }

        private bool AtEndOfFactor()
        {
            SkipSpaces();
            return AtEnd || s[_i] is ')' or '+' or '-' or '−' or '*' or '/' or '×' or '÷';
        }

        private double ParseFactor()
        {
            double b = ParseUnary();
            while (Eat('!')) b = Factorial(b);
            if (Eat('^')) return Math.Pow(b, ParseFactor()); // associativo à direita
            return b;
        }

        private double ParseUnary()
        {
            if (Eat('-', '−')) return -ParseUnary();
            if (Eat('+')) return ParseUnary();
            return ParsePrimary();
        }

        private double ParsePrimary()
        {
            SkipSpaces();
            if (Eat('('))
            {
                double v = ParseExpression();
                if (!Eat(')')) throw new FormatException("parêntese");
                return v;
            }
            if (Word("pi") || Word("π")) return Math.PI;
            foreach (var f in Functions)
                if (Word(f)) return Apply(f, ParseFactorArgument());
            if (Word("e")) return Math.E;
            int start = _i;
            while (!AtEnd && (char.IsDigit(s[_i]) || s[_i] is ',' or '.')) _i++;
            if (start == _i) throw new FormatException("número");
            string n = s[start.._i];
            // "1.234,5" (pt-BR) ou "1,234.5" (en): o último separador é o decimal.
            int lastSep = Math.Max(n.LastIndexOf(','), n.LastIndexOf('.'));
            if (lastSep >= 0)
            {
                string intPart = n[..lastSep].Replace(",", "").Replace(".", "");
                n = intPart + "." + n[(lastSep + 1)..];
            }
            return double.Parse(n, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        /// <summary>Argumento de função: "(…)" ou um fator direto (√9, sin30).</summary>
        private double ParseFactorArgument()
        {
            SkipSpaces();
            if (Eat('('))
            {
                double v = ParseExpression();
                if (!Eat(')')) throw new FormatException("parêntese");
                return v;
            }
            return ParseUnary();
        }

        private double ToRad(double x) => angles == AngleMode.Degrees ? x * Math.PI / 180 : x;
        private double FromRad(double x) => angles == AngleMode.Degrees ? x * 180 / Math.PI : x;

        private double Apply(string f, double x)
        {
            double r = f switch
            {
                "sin" => Clean(Math.Sin(ToRad(x))),
                "cos" => Clean(Math.Cos(ToRad(x))),
                "tan" => Math.Abs(Math.Cos(ToRad(x))) < 1e-12 ? throw new FormatException("tan indefinida") : Clean(Math.Tan(ToRad(x))),
                "asin" => FromRad(Math.Asin(x)),
                "acos" => FromRad(Math.Acos(x)),
                "atan" => FromRad(Math.Atan(x)),
                "sinh" => Math.Sinh(x),
                "cosh" => Math.Cosh(x),
                "tanh" => Math.Tanh(x),
                "sqrt" or "√" => x < 0 ? throw new FormatException("raiz negativa") : Math.Sqrt(x),
                "cbrt" or "∛" => Math.Cbrt(x),
                "ln" => x <= 0 ? throw new FormatException("ln") : Math.Log(x),
                "log" => x <= 0 ? throw new FormatException("log") : Math.Log10(x),
                "log2" => x <= 0 ? throw new FormatException("log2") : Math.Log2(x),
                "exp" => Math.Exp(x),
                "abs" => Math.Abs(x),
                "round" => Math.Round(x, MidpointRounding.AwayFromZero),
                "floor" => Math.Floor(x),
                "ceil" => Math.Ceiling(x),
                _ => throw new FormatException(f),
            };
            if (double.IsNaN(r)) throw new FormatException(f);
            return r;
        }

        /// <summary>sin(180°) dá 1,2e-16 em ponto flutuante; arredonda o "quase zero".</summary>
        private static double Clean(double v) => Math.Abs(v) < 1e-12 ? 0 : v;

        private static double Factorial(double n)
        {
            if (n < 0 || n != Math.Floor(n) || n > 170) throw new FormatException("fatorial");
            double r = 1;
            for (int i = 2; i <= (int)n; i++) r *= i;
            return r;
        }

        private bool Word(string w)
        {
            SkipSpaces();
            if (string.Compare(s, _i, w, 0, w.Length, StringComparison.OrdinalIgnoreCase) == 0)
            {
                int end = _i + w.Length;
                if (end < s.Length && char.IsLetter(s[end])) return false;
                _i = end;
                return true;
            }
            return false;
        }
    }
}
