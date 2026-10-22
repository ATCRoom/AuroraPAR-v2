namespace ATCRoom.Maps
{
    /// <summary>
    /// Cuts a part out of a map (a big file that covers a wide area): only what is inside a circle is kept, so the map of
    /// an airport is small. Lines are cut at the border of the circle; labels and points outside are dropped. The distances
    /// are computed on a flat plane around the centre (enough for a few hundred NM).
    /// </summary>
    public static class MapCut
    {
        /// <summary>NM in one degree of latitude.</summary>
        private const double NmPerDegree = 3440.065 * Math.PI / 180;

        /// <summary>A copy of the map with only what is in the circle (the original is not changed).</summary>
        public static MapFile CutCircle(MapFile map, double centreLatitude, double centreLongitude, double radiusNM)
        {
            MapFile result = new()
            {
                FormatVersion = map.FormatVersion,
                Name = map.Name,
                Airport = map.Airport,
                Source = map.Source,
                Sources = new Dictionary<string, string>(map.Sources, StringComparer.OrdinalIgnoreCase),
                LineColour = map.LineColour,
                LabelColour = map.LabelColour
            };
            if (!(radiusNM > 0) || !MapCoordinates.IsValid(centreLatitude, centreLongitude)) return result;
            double k = Math.Max(0.01, Math.Cos(centreLatitude * Math.PI / 180));
            (double X, double Y) Local(double lat, double lon) => ((lon - centreLongitude) * k * NmPerDegree, (lat - centreLatitude) * NmPerDegree);
            double[] Back((double X, double Y) p) => [centreLatitude + p.Y / NmPerDegree, centreLongitude + p.X / (k * NmPerDegree)];
            bool Inside((double X, double Y) p) => p.X * p.X + p.Y * p.Y <= radiusNM * radiusNM;

            // The parameters (0..1) where the segment q→p crosses the circle, in order.
            List<double> Crossings((double X, double Y) q, (double X, double Y) p)
            {
                double dx = p.X - q.X, dy = p.Y - q.Y;
                double a = dx * dx + dy * dy;
                List<double> found = [];
                if (a < 1e-12) return found;
                double b = 2 * (q.X * dx + q.Y * dy);
                double c = q.X * q.X + q.Y * q.Y - radiusNM * radiusNM;
                double disc = b * b - 4 * a * c;
                if (disc < 0) return found;
                double root = Math.Sqrt(disc);
                foreach (double t in new[] { (-b - root) / (2 * a), (-b + root) / (2 * a) })
                {
                    if (t >= 0 && t <= 1) found.Add(t);
                }
                return found;
            }
            double[] At((double X, double Y) q, (double X, double Y) p, double t) => Back((q.X + (p.X - q.X) * t, q.Y + (p.Y - q.Y) * t));

            foreach (MapLine line in map.Lines)
            {
                List<double[]>? current = null;
                (double X, double Y) previous = default;
                bool previousInside = false;
                void Flush()
                {
                    if (current != null && current.Count >= 2) result.Lines.Add(new MapLine { Name = line.Name, Category = line.Category, Points = current });
                    current = null;
                }
                for (int i = 0; i < line.Points.Count; i++)
                {
                    double[] point = line.Points[i];
                    (double X, double Y) p = Local(point[0], point[1]);
                    bool inside = Inside(p);
                    if (i == 0)
                    {
                        if (inside) current = [[point[0], point[1]]];
                    }
                    else if (previousInside && inside)
                    {
                        current ??= [];
                        current.Add([point[0], point[1]]);
                    }
                    else if (previousInside && !inside)
                    {
                        List<double> t = Crossings(previous, p);
                        if (t.Count > 0 && current != null) current.Add(At(previous, p, t[^1]));
                        Flush();
                    }
                    else if (!previousInside && inside)
                    {
                        List<double> t = Crossings(previous, p);
                        current = [];
                        if (t.Count > 0) current.Add(At(previous, p, t[0]));
                        current.Add([point[0], point[1]]);
                    }
                    else
                    {
                        // Both ends outside: the segment may still cross the circle (in and out).
                        List<double> t = Crossings(previous, p);
                        if (t.Count == 2 && t[1] - t[0] > 1e-9)
                        {
                            current = [At(previous, p, t[0]), At(previous, p, t[1])];
                            Flush();
                        }
                    }
                    previous = p;
                    previousInside = inside;
                }
                Flush();
            }
            foreach (MapLabel label in map.Labels)
            {
                if (Inside(Local(label.Latitude, label.Longitude)))
                {
                    result.Labels.Add(new MapLabel { Text = label.Text, Latitude = label.Latitude, Longitude = label.Longitude, Size = label.Size, Category = label.Category });
                }
            }
            foreach (MapPoint point in map.Points)
            {
                if (Inside(Local(point.Latitude, point.Longitude)))
                {
                    result.Points.Add(new MapPoint
                    {
                        Name = point.Name, Description = point.Description, Latitude = point.Latitude, Longitude = point.Longitude,
                        Symbol = point.Symbol, Category = point.Category
                    });
                }
            }
            return result;
        }

        /// <summary>True when a point is inside the circle (same flat plane as <see cref="CutCircle"/>).</summary>
        public static bool IsInside(double latitude, double longitude, double centreLatitude, double centreLongitude, double radiusNM)
        {
            double k = Math.Max(0.01, Math.Cos(centreLatitude * Math.PI / 180));
            double x = (longitude - centreLongitude) * k * NmPerDegree, y = (latitude - centreLatitude) * NmPerDegree;
            return x * x + y * y <= radiusNM * radiusNM;
        }
    }
}
