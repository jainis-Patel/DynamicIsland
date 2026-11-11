using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DynamicIsland.Services;

internal sealed record CalcHistoryEntry(string Expression, string Result)
{
    public string ChipText => $"{Expression} = {Result}";
}

/// <summary>
/// Lightweight inline calculator and unit converter for the Dynamic Island.
/// Parses expressions such as '12 * 85', '2 ^ 10', 'sqrt(144)', '15% of 600',
/// '150 km in miles', '72 f in c', '16 gb in mb', '255 in hex', '0xFF in dec', 'ans * 2', etc.
/// </summary>
internal static class QuickCalcService
{
    private static readonly Regex PercentOfRegex = new(@"^([+-]?[\d\.]+)\s*%\s*(?:of)\s*([+-]?[\d\.]+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BaseConvertRegex = new(@"^(0x[0-9a-fA-F]+|0b[01]+|\d+)\s*(?:in|to)\s*(hex|bin|binary|dec|decimal)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex UnitConvertRegex = new(@"^([+-]?[\d\.]+)\s*([a-zA-Z]+)\s*(?:in|to)\s*([a-zA-Z]+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PowerRegex = new(@"^([+-]?[\d\.]+)\s*\^\s*([+-]?[\d\.]+)$", RegexOptions.Compiled);
    private static readonly Regex SqrtRegex = new(@"^sqrt\s*\(\s*([\d\.]+)\s*\)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MultiplyXRegex = new(@"(?<=[\d\)])\s*[xX]\s*(?=[\d\(])", RegexOptions.Compiled);
    private static readonly Regex AnsTokenRegex = new(@"\bans\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly DataTable SharedTable = new();
    private static readonly object TableLock = new();
    private static string _lastNumericAnswer = "0";

    public static string LastAnswer => _lastNumericAnswer;

    public static void SetLastAnswer(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        string clean = value.Trim();
        if (clean.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (long.TryParse(clean[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long hexVal))
            {
                _lastNumericAnswer = hexVal.ToString(CultureInfo.InvariantCulture);
            }
        }
        else if (clean.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                long binVal = Convert.ToInt64(clean[2..], 2);
                _lastNumericAnswer = binVal.ToString(CultureInfo.InvariantCulture);
            }
            catch { }
        }
        else if (double.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            _lastNumericAnswer = clean;
        }
    }

    public static (bool Success, string Result, string Explanation) Evaluate(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return (false, "", "");

        string trimmed = AnsTokenRegex.Replace(input.Trim(), _lastNumericAnswer);

        // 0. Programmer Base Conversion: "255 in hex", "0xFF in dec", "42 in bin"
        var baseMatch = BaseConvertRegex.Match(trimmed);
        if (baseMatch.Success)
        {
            string rawNum = baseMatch.Groups[1].Value;
            string targetBase = baseMatch.Groups[2].Value.ToLowerInvariant();
            if (TryParseProgrammerInt(rawNum, out long intVal))
            {
                string formatted = targetBase switch
                {
                    "hex" => $"0x{intVal:X}",
                    "bin" or "binary" => $"0b{Convert.ToString(intVal, 2)}",
                    _ => intVal.ToString(CultureInfo.InvariantCulture)
                };
                return (true, formatted, $"{rawNum} → {targetBase.ToUpperInvariant()}");
            }
        }

        // 1. Percentage check: "15% of 500"
        var pctMatch = PercentOfRegex.Match(trimmed);
        if (pctMatch.Success &&
            double.TryParse(pctMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double pct) &&
            double.TryParse(pctMatch.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double baseVal))
        {
            double res = (pct / 100.0) * baseVal;
            return (true, FormatDouble(res), $"{pct}% of {baseVal}");
        }

        // 2. Unit conversion: "100 km in miles", "72 f in c", "150 lbs in kg", "4 gb in mb"
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

        // 3. Square root: "sqrt(144)"
        var sqrtMatch = SqrtRegex.Match(trimmed);
        if (sqrtMatch.Success &&
            double.TryParse(sqrtMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double sqVal) &&
            sqVal >= 0)
        {
            double res = Math.Sqrt(sqVal);
            return (true, FormatDouble(res), $"√{sqVal}");
        }

        // 4. Exponentiation check: "2 ^ 10"
        var powMatch = PowerRegex.Match(trimmed);
        if (powMatch.Success &&
            double.TryParse(powMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double b) &&
            double.TryParse(powMatch.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double exp))
        {
            double res = Math.Pow(b, exp);
            if (double.IsNaN(res) || double.IsInfinity(res))
                return (false, "", "");
            return (true, FormatDouble(res), $"{b}^{exp}");
        }

        // 5. Mathematical expression via shared DataTable
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
                if (double.IsNaN(d) || double.IsInfinity(d))
                    return (false, "", "");
                return (true, FormatDouble(d), sanitized);
            }
        }
        catch
        {
            // Expression is incomplete while user is still typing
        }

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
        if (from is "g" or "gram" or "grams" && to is "oz" or "ounce" or "ounces") return val / 28.3495;
        if (from is "oz" or "ounce" or "ounces" && to is "g" or "gram" or "grams") return val * 28.3495;

        // Digital Storage (binary 1024 base)
        if (from == "gb" && to == "mb") return val * 1024.0;
        if (from == "mb" && to == "gb") return val / 1024.0;
        if (from == "tb" && to == "gb") return val * 1024.0;
        if (from == "gb" && to == "tb") return val / 1024.0;
        if (from == "mb" && to == "kb") return val * 1024.0;
        if (from == "kb" && to == "mb") return val / 1024.0;

        // Common currency approximations
        if (from == "usd" && to == "inr") return val * 83.5;
        if (from == "inr" && to == "usd") return val / 83.5;
        if (from == "usd" && to == "eur") return val * 0.92;
        if (from == "eur" && to == "usd") return val / 0.92;
        if (from == "usd" && to == "gbp") return val * 0.79;
        if (from == "gbp" && to == "usd") return val / 0.79;

        // Time
        if (from is "min" or "mins" or "minute" or "minutes" && to is "hr" or "hrs" or "hour" or "hours" or "h") return val / 60.0;
        if (from is "hr" or "hrs" or "hour" or "hours" or "h" && to is "min" or "mins" or "minute" or "minutes") return val * 60.0;
        if (from is "min" or "mins" or "minute" or "minutes" && to is "s" or "sec" or "secs" or "second" or "seconds") return val * 60.0;
        if (from is "s" or "sec" or "secs" or "second" or "seconds" && to is "min" or "mins" or "minute" or "minutes") return val / 60.0;

        // Speed
        if (from is "mph" && to is "kmh" or "kph") return val * 1.60934;
        if (from is "kmh" or "kph" && to is "mph") return val / 1.60934;

        return null;
    }

    private static bool TryParseProgrammerInt(string raw, out long value)
    {
        value = 0;
        if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return long.TryParse(raw[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }
        if (raw.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                value = Convert.ToInt64(raw[2..], 2);
                return true;
            }
            catch
            {
                return false;
            }
        }
        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static string FormatDouble(double d)
    {
        if (double.IsNaN(d) || double.IsInfinity(d)) return "Error";
        return Math.Abs(d % 1) <= 1e-6 ? d.ToString("0", CultureInfo.InvariantCulture) : d.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
