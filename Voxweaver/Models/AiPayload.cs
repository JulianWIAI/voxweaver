using System.Text.Json.Serialization;

namespace Voxweaver.Models;

/// <summary>
/// Payload exported to the LLM. Includes a creative theme and one entry per phrase.
/// </summary>
public class AiExportPayload
{
    [JsonPropertyName("theme")]
    public string Theme { get; set; } = string.Empty;

    [JsonPropertyName("sequences")]
    public List<SequenceEntry> Sequences { get; set; } = new();
}

public class SequenceEntry
{
    [JsonPropertyName("lineId")]
    public string LineId { get; set; } = string.Empty;

    [JsonPropertyName("syllableCount")]
    public int SyllableCount { get; set; }
}
