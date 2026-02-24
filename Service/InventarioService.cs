using System.Globalization;
using System.Text.RegularExpressions;
using GeoApi.Models;
using Microsoft.Extensions.Hosting;
using OfficeOpenXml;

namespace GeoApi.Services;

public class InventarioService
{
    private readonly ILogger<InventarioService> _logger;
    private readonly CoordinateTransformService _transform;
    private readonly string _dataPath;
    private readonly object _cacheLock = new();
    private bool _cacheLoaded;
    private List<LuminarioRecord>? _cachedInventario;
    private static readonly Regex DmsRegex = new(@"(\d+)°([\d.]+)'?", RegexOptions.Compiled);
    private static readonly Regex NonNumericDmsRegex = new(@"[^0-9°.']", RegexOptions.Compiled);
    private static readonly Regex TwoDmsRegex = new(@"[NSWEO]\s*\d+°", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex NsRegex = new(@"([NS])\s*(\d+°[\d.]+')", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex EwRegex = new(@"([EW])\s*(\d+°[\d.]+')", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public InventarioService(ILogger<InventarioService> logger, CoordinateTransformService transform, IHostEnvironment env)
    {
        _logger = logger;
        _transform = transform;
        _dataPath = Path.Combine(env.ContentRootPath, "Data", "Inventario_total.xlsx");
        _logger.LogInformation("InventarioService inicializado");
        
        // Intentar cargar inventario inmediatamente para ver errores
        try
        {
            var inventario = LoadInventario();
            _logger.LogInformation($"Inventario precargado: {inventario.Count} registros");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error precargando inventario en constructor");
        }
    }

    /// <summary>
    /// Lee el archivo de inventario Excel y busca el luminario más cercano a las coordenadas dadas
    /// </summary>
    public (LuminarioRecord? Record, double DistanceMeters) FindNearestLuminario(double lat, double lon)
    {
        try
        {
            var inventario = LoadInventario();
            if (inventario == null || inventario.Count == 0)
                return (null, double.MaxValue);

            // Encontrar el registro con la menor distancia
            LuminarioRecord? nearest = null;
            var minDistance = double.MaxValue;
            foreach (var record in inventario)
            {
                var dist = record.DistanceTo(lat, lon);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    nearest = record;
                }
            }

            return (nearest, minDistance);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al buscar luminario más cercano");
            return (null, double.MaxValue);
        }
    }

    private List<LuminarioRecord> LoadInventario()
    {
        // Usar caché si ya está cargado
        if (_cacheLoaded && _cachedInventario != null)
            return _cachedInventario;

        lock (_cacheLock)
        {
            if (_cacheLoaded && _cachedInventario != null)
                return _cachedInventario;

            var records = new List<LuminarioRecord>();

            if (!File.Exists(_dataPath))
            {
                _logger.LogWarning($"Archivo de inventario no encontrado: {_dataPath}");
                _cachedInventario = records;
                _cacheLoaded = true;
                return records;
            }

            try
            {
                using var package = new ExcelPackage(new FileInfo(_dataPath));
                var worksheet = package.Workbook.Worksheets[0];
                
                if (worksheet?.Dimension == null)
                {
                    _logger.LogWarning("El worksheet no tiene datos");
                    _cachedInventario = records;
                    _cacheLoaded = true;
                    return records;
                }

                var rowCount = worksheet.Dimension.End.Row;
                _logger.LogInformation($"Total de filas en Excel: {rowCount}");
                
                // Detectar índices de columnas basado en encabezados (fila 1)
                var columnIndexes = GetColumnIndexes(worksheet);
                _logger.LogInformation($"Columnas detectadas: {string.Join(", ", columnIndexes.Select(kv => $"{kv.Key}={kv.Value}"))}");

                // Procesar filas de datos (a partir de fila 2)
                int validRecords = 0;
                int debugCount = 0;
                for (int row = 2; row <= rowCount; row++)
                {
                    try
                    {
                        // Debug: mostrar algunos valores de lat/lon
                        if (debugCount < 5 && columnIndexes.TryGetValue("Coordenadas", out var debugCol))
                        {
                            var coordVal = worksheet.Cells[row, debugCol].Value?.ToString();
                            _logger.LogInformation($"Fila {row} - Coordenadas raw: '{coordVal}'");
                            debugCount++;
                        }
                        
                        var record = ParseRow(worksheet, row, columnIndexes);
                        if (record != null)
                        {
                            records.Add(record);
                            validRecords++;
                            if (validRecords <= 3)
                            {
                                _logger.LogInformation($"Fila {row}: Barrio={record.Barrio}, Lat={record.Lat}, Lon={record.Lon}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Error procesando fila {row}");
                    }
                }

                _logger.LogInformation($"Inventario cargado: {validRecords} registros válidos de {rowCount - 1} filas");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar inventario desde Excel");
            }

            _cachedInventario = records;
            _cacheLoaded = true;
            return records;
        }
    }

    private Dictionary<string, int> GetColumnIndexes(ExcelWorksheet worksheet)
    {
        var indexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        
        for (int col = 1; col <= worksheet.Dimension?.End.Column; col++)
        {
            var cellValue = worksheet.Cells[1, col].Value;
            if (cellValue == null)
                continue;
                
            var header = cellValue.ToString()?.Trim().ToLower() ?? "";
            
            if (string.IsNullOrWhiteSpace(header))
                continue;

            _logger.LogInformation($"Col {col}: '{header}'");

            // Mapeo flexible de nombres de columnas
            if (header.Contains("barrio") && !indexes.ContainsKey("Barrio"))
                indexes["Barrio"] = col;
            else if ((header.Contains("dirección") || header.Contains("direccion") || header.Contains("address") || header.Contains("dir")) && !indexes.ContainsKey("DireccionFinal"))
                indexes["DireccionFinal"] = col;
            else if ((header.Contains("código") || header.Contains("codigo") || header.Contains("luminaria") || header.Contains("id")) && !indexes.ContainsKey("CodigoLuminaria"))
                indexes["CodigoLuminaria"] = col;
            else if ((header.Contains("tecnología") || header.Contains("tecnologia") || header.Contains("technology") || header.Contains("tipo")) && !indexes.ContainsKey("Tecnologia"))
                indexes["Tecnologia"] = col;
            else if ((header.Contains("potencia") || header.Contains("power") || header.Contains("watt") || header.Contains("w")) && !indexes.ContainsKey("Potencia"))
                indexes["Potencia"] = col;
            else if (header == "coordenadas" && !indexes.ContainsKey("Coordenadas"))
                indexes["Coordenadas"] = col;
            else if (header.StartsWith("lat") && !indexes.ContainsKey("Lat"))
                indexes["Lat"] = col;
            else if ((header.StartsWith("lon") || header.StartsWith("long")) && !indexes.ContainsKey("Lon"))
                indexes["Lon"] = col;
            else if (header.Contains("easting") || (header == "x" && !indexes.ContainsKey("Easting")))
                indexes["Easting"] = col;
            else if (header.Contains("northing") || (header == "y" && !indexes.ContainsKey("Northing")))
                indexes["Northing"] = col;
            else if (header.Contains("latitud") && !indexes.ContainsKey("Latitud"))
                indexes["Latitud"] = col;
            else if (header.Contains("longitud") && !indexes.ContainsKey("Longitud"))
                indexes["Longitud"] = col;
        }

        _logger.LogInformation($"Índices finales: {string.Join(", ", indexes.Select(kv => $"{kv.Key}={kv.Value}"))}");
        return indexes;
    }

    private LuminarioRecord? ParseRow(ExcelWorksheet worksheet, int row, Dictionary<string, int> columnIndexes)
    {
        var record = new LuminarioRecord();

        // Leer valores disponibles
        if (columnIndexes.TryGetValue("Barrio", out var col))
            record.Barrio = worksheet.Cells[row, col].Value?.ToString()?.Trim();

        if (columnIndexes.TryGetValue("DireccionFinal", out col))
            record.DireccionFinal = worksheet.Cells[row, col].Value?.ToString()?.Trim();

        if (columnIndexes.TryGetValue("CodigoLuminaria", out col))
            record.CodigoLuminaria = worksheet.Cells[row, col].Value?.ToString()?.Trim();

        if (columnIndexes.TryGetValue("Tecnologia", out col))
            record.Tecnologia = worksheet.Cells[row, col].Value?.ToString()?.Trim();

        if (columnIndexes.TryGetValue("Potencia", out col))
            record.Potencia = worksheet.Cells[row, col].Value?.ToString()?.Trim();

        // Intentar obtener coordenadas de diferentes formas
        double? lat = null;
        double? lon = null;

        // Primero intenta con Lat/Lon directo
        if (columnIndexes.TryGetValue("Lat", out col))
            lat = ExtractDouble(worksheet.Cells[row, col].Value);

        if (columnIndexes.TryGetValue("Lon", out col))
            lon = ExtractDouble(worksheet.Cells[row, col].Value);

        // Si no tiene Lat/Lon, intenta con Latitud/Longitud
        if ((lat == null || lon == null) && columnIndexes.TryGetValue("Latitud", out col))
            lat = ExtractDouble(worksheet.Cells[row, col].Value);

        if ((lat == null || lon == null) && columnIndexes.TryGetValue("Longitud", out col))
            lon = ExtractDouble(worksheet.Cells[row, col].Value);

        // Si es una columna "Coordenadas" con ambas juntas (ej: "N 10°44.710', W 074°45.460'")
        if ((lat == null || lon == null) && columnIndexes.TryGetValue("Coordenadas", out col))
        {
            var coordStr = worksheet.Cells[row, col].Value?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(coordStr))
            {
                // Intenta separar por coma
                var parts = coordStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                
                if (parts.Length == 2)
                {
                    // Formato: "N 10°44.710', W 074°45.460'"
                    var latStr = parts[0].Trim();
                    var lonStr = parts[1].Trim();
                    
                    var parsedLat = ParseDMSCoordinate(latStr);
                    var parsedLon = ParseDMSCoordinate(lonStr);
                    
                    if (parsedLat.HasValue && parsedLon.HasValue)
                    {
                        lat = parsedLat.Value;
                        lon = parsedLon.Value;
                    }
                }
                else if (parts.Length == 1)
                {
                    // Podría ser un solo valor o formato diferente
                    // Intenta buscar dentro si es "lat, lon" o apenas dos coordenadas
                    var singlePart = coordStr;
                    
                    // Si tiene dos parejas de grados/minutos, asumir que es lat,lon
                    var degreeMatches = TwoDmsRegex.Matches(singlePart);
                    if (degreeMatches.Count >= 2)
                    {
                        // Intenta extraer N/S primero, luego E/W
                        var nsMatch = NsRegex.Match(singlePart);
                        var ewMatch = EwRegex.Match(singlePart);
                        
                        if (nsMatch.Success && ewMatch.Success)
                        {
                            var latVal = ParseDMSCoordinate(nsMatch.Groups[1].Value + " " + nsMatch.Groups[2].Value);
                            var lonVal = ParseDMSCoordinate(ewMatch.Groups[1].Value + " " + ewMatch.Groups[2].Value);
                            
                            if (latVal.HasValue && lonVal.HasValue)
                            {
                                lat = latVal;
                                lon = lonVal;
                            }
                        }
                    }
                }
            }
        }

        // Si tampoco tiene eso, intenta con Easting/Northing (MAGNA-SIRGAS)
        if ((lat == null || lon == null) && 
            columnIndexes.TryGetValue("Easting", out col) && 
            columnIndexes.TryGetValue("Northing", out var colNorthing))
        {
            var easting = ExtractDouble(worksheet.Cells[row, col].Value);
            var northing = ExtractDouble(worksheet.Cells[row, colNorthing].Value);

            if (easting.HasValue && northing.HasValue)
            {
                var transformed = _transform.TransformToWgs84(easting.Value, northing.Value);
                lat = transformed.Lat;
                lon = transformed.Lon;
            }
        }

        record.Lat = lat;
        record.Lon = lon;

        // Solo retornar si tiene coordenadas válidas
        return (lat.HasValue && lon.HasValue) ? record : null;
    }

    private double? ExtractDouble(object? value)
    {
        if (value == null)
            return null;

        if (value is double d)
            return d;

        if (double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            return parsed;

        return null;
    }

    /// <summary>
    /// Convierte coordenadas en formato DMS (Degree Minute Second) a decimal
    /// Ej: "N 10°44.710'" -> 10.7451666...
    /// Ej: "W 074°45.460'" -> -74.7576666...
    /// </summary>
    private double? ParseDMSCoordinate(string? coordStr)
    {
        if (string.IsNullOrWhiteSpace(coordStr))
            return null;

        try
        {
            coordStr = coordStr.Trim();
            
            // Detectar dirección (N, S, E, W)
            bool isNegative = coordStr.StartsWith("S", StringComparison.OrdinalIgnoreCase) ||
                             coordStr.StartsWith("W", StringComparison.OrdinalIgnoreCase);
            
            // Remover letras de dirección
            var numericPart = NonNumericDmsRegex.Replace(coordStr, "");
            
            // Formato típico: "10°44.710'" (grados°minutos.fracciones')
            var match = DmsRegex.Match(numericPart);
            
            if (match.Success)
            {
                if (double.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var degrees) &&
                    double.TryParse(match.Groups[2].Value, CultureInfo.InvariantCulture, out var minutes))
                {
                    var result = degrees + (minutes / 60.0);
                    return isNegative ? -result : result;
                }
            }
        }
        catch (Exception)
        {
            // Log silencioso para no saturar logs
        }

        return null;
    }
}
