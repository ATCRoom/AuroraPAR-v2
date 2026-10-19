namespace AuroraPAR
{
    /// <summary>
    /// The Earth around a runway: a sphere with the radius of curvature of the WGS84 ellipsoid at the threshold in the
    /// direction of the runway (Euler's formula). Along the final the distances are then right within a few metres
    /// up to 40 NM (a fixed mean radius of 6371 km was up to about 0.5 % off). Bearings on a sphere do not depend on
    /// the radius, so the runway headings of runways.par stay valid (CRSCalculator: "Heading for AuroraPAR").
    ///
    /// This file uses no WPF and no other file of the radar: the simulation engine (Sim/Engine) links it, so the
    /// simulated aircraft are placed with exactly the same formulas the radar measures them with. The overload for a
    /// <c>Runway</c> is in Data.cs.
    /// </summary>
    internal static partial class Earth
    {
        private const double SemiMajorAxis = 6378137.0;
        private const double Flattening = 1 / 298.257223563;
        private const double EccentricitySquared = Flattening * (2 - Flattening);

        /// <summary>Radius in metres for a runway at this latitude with this true heading (degrees).</summary>
        public static double Radius(double latitude, double heading)
        {
            double sinLatitude = Math.Sin(latitude * Math.PI / 180);
            double w = 1 - EccentricitySquared * sinLatitude * sinLatitude;
            double meridian = SemiMajorAxis * (1 - EccentricitySquared) / (w * Math.Sqrt(w));
            double primeVertical = SemiMajorAxis / Math.Sqrt(w);
            double azimuth = heading * Math.PI / 180;
            double cos = Math.Cos(azimuth);
            double sin = Math.Sin(azimuth);
            return 1 / (cos * cos / meridian + sin * sin / primeVertical);
        }

        /// <summary>Initial bearing (degrees true, 0–360) from a point to another, on a sphere.</summary>
        public static double Bearing(double latitude1, double longitude1, double latitude2, double longitude2)
        {
            double phi1 = latitude1 * Math.PI / 180;
            double phi2 = latitude2 * Math.PI / 180;
            double deltaLambda = (longitude2 - longitude1) * Math.PI / 180;
            double y = Math.Sin(deltaLambda) * Math.Cos(phi2);
            double x = Math.Cos(phi1) * Math.Sin(phi2) - Math.Sin(phi1) * Math.Cos(phi2) * Math.Cos(deltaLambda);
            return (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;
        }

        /// <summary>Point at a distance (NM, may be negative) and initial bearing (degrees) from a point, on the sphere of the given radius.</summary>
        public static (double Latitude, double Longitude) Destination(double latitude, double longitude, double bearing, double distanceNM, double radius)
        {
            double delta = distanceNM * 1852 / radius;
            double theta = bearing * Math.PI / 180;
            double phi1 = latitude * Math.PI / 180;
            double lambda1 = longitude * Math.PI / 180;
            double phi2 = Math.Asin(Math.Sin(phi1) * Math.Cos(delta) + Math.Cos(phi1) * Math.Sin(delta) * Math.Cos(theta));
            double lambda2 = lambda1 + Math.Atan2(Math.Sin(theta) * Math.Sin(delta) * Math.Cos(phi1), Math.Cos(delta) - Math.Sin(phi1) * Math.Sin(phi2));
            return (phi2 * 180 / Math.PI, (lambda2 * 180 / Math.PI + 540) % 360 - 180);
        }

        /// <summary>Great-circle distance between two points (NM), on the sphere of the given radius (haversine).</summary>
        public static double DistanceNM(double latitude1, double longitude1, double latitude2, double longitude2, double radius)
        {
            double phi1 = latitude1 * Math.PI / 180;
            double phi2 = latitude2 * Math.PI / 180;
            double deltaPhi = phi2 - phi1;
            double deltaLambda = (longitude2 - longitude1) * Math.PI / 180;
            double a = Math.Sin(deltaPhi / 2) * Math.Sin(deltaPhi / 2) + Math.Cos(phi1) * Math.Cos(phi2) * Math.Sin(deltaLambda / 2) * Math.Sin(deltaLambda / 2);
            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return c * radius / 1852;
        }
    }
}
