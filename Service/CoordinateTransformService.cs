using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;

namespace GeoApi.Services;

public sealed class CoordinateTransformService
{
    private readonly ICoordinateTransformation _transform;
    private readonly ICoordinateTransformation _inverseTransform;

    public CoordinateTransformService()
    {
        var csFactory = new CoordinateSystemFactory();
        var ctFactory = new CoordinateTransformationFactory();

        // WGS84 (EPSG:4326)
        var wgs84 = GeographicCoordinateSystem.WGS84;

        // EPSG:9377 - MAGNA-SIRGAS / Origen-Nacional (WKT oficial simplificado)
        var origenNacionalWkt = @"
PROJCS[""MAGNA-SIRGAS_Origen-Nacional"",
    GEOGCS[""GCS_MAGNA"",
        DATUM[""D_MAGNA"",
            SPHEROID[""GRS_1980"",6378137,298.257222101]],
        PRIMEM[""Greenwich"",0],
        UNIT[""Degree"",0.0174532925199433]],
    PROJECTION[""Transverse_Mercator""],
    PARAMETER[""latitude_of_origin"",4],
    PARAMETER[""central_meridian"",-73],
    PARAMETER[""scale_factor"",0.9992],
    PARAMETER[""false_easting"",5000000],
    PARAMETER[""false_northing"",2000000],
    UNIT[""Meter"",1]]
";

        var origenNacional = csFactory.CreateFromWkt(origenNacionalWkt);

        _transform = ctFactory.CreateFromCoordinateSystems(wgs84, origenNacional);
        _inverseTransform = ctFactory.CreateFromCoordinateSystems(origenNacional, wgs84);
    }

    public (double Lat, double Lon) TransformToWgs84(double easting, double northing)
    {
        // Orden importante: (easting, northing)
        var result = _inverseTransform.MathTransform.Transform(new[] { easting, northing });

        return (Lat: result[1], Lon: result[0]);
    }

    public (double Easting, double Northing) ToOrigenNacional(double lat, double lon)
    {
        if (lat < -90 || lat > 90)
            throw new ArgumentOutOfRangeException(nameof(lat), "Lat debe estar entre -90 y 90.");

        if (lon < -180 || lon > 180)
            throw new ArgumentOutOfRangeException(nameof(lon), "Lon debe estar entre -180 y 180.");

        // Orden importante: (lon, lat)
        var result = _transform.MathTransform.Transform(new[] { lon, lat });

        return (Easting: result[0], Northing: result[1]);
    }
}
