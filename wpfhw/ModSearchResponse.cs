using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace wpfhw;

public class ModSearchResponse
{
    [JsonIgnore]
    public List<ModSearchHit> Hits { get; set; } = new();

    [JsonPropertyName("hits")]
    public List<ModSearchHit>? HitsRaw
    {
        get => Hits;
        set => Hits = value ?? new();
    }

    [JsonPropertyName("offset")]
    public int Offset { get; set; }

    [JsonPropertyName("limit")]
    public int Limit { get; set; }

    [JsonPropertyName("total_hits")]
    public int TotalHits { get; set; }
}
