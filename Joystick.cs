using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AuroraPAR
{
    /// <summary>What the stick (X/Y axes) of the joystick moves.</summary>
    internal enum JoystickRole
    {
        /// <summary>The selected test aircraft while the test traffic window is open with one selected, else the antenna.</summary>
        Automatic,
        AntennaTilt,
        TestAircraft
    }

    /// <summary>Commands that can be given to the joystick buttons and to the directions of the hat.</summary>
    internal enum JoystickCommand
    {
        FinalCourse,
        NormalRate,
        ReduceRate,
        IncreaseRate,
        OnPath,
        NextAircraft,
        Pause,
        TurnRate,
        TiltUp,
        TiltDown,
        TiltLeft,
        TiltRight,
        TiltNeutral,
        SwitchRole
    }

    /// <summary>Joystick settings (the same for all profiles).</summary>
    internal sealed class JoystickSettings
    {
        public bool Enabled { get; set; } = true;
        /// <summary>Name of the device to use; null: the first one found.</summary>
        public string? Device { get; set; }
        public JoystickRole Role { get; set; } = JoystickRole.Automatic;
        /// <summary>Part of the travel around the centre that does nothing (0 to 0.5).</summary>
        public double DeadZone { get; set; } = 0.10;
        /// <summary>Test aircraft: stick pulled back = climb / less descent, as in an aircraft.</summary>
        public bool PullToClimb { get; set; } = true;
        /// <summary>Input of each command (<see cref="Joystick"/> input numbers: 1–32 buttons, 33–36 hat).</summary>
        public Dictionary<string, int> Buttons { get; set; } = DefaultButtons();

        public static Dictionary<string, int> DefaultButtons() => new()
        {
            [nameof(JoystickCommand.FinalCourse)] = 1,
            [nameof(JoystickCommand.NormalRate)] = 2,
            [nameof(JoystickCommand.NextAircraft)] = 3,
            [nameof(JoystickCommand.SwitchRole)] = 4,
            [nameof(JoystickCommand.TiltUp)] = Joystick.HatUp,
            [nameof(JoystickCommand.TiltRight)] = Joystick.HatRight,
            [nameof(JoystickCommand.TiltDown)] = Joystick.HatDown,
            [nameof(JoystickCommand.TiltLeft)] = Joystick.HatLeft
        };

        public int InputOf(JoystickCommand command) => Buttons.TryGetValue(command.ToString(), out int input) ? input : 0;

        /// <summary>Gives the input to the command (0: none); an input belongs to one command only.</summary>
        public void Assign(JoystickCommand command, int input)
        {
            if (input > 0)
            {
                foreach (string key in Buttons.Where(b => b.Value == input).Select(b => b.Key).ToList()) Buttons.Remove(key);
                Buttons[command.ToString()] = input;
            }
            else
            {
                Buttons.Remove(command.ToString());
            }
        }

        public void Normalize()
        {
            Buttons ??= DefaultButtons();
            foreach (string key in Buttons.Keys.ToList())
            {
                if (!Enum.TryParse(key, out JoystickCommand _) || Buttons[key] < 1 || Buttons[key] > Joystick.InputCount) Buttons.Remove(key);
            }
            DeadZone = double.IsFinite(DeadZone) ? Math.Clamp(DeadZone, 0, 0.5) : 0.10;
            if (!Enum.IsDefined(Role)) Role = JoystickRole.Automatic;
            if (string.IsNullOrWhiteSpace(Device)) Device = null;
        }
    }

    /// <summary>
    /// Joysticks and gamepads through the Windows multimedia joystick functions (winmm): any USB game controller
    /// works without a driver, and it is read also when the window is not in front.
    /// Inputs are numbered 1–32 (buttons) and 33–36 (hat up, right, down, left).
    /// </summary>
    internal static class Joystick
    {
        public const int ButtonCount = 32;
        public const int HatUp = 33;
        public const int HatRight = 34;
        public const int HatDown = 35;
        public const int HatLeft = 36;
        public const int InputCount = 36;
        private const double ScanSeconds = 3;

        public sealed class Device
        {
            public int Id;
            public string Name = "";
            public int Buttons;
            public bool HasPov;
            /// <summary>Axes present (X, Y, Z, R, U, V) and their raw limits.</summary>
            public bool[] HasAxis = new bool[6];
            public uint[] Min = new uint[6];
            public uint[] Max = new uint[6];
        }

        public sealed class State
        {
            /// <summary>Axes −1..1: X right, Y forward (away from the user), then Z, R, U, V.</summary>
            public double[] Axes = new double[6];
            public uint Buttons;
            /// <summary>Hat direction in hundredths of degree (0 up, 9000 right), −1 centred.</summary>
            public int Pov = -1;

            public double X => Axes[0];
            public double Y => Axes[1];

            public bool IsPressed(int input)
            {
                if (input >= 1 && input <= ButtonCount) return (Buttons & (1u << (input - 1))) != 0;
                if (Pov < 0) return false;
                // Diagonals press both directions.
                double angle = Pov / 100.0;
                return input switch
                {
                    HatUp => angle >= 292.5 || angle <= 67.5,
                    HatRight => angle >= 22.5 && angle <= 157.5,
                    HatDown => angle >= 112.5 && angle <= 247.5,
                    HatLeft => angle >= 202.5 && angle <= 337.5,
                    _ => false
                };
            }

            public IEnumerable<int> Pressed() => Enumerable.Range(1, InputCount).Where(IsPressed);
        }

        public static string InputName(int input) => input switch
        {
            <= 0 => "—",
            HatUp => "Hat ↑",
            HatRight => "Hat →",
            HatDown => "Hat ↓",
            HatLeft => "Hat ←",
            _ => $"Button {input}"
        };

        private static volatile List<Device> devices = [];
        private static DateTime lastScan = DateTime.MinValue;
        private static int scanning;

        /// <summary>Devices found by the last scan; a new scan runs in the background every few seconds.</summary>
        public static IReadOnlyList<Device> Devices
        {
            get
            {
                if ((DateTime.UtcNow - lastScan).TotalSeconds > ScanSeconds) Rescan();
                return devices;
            }
        }

        /// <summary>Looks for the devices again, in the background (the functions can be slow with nothing connected).</summary>
        public static void Rescan(bool force = false)
        {
            bool configChanged = force || devices.Count == 0;
            if (Interlocked.Exchange(ref scanning, 1) == 1) return;
            lastScan = DateTime.UtcNow;
            Task.Run(() =>
            {
                try
                {
                    devices = Scan(configChanged);
                }
                catch
                {
                    devices = [];
                }
                finally
                {
                    lastScan = DateTime.UtcNow;
                    Interlocked.Exchange(ref scanning, 0);
                }
            });
        }

        /// <summary>The device of this name, else the first one; null with none connected.</summary>
        public static Device? Find(string? name)
        {
            IReadOnlyList<Device> list = Devices;
            return list.FirstOrDefault(d => d.Name == name) ?? list.FirstOrDefault();
        }

        /// <summary>Reads the device now; null if it is no longer there (a new scan is started).</summary>
        public static State? Read(Device device)
        {
            JOYINFOEX info = new() { dwSize = Marshal.SizeOf<JOYINFOEX>(), dwFlags = JOY_RETURNALL | JOY_RETURNPOVCTS };
            try
            {
                if (joyGetPosEx(device.Id, ref info) != 0)
                {
                    Rescan();
                    return null;
                }
            }
            catch
            {
                return null;
            }
            State state = new() { Buttons = (uint)info.dwButtons };
            uint[] raw = [(uint)info.dwXpos, (uint)info.dwYpos, (uint)info.dwZpos, (uint)info.dwRpos, (uint)info.dwUpos, (uint)info.dwVpos];
            for (int i = 0; i < 6; i++)
            {
                if (!device.HasAxis[i]) continue;
                double min = device.Min[i];
                double max = device.Max[i] > device.Min[i] ? device.Max[i] : 65535;
                state.Axes[i] = Math.Clamp((raw[i] - min) / (max - min) * 2 - 1, -1, 1);
            }
            // Y of the joystick functions grows towards the user.
            state.Axes[1] = -state.Axes[1];
            int pov = info.dwPOV & 0xFFFF;
            state.Pov = device.HasPov && pov != 0xFFFF && pov < 36000 ? pov : -1;
            return state;
        }

        private static List<Device> Scan(bool configChanged)
        {
            // Devices plugged in after the start are seen only after this (not at every scan: it may disturb a device in use).
            if (configChanged) joyConfigChanged(0);
            List<Device> found = [];
            int count = (int)Math.Min(16, joyGetNumDevs());
            for (int id = 0; id < count; id++)
            {
                JOYINFOEX info = new() { dwSize = Marshal.SizeOf<JOYINFOEX>(), dwFlags = JOY_RETURNALL };
                if (joyGetPosEx(id, ref info) != 0) continue;
                JOYCAPS caps = new();
                if (joyGetDevCaps((IntPtr)id, ref caps, Marshal.SizeOf<JOYCAPS>()) != 0) continue;
                Device device = new()
                {
                    Id = id,
                    Buttons = (int)Math.Min(ButtonCount, caps.wNumButtons),
                    HasPov = (caps.wCaps & JOYCAPS_HASPOV) != 0
                };
                device.HasAxis[0] = device.HasAxis[1] = true;
                device.HasAxis[2] = (caps.wCaps & JOYCAPS_HASZ) != 0;
                device.HasAxis[3] = (caps.wCaps & JOYCAPS_HASR) != 0;
                device.HasAxis[4] = (caps.wCaps & JOYCAPS_HASU) != 0;
                device.HasAxis[5] = (caps.wCaps & JOYCAPS_HASV) != 0;
                device.Min = [caps.wXmin, caps.wYmin, caps.wZmin, caps.wRmin, caps.wUmin, caps.wVmin];
                device.Max = [caps.wXmax, caps.wYmax, caps.wZmax, caps.wRmax, caps.wUmax, caps.wVmax];
                string name = OemName(caps.wMid, caps.wPid) ?? caps.szPname?.Trim() ?? "";
                if (name.Length == 0) name = "Joystick";
                string unique = name;
                for (int n = 2; found.Any(d => d.Name == unique); n++) unique = $"{name} ({n})";
                device.Name = unique;
                found.Add(device);
            }
            return found;
        }

        /// <summary>The product name Windows keeps for the device (the joystick functions only give a generic one).</summary>
        private static string? OemName(ushort vendor, ushort product)
        {
            string path = $@"System\CurrentControlSet\Control\MediaProperties\PrivateProperties\Joystick\OEM\VID_{vendor:X4}&PID_{product:X4}";
            foreach (RegistryKey root in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                try
                {
                    using RegistryKey? key = root.OpenSubKey(path);
                    if (key?.GetValue("OEMName") is string name && name.Trim().Length > 0) return name.Trim();
                }
                catch
                {
                    // Not readable: the generic name.
                }
            }
            return null;
        }

        private const int JOY_RETURNALL = 0xFF;
        private const int JOY_RETURNPOVCTS = 0x200;
        private const int JOYCAPS_HASZ = 0x1;
        private const int JOYCAPS_HASR = 0x2;
        private const int JOYCAPS_HASU = 0x4;
        private const int JOYCAPS_HASV = 0x8;
        private const int JOYCAPS_HASPOV = 0x10;

        [StructLayout(LayoutKind.Sequential)]
        private struct JOYINFOEX
        {
            public int dwSize;
            public int dwFlags;
            public int dwXpos;
            public int dwYpos;
            public int dwZpos;
            public int dwRpos;
            public int dwUpos;
            public int dwVpos;
            public int dwButtons;
            public int dwButtonNumber;
            public int dwPOV;
            public int dwReserved1;
            public int dwReserved2;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct JOYCAPS
        {
            public ushort wMid;
            public ushort wPid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szPname;
            public uint wXmin;
            public uint wXmax;
            public uint wYmin;
            public uint wYmax;
            public uint wZmin;
            public uint wZmax;
            public uint wNumButtons;
            public uint wPeriodMin;
            public uint wPeriodMax;
            public uint wRmin;
            public uint wRmax;
            public uint wUmin;
            public uint wUmax;
            public uint wVmin;
            public uint wVmax;
            public uint wCaps;
            public uint wMaxAxes;
            public uint wNumAxes;
            public uint wMaxButtons;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szRegKey;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szOEMVxD;
        }

        [DllImport("winmm.dll")]
        private static extern uint joyGetNumDevs();

        [DllImport("winmm.dll")]
        private static extern int joyConfigChanged(int dwFlags);

        [DllImport("winmm.dll")]
        private static extern int joyGetPosEx(int uJoyID, ref JOYINFOEX pji);

        [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "joyGetDevCapsW")]
        private static extern int joyGetDevCaps(IntPtr uJoyID, ref JOYCAPS pjc, int cbjc);
    }

    /// <summary>
    /// A window whose selected aircraft the joystick can fly: the test traffic window or the Instructor window.
    /// </summary>
    internal interface IJoystickTarget
    {
        /// <summary>An aircraft is selected (the joystick moves it in the automatic role).</summary>
        bool HasSelection { get; }
        /// <summary>The stick moved (x right, y up, −1..1); (0, 0) = released.</summary>
        void JoystickStick(double x, double y);
        /// <summary>A command from a joystick button.</summary>
        void RunJoystickCommand(JoystickCommand command);
    }

    /// <summary>
    /// Reads the joystick about 25 times a second and gives its movements to the radar: the stick moves the
    /// selected test aircraft or tilts the antenna (<see cref="JoystickRole"/>), the buttons and the hat give the
    /// commands assigned in <see cref="JoystickSettings"/>.
    /// </summary>
    internal sealed class JoystickController
    {
        /// <summary>Deflection that tilts the antenna by one step, and the repeat while held (s).</summary>
        private const double TiltThreshold = 0.5;
        private const double RepeatDelay = 0.5;
        private const double RepeatInterval = 0.25;
        /// <summary>Beyond this the stick counts as fully deflected (the travel of cheap sticks ends a little before).</summary>
        private const double FullDeflection = 0.92;

        private readonly Func<JoystickSettings> settings;
        private readonly Func<IJoystickTarget?> aircraftWindow;
        private readonly Action<int, int> tilt;
        private readonly Action neutral;
        private readonly Action<string> banner;
        private readonly Dictionary<int, DateTime> held = [];
        private readonly Dictionary<int, DateTime> nextRepeat = [];
        private JoystickRole? sessionRole;
        /// <summary>Role in the settings when the switch button was pressed: changed there, the switch is forgotten.</summary>
        private JoystickRole sessionBase;
        private bool resync = true;
        private double sentX;
        private double sentY;
        private IJoystickTarget? sentTo;

        /// <summary>No commands while the joystick window waits for a button to assign.</summary>
        public static bool Suspended { get; set; }

        public JoystickController(Func<JoystickSettings> settings, Func<IJoystickTarget?> aircraftWindow,
            Action<int, int> tilt, Action neutral, Action<string> banner)
        {
            this.settings = settings;
            this.aircraftWindow = aircraftWindow;
            this.tilt = tilt;
            this.neutral = neutral;
            this.banner = banner;
        }

        /// <summary>Applies the dead zone: 0 inside, then from 0 to 1 up to <see cref="FullDeflection"/>.</summary>
        public static double Shape(double value, double deadZone)
        {
            double size = Math.Abs(value);
            if (size <= deadZone) return 0;
            return Math.Sign(value) * Math.Min(1, (size - deadZone) / Math.Max(0.05, FullDeflection - deadZone));
        }

        /// <summary>What the stick moves now.</summary>
        public JoystickRole CurrentRole
        {
            get
            {
                if (sessionRole != null && settings().Role != sessionBase) sessionRole = null;
                JoystickRole role = sessionRole ?? settings().Role;
                if (role != JoystickRole.Automatic) return role;
                return aircraftWindow()?.HasSelection == true ? JoystickRole.TestAircraft : JoystickRole.AntennaTilt;
            }
        }

        public void Tick()
        {
            JoystickSettings options = settings();
            Joystick.State? state = null;
            if (options.Enabled && !Suspended)
            {
                Joystick.Device? device = Joystick.Find(options.Device);
                if (device != null) state = Joystick.Read(device);
            }
            DateTime now = DateTime.UtcNow;
            if (state == null)
            {
                SendStick(0, 0);
                resync = true;
                return;
            }
            if (resync)
            {
                // After a pause (button being assigned, device away): what is already held does nothing until released.
                resync = false;
                held.Clear();
                nextRepeat.Clear();
                foreach (int input in state.Pressed())
                {
                    held[input] = now;
                    nextRepeat[input] = DateTime.MaxValue;
                }
            }
            double x = Shape(state.X, options.DeadZone);
            double y = Shape(state.Y, options.DeadZone);
            if (CurrentRole == JoystickRole.TestAircraft)
            {
                ReleaseHeld(-1);
                ReleaseHeld(-2);
                // Pad: up = climb (less descent).
                SendStick(x, options.PullToClimb ? -y : y);
            }
            else
            {
                SendStick(0, 0);
                // Antenna: forward = up; one step, then repeated while held.
                Repeat(-1, Math.Abs(y) >= TiltThreshold, now, () => tilt(Math.Sign(y), 0));
                Repeat(-2, Math.Abs(x) >= TiltThreshold, now, () => tilt(0, Math.Sign(x)));
            }
            foreach (JoystickCommand command in Enum.GetValues<JoystickCommand>())
            {
                int input = options.InputOf(command);
                if (input <= 0) continue;
                bool pressed = state.IsPressed(input);
                bool repeats = command is JoystickCommand.TiltUp or JoystickCommand.TiltDown or JoystickCommand.TiltLeft or JoystickCommand.TiltRight;
                if (repeats)
                {
                    Repeat(input, pressed, now, () => Execute(command));
                }
                else if (pressed && !held.ContainsKey(input))
                {
                    held[input] = now;
                    Execute(command);
                }
                else if (!pressed)
                {
                    held.Remove(input);
                }
            }
        }

        private void ReleaseHeld(int key)
        {
            held.Remove(key);
            nextRepeat.Remove(key);
        }

        /// <summary>Runs the action when pressed, then again after a delay and at intervals while held.</summary>
        private void Repeat(int key, bool pressed, DateTime now, Action action)
        {
            if (!pressed)
            {
                ReleaseHeld(key);
                return;
            }
            if (!held.ContainsKey(key))
            {
                held[key] = now;
                nextRepeat[key] = now.AddSeconds(RepeatDelay);
                action();
            }
            else if (now >= nextRepeat[key])
            {
                nextRepeat[key] = now.AddSeconds(RepeatInterval);
                action();
            }
        }

        /// <summary>Gives the stick to the test traffic window, only when it changes (and centred once when let go).</summary>
        private void SendStick(double x, double y)
        {
            IJoystickTarget? window = aircraftWindow();
            if (sentTo != null && sentTo != window)
            {
                sentX = sentY = 0;
                sentTo = null;
            }
            if (window == null) return;
            if (Math.Abs(x - sentX) < 0.01 && Math.Abs(y - sentY) < 0.01 && (x != 0 || y != 0 || (sentX == 0 && sentY == 0))) return;
            sentX = x;
            sentY = y;
            sentTo = window;
            window.JoystickStick(x, y);
        }

        private void Execute(JoystickCommand command)
        {
            switch (command)
            {
                case JoystickCommand.TiltUp: tilt(1, 0); return;
                case JoystickCommand.TiltDown: tilt(-1, 0); return;
                case JoystickCommand.TiltLeft: tilt(0, -1); return;
                case JoystickCommand.TiltRight: tilt(0, 1); return;
                case JoystickCommand.TiltNeutral: neutral(); return;
                case JoystickCommand.SwitchRole:
                    sessionRole = CurrentRole == JoystickRole.TestAircraft ? JoystickRole.AntennaTilt : JoystickRole.TestAircraft;
                    sessionBase = settings().Role;
                    banner(sessionRole == JoystickRole.TestAircraft
                        ? (aircraftWindow()?.HasSelection == true ? "JOYSTICK: AIRCRAFT" : "JOYSTICK: AIRCRAFT (NONE SELECTED)")
                        : "JOYSTICK: ANTENNA TILT");
                    return;
            }
            IJoystickTarget? window = aircraftWindow();
            if (window == null)
            {
                banner("NO AIRCRAFT WINDOW (KEY T OR I)");
                return;
            }
            window.RunJoystickCommand(command);
        }
    }
}
