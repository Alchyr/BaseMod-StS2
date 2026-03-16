namespace BaseLib.Config;

[AttributeUsage(AttributeTargets.Property)]
public class ConfigSectionAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

[AttributeUsage(AttributeTargets.Property)]
public class SliderRangeAttribute(double min, double max, double step = 1.0) : Attribute
{
    public double Min { get; } = min;
    public double Max { get; } = max;
    public double Step { get; } = step;
}

[AttributeUsage(AttributeTargets.Property)]
public class SliderLabelFormatAttribute(string format) : Attribute
{
    public string Format { get; } = format;
}