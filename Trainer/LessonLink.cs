using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace AuroraPAR
{
    /// <summary>Side of a lesson between two PCs: the instructor (Instructor+, the simulation) or the student (PAR / RDR).</summary>
    internal enum LessonRole
    {
        Instructor,
        Student
    }

    /// <summary>
    /// AuroraPAR v2 Trainer: the lesson link between the instructor's PC and the student's PC, through the public MQTT
    /// relays of the coordination panel (encrypted connection, nothing to install, no port to open). The channel is
    /// computed from the lesson code (it cannot be found without it). Each side sends one small message about every
    /// second (the instructor every half second); nothing is kept on the relay, so a student who joins later sees only
    /// what is sent from then on. The other side is "present" while its messages arrive.
    /// </summary>
    internal sealed class LessonLink : IDisposable
    {
        /// <summary>The other side is present while its last message is younger than this (seconds).</summary>
        public const double PresentSeconds = 5;

        private static (string Host, int Port, string? WebSocket)[] Brokers => CoordinationLink.Brokers;

        public LessonRole Role { get; }
        public string Code { get; }
        /// <summary>A random name of this run of the link (the numbers of the "forget" start again with it).</summary>
        public string Session { get; } = Guid.NewGuid().ToString("N")[..8];

        private readonly string channel;
        private readonly string clientId = "aurorapar-lesson-" + Guid.NewGuid().ToString("N")[..12];
        private readonly MqttFactory factory = new();
        private volatile IMqttClient? client;
        private volatile int brokerIndex;
        private readonly CancellationTokenSource stop = new();
        private DateTime connectedAt = DateTime.MinValue;
        /// <summary>Time (UTC ticks, of this PC) of the last message of the other side; 0: never.</summary>
        private long lastHeardTicks;
        /// <summary>1 while a message is being sent (one at a time: a new one is dropped meanwhile).</summary>
        private int sending;

        /// <summary>A message of the other side (raised on a background thread).</summary>
        public event Action<string>? Received;

        public LessonLink(LessonRole role, string code)
        {
            Role = role;
            Code = code;
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes("AuroraPAR lesson " + code));
            channel = "aurorapar/lesson/v1/" + Convert.ToHexString(hash)[..20].ToLowerInvariant();
            CoordinationLink.Log($"Lesson: start as {role}");
            _ = Task.Run(Run);
        }

        private string OwnTopic => channel + (Role == LessonRole.Instructor ? "/i" : "/s");
        private string OtherTopic => channel + (Role == LessonRole.Instructor ? "/s" : "/i");

        public bool Connected => client?.IsConnected == true;
        /// <summary>Relay in use, or null while not connected.</summary>
        public string? RelayName => Connected ? Brokers[brokerIndex].Host : null;

        /// <summary>The other side has been heard at least once.</summary>
        public bool EverHeard => Interlocked.Read(ref lastHeardTicks) != 0;

        /// <summary>The other side is linked now (a message less than <see cref="PresentSeconds"/> ago).</summary>
        public bool OtherPresent
        {
            get
            {
                long ticks = Interlocked.Read(ref lastHeardTicks);
                return ticks != 0 && (DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalSeconds < PresentSeconds;
            }
        }

        /// <summary>The first relay of the list with another name than the first one (the "other relay").</summary>
        private static int OtherRelayIndex
        {
            get
            {
                for (int i = 1; i < Brokers.Length; i++) if (Brokers[i].Host != Brokers[0].Host) return i;
                return 0;
            }
        }

        /// <summary>Connects, reconnects after a network problem, and changes relay when the other side is not heard.</summary>
        private async Task Run()
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    if (!Connected)
                    {
                        await Connect();
                    }
                    else
                    {
                        // Each side uses the first relay that works on its own network: the two may end up on two relays,
                        // both connected but deaf to each other. The student then tries the other relay soon, the
                        // instructor much later (so they meet, instead of changing relay together).
                        double alone = Role == LessonRole.Student ? 15 : 60;
                        long ticks = Interlocked.Read(ref lastHeardTicks);
                        DateTime heard = ticks == 0 ? DateTime.MinValue : new DateTime(ticks, DateTimeKind.Utc);
                        DateTime since = heard > connectedAt ? heard : connectedAt;
                        if ((DateTime.UtcNow - since).TotalSeconds > alone)
                        {
                            CoordinationLink.Log($"Lesson: the other side is not heard on {Brokers[brokerIndex].Host}, trying the other relay");
                            await Disconnect();
                            brokerIndex = Brokers[brokerIndex].Host == Brokers[0].Host ? OtherRelayIndex : 0;
                        }
                    }
                }
                catch (Exception)
                {
                    // Never stop the loop.
                }
                try
                {
                    await Task.Delay(Connected ? 1000 : 2000, stop.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task Connect()
        {
            IMqttClient next = factory.CreateMqttClient();
            (string host, int port, string? webSocket) = Brokers[brokerIndex];
            try
            {
                next.ApplicationMessageReceivedAsync += OnMessage;
                CoordinationLink.Log($"Lesson: connecting to {host}:{port} ({(webSocket != null ? "secure WebSocket" : "MQTT over TLS")})...");
                MqttClientOptionsBuilder builder = new MqttClientOptionsBuilder();
                builder = webSocket != null
                    ? builder.WithWebSocketServer(o => o.WithUri(webSocket))
                    : builder.WithTcpServer(host, port);
                MqttClientOptions options = builder
                    .WithTlsOptions(o => o.UseTls())
                    .WithClientId(clientId)
                    .WithCleanSession()
                    .WithKeepAlivePeriod(TimeSpan.FromSeconds(20))
                    .Build();
                using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                await next.ConnectAsync(options, timeout.Token);
                MqttClientSubscribeOptions subscribe = factory.CreateSubscribeOptionsBuilder()
                    .WithTopicFilter(f => f.WithTopic(OtherTopic).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce))
                    .Build();
                await next.SubscribeAsync(subscribe, timeout.Token);
                if (stop.IsCancellationRequested)
                {
                    next.ApplicationMessageReceivedAsync -= OnMessage;
                    next.Dispose();
                    return;
                }
                IMqttClient? old = client;
                client = next;
                if (old != null)
                {
                    old.ApplicationMessageReceivedAsync -= OnMessage;
                    old.Dispose();
                }
                connectedAt = DateTime.UtcNow;
                CoordinationLink.Log($"Lesson: connected to {host}:{port} as {Role}");
            }
            catch (Exception error)
            {
                CoordinationLink.Log($"Lesson: failed ({error.GetType().Name}: {error.Message})");
                next.ApplicationMessageReceivedAsync -= OnMessage;
                next.Dispose();
                // No network or relay not reachable: the loop tries again, with the next relay.
                brokerIndex = (brokerIndex + 1) % Brokers.Length;
            }
        }

        private async Task Disconnect()
        {
            IMqttClient? old = Interlocked.Exchange(ref client, null);
            if (old == null) return;
            try
            {
                if (old.IsConnected) await old.DisconnectAsync();
            }
            catch (Exception)
            {
            }
            old.ApplicationMessageReceivedAsync -= OnMessage;
            old.Dispose();
        }

        /// <summary>Sends a message to the other side (without waiting; dropped if the previous one is still on its way).</summary>
        public void Send(string payload)
        {
            IMqttClient? current = client;
            if (current?.IsConnected != true) return;
            if (Interlocked.Exchange(ref sending, 1) == 1) return;
            _ = Task.Run(async () =>
            {
                try
                {
                    MqttApplicationMessage message = new MqttApplicationMessageBuilder()
                        .WithTopic(OwnTopic)
                        .WithPayload(payload)
                        .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
                        .Build();
                    using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(5));
                    await current.PublishAsync(message, timeout.Token);
                }
                catch (Exception)
                {
                    // Lost: the next one follows in a second.
                }
                finally
                {
                    Interlocked.Exchange(ref sending, 0);
                }
            });
        }

        private Task OnMessage(MqttApplicationMessageReceivedEventArgs e)
        {
            try
            {
                if (e.ApplicationMessage.Topic == OtherTopic)
                {
                    Interlocked.Exchange(ref lastHeardTicks, DateTime.UtcNow.Ticks);
                    Received?.Invoke(Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment));
                }
            }
            catch (Exception)
            {
                // A malformed message is ignored.
            }
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            stop.Cancel();
            CoordinationLink.Log("Lesson: stop");
            try
            {
                // On a pool thread: waiting on the UI thread for a task that resumes there would hang.
                Task.Run(Disconnect).Wait(TimeSpan.FromSeconds(2));
            }
            catch (Exception)
            {
            }
        }
    }

    /// <summary>An aircraft in a lesson message (short names: the messages travel twice a second).</summary>
    internal sealed class LessonAircraft
    {
        [JsonPropertyName("c")] public string Callsign { get; set; } = "";
        [JsonPropertyName("la")] public double Latitude { get; set; }
        [JsonPropertyName("lo")] public double Longitude { get; set; }
        [JsonPropertyName("al")] public double Altitude { get; set; }
        [JsonPropertyName("tr")] public double Track { get; set; }
        [JsonPropertyName("sp")] public double Speed { get; set; }
        [JsonPropertyName("sq")] public string? Squawk { get; set; }

        public static LessonAircraft From(Aircraft aircraft) => new()
        {
            Callsign = aircraft.Callsign,
            Latitude = Math.Round(aircraft.Latitude, 6),
            Longitude = Math.Round(aircraft.Longitude, 6),
            Altitude = Math.Round(aircraft.Altitude),
            Track = Math.Round(aircraft.Track, 1),
            Speed = Math.Round(aircraft.Speed, 1),
            Squawk = aircraft.Squawk
        };

        [JsonIgnore]
        public bool IsValid => !string.IsNullOrWhiteSpace(Callsign) && Callsign.Length <= 16
            && double.IsFinite(Latitude) && Math.Abs(Latitude) <= 90 && double.IsFinite(Longitude) && Math.Abs(Longitude) <= 180
            && double.IsFinite(Altitude) && double.IsFinite(Track) && double.IsFinite(Speed) && Speed >= 0
            && (Squawk == null || Squawk.Length <= 4);

        /// <summary>The aircraft for the radar: simulated (from the instructor) or real traffic (the student's IVAO).</summary>
        public Aircraft ToAircraft(bool simulated) => new()
        {
            Callsign = Callsign.Trim(),
            Latitude = Latitude,
            Longitude = Longitude,
            Altitude = Altitude,
            Track = ((Track % 360) + 360) % 360,
            Speed = Speed,
            Squawk = Squawk,
            IsTest = simulated,
            IsSim = simulated
        };
    }

    /// <summary>The student's runway in full (the runway files of the two PCs may differ).</summary>
    internal sealed record LessonRunway
    {
        [JsonPropertyName("i")] public string Icao { get; init; } = "";
        [JsonPropertyName("d")] public string Designator { get; init; } = "";
        [JsonPropertyName("h")] public double Heading { get; init; }
        [JsonPropertyName("dh")] public double DefaultHeading { get; init; }
        [JsonPropertyName("e")] public double Elevation { get; init; }
        [JsonPropertyName("la")] public double Latitude { get; init; }
        [JsonPropertyName("lo")] public double Longitude { get; init; }
        [JsonPropertyName("l")] public double LengthM { get; init; }
        [JsonPropertyName("w")] public double WidthM { get; init; }
        [JsonPropertyName("gs")] public double GlideSlope { get; init; }
        [JsonPropertyName("tch")] public double Tch { get; init; }
        [JsonPropertyName("mdh")] public double Mdh { get; init; }
        [JsonPropertyName("r")] public double Range { get; init; }
        [JsonPropertyName("td")] public double? TouchdownM { get; init; }
        [JsonPropertyName("mv")] public double? MagneticVariation { get; init; }

        public static LessonRunway From(Runway runway) => new()
        {
            Icao = runway.ICAO,
            Designator = runway.Designator,
            Heading = runway.Heading,
            DefaultHeading = runway.DefaultHeading,
            Elevation = runway.Elevation,
            Latitude = runway.Latitude,
            Longitude = runway.Longitude,
            LengthM = runway.LengthM,
            WidthM = runway.WidthM,
            GlideSlope = runway.GlideSlope,
            Tch = runway.TCH,
            Mdh = runway.MDH,
            Range = runway.Distance,
            TouchdownM = runway.TouchdownOverrideM,
            MagneticVariation = runway.MagneticVariation
        };

        [JsonIgnore]
        public bool IsValid => !string.IsNullOrWhiteSpace(Icao) && Icao.Length <= 8 && Designator.Length <= 16
            && double.IsFinite(Latitude) && Math.Abs(Latitude) <= 90 && double.IsFinite(Longitude) && Math.Abs(Longitude) <= 180
            && double.IsFinite(Heading) && double.IsFinite(DefaultHeading) && double.IsFinite(Elevation)
            && double.IsFinite(LengthM) && LengthM >= 0 && double.IsFinite(WidthM) && WidthM >= 0
            && double.IsFinite(GlideSlope) && GlideSlope > 0 && GlideSlope < 10 && double.IsFinite(Tch) && double.IsFinite(Mdh)
            && double.IsFinite(Range) && Range > 0 && Range <= 100
            && (TouchdownM is not double t || double.IsFinite(t)) && (MagneticVariation is not double v || double.IsFinite(v));

        public Runway ToRunway() => new()
        {
            ICAO = Icao.Trim().ToUpperInvariant(),
            Designator = Designator,
            Heading = ((Heading % 360) + 360) % 360,
            DefaultHeading = ((DefaultHeading % 360) + 360) % 360,
            Elevation = Elevation,
            Latitude = Latitude,
            Longitude = Longitude,
            LengthM = LengthM,
            WidthM = WidthM,
            GlideSlope = GlideSlope,
            TCH = Tch,
            MDH = Mdh,
            DefaultMDH = Mdh,
            Distance = Range,
            DefaultDistance = Range,
            TouchdownOverrideM = TouchdownM,
            MagneticVariation = MagneticVariation
        };
    }

    /// <summary>Instructor → student, about twice a second (also with no aircraft: it tells that the link works).</summary>
    internal sealed class InstructorLessonMessage
    {
        [JsonPropertyName("v")] public int Version { get; set; } = 1;
        /// <summary>The run of the instructor's link (<see cref="LessonLink.Session"/>): a new one restarts the numbers of <see cref="Forget"/>.</summary>
        [JsonPropertyName("id")] public string? Session { get; set; }
        /// <summary>The simulated aircraft.</summary>
        [JsonPropertyName("a")] public List<LessonAircraft>? Aircraft { get; set; }
        /// <summary>QNH of the instructor (hPa; 0: none), used by the student only without Aurora.</summary>
        [JsonPropertyName("q")] public int Qnh { get; set; }
        /// <summary>Wind of the simulation (from, true degrees; knots), while its aircraft fly.</summary>
        [JsonPropertyName("wf")] public double? WindFrom { get; set; }
        [JsonPropertyName("wk")] public double? WindKnots { get; set; }
        /// <summary>Aircraft removed or started again, each with a number (sent a few times).</summary>
        [JsonPropertyName("f")] public List<LessonForget>? Forget { get; set; }
    }

    internal sealed class LessonForget
    {
        [JsonPropertyName("n")] public int Number { get; set; }
        [JsonPropertyName("c")] public string Callsign { get; set; } = "";
    }

    /// <summary>Student → instructor, about once a second.</summary>
    internal sealed class StudentLessonMessage
    {
        [JsonPropertyName("v")] public int Version { get; set; } = 1;
        [JsonPropertyName("r")] public LessonRunway? Runway { get; set; }
        /// <summary>The azimuth beam and the scan limits of the student's PAR from above: latitude, longitude, latitude, ...</summary>
        [JsonPropertyName("b")] public double[]? Beam { get; set; }
        [JsonPropertyName("s")] public double[]? Scan { get; set; }
        /// <summary>The simulated aircraft that the student's PAR sees.</summary>
        [JsonPropertyName("seen")] public List<string>? Seen { get; set; }
        /// <summary>The student's IVAO traffic (from his Aurora).</summary>
        [JsonPropertyName("t")] public List<LessonAircraft>? Traffic { get; set; }

        public static double[] Flatten(IEnumerable<(double Latitude, double Longitude)> points) =>
            points.SelectMany(p => new[] { Math.Round(p.Latitude, 6), Math.Round(p.Longitude, 6) }).ToArray();

        public static List<(double Latitude, double Longitude)> Points(double[]? values)
        {
            List<(double, double)> points = [];
            if (values == null || values.Length % 2 != 0 || values.Length > 64) return points;
            for (int i = 0; i < values.Length; i += 2)
            {
                if (!double.IsFinite(values[i]) || !double.IsFinite(values[i + 1]) || Math.Abs(values[i]) > 90 || Math.Abs(values[i + 1]) > 180) return [];
                points.Add((values[i], values[i + 1]));
            }
            return points;
        }
    }
}
