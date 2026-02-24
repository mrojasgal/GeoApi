using GeoApi.Models;
using GeoApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace GeoApi.Controllers;

[ApiController]
[Route("geocode")]
public sealed class GeocodeController : ControllerBase
{
    private readonly OpenStreetMapService _osm;
    private readonly CoordinateTransformService _transform;
    private readonly InventarioService _inventario;
    private readonly OpenAiImageService _openAi;
    private readonly ILogger<GeocodeController> _logger;

    public GeocodeController(
        OpenStreetMapService osm,
        CoordinateTransformService transform,
        InventarioService inventario,
        OpenAiImageService openAi,
        ILogger<GeocodeController> logger)
    {
        _osm = osm;
        _transform = transform;
        _inventario = inventario;
        _openAi = openAi;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Geocode(
        [FromQuery] string country,
        [FromQuery] string city,
        [FromQuery] string address,
        CancellationToken ct = default)
    {
        country = (country ?? "").Trim();
        city = (city ?? "").Trim();
        address = (address ?? "").Trim();

        if (string.IsNullOrWhiteSpace(country) ||
            string.IsNullOrWhiteSpace(city) ||
            string.IsNullOrWhiteSpace(address))
        {
            return BadRequest(new
            {
                error = "Debe enviar country, city y address como parámetros obligatorios.",
                example = "/geocode?country=Colombia&city=Bogotá&address=Carrera 7 #10-20"
            });
        }

        try
        {
            // 1) OSM (lat/lon)
            var geo = await _osm.GeocodeAsync(country, city, address, ct);
            if (geo is null)
            {
                return NotFound(new
                {
                    error = "No se encontraron resultados para la dirección suministrada.",
                    input = new { country, city, address }
                });
            }

            // 2) Conversión automática EPSG:9377
            var (easting, northing) = _transform.ToOrigenNacional(geo.Lat, geo.Lon);

            // 3) Buscar luminario más cercano en el inventario
            var (luminarioMasCercano, distanciaMetros) = _inventario.FindNearestLuminario(geo.Lat, geo.Lon);
            
            LuminarioMatch? lumin = null;
            if (luminarioMasCercano != null)
            {
                var imageUrl = await _openAi.GenerateLuminarioImageAsync(
                    luminarioMasCercano.Barrio ?? "desconocido",
                    luminarioMasCercano.Tecnologia ?? "desconocida",
                    luminarioMasCercano.Potencia ?? "desconocida",
                    ct);

                lumin = new LuminarioMatch
                {
                    Barrio = luminarioMasCercano.Barrio,
                    DireccionFinal = luminarioMasCercano.DireccionFinal,
                    CodigoLuminaria = luminarioMasCercano.CodigoLuminaria,
                    Tecnologia = luminarioMasCercano.Tecnologia,
                    Potencia = luminarioMasCercano.Potencia,
                    DistanciaMetros = distanciaMetros,
                    ImagenUrl = imageUrl
                };
            }

            // 4) Respuesta unificada (WGS84 + EPSG:9377 + Luminario)
            var res = new GeocodeResponseDto
            {
                Country = geo.Country,
                City = geo.City,
                Address = geo.Address,
                QueryUsed = geo.QueryUsed,
                DisplayName = geo.DisplayName,
                Lat = geo.Lat,
                Lon = geo.Lon,
                Easting = easting,
                Northing = northing,
                Source = geo.Source,
                LuminarioMasCercano = lumin
            };

            return Ok(res);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en /geocode con conversión EPSG:9377.");
            return StatusCode(500, new { error = "Error interno en geocode/conversión." });
        }
    }
}
