using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace AuroraPAR
{
    /// <summary>Side of the coordination: the radar (approach / PAR) or the tower.</summary>
    internal enum CoordinationRole
    {
        Radar,
        Tower,
        /// <summary>Instructor: sees the lights and who is online, cannot press anything.</summary>
        Monitor
    }

    internal enum LightState
    {
        Off,
        /// <summary>Called by one side, waiting for the other side.</summary>
        Flashing,
        /// <summary>Acknowledged by the other side.</summary>
        Steady
    }

    /// <summary>When the panel sounds.</summary>
    internal enum CoordinationSound
    {
        /// <summary>Every press, on both panels (also your own).</summary>
        EveryPress,
        /// <summary>Only the presses of the other side (calls, acknowledges, reset).</summary>
        OtherSide
    }

    /// <summary>Options of the coordination panel (saved with the settings, not in the profiles).</summary>
    internal class CoordinationSettings
    {
        public const int Lights = 5;
        /// <summary>As the real panel: blue, white, yellow, red, green; reset black (white on the real one, on its own row).</summary>
        public static readonly string[] DefaultColors = ["#2E6BFF", "#FFFFFF", "#FFD400", "#FF2020", "#20C840", "#101010"];

        /// <summary>Airport typed by hand (needed when connected as observer); null: from the callsign.</summary>
        public string? Airport { get; set; }
        /// <summary>Role chosen by hand; null: from the callsign (_TWR = tower, anything else = radar).</summary>
        public CoordinationRole? Role { get; set; }
        /// <summary>
        /// Panel code (optional): a secret shared by the panels of a group, part of the channel name, so that only who
        /// knows it can join. Null: the open channel of the airport (as before).
        /// </summary>
        public string? Code { get; set; }
        public const int MaxCodeLength = 12;
        /// <summary>Colours of the five lights and of the reset button.</summary>
        public string[] Colors { get; set; } = (string[])DefaultColors.Clone();
        public static readonly string[] DefaultLabels = ["", "", "", "", "", "RESET"];
        public const int MaxLabelLength = 10;
        /// <summary>Text engraved under each button (empty: none). Only on this panel.</summary>
        public string[] Labels { get; set; } = (string[])DefaultLabels.Clone();
        /// <summary>Panel always on top of the other windows.</summary>
        public bool Topmost { get; set; } = true;
        /// <summary>Sound at every press on both panels, or only for the presses of the other side.</summary>
        public CoordinationSound Sound { get; set; } = CoordinationSound.EveryPress;

        /// <summary>The panel for phones and tablets (a web page, see docs/coord).</summary>
        public const string PhonePanelUrl = "https://atcroom.github.io/AuroraPAR-v2/coord/";

        /// <summary>
        /// Link that opens the phone panel already set: airport, role, colours and engraved texts.
        /// </summary>
        public string PhoneLink(string? airport, CoordinationRole? role)
        {
            List<string> query = [];
            if (airport != null) query.Add("apt=" + Uri.EscapeDataString(airport));
            if (role != null) query.Add("role=" + role.Value.ToString().ToLowerInvariant());
            // Always present (empty: no code), so a phone that had a code drops it with the new link.
            query.Add("k=" + (Code ?? ""));
            query.Add("c=" + string.Join(",", Colors.Select(c => ColorText.ToHex(ColorText.Parse(c, System.Windows.Media.Colors.White)).TrimStart('#'))));
            query.Add("l=" + string.Join(",", Labels.Select(Uri.EscapeDataString)));
            query.Add("s=" + (Sound == CoordinationSound.OtherSide ? "other" : "every"));
            return PhonePanelUrl + "?" + string.Join("&", query);
        }

        public void Normalize()
        {
            if (Colors == null || Colors.Length != DefaultColors.Length) Colors = (string[])DefaultColors.Clone();
            if (Labels == null || Labels.Length != DefaultLabels.Length) Labels = (string[])DefaultLabels.Clone();
            for (int i = 0; i < Labels.Length; i++)
            {
                string label = (Labels[i] ?? "").Trim().ToUpperInvariant();
                Labels[i] = label.Length > MaxLabelLength ? label[..MaxLabelLength] : label;
            }
            for (int i = 0; i < Colors.Length; i++)
            {
                if (!ColorText.TryParse(Colors[i], out _)) Colors[i] = DefaultColors[i];
            }
            if (Airport != null)
            {
                Airport = Airport.Trim().ToUpperInvariant();
                if (Airport.Length == 0) Airport = null;
            }
            if (Role is CoordinationRole role && !Enum.IsDefined(role)) Role = null;
            Code = NormalizeCode(Code);
            if (!Enum.IsDefined(Sound)) Sound = CoordinationSound.EveryPress;
        }

        /// <summary>Panel code as used in the channel: letters and digits only, upper case, at most 12; null when empty.</summary>
        public static string? NormalizeCode(string? code)
        {
            string text = new string((code ?? "").ToUpperInvariant().Where(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9').ToArray());
            if (text.Length > MaxCodeLength) text = text[..MaxCodeLength];
            return text.Length == 0 ? null : text;
        }

        /// <summary>A new random panel code: 6 letters and digits, without the ones easy to confuse (0/O, 1/I/L).</summary>
        public static string GenerateCode()
        {
            const string alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
            return new string(Enumerable.Range(0, 6).Select(_ => alphabet[System.Security.Cryptography.RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray());
        }

        /// <summary>
        /// Airport and role from the connected callsign: "LIRF_APP" = LIRF, radar; "LIRF_TWR" = LIRF, tower.
        /// Observers ("..._OBS") and callsigns without an airport give nothing.
        /// </summary>
        public static (string? Airport, CoordinationRole? Role) FromCallsign(string? callsign)
        {
            if (string.IsNullOrWhiteSpace(callsign)) return (null, null);
            string[] parts = callsign.Trim().ToUpperInvariant().Split('_', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return (null, null);
            string suffix = parts[^1];
            if (suffix == "OBS") return (null, null);
            string? airport = parts[0].Length == 4 && parts[0].All(char.IsLetter) ? parts[0] : null;
            return (airport, suffix == "TWR" ? CoordinationRole.Tower : CoordinationRole.Radar);
        }
    }

    /// <summary>
    /// State of the lights shared by the two panels.
    /// Same rule for every light: the first side that presses it makes it flash (and the other side hears an
    /// alert); when the other side presses it, it becomes steady (acknowledged). The meaning of each light is
    /// up to the controllers. Reset switches everything off.
    /// </summary>
    internal class CoordinationState
    {
        public LightState[] Lights { get; set; } = new LightState[CoordinationSettings.Lights];
        /// <summary>Side that made each light flash.</summary>
        public CoordinationRole?[] CalledBy { get; set; } = new CoordinationRole?[CoordinationSettings.Lights];
        /// <summary>Who sent this state (to ignore our own messages) and when.</summary>
        public string Sender { get; set; } = "";
        public long Time { get; set; }

        public CoordinationState Copy()
        {
            return new CoordinationState
            {
                Lights = (LightState[])Lights.Clone(),
                CalledBy = (CoordinationRole?[])CalledBy.Clone(),
                Sender = Sender,
                Time = Time
            };
        }

        /// <summary>A side presses a light.</summary>
        public void Press(int light, CoordinationRole role)
        {
            switch (Lights[light])
            {
                case LightState.Off:
                    Lights[light] = LightState.Flashing;
                    CalledBy[light] = role;
                    break;
                case LightState.Flashing when CalledBy[light] != role:
                    Lights[light] = LightState.Steady;
                    break;
                case LightState.Flashing:
                    // Pressed again by the side that called: call cancelled.
                    Lights[light] = LightState.Off;
                    CalledBy[light] = null;
                    break;
            }
        }

        public void Reset()
        {
            Lights = new LightState[CoordinationSettings.Lights];
            CalledBy = new CoordinationRole?[CoordinationSettings.Lights];
        }

        public bool IsValid => Lights?.Length == CoordinationSettings.Lights && CalledBy?.Length == CoordinationSettings.Lights;
    }

    /// <summary>
    /// Link between the radar and tower panels of the same airport through a public MQTT relay (encrypted
    /// connection, nothing to install, no port to open). The state of the lights is a retained message, so a
    /// panel started later shows the current lights at once. Each side also publishes whether it is online.
    /// </summary>
    internal sealed class CoordinationLink : IDisposable
    {
        /// <summary>
        /// Log of the link, for problems that are hard to see (relay not reachable, blocked name, ...):
        /// %AppData%\AuroraPAR\coord-AuroraPAR.log or coord-AuroraCoord.log, about 200 KB at most (then kept as .old).
        /// </summary>
        public static string LogPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AuroraPAR",
            $"coord-{System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "AuroraPAR"}.log");
        private static readonly object logLock = new();

        public static void Log(string text)
        {
            try
            {
                lock (logLock)
                {
                    string path = LogPath;
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    if (File.Exists(path) && new FileInfo(path).Length > 200_000) File.Move(path, path + ".old", overwrite: true);
                    File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {text}{Environment.NewLine}");
                }
            }
            catch (Exception)
            {
                // The log must never disturb the panel.
            }
        }

        /// <summary>The exception and its inner ones in one line (e.g. "No such host is known").</summary>
        private static string Describe(Exception error)
        {
            List<string> parts = [];
            for (Exception? e = error; e != null && parts.Count < 4; e = e.InnerException) parts.Add($"{e.GetType().Name}: {e.Message}");
            return string.Join(" <- ", parts);
        }

        /// <summary>
        /// Public relays and ways to reach them, tried in this order (always encrypted). Both panels use the first one
        /// that works, so normally the same relay. Some networks block the MQTT port 8883: then the same relay is
        /// reached through a secure WebSocket (as the phone panel), before trying the other relay.
        /// </summary>
        private static readonly (string Host, int Port, string? WebSocket)[] Brokers =
        [
            ("broker.emqx.io", 8883, null),
            ("broker.emqx.io", 8084, "wss://broker.emqx.io:8084/mqtt"),
            ("broker.hivemq.com", 8883, null),
            ("broker.hivemq.com", 8884, "wss://broker.hivemq.com:8884/mqtt")
        ];
        private int brokerIndex;

        /// <summary>Relay in use (for the status tooltip), or null when not connected.</summary>
        public string? RelayName => Connected ? $"{Brokers[brokerIndex].Host}:{Brokers[brokerIndex].Port}" : null;
        private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

        private readonly string clientId = "aurorapar-" + Guid.NewGuid().ToString("N")[..12];
        private readonly MqttFactory factory = new();
        private IMqttClient? client;
        private string? channel;
        private CoordinationRole role;
        private readonly CancellationTokenSource stop = new();
        private readonly SemaphoreSlim gate = new(1, 1);
        /// <summary>Leaving the channel: our own "offline" must not be answered.</summary>
        private volatile bool leaving;

        /// <summary>New state received from the other panel (raised on a background thread).</summary>
        public event Action<CoordinationState>? StateReceived;
        /// <summary>The other side went online or offline (raised on a background thread).</summary>
        public event Action<CoordinationRole, bool>? PresenceChanged;

        public bool Connected => client?.IsConnected == true;
        public string? Airport { get; private set; }
        /// <summary>Panel code of the channel joined (null: the open channel of the airport).</summary>
        public string? Code { get; private set; }

        /// <summary>
        /// Channel of an airport: a name that is not trivial to guess, the same for both sides. With a panel code the
        /// code is part of the hashed text, so the channel cannot be computed without it.
        /// </summary>
        private static string ChannelOf(string airport, string? code)
        {
            string text = "AuroraPAR coordination panel " + airport + (code == null ? "" : " " + code);
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
            return $"aurorapar/coord/v1/{airport}-{Convert.ToHexString(hash)[..16].ToLowerInvariant()}";
        }

        /// <summary>
        /// Joins the channel of an airport with a role (or leaves when <paramref name="airport"/> is null), with the
        /// panel code if any. Reconnects by itself while joined.
        /// </summary>
        public async Task Join(string? airport, CoordinationRole newRole, string? code = null)
        {
            await gate.WaitAsync();
            try
            {
                if (airport == Airport && newRole == role && code == Code && (airport == null || Connected)) return;
                await DisconnectInternal();
                Airport = airport;
                Code = code;
                role = newRole;
                channel = airport == null ? null : ChannelOf(airport, code);
                brokerIndex = 0;
                Log(airport == null ? "Leave: no airport / role" : $"Join {airport} as {newRole}{(code == null ? "" : " with a panel code")}");
                PresenceChanged?.Invoke(CoordinationRole.Radar, false);
                PresenceChanged?.Invoke(CoordinationRole.Tower, false);
                if (channel != null) await ConnectInternal();
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>Called periodically: reconnects after a network problem.</summary>
        public async Task KeepAlive()
        {
            if (channel == null || Connected || stop.IsCancellationRequested) return;
            await gate.WaitAsync();
            try
            {
                if (channel != null && !Connected) await ConnectInternal();
            }
            finally
            {
                gate.Release();
            }
        }

        private string PresenceTopic(CoordinationRole r) => $"{channel}/presence/{r}";
        private string StateTopic => $"{channel}/state";

        private async Task ConnectInternal()
        {
            if (channel == null) return;
            try
            {
                client?.Dispose();
                client = factory.CreateMqttClient();
                client.ApplicationMessageReceivedAsync += OnMessage;
                (string host, int port, string? webSocket) = Brokers[brokerIndex];
                Log($"Connecting to {host}:{port} ({(webSocket != null ? "secure WebSocket" : "MQTT over TLS")})...");
                MqttClientOptionsBuilder builder = new MqttClientOptionsBuilder();
                builder = webSocket != null
                    ? builder.WithWebSocketServer(o => o.WithUri(webSocket))
                    : builder.WithTcpServer(host, port);
                builder = builder
                    .WithTlsOptions(o => o.UseTls())
                    .WithClientId(clientId)
                    .WithCleanSession()
                    .WithKeepAlivePeriod(TimeSpan.FromSeconds(20));
                if (role != CoordinationRole.Monitor)
                {
                    // If the connection is lost, the relay tells the others that this side went offline.
                    builder = builder
                        .WithWillTopic(PresenceTopic(role))
                        .WithWillPayload("offline")
                        .WithWillRetain()
                        .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce);
                }
                MqttClientOptions options = builder.Build();
                using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                await client.ConnectAsync(options, timeout.Token);
                // Everybody follows the lights and both sides' presence (the monitor shows both).
                MqttClientSubscribeOptions subscribe = factory.CreateSubscribeOptionsBuilder()
                    .WithTopicFilter(f => f.WithTopic(StateTopic).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
                    .WithTopicFilter(f => f.WithTopic(PresenceTopic(CoordinationRole.Radar)).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
                    .WithTopicFilter(f => f.WithTopic(PresenceTopic(CoordinationRole.Tower)).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
                    .Build();
                await client.SubscribeAsync(subscribe, timeout.Token);
                if (role != CoordinationRole.Monitor)
                {
                    await Publish(PresenceTopic(role), "online");
                }
                Log($"Connected to {host}:{port}, channel of {Airport} as {role}");
                client.DisconnectedAsync += e =>
                {
                    if (!leaving) Log($"Disconnected from {host}:{port}: {e.Reason}{(e.Exception != null ? " - " + Describe(e.Exception) : "")}");
                    return Task.CompletedTask;
                };
            }
            catch (Exception error)
            {
                Log($"Failed: {Describe(error)}");
                // No network or relay not reachable: KeepAlive tries again, with the next relay.
                brokerIndex = (brokerIndex + 1) % Brokers.Length;
            }
        }

        private async Task DisconnectInternal()
        {
            if (client == null) return;
            leaving = true;
            try
            {
                if (client.IsConnected)
                {
                    if (role != CoordinationRole.Monitor) await Publish(PresenceTopic(role), "offline");
                    await client.DisconnectAsync();
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                leaving = false;
            }
            client.ApplicationMessageReceivedAsync -= OnMessage;
            client.Dispose();
            client = null;
        }

        private async Task Publish(string topic, string payload)
        {
            if (client?.IsConnected != true) return;
            MqttApplicationMessage message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithRetainFlag()
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();
            await client.PublishAsync(message, stop.Token);
        }

        /// <summary>Sends the new state of the lights to the other panel.</summary>
        public async Task Send(CoordinationState state)
        {
            // The monitor only watches.
            if (channel == null || role == CoordinationRole.Monitor) return;
            state.Sender = clientId;
            state.Time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            try
            {
                await Publish(StateTopic, JsonSerializer.Serialize(state, Json));
            }
            catch (Exception)
            {
            }
        }

        private Task OnMessage(MqttApplicationMessageReceivedEventArgs e)
        {
            try
            {
                string topic = e.ApplicationMessage.Topic;
                string payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
                if (topic == StateTopic)
                {
                    CoordinationState? state = payload.Length == 0 ? new CoordinationState() : JsonSerializer.Deserialize<CoordinationState>(payload, Json);
                    if (state != null && state.IsValid && state.Sender != clientId)
                    {
                        StateReceived?.Invoke(state);
                    }
                }
                else if (topic == PresenceTopic(CoordinationRole.Radar) || topic == PresenceTopic(CoordinationRole.Tower))
                {
                    CoordinationRole side = topic == PresenceTopic(CoordinationRole.Radar) ? CoordinationRole.Radar : CoordinationRole.Tower;
                    bool online = payload == "online";
                    // Another panel of our side (e.g. the phone panel) went offline: we are still here.
                    if (!online && side == role && !leaving && !stop.IsCancellationRequested && Connected)
                    {
                        online = true;
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await Publish(PresenceTopic(side), "online");
                            }
                            catch (Exception)
                            {
                            }
                        });
                    }
                    Log($"{side} panel {(online ? "linked" : "gone")}");
                    PresenceChanged?.Invoke(side, online);
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
            try
            {
                // On a pool thread: waiting on the UI thread for a task that resumes there would hang.
                Task.Run(DisconnectInternal).Wait(TimeSpan.FromSeconds(2));
            }
            catch (Exception)
            {
            }
        }
    }
}
