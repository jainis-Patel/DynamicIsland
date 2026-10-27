using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DynamicIsland.Services;

/// <summary>
/// Lightweight inline calculator and unit converter for the Dynamic Island.
/// Parses expressions such as '12 * 85', '2 ^ 10', '15% of 600', '150 km in miles', '72 f in c', etc.
/// </summary>
internal static class QuickCalcService
{
    private static readonly Regex PercentOfRegex = new(@"^([\d\.]+)\s*%\s*(?:of)\s*([\d\.]+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex UnitConvertRegex = new(@"^([\d\.]+)\s*([a-zA-Z]+)\s*(?:in|to)\s*([a-zA-Z]+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PowerRegex = new(@"^([\d\.]+)\s*\^\s*([\d\.]+)$", RegexOptions.Compiled);
    private static readonly Regex MultiplyXRegex = new(@"(?<=[\d\)])\s*[xX]\s*(?=[\d\(])", RegexOptions.Compiled);

    private static readonly DataTable SharedTable = new();
    private static readonly object TableLock = new();

    public static (bool Success, string Result, string Explanation) Evaluate(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return (false, "", "");

        string trimmed = input.Trim();

        // 1. Percentage check: "15% of 500"
        var pctMatch = PercentOfRegex.Match(trimmed);
        if (pctMatch.Success &&
            double.TryParse(pctMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double pct) &&
            double.TryParse(pctMatch.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double baseVal))
        {
            double res = (pct / 100.0) * baseVal;
            return (true, FormatDouble(res), $"{pct}% of {baseVal}");
        }

        // 2. Unit conversion: "100 km in miles", "72 f in c", "150 lbs in kg"
        var unitMatch = UnitConvertRegex.Match(trimmed);
        if (unitMatch.Success &&
            double.TryParse(unitMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
        {
            string from = unitMatch.Groups[2].Value.ToLowerInvariant();
            string to = unitMatch.Groups[3].Value.ToLowerInvariant();

            var converted = TryConvertUnits(val, from, to);
            if (converted.HasValue)
            {
                return (true, FormatDouble(converted.Value), $"{val} {from} → {to}");
            }
        }

        // 3. Exponentiation check: "2 ^ 10"
        var powMatch = PowerRegex.Match(trimmed);
        if (powMatch.Success &&
            double.TryParse(powMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double b) &&
            double.TryParse(powMatch.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double exp))
        {
            double res = Math.Pow(b, exp);
            return (true, FormatDouble(res), $"{b}^{exp}");
        }

        // 4. Mathematical expression via shared DataTable
        try
        {
            string sanitized = SanitizeMath(trimmed);
            object? result;
            lock (TableLock)
            {
                result = SharedTable.Compute(sanitized, "");
            }
            if (result != null && result != DBNull.Value)
            {
                double d = Convert.ToDouble(result, CultureInfo.InvariantCulture);
                return (true, FormatDouble(d), sanitized);
            }
        }
        catch { }

        return (false, "", "");
    }

    private static string SanitizeMath(string exp)
    {
        string step = MultiplyXRegex.Replace(exp, " * ");
        return step.Replace("×", "*")
                   .Replace("÷", "/");
    }

    private static double? TryConvertUnits(double val, string from, string to)
    {
        // Temperature
        if (from == "c" && to == "f") return (val * 9.0 / 5.0) + 32;
        if (from == "f" && to == "c") return (val - 32) * 5.0 / 9.0;
        if (from == "c" && to == "k") return val + 273.15;
        if (from == "k" && to == "c") return val - 273.15;

        // Length
        if (from is "km" or "kilometer" or "kilometers" && to is "mile" or "miles" or "mi") return val * 0.621371;
        if (from is "mile" or "miles" or "mi" && to is "km" or "kilometer" or "kilometers") return val / 0.621371;
        if (from is "m" or "meter" or "meters" && to is "ft" or "feet") return val * 3.28084;
        if (from is "ft" or "feet" && to is "m" or "meter" or "meters") return val / 3.28084;
        if (from is "inch" or "inches" or "in" && to is "cm") return val * 2.54;
        if (from is "cm" && to is "inch" or "inches" or "in") return val / 2.54;

        // Weight
        if (from is "kg" or "kilogram" or "kilograms" && to is "lbs" or "lb" or "pound" or "pounds") return val * 2.20462;
        if (from is "lbs" or "lb" or "pound" or "pounds" && to is "kg" or "kilogram" or "kilograms") return val / 2.20462;

        // Common currency approximations
        if (from == "usd" && to == "inr") return val * 83.5;
        if (from == "inr" && to == "usd") return val / 83.5;
        if (from == "usd" && to == "eur") return val * 0.92;
        if (from == "eur" && to == "usd") return val / 0.92;
        if (from == "usd" && to == "gbp") return val * 0.79;
        if (from == "gbp" && to == "usd") return val / 0.79;

        return null;
    }

    private static string FormatDouble(double d)
    {
        if (double.IsNaN(d) || double.IsInfinity(d)) return "Error";
        return Math.Abs(d % 1) <= 1e-6 ? d.ToString("0", CultureInfo.InvariantCulture) : d.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
