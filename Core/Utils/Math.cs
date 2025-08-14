using System.Text.RegularExpressions;

namespace Core.Utils;

public static class Math
{
    // 评估一个复杂数学表达式，返回一个decimal类型的值
    public static decimal Evaluate(string expression)
    {
        // 去掉空格
        expression = expression.Replace(" ", "");
        // 处理括号
        while (expression.Contains('('))
        {
            // 找到最内层的括号
            var start = expression.LastIndexOf("(");
            var end = expression.IndexOf(")", start);
            // 计算括号内的子表达式
            var subResult = Evaluate(expression.Substring(start + 1, end - start - 1));
            // 用子表达式的结果替换括号
            expression = expression.Remove(start, end - start + 1).Insert(start, subResult.ToString());
        }

        // 处理幂运算
        while (expression.Contains('^'))
        {
            // 使用正则表达式匹配第一个幂运算符及其两边的操作数
            var match = Regex.Match(expression, @"(-?\d+(\.\d+)?)\^(-?\d+(\.\d+)?)");
            // 获取操作数和运算符
            var left = decimal.Parse(match.Groups[1].Value);
            var right = decimal.Parse(match.Groups[3].Value);
            // 计算幂运算结果
            var subResult = (decimal)System.Math.Pow((double)left, (double)right);
            // 用幂运算结果替换原来的子表达式
            expression = expression.Remove(match.Index, match.Length).Insert(match.Index, subResult.ToString());
        }

        // 处理乘法和除法
        while (expression.Contains('*') || expression.Contains('/'))
        {
            // 使用正则表达式匹配第一个乘法或除法运算符及其两边的操作数
            var match = Regex.Match(expression, @"(-?\d+(\.\d+)?)(\*|/)(-?\d+(\.\d+)?)");
            // 获取操作数和运算符
            var left = decimal.Parse(match.Groups[1].Value);
            var right = decimal.Parse(match.Groups[4].Value);
            var op = match.Groups[3].Value;
            // 计算乘法或除法结果
            var subResult = op == "*" ? left * right : left / right;
            if (match.Groups[1].Value.StartsWith("-"))
            {
                expression = expression.Remove(match.Index, match.Length)
                    .Insert(match.Index, $"+{subResult}");
                continue;
            }

            // 用乘法或除法结果替换原来的子表达式
            expression = expression.Remove(match.Index, match.Length).Insert(match.Index, subResult.ToString());
        }

        // 处理加法和减法
        while (expression.Contains('+') || expression.Contains('-'))
        {
            // 使用正则表达式匹配第一个加法或减法运算符及其两边的操作数
            var match = Regex.Match(expression, @"(-?\d+(\.?\d+?)?)(\+|-)(-?\d+(\.\d+)?)");
            if (string.IsNullOrWhiteSpace(match.Value)) break;

            // 获取操作数和运算符
            var left = decimal.Parse(match.Groups[1].Value);
            var right = decimal.Parse(match.Groups[4].Value);
            var op = match.Groups[3].Value;
            // 计算加法或减法结果
            var subResult = op == "+" ? left + right : left - right;
            // 用加法或减法结果替换原来的子表达式
            expression = expression.Remove(match.Index, match.Length).Insert(match.Index, subResult.ToString());
        }

        // 返回最终结果，转换为decimal类型

        var deciMal = decimal.Parse(expression);
        //log.Debug($"{deciMal}");
        return deciMal;
    }
}