using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FileCopier;

/// <summary>
/// The settings files beside the executable: appsettings.json, and appsettings.local.json whose
/// settings override it.
/// </summary>
internal static class SettingsFiles
{
    public const string Main = "appsettings.json";
    public const string Local = "appsettings.local.json";

    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        // Keep backslashes and non-English folder names readable in Notepad.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Saves a text setting in the file it takes effect from: appsettings.local.json when that file
    /// sets it, otherwise appsettings.json. Only the value changes, so comments and layout are kept.
    /// Returns the name of the file written.
    /// </summary>
    public static string Save(string directory, string name, string value)
    {
        var localPath = Path.Combine(directory, Local);
        var path = File.Exists(localPath) && FindValue(File.ReadAllBytes(localPath), name) != null
            ? localPath
            : Path.Combine(directory, Main);

        var content = File.Exists(path) ? File.ReadAllBytes(path) : Encoding.UTF8.GetBytes("{}");
        var encodedValue = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, WriteOptions));
        byte[] updated;
        if (FindValue(content, name) is var (start, end))
        {
            updated = [.. content.AsSpan(0, start), .. encodedValue, .. content.AsSpan(end)];
        }
        else
        {
            // A new setting: the file is written again without its comments.
            var settings = JsonNode.Parse(SkipByteOrderMark(content), documentOptions: ReadOptions) as JsonObject
                ?? throw new InvalidDataException($"{path} must contain a JSON object.");
            settings[name] = value;
            updated = Encoding.UTF8.GetBytes(settings.ToJsonString(WriteOptions) + Environment.NewLine);
        }

        // Replace the file in one step so an interrupted save cannot leave half a file.
        var temporaryPath = path + ".tmp";
        File.WriteAllBytes(temporaryPath, updated);
        File.Move(temporaryPath, path, overwrite: true);
        return Path.GetFileName(path);
    }

    /// <summary>Byte range of a top-level setting's value; names match without case, as settings do.</summary>
    private static (int Start, int End)? FindValue(byte[] content, string name)
    {
        var offset = content.Length - SkipByteOrderMark(content).Length;
        var reader = new Utf8JsonReader(content.AsSpan(offset), new JsonReaderOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            throw new InvalidDataException("A settings file must contain a JSON object.");

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var matches = string.Equals(reader.GetString(), name, StringComparison.OrdinalIgnoreCase);
            reader.Read();
            var start = (int)reader.TokenStartIndex;
            reader.Skip();
            if (matches) return (offset + start, offset + (int)reader.BytesConsumed);
        }
        return null;
    }

    private static ReadOnlySpan<byte> SkipByteOrderMark(byte[] content) =>
        content.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? content.AsSpan(Encoding.UTF8.Preamble.Length) : content;
}
