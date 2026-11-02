namespace MatchingService.Domain.Entities;

public record GeoPoint(double Lat, double Lng)
{
    // Khoảng cách theo đường chim bay (km), công thức Haversine
    public double DistanceKmTo(GeoPoint other)
    {
        const double earthRadiusKm = 6371;
        double ToRad(double deg) => deg * Math.PI / 180;

        var dLat = ToRad(other.Lat - Lat);
        var dLng = ToRad(other.Lng - Lng);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(ToRad(Lat)) * Math.Cos(ToRad(other.Lat)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return earthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}
