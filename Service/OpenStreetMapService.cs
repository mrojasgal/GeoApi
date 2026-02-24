using System.Globalization;
using System.Net;
using System.Text.Json;
using GeoApi.Models;
using Microsoft.Extensions.Options;

namespace GeoApi.Services;

public sealed class OpenStreetMapOptions
{
    public string NominatimBaseUrl { get; set; } = "https://nominatim.openstreetmap.org";
}

public sealed class OpenStreetMapService
{
    private readonly HttpClient _http;
    private readonly OpenStreetMapOptions _options;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public OpenStreetMapService(HttpClient http, IOptions<OpenStreetMapOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<GeocodeResponseDto?> GeocodeAsync(string country, string city, string address, CancellationToken ct = default)
    {
        // Construir query principal
        var queryUsed = $"{address}, {city}, {country}";

        // Construye URL segura con encoding
        var url = $"{_options.NominatimBaseUrl.TrimEnd('/')}/search" +
                  $"?q={WebUtility.UrlEncode(queryUsed)}" +
                  $"&format=json&limit=1&addressdetails=0";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);

        // IMPORTANTE: Nominatim requiere un User-Agent identificable
        // Ajusta el valor a tu organización/proyecto
        req.Headers.UserAgent.ParseAdd("GeoApi/1.0 (contact: soporte@tu-dominio.com)");

        // Aceptar JSON explícitamente
        req.Headers.Accept.ParseAdd("application/json");

        using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!res.IsSuccessStatusCode)
        {
            // Puedes extender esto luego con más detalle
            return null;
        }

        List<NominatimResult>? results;
        try
        {
            await using var stream = await res.Content.ReadAsStreamAsync(ct);
            results = await JsonSerializer.DeserializeAsync<List<NominatimResult>>(stream, JsonOptions, ct);
        }
        catch
        {
            // JSON inesperado
            return null;
        }

        var first = results?.FirstOrDefault();
        if (first?.Lat is null || first?.Lon is null)
            return null;

        if (!double.TryParse(first.Lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat))
            return null;

        if (!double.TryParse(first.Lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
            return null;

        return new GeocodeResponseDto
        {
            Country = country,
            City = city,
            Address = address,
            QueryUsed = queryUsed,
            DisplayName = first.DisplayName ?? "",
            Lat = lat,
            Lon = lon,
            Source = "nominatim"
        };
    }
}
