using System.Windows;
using System.Windows.Input;
using ATCRoom.Maps;

namespace AuroraPAR
{
    /// <summary>
    /// Instructor+: picking the points of a route (a click while <see cref="PickingPoints"/>, given when the button is let
    /// go) and moving the points of the selected route with the left button (drag and drop, <see cref="RoutePointMoved"/>),
    /// also while it is flown. <see cref="MapPointsNear"/> gives the points of the maps near a place, to choose one.
    /// </summary>
    internal sealed partial class PlanView
    {
        /// <summary>A point of the selected route was dropped somewhere else: the route's callsign (null: the flight plan being made), the point (its tag), the new place (latitude, longitude) and where (in the view).</summary>
        public event Action<string?, object, double, double, Point>? RoutePointMoved;

        /// <summary>A right click on a point of the selected route: the route's callsign (null: the flight plan being made), the point (its tag), where.</summary>
        public event Action<string?, object, Point>? RoutePointMenuRequested;
        /// <summary>The right button pressed on a point of the selected route: its menu opens when the button is let go.</summary>
        private (string? Callsign, object Tag, Point At)? pendingPointMenu;

        /// <summary>Right button on a point of the selected route (within a few pixels). True: taken.</summary>
        private bool RouteRightPress(Point at)
        {
            pendingPointMenu = null;
            if (RoutePointMenuRequested == null || routeHandles.Count == 0) return false;
            (Point At, string? Callsign, object Tag) handle = routeHandles.OrderBy(h => (h.At - at).Length).First();
            if ((handle.At - at).Length > 7) return false;
            pendingPointMenu = (handle.Callsign, handle.Tag, at);
            return true;
        }

        /// <summary>Where the points of the selected routes were drawn the last time (to take one with the mouse).</summary>
        private readonly List<(Point At, string? Callsign, object Tag)> routeHandles = [];

        /// <summary>A point of a route taken with the left button: which, where it is now, moved yet, let go (waiting for the owner's choice).</summary>
        private sealed class RouteDrag
        {
            public string? Callsign;
            public object Tag = new();
            public Point Start;
            public Point Current;
            public bool Moved;
            public bool Released;
        }
        private RouteDrag? routeDrag;
        /// <summary>The left button pressed while picking points: the point is given when it is let go (no menu under a button still down).</summary>
        private Point? pickFrom;

        /// <summary>The latitude and longitude of a place of the view, or null (nothing drawn yet).</summary>
        public (double Latitude, double Longitude)? GeoAt(Point at)
        {
            Runway? runway = Runway;
            if (runway == null || lastScale <= 0) return null;
            double east = (at.X - lastCentre.X) / lastScale;
            double north = (lastCentre.Y - at.Y) / lastScale;
            double distance = Math.Sqrt(east * east + north * north);
            return distance < 1e-9 ? (runway.Latitude, runway.Longitude)
                : Earth.Destination(runway.Latitude, runway.Longitude, Math.Atan2(east, north) * 180 / Math.PI, distance, Earth.Radius(runway));
        }

        /// <summary>The named points of the maps shown, within a few pixels of a place of the view (the nearest first, at most 8).</summary>
        public List<(string Name, MapSymbol Symbol, double Latitude, double Longitude)> MapPointsNear(Point at, double pixels = 12)
        {
            List<(double Distance, string Name, MapSymbol Symbol, double Latitude, double Longitude)> found = [];
            if (lastScale <= 0) return [];
            foreach (PlacedLayer layer in placed)
            {
                if (!layer.Layer.ShowShapes && !layer.Layer.ShowTexts) continue;
                foreach ((Vector local, MapPoint point) in layer.Points)
                {
                    string name = point.Name.Trim();
                    if (name.Length == 0) continue;
                    Point p = new(lastCentre.X + local.X * lastScale, lastCentre.Y - local.Y * lastScale);
                    double distance = (p - at).Length;
                    if (distance > pixels) continue;
                    found.Add((distance, name.ToUpperInvariant(), layer.Layer.Symbol ?? point.Symbol, point.Latitude, point.Longitude));
                }
            }
            return found.OrderBy(f => f.Distance)
                .DistinctBy(f => (f.Name, f.Symbol))
                .Take(8)
                .Select(f => (f.Name, f.Symbol, f.Latitude, f.Longitude))
                .ToList();
        }

        /// <summary>The owner has done with a dropped point (moved it, or not): it is drawn from the route again.</summary>
        public void EndRouteMove()
        {
            routeDrag = null;
            InvalidateVisual();
        }

        /// <summary>Where a point of a route is drawn: where it is being dragged to, else its own place.</summary>
        private Point RoutePointShown(string? callsign, object? tag, Point placed) =>
            routeDrag != null && tag != null && ReferenceEquals(routeDrag.Tag, tag) && string.Equals(routeDrag.Callsign, callsign, StringComparison.OrdinalIgnoreCase)
                ? routeDrag.Current : placed;

        /// <summary>Left button: on a point of the selected route, the start of a drag; while picking, the start of a pick. True: taken.</summary>
        private bool RoutePress(Point at)
        {
            routeDrag = null;
            pickFrom = null;
            (Point At, string? Callsign, object Tag)? handle = routeHandles.Count == 0 ? null : routeHandles.OrderBy(h => (h.At - at).Length).First();
            if (handle != null && (handle.Value.At - at).Length <= 7)
            {
                routeDrag = new RouteDrag { Callsign = handle.Value.Callsign, Tag = handle.Value.Tag, Start = at, Current = handle.Value.At };
                CaptureMouse();
                return true;
            }
            if (!PickingPoints) return false;
            pickFrom = at;
            CaptureMouse();
            return true;
        }

        /// <summary>The mouse moving: a point being dragged follows it; the cursor shows that a point can be taken. True: taken.</summary>
        private bool RouteMove(Point at)
        {
            if (routeDrag != null && !routeDrag.Released)
            {
                if (!routeDrag.Moved && (at - routeDrag.Start).Length < 3) return true;
                routeDrag.Moved = true;
                routeDrag.Current = at;
                InvalidateVisual();
                return true;
            }
            if (pickFrom is Point pressed)
            {
                // FREE: dragged more than a few pixels, it moves the view instead of picking a point.
                if (Centre != CentreMode.Free || (at - pressed).Length < 4) return true;
                pickFrom = null;
                StartLeftPan(pressed);
                return false;
            }
            if (leftPan) return false;
            bool overPoint = routeHandles.Any(h => (h.At - at).Length <= 7);
            Cursor = overPoint ? Cursors.SizeAll : PickingPoints ? Cursors.Cross : Centre == CentreMode.Free ? Cursors.Hand : null;
            return false;
        }

        /// <summary>Left button let go: a point dropped (<see cref="RoutePointMoved"/>), or a point picked where the button is let go (<see cref="PointPicked"/>). True: taken.</summary>
        private bool RouteRelease(Point at)
        {
            if (routeDrag != null && !routeDrag.Released)
            {
                RouteDrag drag = routeDrag;
                if (drag.Moved && GeoAt(at) is { } dropped)
                {
                    // Drawn where it was dropped until the owner has done with it (a choice in a menu); released
                    // before the mouse is let go, so losing the capture does not forget it.
                    drag.Released = true;
                    drag.Current = at;
                    if (IsMouseCaptured) ReleaseMouseCapture();
                    InvalidateVisual();
                    RoutePointMoved?.Invoke(drag.Callsign, drag.Tag, dropped.Latitude, dropped.Longitude, at);
                    return true;
                }
                routeDrag = null;
                if (IsMouseCaptured) ReleaseMouseCapture();
                InvalidateVisual();
                // A click on a point (not moved): while picking, a point there; else a track there is selected.
                if (PickingPoints)
                {
                    if (GeoAt(at) is { } here) PointPicked?.Invoke(here.Latitude, here.Longitude, at);
                }
                else
                {
                    (Point At, string Callsign)? track = drawn.Count == 0 ? null : drawn.OrderBy(d => (d.At - at).Length).First();
                    if (track != null && (track.Value.At - at).Length <= 16) AircraftClicked?.Invoke(track.Value.Callsign);
                }
                return true;
            }
            if (pickFrom != null)
            {
                pickFrom = null;
                if (IsMouseCaptured) ReleaseMouseCapture();
                if (!PickingPoints) return true;
                if (GeoAt(at) is { } picked) PointPicked?.Invoke(picked.Latitude, picked.Longitude, at);
                else System.Media.SystemSounds.Beep.Play();
                return true;
            }
            return false;
        }

        /// <summary>The mouse taken away (another window, a menu): a drag not dropped is forgotten.</summary>
        private void RouteCaptureLost()
        {
            pickFrom = null;
            if (routeDrag != null && !routeDrag.Released)
            {
                routeDrag = null;
                InvalidateVisual();
            }
        }
    }
}
