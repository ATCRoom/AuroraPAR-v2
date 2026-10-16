using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Shapes;

namespace AuroraPAR
{
    /// <summary>
    /// The Earth around a runway: a sphere with the radius of curvature of the WGS84 ellipsoid at the threshold in the
    /// direction of the runway (Euler's formula). Along the final the distances are then right within a few metres
    /// up to 40 NM (a fixed mean radius of 6371 km was up to about 0.5 % off). Bearings on a sphere do not depend on
    /// the radius, so the runway headings of runways.par stay valid (CRSCalculator: "Heading for AuroraPAR").
    /// </summary>
    internal static class Earth
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

        public static double Radius(Runway runway) => Radius(runway.Latitude, runway.Heading);

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
    }

    internal class Runway
    {
        public string ICAO { get; set; } = "ZZZZ";
        public string Designator { get; set; } = "00";
        /// <summary>
        /// Heading in deg.
        /// </summary>
        public double Heading { get; set; } = 0;
        private double? defaultHeading;
        /// <summary>
        /// Heading of the runway file. <see cref="Heading"/> can be changed on the fly with the CRS selector (never
        /// saved): the centreline then turns around the threshold.
        /// </summary>
        public double DefaultHeading { get => defaultHeading ?? Heading; set => defaultHeading = value; }
        /// <summary>True when the heading in use is not the one of the runway file (CRS selector).</summary>
        public bool CourseChanged => Math.Abs(Heading - DefaultHeading) > 1e-6;
        /// <summary>CRS selector: heading for this session (true, degrees).</summary>
        public void SetSessionHeading(double trueHeading)
        {
            defaultHeading ??= Heading;
            Heading = (trueHeading % 360 + 360) % 360;
        }
        /// <summary>Back to the heading of the runway file.</summary>
        public void ResetHeading() => Heading = DefaultHeading;
        /// <summary>
        /// Elevation in ft.
        /// </summary>
        public double Elevation { get; set; } = 0;
        /// <summary>
        /// Plus is north.
        /// </summary>
        public double Latitude { get; set; }
        /// <summary>
        /// Plus is east.
        /// </summary>
        public double Longitude { get; set; }
        private double _length = 0;
        /// <summary>
        /// Length in meters.
        /// </summary>
        public double LengthM { get { return _length; } set { _length = value; } }
        /// <summary>
        /// Length in nautical miles.
        /// </summary>
        public double LengthNM { get { return LengthM / 1852; } set { _length = value * 1852; } }
        /// <summary>
        /// Glide slope angle in degrees.
        /// </summary>
        public double GlideSlope { get; set; } = 3.0;
        /// <summary>
        /// Threshold crossing height.
        /// </summary>
        public double TCH { get; set; } = 50;
        /// <summary>
        /// Minimum descend height.
        /// </summary>
        public double MDH { get; set; } = 200;
        /// <summary>
        /// Decision height from the runway file. <see cref="MDH"/> can be changed on the fly during the session
        /// (never saved) and is reset to this value when the runway is selected again.
        /// </summary>
        public double DefaultMDH { get; set; } = 200;
        /// <summary>
        /// Length of the decision height line from the touchdown point, in NM.
        /// </summary>
        public const double DecisionHeightLineLength = 3;
        /// <summary>
        /// Distance from the runway to be displayed in NM.
        /// </summary>
        public double Distance { get; set; } = 10.0;
        /// <summary>
        /// Default display range of this runway in NM, as read from the runway file (not changed by zooming).
        /// </summary>
        public double DefaultDistance { get; set; } = 10.0;
        private double _width = 0;
        /// <summary>
        /// Width in meters.
        /// </summary>
        public double WidthM { get { return _width; } set { _width = value; } }
        /// <summary>
        /// Width in nautical miles.
        /// </summary>
        public double WidthNM { get { return WidthM / 1852; } set { _width = value * 1852; } }
        /// <summary>
        /// Distance of the touchdown point beyond the threshold, in meters, when given in the runway file.
        /// When null it is calculated as the point where the glide path reaches the runway.
        /// </summary>
        public double? TouchdownOverrideM { get; set; }
        /// <summary>
        /// Magnetic variation of this runway (degrees, East positive), when given in the runway file.
        /// When null the default of the profile is used (see <see cref="Profile.MagneticVariation"/>).
        /// </summary>
        public double? MagneticVariation { get; set; }

        /// <summary>
        /// Altitude (ft, as on the chart) where the final approach starts (FAP ALT), optional: drawn as a fixed height
        /// line when the profile shows it (see <see cref="Profile.ShowFapLine"/>).
        /// </summary>
        public double? FapAltitude { get; set; }
        /// <summary>
        /// Terrain / obstacle profile under the approach, from terrain.par (the same for all the approaches of the
        /// runway); empty if none. See <see cref="TerrainFile"/>.
        /// </summary>
        public IReadOnlyList<TerrainStep> Terrain { get; set; } = [];
        /// <summary>Ground clutter zones of the runway from clutter.par (none: invented clutter). See <see cref="GroundClutter"/>.</summary>
        public IReadOnlyList<ClutterZone> ClutterZones { get; set; } = [];

        /// <summary>
        /// Magnetic final course: true heading corrected with the variation of the runway, or the given default.
        /// </summary>
        public int FinalCourse(double defaultVariation)
        {
            return AuroraPAR.MagneticVariation.FinalCourse(Heading, MagneticVariation ?? defaultVariation);
        }
        /// <summary>
        /// Line number in the runway file this runway was read from (null for a new runway).
        /// </summary>
        public int? SourceLine { get; set; }
        /// <summary>
        /// Original text of the line; when saving, an unchanged runway is written back exactly as it was.
        /// </summary>
        public string? SourceText { get; set; }
        /// <summary>
        /// For a new runway: line of the file after which it is saved (e.g. right after the runway it was copied from).
        /// </summary>
        public int? InsertAfterLine { get; set; }
        /// <summary>
        /// Distance of the touchdown point beyond the threshold, in meters.
        /// Default: where the glide path, crossing the threshold at TCH, reaches the runway (TCH / tan(glide slope)).
        /// </summary>
        public double TouchdownM
        {
            get
            {
                if (TouchdownOverrideM is double m) return m;
                if (GlideSlope <= 0) return 0;
                return TCH * 0.3048 / Math.Tan(GlideSlope * Math.PI / 180);
            }
        }
        /// <summary>
        /// Distance of the touchdown point beyond the threshold, in nautical miles.
        /// </summary>
        public double TouchdownNM => TouchdownM / 1852;
        public const double FeetPerNM = 6076.11549;
        /// <summary>
        /// Height in ft above the threshold elevation of a line starting at the touchdown point (on the runway)
        /// with angle GlideSlope + <paramref name="angleOffset"/>, at <paramref name="distanceFromTouchdownNM"/>.
        /// With offset 0 this is the ideal glide path; with the approach limits (see <see cref="Radar"/>) their lines.
        /// </summary>
        public double GlidePathHeight(double distanceFromTouchdownNM, double angleOffset = 0)
        {
            return distanceFromTouchdownNM * Math.Tan((GlideSlope + angleOffset) * Math.PI / 180) * FeetPerNM;
        }
        /// <summary>
        /// Distance from the touchdown point, in NM, where the glide path reaches the MDH (missed approach point).
        /// </summary>
        public double MissedApproachPointNM
        {
            get
            {
                double t = Math.Tan(GlideSlope * Math.PI / 180);
                return t > 0 ? MDH / (t * FeetPerNM) : 0;
            }
        }

        /// <summary>
        /// True for an approach with a glide path angle typed by the controller (modern display), not one of the
        /// lines of the runway file: "unpublished approach".
        /// </summary>
        public bool IsUnpublished { get; private set; }

        /// <summary>
        /// Copy of this approach with another glide path angle, marked as unpublished. Everything else (DH, range,
        /// touchdown, magnetic variation) is the same; the touchdown point follows the angle when it is not given
        /// in the file.
        /// </summary>
        public Runway WithGlideSlope(double glideSlope)
        {
            _ = BaseDesignator; // computed with the published angle, kept by the copy
            Runway copy = (Runway)MemberwiseClone();
            copy.GlideSlope = glideSlope;
            copy.IsUnpublished = true;
            copy.SourceLine = null;
            copy.SourceText = null;
            copy.InsertAfterLine = null;
            return copy;
        }

        /// <summary>Glide path angle as written on the screen (e.g. 3.0, 2.75).</summary>
        public static string FormatGlideSlope(double degrees)
        {
            return degrees.ToString("0.0#", CultureInfo.InvariantCulture);
        }

        /// <summary>Missed approach point distance as written on the screen (e.g. 0.98), "----" when not known.</summary>
        public string MissedApproachPointText()
        {
            double nm = MissedApproachPointNM;
            if (!(nm > 0) || double.IsInfinity(nm)) return "----";
            return nm.ToString(nm < 10 ? "0.00" : "0.0", CultureInfo.InvariantCulture);
        }

        private string? baseDesignator;
        /// <summary>
        /// Designator of the runway without the glide path angle, when the file writes it in the designator
        /// (e.g. "11 2.8" with a 2.8° glide path gives "11"). Otherwise the designator as it is.
        /// </summary>
        public string BaseDesignator => baseDesignator ??= StripAngle(Designator, GlideSlope);

        /// <summary>
        /// Airport and runway: the lines of the file with the same key are the approaches of one runway
        /// (e.g. "LIPC;11 2.8;..." and "LIPC;11 2.5;..." are both "LIPC 11").
        /// </summary>
        public string RunwayKey => $"{ICAO} {BaseDesignator}";

        /// <summary>Name in the runway list: one entry per runway.</summary>
        public string DisplayName => RunwayKey;

        private static string StripAngle(string designator, double glideSlope)
        {
            string[] parts = designator.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length >= 2
                && double.TryParse(parts[^1].TrimEnd('°'), NumberStyles.Float, CultureInfo.InvariantCulture, out double angle)
                && Math.Abs(angle - glideSlope) < 0.051)
            {
                return string.Join(" ", parts[..^1]);
            }
            return designator;
        }

        public override string ToString()
        {
            return $"{ICAO} {Designator}";
        }

    }
    internal class Aircraft
    {
        public string Callsign { get; set; } = "ABC1234";
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double Altitude { get; set; } = 0;
        public double Track { get; set; }
        public double Speed { get; set; }
        /// <summary>
        /// Vertical speed in ft/min (negative descending), estimated from the altitude history; null when not known yet.
        /// </summary>
        public double? VerticalSpeedFpm { get; set; }
        /// <summary>
        /// SSR (transponder) code, four octal digits, or null when Aurora gives none (or 0000).
        /// </summary>
        public string? Squawk { get; set; }
        /// <summary>
        /// Estimated (smoothed) position of the track filter of the modern display, or null (see <see cref="TrackFilter"/>).
        /// </summary>
        public Aircraft? Filtered { get; set; }
        /// <summary>Virtual aircraft of the test traffic (see <see cref="TestTraffic"/>), not from Aurora.</summary>
        public bool IsTest { get; set; }

        /// <summary>Copy of the data (without <see cref="Filtered"/>).</summary>
        public Aircraft Copy() => new()
        {
            Callsign = Callsign,
            Latitude = Latitude,
            Longitude = Longitude,
            Altitude = Altitude,
            Track = Track,
            Speed = Speed,
            VerticalSpeedFpm = VerticalSpeedFpm,
            Squawk = Squawk,
            IsTest = IsTest
        };
        /// <summary>
        /// Distance to runway.
        /// </summary>
        /// <param name="runway">Runway.</param>
        /// <returns>Distance in NM.</returns>
        public double Distance(Runway runway)
        {
            double lat1Rad = Latitude*double.Pi/180;
            double lon1Rad = Longitude * double.Pi / 180;
            double lat2Rad = runway.Latitude * double.Pi / 180;
            double lon2Rad = runway.Longitude*double.Pi/180;
            double dLat = lat2Rad - lat1Rad;
            double dLon = lon2Rad - lon1Rad;
            // Haversine formula
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(lat1Rad) * Math.Cos(lat2Rad) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return c * Earth.Radius(runway) / 1852;
        }
        /// <summary>
        /// Distance from the threshold measured along the extended runway centreline (ignoring the lateral offset), in NM.
        /// </summary>
        public double AlongTrackDistance(Runway runway)
        {
            double d = Distance(runway);
            double xt = LateralOffset(runway);
            double along = Math.Sqrt(Math.Max(0, d * d - xt * xt));
            // Positive on the approach side of the threshold, negative once the aircraft has passed it
            // (over the runway): the approach direction is the opposite of the runway heading.
            double approachBearing = (runway.Heading + 180) % 360;
            bool pastThreshold = Math.Cos((BearingFromRunway(runway) - approachBearing) * Math.PI / 180) < 0;
            return pastThreshold ? -along : along;
        }
        /// <summary>
        /// Distance from the touchdown point measured along the extended runway centreline, in NM.
        /// This is the distance controllers give on final ("4 miles from touchdown").
        /// </summary>
        public double DistanceFromTouchdown(Runway runway)
        {
            return AlongTrackDistance(runway) + runway.TouchdownNM;
        }
        public double LateralOffset(Runway runway)
        {
            // Calculate initial bearing from runway to aircraft
            double bearingToAircraft = BearingFromRunway(runway);

            double R = Earth.Radius(runway); // Earth radius in meters, around this runway

            // Angular distance from runway to aircraft (radians)
            double delta13 = Distance(runway) * 1852 / R; // Distance(runway) returns NM, convert to meters then to radians

            // Bearings in radians
            double theta13 = bearingToAircraft * Math.PI / 180.0; // runway -> aircraft
            double theta12 = runway.Heading * Math.PI / 180.0;     // runway heading

            // Cross-track distance formula (meters)
            double xt = Math.Asin(Math.Sin(delta13) * Math.Sin(theta13 - theta12)) * R;

            // Convert meters back to NM
            double lateralOffsetNM = xt / 1852.0;

            return lateralOffsetNM;
        }
        public double BearingFromRunway(Runway runway)
        {
            double lat1Rad = runway.Latitude * Math.PI / 180;
            double lon1Rad = runway.Longitude * Math.PI / 180;
            double lat2Rad = Latitude * Math.PI / 180;
            double lon2Rad = Longitude * Math.PI / 180;

            double dLon = lon2Rad - lon1Rad;
            double y = Math.Sin(dLon) * Math.Cos(lat2Rad);
            double x = Math.Cos(lat1Rad) * Math.Sin(lat2Rad) -
                       Math.Sin(lat1Rad) * Math.Cos(lat2Rad) * Math.Cos(dLon);
            double bearingToAircraft = (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;
            return bearingToAircraft;
        }
    }
    internal struct Distance(double distance)
    {
        double distance = distance;
        public static implicit operator double(Distance d) => d.distance;
        public static implicit operator Distance(int distance) => new Distance(distance);
        public static implicit operator Distance(double distance) => new Distance(distance);
        /// <summary>The range list writes the ranges in km (rounded), as the distance unit of the profile.</summary>
        public static bool Kilometres { get; set; }
        /// <summary>Unit of the range keys and readouts: KM or NM.</summary>
        public static string UnitText => Kilometres ? "KM" : "NM";
        /// <summary>A range (NM) as written on the range keys and readouts, in the unit of the profile (9.3, 19, 37 km).</summary>
        public static string ValueText(double nm)
        {
            double value = Kilometres ? nm * DistanceUnits.KilometresPerNM : nm;
            return value.ToString(Kilometres && value >= 10 ? "0" : "0.#", System.Globalization.CultureInfo.InvariantCulture);
        }

        public override string ToString()
        {
            return Kilometres
                ? $"{(distance * DistanceUnits.KilometresPerNM).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} km"
                : $"{distance} nm";
        }
    }

    internal class DataFile
    {
        //Format: ICAO;DESIGNATOR;HEADING;ELEVATION;LATITUDE;LONGITUDE;LENGTH IN METERS;WIDTH IN METERS;GLIDE SLOPE;TCH;MDH;DEFAULT DISTANCE[;TOUCHDOWN DISTANCE FROM THRESHOLD IN METERS (optional)]
        public const string FormatComment = "# ICAO;DESIGNATOR;HEADING(deg true);THRESHOLD ELEVATION(ft);THRESHOLD LATITUDE;THRESHOLD LONGITUDE;LENGTH(m);WIDTH(m);GLIDE SLOPE(deg);TCH(ft);DH(ft);DEFAULT RANGE(NM)[;TOUCHDOWN FROM THRESHOLD(m)][;MAGNETIC VARIATION e.g. 3E or 2W][;FAP ALT(ft)]";

        public static async Task<Runway[]> GetRunways(string path)
        {
            return AttachClutter(TerrainFile.Attach(Parse(await System.IO.File.ReadAllLinesAsync(path)), TerrainFile.PathFor(path)), path);
        }

        public static Runway[] ReadRunways(string path)
        {
            return AttachClutter(TerrainFile.Attach(Parse(System.IO.File.ReadAllLines(path)), TerrainFile.PathFor(path)), path);
        }

        /// <summary>Ground clutter zones of clutter.par (next to runways.par) on the runways.</summary>
        private static Runway[] AttachClutter(Runway[] runways, string runwaysPath)
        {
            Dictionary<string, List<ClutterZone>> zones = GroundClutter.Read(GroundClutter.PathFor(runwaysPath));
            foreach (Runway runway in runways)
            {
                runway.ClutterZones = zones.TryGetValue(TerrainFile.Key(runway.ICAO, runway.Designator), out List<ClutterZone>? list) ? list : [];
            }
            return runways;
        }

        private static Runway[] Parse(string[] lines)
        {
            List<Runway> runways = [];
            for (int i = 0; i < lines.Length; i++)
            {
                if (TryParseLine(lines[i], out Runway? runway))
                {
                    runway.SourceLine = i;
                    runway.SourceText = lines[i];
                    runways.Add(runway);
                }
            }
            return runways.ToArray();
        }

        /// <summary>
        /// Reads one line of the runway file. Lines that are incomplete or contain invalid numbers
        /// (e.g. comments starting with #, empty lines) are not runways and return false.
        /// </summary>
        public static bool TryParseLine(string line, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Runway? runway)
        {
            runway = null;
            string[] linedata = line.Split(';', StringSplitOptions.TrimEntries);
            // 12 fields are needed (indexes 0 to 11).
            if (linedata.Length >= 12
                && TryParse(linedata[2], out double heading)
                && TryParse(linedata[3], out double elevation)
                && TryParse(linedata[4], out double latitude)
                && TryParse(linedata[5], out double longitude)
                && TryParse(linedata[6], out double length)
                && TryParse(linedata[7], out double width)
                && TryParse(linedata[8], out double glideSlope)
                && TryParse(linedata[9], out double tch)
                && TryParse(linedata[10], out double mdh)
                && TryParse(linedata[11], out double distance))
            {
                runway = new()
                {
                    ICAO = linedata[0],
                    Designator = linedata[1],
                    Heading = heading,
                    DefaultHeading = heading,
                    Elevation = elevation,
                    Latitude = latitude,
                    Longitude = longitude,
                    LengthM = length,
                    WidthM = width,
                    GlideSlope = glideSlope,
                    TCH = tch,
                    MDH = mdh,
                    DefaultMDH = mdh,
                    Distance = distance,
                    DefaultDistance = distance
                };
                // Optional 13th field: touchdown point distance beyond the threshold, in meters.
                if (linedata.Length >= 13 && TryParse(linedata[12], out double touchdown))
                {
                    runway.TouchdownOverrideM = touchdown;
                }
                // Optional 14th field: magnetic variation (e.g. 3E, 2W). The 13th can then be empty.
                if (linedata.Length >= 14 && AuroraPAR.MagneticVariation.TryParse(linedata[13], out double variation))
                {
                    runway.MagneticVariation = variation;
                }
                // Optional 15th field: final approach altitude (FAP ALT, ft). The 13th and 14th can then be empty.
                if (linedata.Length >= 15 && TryParse(linedata[14], out double fapAltitude) && fapAltitude > 0)
                {
                    runway.FapAltitude = fapAltitude;
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Writes the runway file keeping its layout: every line that is not a runway (comments, separators)
        /// stays where it was; runways read from the file are written at their original line (exactly as they were
        /// if unchanged, see <see cref="Runway.SourceText"/>); deleted runways are removed; new runways go right after
        /// <see cref="Runway.InsertAfterLine"/>, or at the end. The previous file is kept as ".bak".
        /// Saved values are the file values (DefaultMDH, DefaultDistance), not the ones changed on the fly.
        /// </summary>
        public static void SaveRunways(string path, IReadOnlyList<Runway> runways)
        {
            string[] original = System.IO.File.Exists(path) ? System.IO.File.ReadAllLines(path) : [];
            Dictionary<int, Runway> bySource = runways.Where(r => r.SourceLine != null).ToDictionary(r => r.SourceLine!.Value);
            ILookup<int, Runway> inserted = runways.Where(r => r.SourceLine == null && r.InsertAfterLine != null).ToLookup(r => r.InsertAfterLine!.Value);
            List<string> lines = [];
            if (original.Length == 0)
            {
                lines.Add(FormatComment);
            }
            for (int i = 0; i < original.Length; i++)
            {
                if (TryParseLine(original[i], out _))
                {
                    if (bySource.TryGetValue(i, out Runway? runway))
                    {
                        lines.Add(ToLine(runway));
                    }
                }
                else
                {
                    lines.Add(original[i]);
                }
                foreach (Runway runway in inserted[i])
                {
                    lines.Add(ToLine(runway));
                }
            }
            foreach (Runway runway in runways.Where(r => r.SourceLine == null && (r.InsertAfterLine == null || r.InsertAfterLine >= original.Length)))
            {
                lines.Add(ToLine(runway));
            }
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Copy(path, path + ".bak", overwrite: true);
            }
            string temp = path + ".tmp";
            System.IO.File.WriteAllLines(temp, lines);
            System.IO.File.Move(temp, path, overwrite: true);
        }

        private static string ToLine(Runway r)
        {
            if (r.SourceText != null)
            {
                return r.SourceText;
            }
            string line = string.Join(";",
                r.ICAO, r.Designator, Format(r.DefaultHeading), Format(r.Elevation),
                CoordinateParser.Format(r.Latitude), CoordinateParser.Format(r.Longitude),
                Format(r.LengthM), Format(r.WidthM), Format(r.GlideSlope), Format(r.TCH),
                Format(r.DefaultMDH), Format(r.DefaultDistance));
            // Optional fields: touchdown, magnetic variation, FAP altitude; the empty ones at the end are left out.
            List<string> optional =
            [
                r.TouchdownOverrideM is double touchdown ? Format(touchdown) : "",
                r.MagneticVariation is double variation ? AuroraPAR.MagneticVariation.Format(variation) : "",
                r.FapAltitude is double fap ? Format(fap) : ""
            ];
            while (optional.Count > 0 && optional[^1].Length == 0) optional.RemoveAt(optional.Count - 1);
            foreach (string field in optional) line += ";" + field;
            return line;
        }

        private static string Format(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Numbers in the file always use a dot as decimal separator, whatever the Windows language.
        /// </summary>
        private static bool TryParse(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }

    /// <summary>One step of a terrain / obstacle profile: from-to distance from the threshold (NM) and altitude (ft AMSL).</summary>
    internal readonly record struct TerrainStep(double FromNM, double ToNM, double AltitudeFt);

    /// <summary>
    /// terrain.par, next to runways.par: one line per runway (not per approach), e.g.
    /// <c>LIRN;24;9.4-11.1=3310;11.1-13.8=3910</c> = ICAO; runway (first word of the designator in runways.par);
    /// steps FROM-TO (NM from the threshold) = ALTITUDE (ft above sea level, as on the charts). Lines starting with
    /// # are comments. A missing or damaged file gives no terrain (never an error at start).
    /// </summary>
    internal static class TerrainFile
    {
        public const string FileName = "terrain.par";
        public const string FormatComment = "# ICAO;RUNWAY;FROM-TO(NM from threshold)=ALTITUDE(ft AMSL);... e.g. LIRN;24;9.4-11.1=3310;11.1-13.8=3910";

        public static string PathFor(string runwaysPath) =>
            System.IO.Path.Combine(System.IO.Path.GetDirectoryName(runwaysPath) ?? "", FileName);

        /// <summary>Runway name used in terrain.par: the first word of the designator ("14 3.0" → "14").</summary>
        public static string RunwayName(string designator) =>
            designator.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

        public static string Key(string icao, string designator) => $"{icao.Trim().ToUpperInvariant()} {RunwayName(designator).ToUpperInvariant()}";

        /// <summary>Profiles by <see cref="Key"/>; empty if the file is missing or cannot be read.</summary>
        public static Dictionary<string, List<TerrainStep>> Read(string path)
        {
            Dictionary<string, List<TerrainStep>> terrain = [];
            try
            {
                if (!System.IO.File.Exists(path)) return terrain;
                foreach (string line in System.IO.File.ReadAllLines(path))
                {
                    if (TryParseLine(line, out string key, out List<TerrainStep> steps, out _)) terrain[key] = steps;
                }
            }
            catch (Exception)
            {
                // Not readable: no terrain.
            }
            return terrain;
        }

        public static Runway[] Attach(Runway[] runways, string path)
        {
            Dictionary<string, List<TerrainStep>> terrain = Read(path);
            foreach (Runway runway in runways)
            {
                runway.Terrain = terrain.TryGetValue(Key(runway.ICAO, runway.Designator), out List<TerrainStep>? steps) ? steps : [];
            }
            return runways;
        }

        private static bool TryParseLine(string line, out string key, out List<TerrainStep> steps, out string? error)
        {
            key = "";
            steps = [];
            error = null;
            string text = line.Trim();
            if (text.Length == 0 || text.StartsWith('#')) return false;
            string[] fields = text.Split(';', 3);
            if (fields.Length < 3) return false;
            key = Key(fields[0], fields[1]);
            return TryParseSteps(fields[2], out steps, out error);
        }

        /// <summary>
        /// Steps as written in the file and in the runway editor: <c>9.4-11.1=3310; 11.1-13.8=3910</c> (separated by ;).
        /// Empty text = no steps (valid).
        /// </summary>
        public static bool TryParseSteps(string text, out List<TerrainStep> steps, out string? error)
        {
            steps = [];
            error = null;
            foreach (string raw in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string[] parts = raw.Split('=', StringSplitOptions.TrimEntries);
                string[] range = parts[0].Split('-', StringSplitOptions.TrimEntries);
                if (parts.Length != 2 || range.Length != 2
                    || !TryNumber(range[0], out double from) || !TryNumber(range[1], out double to) || !TryNumber(parts[1], out double altitude))
                {
                    error = $"\"{raw}\": write FROM-TO=ALTITUDE, e.g. 9.4-11.1=3310";
                    return false;
                }
                if (from < 0 || to <= from || to > 100)
                {
                    error = $"\"{raw}\": the distances must go up, between 0 and 100 NM";
                    return false;
                }
                if (altitude < -1500 || altitude > 30000)
                {
                    error = $"\"{raw}\": altitude between -1500 and 30000 ft";
                    return false;
                }
                steps.Add(new TerrainStep(from, to, altitude));
            }
            steps.Sort((a, b) => a.FromNM.CompareTo(b.FromNM));
            return true;
        }

        public static string FormatSteps(IEnumerable<TerrainStep> steps) =>
            string.Join("; ", steps.Select(s => $"{Number(s.FromNM)}-{Number(s.ToNM)}={Number(s.AltitudeFt)}"));

        /// <summary>
        /// Writes the profiles of the given runways (key → steps text, empty = no line), keeping comments and the lines
        /// of the other runways; a copy of the old file is kept as terrain.par.bak.
        /// </summary>
        public static void Save(string path, IReadOnlyDictionary<string, (string Icao, string Runway, string Steps)> profiles)
        {
            List<string> lines = [];
            HashSet<string> written = [];
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Copy(path, path + ".bak", overwrite: true);
                foreach (string line in System.IO.File.ReadAllLines(path))
                {
                    string text = line.Trim();
                    string[] fields = text.Split(';', 3);
                    if (text.Length == 0 || text.StartsWith('#') || fields.Length < 3)
                    {
                        lines.Add(line);
                        continue;
                    }
                    string key = Key(fields[0], fields[1]);
                    if (!profiles.TryGetValue(key, out var profile))
                    {
                        lines.Add(line);
                        continue;
                    }
                    if (written.Add(key) && profile.Steps.Trim().Length > 0) lines.Add(LineOf(profile));
                }
            }
            else
            {
                lines.Add(FormatComment);
            }
            foreach ((string key, var profile) in profiles)
            {
                if (!written.Contains(key) && profile.Steps.Trim().Length > 0) lines.Add(LineOf(profile));
            }
            System.IO.File.WriteAllLines(path, lines);
        }

        private static string LineOf((string Icao, string Runway, string Steps) profile)
        {
            TryParseSteps(profile.Steps, out List<TerrainStep> steps, out _);
            return $"{profile.Icao.Trim().ToUpperInvariant()};{RunwayName(profile.Runway)};{string.Join(";", steps.Select(s => $"{Number(s.FromNM)}-{Number(s.ToNM)}={Number(s.AltitudeFt)}"))}";
        }

        private static bool TryNumber(string text, out double value) =>
            double.TryParse(text.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);

        private static string Number(double value) => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }
}
