namespace GeoApi.Models;

public sealed class GeocodeResponseDto
{
    public string Country { get; init; } = "";
    public string City { get; init; } = "";
    public string Address { get; init; } = "";

    public string QueryUsed { get; init; } = "";
    public string DisplayName { get; init; } = "";

    // EPSG:4326 (WGS84)
    public double Lat { get; init; }
    public double Lon { get; init; }
    public string SourceEpsg { get; init; } = "EPSG:4326";

    // EPSG:9377 (MAGNA-SIRGAS / Origen Nacional)
    public double Easting { get; init; }
    public double Northing { get; init; }
    public string TargetEpsg { get; init; } = "EPSG:9377";

    public string Source { get; init; } = "nominatim";

    // Datos del luminario más cercano
    public LuminarioMatch? LuminarioMasCercano { get; init; }
}

public sealed class LuminarioMatch
{
    public string? Barrio { get; init; }
    public string? DireccionFinal { get; init; }
    public string? CodigoLuminaria { get; init; }
    public string? Tecnologia { get; init; }
    public string? Potencia { get; init; }
    public double DistanciaMetros { get; init; }
    public string? ImagenUrl { get; init; }
}
