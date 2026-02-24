namespace GeoApi.Models;

public class LuminarioRecord
{
    public string? Barrio { get; set; }
    public string? DireccionFinal { get; set; }
    public string? CodigoLuminaria { get; set; }
    public string? Tecnologia { get; set; }
    public string? Potencia { get; set; }
    public double? Lat { get; set; }
    public double? Lon { get; set; }
    
    /// <summary>
    /// Calcula la distancia en metros a otro punto usando la fórmula de Haversine
    /// </summary>
    public double DistanceTo(double otherLat, double otherLon)
    {
        if (Lat == null || Lon == null)
            return double.MaxValue;

        const double earthRadiusKm = 6371.0;
        var lat1Rad = Math.PI * Lat.Value / 180.0;
        var lat2Rad = Math.PI * otherLat / 180.0;
        var deltaLatRad = Math.PI * (otherLat - Lat.Value) / 180.0;
        var deltaLonRad = Math.PI * (otherLon - Lon.Value) / 180.0;

        var a = Math.Sin(deltaLatRad / 2) * Math.Sin(deltaLatRad / 2) +
                Math.Cos(lat1Rad) * Math.Cos(lat2Rad) *
                Math.Sin(deltaLonRad / 2) * Math.Sin(deltaLonRad / 2);
        
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return earthRadiusKm * c * 1000; // Convertir a metros
    }
}
