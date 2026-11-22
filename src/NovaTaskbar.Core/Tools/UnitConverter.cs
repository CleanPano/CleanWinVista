namespace NovaTaskbar.Core.Tools;

/// <summary>Conversão de unidades (local). Temperatura tem fórmula própria; o resto usa fator para a unidade-base.</summary>
public static class UnitConverter
{
    public sealed record Unit(string Id, string Name, double Factor);
    public sealed record Category(string Id, string Name, IReadOnlyList<Unit> Units);

    public static readonly IReadOnlyList<Category> Categories =
    [
        new("length", "Length", [new("mm", "Millimeter", 0.001), new("cm", "Centimeter", 0.01), new("m", "Meter", 1), new("km", "Kilometer", 1000),
            new("in", "Inch", 0.0254), new("ft", "Foot", 0.3048), new("yd", "Yard", 0.9144), new("mi", "Mile", 1609.344)]),
        new("mass", "Weight", [new("mg", "Milligram", 1e-6), new("g", "Gram", 0.001), new("kg", "Kilogram", 1), new("t", "Tonne", 1000),
            new("oz", "Ounce", 0.028349523125), new("lb", "Pound", 0.45359237)]),
        new("volume", "Volume", [new("ml", "Milliliter", 0.001), new("l", "Liter", 1), new("tsp", "Teaspoon (US)", 0.00492892159375),
            new("tbsp", "Tablespoon (US)", 0.01478676478125), new("cup", "Cup (US)", 0.2365882365), new("floz", "Fluid ounce (US)", 0.0295735295625),
            new("gal", "Gallon (US)", 3.785411784)]),
        new("temp", "Temperature", [new("c", "Celsius", 1), new("f", "Fahrenheit", 1), new("k", "Kelvin", 1)]),
        new("speed", "Speed", [new("kmh", "km/h", 1 / 3.6), new("ms", "m/s", 1), new("mph", "mph", 0.44704), new("kn", "Knot", 0.514444)]),
        new("data", "Data", [new("b", "Byte", 1), new("kb", "KB", 1024), new("mb", "MB", 1024d * 1024), new("gb", "GB", 1024d * 1024 * 1024),
            new("tb", "TB", 1024d * 1024 * 1024 * 1024)]),
        new("time", "Time", [new("s", "Second", 1), new("min", "Minute", 60), new("h", "Hour", 3600), new("d", "Day", 86400), new("wk", "Week", 604800)]),
    ];

    public static double Convert(string categoryId, string from, string to, double value)
    {
        if (categoryId == "temp")
        {
            double c = from switch { "f" => (value - 32) * 5 / 9, "k" => value - 273.15, _ => value };
            return to switch { "f" => c * 9 / 5 + 32, "k" => c + 273.15, _ => c };
        }
        var cat = Categories.First(x => x.Id == categoryId);
        double f = cat.Units.First(u => u.Id == from).Factor, t = cat.Units.First(u => u.Id == to).Factor;
        return value * f / t;
    }
}
