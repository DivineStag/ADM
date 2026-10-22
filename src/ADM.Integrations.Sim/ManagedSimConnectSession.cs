using System.Runtime.InteropServices;
#if HAS_SIMCONNECT_SDK
using Microsoft.FlightSimulator.SimConnect;
#endif

namespace ADM.Integrations.Sim;

/// <summary>Official locally installed MSFS 2024 SDK. No simulator writes or client events are exposed.</summary>
public sealed class ManagedSimConnectSession : ISimConnectSession
{
#if HAS_SIMCONNECT_SDK
    private enum Definition { Aircraft }
    private enum Request { Aircraft }
    private readonly EventWaitHandle _ready = new(false, EventResetMode.AutoReset);
    private SimConnect? _client;
    private int _owner;
    public static bool SdkAvailable => true;
    public event Action<SimConnectData>? Data;
    public void Open()
    {
        _owner = Environment.CurrentManagedThreadId;
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess) throw new NotSupportedException("SimConnect requires Windows x64.");
        _client = new SimConnect("ADM integration proof", IntPtr.Zero, 0, _ready, 0);
        _client.OnRecvQuit += (_, _) => throw new IOException("Simulator exited.");
        _client.OnRecvException += (_, exception) => throw new IOException($"SimConnect exception {exception.dwException}, send {exception.dwSendID}, index {exception.dwIndex}.");
        _client.OnRecvOpen += (_, _) => Subscribe();
        _client.OnRecvSimobjectData += (_, message) =>
        {
            if (message.dwRequestID != (uint)Request.Aircraft || message.dwData.Length != 1 || message.dwData[0] is not AircraftData data) return;
            Data?.Invoke(SimConnectData.FlightTracking(data.OnGround, data.GroundSpeed, data.Latitude, data.Longitude, data.Title));
        };
    }
    private void Subscribe()
    {
        CheckThread();
        string[] names = ["SIM ON GROUND", "GROUND VELOCITY", "PLANE LATITUDE", "PLANE LONGITUDE"];
        string[] units = ["Bool", "knots", "degrees", "degrees"];
        for (int index = 0; index < names.Length; index++)
            _client!.AddToDataDefinition(Definition.Aircraft, names[index], units[index], SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
        _client!.AddToDataDefinition(Definition.Aircraft, "TITLE", null, SIMCONNECT_DATATYPE.STRING256, 0, SimConnect.SIMCONNECT_UNUSED);
        _client.RegisterDataDefineStruct<AircraftData>(Definition.Aircraft);
        _client.RequestDataOnSimObject(Request.Aircraft, Definition.Aircraft, SimConnect.SIMCONNECT_OBJECT_ID_USER,
            SIMCONNECT_PERIOD.SECOND, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
    }
    public void Dispatch(TimeSpan maximumWait)
    {
        CheckThread();
        if (_ready.WaitOne(maximumWait)) _client!.ReceiveMessage();
    }
    public void Dispose() { if (_owner != 0) CheckThread(); _client?.Dispose(); _ready.Dispose(); }
    private void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != _owner) throw new InvalidOperationException("SimConnect called from a different thread.");
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    private struct AircraftData
    {
        public double OnGround;
        public double GroundSpeed;
        public double Latitude;
        public double Longitude;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Title;
    }
#else
    public static bool SdkAvailable => false;
    public event Action<SimConnectData>? Data { add { } remove { } }
    public void Open() => throw new NotSupportedException("MSFS 2024 SDK was not present at build time. Rebuild with -p:MsfsSdkPath=\"C:\\MSFS 2024 SDK\".");
    public void Dispatch(TimeSpan maximumWait) => throw new NotSupportedException("SimConnect SDK unavailable.");
    public void Dispose() { }
#endif
}
