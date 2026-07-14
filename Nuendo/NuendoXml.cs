using System.Globalization;
using System.Xml.Linq;

namespace MgaWwiseImporter.Nuendo;

internal static class NuendoXml
{
    public static double? ReadFloatChild(XElement parent, string name)
    {
        var element = parent.Elements("float")
            .FirstOrDefault(e => (string?)e.Attribute("name") == name);
        var value = (string?)element?.Attribute("value");
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    public static int? ReadIntChild(XElement parent, string name)
    {
        var element = parent.Elements("int")
            .FirstOrDefault(e => (string?)e.Attribute("name") == name);
        var value = (string?)element?.Attribute("value");
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    public static string ReadStringChild(XElement parent, string name)
    {
        var element = parent.Elements("string")
            .FirstOrDefault(e => (string?)e.Attribute("name") == name);
        return (string?)element?.Attribute("value") ?? string.Empty;
    }

    public static double? ReadNumberChild(XElement parent, string name)
    {
        return ReadFloatChild(parent, name) ?? ReadIntChild(parent, name);
    }
}
