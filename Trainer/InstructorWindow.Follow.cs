using System.Globalization;
using System.Windows;
using ATCRoom.Sim;

namespace AuroraPAR
{
    /// <summary>
    /// AuroraPAR v2 Trainer: the Instructor+ follows the runway of the student's PAR. With no simulated aircraft it does it
    /// by itself (InstructorWindow.FollowRadarRunway); while aircraft fly the instructor is asked whether to remove them
    /// and follow (once for each new runway: "No" keeps them, and the question is not repeated for that runway). A new
    /// airport brings its METAR by itself (from Aurora when it has that airport, else from the internet).
    /// </summary>
    internal sealed partial class InstructorWindow
    {
        /// <summary>The runway the instructor chose not to follow (No to the question), or null.</summary>
        private string? declinedRunway;
        private bool askingToFollow;

        private static string RunwayIdentity(Runway runway) => string.Join("|", runway.RunwayKey,
            runway.Latitude.ToString("R", CultureInfo.InvariantCulture), runway.Longitude.ToString("R", CultureInfo.InvariantCulture),
            runway.DefaultHeading.ToString("R", CultureInfo.InvariantCulture), runway.GlideSlope.ToString("R", CultureInfo.InvariantCulture),
            runway.Elevation.ToString("R", CultureInfo.InvariantCulture));

        /// <summary>The student's PAR is on another runway while aircraft fly: ask (not inside the refresh).</summary>
        private void OfferToFollow(Runway? reference, Runway radar)
        {
            if (reference == null || askingToFollow) return;
            string identity = RunwayIdentity(radar);
            if (identity == declinedRunway) return;
            askingToFollow = true;
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    Runway? now = bridge.Reference;
                    int flying = bridge.Count;
                    // Meanwhile the aircraft are gone (it follows by itself) or the PAR came back.
                    if (now == null || flying == 0 || RunwayIdentity(radarRunway()) != identity) return;
                    bool otherAirport = !string.Equals(now.ICAO.Trim(), radar.ICAO.Trim(), StringComparison.OrdinalIgnoreCase);
                    string text = (otherAirport
                            ? $"The student's PAR is now at another airport: {radar.DisplayName}.\nThe simulation is still at {now.DisplayName}."
                            : $"The student's PAR is now on {radar.DisplayName}.\nThe simulation is still on {now.DisplayName}.")
                        + $"\n\nRemove the {flying} simulated aircraft and follow the new runway?\n\n" +
                        $"Yes: they are removed (their flight plans wait again), new aircraft start on {radar.DisplayName}." +
                        (otherAirport ? " The METAR of the new airport is asked by itself." : "") +
                        $"\nNo: they stay, and new aircraft are still made on {now.DisplayName}.";
                    if (MessageBox.Show(this, text, Title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        declinedRunway = null;
                        // The next refresh (inside ClearAllAircraft) takes the new runway.
                        ClearAllAircraft();
                    }
                    else
                    {
                        declinedRunway = identity;
                    }
                }
                finally
                {
                    askingToFollow = false;
                }
            });
        }

        /// <summary>The reference runway changed: at a new airport the old METAR goes and the new one is asked.</summary>
        private void ReferenceChanged(string? oldAirport, Runway runway)
        {
            declinedRunway = null;
            if (oldAirport == null || string.Equals(oldAirport.Trim(), runway.ICAO.Trim(), StringComparison.OrdinalIgnoreCase)) return;
            metarSource.Text = "";
            metarSource.ToolTip = null;
            MetarForNewAirport();
        }

        /// <summary>
        /// The METAR of the new airport, as From METAR does, but without a message when there is none: from Aurora when it
        /// gives the METAR of this airport, else from the internet.
        /// </summary>
        private void MetarForNewAirport()
        {
            if (auroraConnected() && MetarWind.TryParse(metar(), out double from, out double speed, out double gust))
            {
                if (!double.IsNaN(from)) windFromBox.Text = from.ToString("000", CultureInfo.InvariantCulture);
                windSpeedBox.Text = Math.Round(speed).ToString(CultureInfo.InvariantCulture);
                windGustBox.Text = Math.Round(gust).ToString(CultureInfo.InvariantCulture);
                ApplyWind();
                metarSource.Text = "from Aurora";
                return;
            }
            WindFromInternet(quiet: true);
        }
    }
}
