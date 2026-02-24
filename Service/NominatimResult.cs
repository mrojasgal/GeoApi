using System.Text.Json.Serialization;

namespace GeoApi.Services;

internal sealed class NominatimResult
{
    // Nominatim devuelve lat/lon como string
    [JsonPropertyName("lat")]
    public string? Lat { get; set; }

    [JsonPropertyName("lon")]
    public string? Lon { get; set; }

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }
}
