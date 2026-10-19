using AuroraPAR;

namespace ATCRoom.Sim
{
    /// <summary>
    /// The flat working plane of the simulation, centred on the threshold of the reference runway: x east, y north
    /// (true north at the threshold), in NM. A point of the plane is placed on the Earth as an azimuthal equidistant
    /// projection: its distance from the threshold and its bearing are kept. With the same sphere as the radar
    /// (<see cref="Earth.Radius(double, double)"/> at the threshold, in the direction of the runway) every straight
    /// line through the threshold is a great circle of the radar: an aircraft on the extended centreline of the plane
    /// is exactly on the centreline of the PAR, at the same distance.
    ///
    /// Exact only while the radar uses the same runway with the same heading: a course changed by the student
    /// (CRS) changes the radar's radius by a fraction of a percent. Away from the threshold the y axis is no longer
    /// exactly true north (meridian convergence, about 0.5° at 30 NM): the track of an aircraft is therefore taken
    /// from its successive positions on the Earth, not from its heading in the plane.
    /// </summary>
    public sealed class LocalPlane
    {
        /// <summary>Threshold of the reference runway (degrees) and its true heading (degrees, the one of the runway file).</summary>
        public double Latitude { get; }
        public double Longitude { get; }
        public double Heading { get; }
        /// <summary>Radius of the sphere (metres): the radar's one for this runway.</summary>
        public double Radius { get; }

        public LocalPlane(double latitude, double longitude, double heading)
        {
            Latitude = latitude;
            Longitude = longitude;
            Heading = heading;
            Radius = Earth.Radius(latitude, heading);
        }

        /// <summary>Latitude and longitude (degrees) of a point of the plane (NM east, NM north of the threshold).</summary>
        public (double Latitude, double Longitude) ToGeo(double x, double y)
        {
            double distance = Math.Sqrt(x * x + y * y);
            if (distance < 1e-12) return (Latitude, Longitude);
            double bearing = Math.Atan2(x, y) * 180 / Math.PI;
            return Earth.Destination(Latitude, Longitude, bearing, distance, Radius);
        }

        /// <summary>Point of the plane (NM east, NM north) of a latitude and longitude (degrees).</summary>
        public (double X, double Y) FromGeo(double latitude, double longitude)
        {
            double distance = Earth.DistanceNM(Latitude, Longitude, latitude, longitude, Radius);
            if (distance < 1e-12) return (0, 0);
            double bearing = Earth.Bearing(Latitude, Longitude, latitude, longitude) * Math.PI / 180;
            return (distance * Math.Sin(bearing), distance * Math.Cos(bearing));
        }
    }
}
