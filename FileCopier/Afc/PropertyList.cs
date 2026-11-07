using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace FileCopier.Afc;

/// <summary>
/// Minimal XML property-list codec for usbmuxd and lockdownd messages and pairing records.
/// Supports dict, array, string, integer, real, true/false, data, and date values.
/// </summary>
internal static class PropertyList
{
    public static byte[] Serialize(IReadOnlyDictionary<string, object> dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        var document = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XDocumentType("plist", "-//Apple//DTD PLIST 1.0//EN", "http://www.apple.com/DTDs/PropertyList-1.0.dtd", null),
            new XElement("plist", new XAttribute("version", "1.0"), ToElement(dictionary)));

        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false) }))
        {
            document.Save(writer);
        }
        return stream.ToArray();
    }

    public static Dictionary<string, object> Parse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        // Device input: never resolve external entities or process the DTD.
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
        try
        {
            using var reader = XmlReader.Create(new MemoryStream(data), settings);
            var root = XDocument.Load(reader).Root;
            if (root?.Name != "plist")
                throw new InvalidDataException("Property list has no <plist> root.");

            var value = root.Elements().SingleOrDefault()
                ?? throw new InvalidDataException("Property list is empty.");
            return FromElement(value) as Dictionary<string, object>
                ?? throw new InvalidDataException("Property list root is not a dictionary.");
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException("Property list is not valid XML.", ex);
        }
    }

    private static XElement ToElement(object value) => value switch
    {
        string text => new XElement("string", text),
        bool flag => new XElement(flag ? "true" : "false"),
        int or long or uint or ulong or ushort => new XElement("integer", Convert.ToString(value, CultureInfo.InvariantCulture)),
        double number => new XElement("real", number.ToString("R", CultureInfo.InvariantCulture)),
        byte[] bytes => new XElement("data", Convert.ToBase64String(bytes)),
        IReadOnlyDictionary<string, object> dictionary => new XElement("dict",
            dictionary.SelectMany(pair => new[] { new XElement("key", pair.Key), ToElement(pair.Value) })),
        IEnumerable<object> items => new XElement("array", items.Select(ToElement)),
        _ => throw new ArgumentException($"Unsupported property-list value type {value.GetType().Name}.")
    };

    private static object FromElement(XElement element)
    {
        switch (element.Name.LocalName)
        {
            case "dict":
                var dictionary = new Dictionary<string, object>(StringComparer.Ordinal);
                var children = element.Elements().ToList();
                if (children.Count % 2 != 0)
                    throw new InvalidDataException("Property-list dictionary has a key without a value.");
                for (var index = 0; index < children.Count; index += 2)
                {
                    if (children[index].Name != "key")
                        throw new InvalidDataException("Property-list dictionary entry has no key.");
                    dictionary[children[index].Value] = FromElement(children[index + 1]);
                }
                return dictionary;
            case "array":
                return element.Elements().Select(FromElement).ToList();
            case "string":
                return element.Value;
            case "integer":
                if (long.TryParse(element.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var signed))
                    return signed;
                if (ulong.TryParse(element.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var unsigned))
                    return unsigned;
                throw new InvalidDataException($"Invalid property-list integer '{element.Value}'.");
            case "real":
                return double.Parse(element.Value, NumberStyles.Float, CultureInfo.InvariantCulture);
            case "true":
                return true;
            case "false":
                return false;
            case "data":
                return Convert.FromBase64String(element.Value);
            case "date":
                return DateTime.Parse(element.Value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
            default:
                throw new InvalidDataException($"Unsupported property-list element <{element.Name.LocalName}>.");
        }
    }

    public static string GetString(this IReadOnlyDictionary<string, object> dictionary, string key) =>
        dictionary.TryGetValue(key, out var value) && value is string text
            ? text
            : throw new InvalidDataException($"Property list has no string '{key}'.");

    public static byte[] GetData(this IReadOnlyDictionary<string, object> dictionary, string key) =>
        dictionary.TryGetValue(key, out var value) && value is byte[] data
            ? data
            : throw new InvalidDataException($"Property list has no data '{key}'.");

    public static long GetInteger(this IReadOnlyDictionary<string, object> dictionary, string key) =>
        dictionary.TryGetValue(key, out var value) && value is long number
            ? number
            : throw new InvalidDataException($"Property list has no integer '{key}'.");

    public static bool GetBoolean(this IReadOnlyDictionary<string, object> dictionary, string key) =>
        dictionary.TryGetValue(key, out var value) && value is true;
}
