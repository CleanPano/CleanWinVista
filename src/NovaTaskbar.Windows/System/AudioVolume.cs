using System.Runtime.InteropServices;
using NovaTaskbar.Windows.Interop;

namespace NovaTaskbar.Windows.SystemInfo;

/// <summary>
/// Volume do dispositivo de saída padrão via Core Audio (IMMDeviceEnumerator → IAudioEndpointVolume).
/// O dispositivo padrão é consultado a cada leitura, então trocar de fone/caixa é refletido sozinho.
/// </summary>
public sealed class AudioVolume
{
    private static Guid s_eventContext = Guid.NewGuid();

    public readonly record struct State(bool Available, float Level, bool Muted, string DeviceName);

    /// <summary>Dispositivo de saída ativo (alto-falantes, fone, HDMI…).</summary>
    public readonly record struct Device(string Id, string Name, bool IsDefault);

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice? device);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice? device);
    }

    /// <summary>
    /// Interface do Windows (não documentada, estável desde o Windows 7; usada por EarTrumpet/SoundSwitch) para trocar
    /// o dispositivo padrão. Só o método SetDefaultEndpoint é usado; os anteriores ficam como espaço da vtable.
    /// </summary>
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(); [PreserveSig] int GetDeviceFormat(); [PreserveSig] int ResetDeviceFormat();
        [PreserveSig] int SetDeviceFormat(); [PreserveSig] int GetProcessingPeriod(); [PreserveSig] int SetProcessingPeriod();
        [PreserveSig] int GetShareMode(); [PreserveSig] int SetShareMode(); [PreserveSig] int GetPropertyValue(); [PreserveSig] int SetPropertyValue();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);
    }

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    private class PolicyConfigClient { }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object? iface);
        [PreserveSig] int OpenPropertyStore(int access, out NativeMethods.IPropertyStore? properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    private const int eRender = 0, eCapture = 1, eMultimedia = 1, eCommunications = 2, CLSCTX_ALL = 23;
    private static readonly NativeMethods.PROPERTYKEY PKEY_Device_FriendlyName =
        new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);

    public State Read()
    {
        return With<State?>((ep, name) =>
        {
            ep.GetMasterVolumeLevelScalar(out float level);
            ep.GetMute(out bool muted);
            return new State(true, level, muted, name);
        }) ?? new State(false, 0, false, "");
    }

    public bool SetLevel(float level)
    {
        level = Math.Clamp(level, 0f, 1f);
        return With((ep, _) => ep.SetMasterVolumeLevelScalar(level, ref s_eventContext) >= 0);
    }

    public bool SetMuted(bool muted) => With((ep, _) => ep.SetMute(muted, ref s_eventContext) >= 0);

    /// <summary>Dispositivos de saída ativos, com o padrão marcado.</summary>
    public IReadOnlyList<Device> Devices()
    {
        var list = new List<Device>();
        object? enumObj = null;
        IMMDeviceCollection? col = null;
        try
        {
            enumObj = new MMDeviceEnumerator();
            var en = (IMMDeviceEnumerator)enumObj;
            string defaultId = "";
            if (en.GetDefaultAudioEndpoint(eRender, eMultimedia, out var def) >= 0 && def != null)
            {
                def.GetId(out defaultId);
                Marshal.ReleaseComObject(def);
            }
            if (en.EnumAudioEndpoints(eRender, 1 /* DEVICE_STATE_ACTIVE */, out var colPtr) < 0 || colPtr == IntPtr.Zero) return list;
            col = (IMMDeviceCollection)Marshal.GetObjectForIUnknown(colPtr);
            Marshal.Release(colPtr);
            col.GetCount(out uint n);
            for (uint i = 0; i < n; i++)
            {
                if (col.Item(i, out var d) < 0 || d is null) continue;
                try
                {
                    d.GetId(out string id);
                    string name = id;
                    if (d.OpenPropertyStore(0, out var store) >= 0 && store != null)
                    {
                        name = NativeMethods.ReadString(store, PKEY_Device_FriendlyName) ?? id;
                        Marshal.ReleaseComObject(store);
                    }
                    list.Add(new Device(id, name, id == defaultId));
                }
                finally { Marshal.ReleaseComObject(d); }
            }
        }
        catch (COMException) { }
        finally
        {
            if (col != null) Marshal.ReleaseComObject(col);
            if (enumObj != null) Marshal.ReleaseComObject(enumObj);
        }
        return list.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Torna <paramref name="deviceId"/> o dispositivo padrão (console, multimídia e comunicação).</summary>
    public bool SetDefault(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return false;
        object? obj = null;
        try
        {
            obj = new PolicyConfigClient();
            var pc = (IPolicyConfig)obj;
            bool ok = true;
            for (int role = 0; role <= 2; role++) ok &= pc.SetDefaultEndpoint(deviceId, role) >= 0;
            return ok;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException) { return false; }
        finally { if (obj != null) Marshal.ReleaseComObject(obj); }
    }

    /// <summary>Microfone padrão: silenciado? (null = sem microfone).</summary>
    public bool? MicMuted() => With<bool?>((ep, _) => { ep.GetMute(out bool m); return m; }, eCapture);

    public bool SetMicMuted(bool muted) => With((ep, _) => ep.SetMute(muted, ref s_eventContext) >= 0, eCapture);

    private static T? With<T>(Func<IAudioEndpointVolume, string, T> action, int flow = eRender)
    {
        object? enumObj = null, epObj = null;
        IMMDevice? device = null;
        try
        {
            enumObj = new MMDeviceEnumerator();
            var en = (IMMDeviceEnumerator)enumObj;
            if (en.GetDefaultAudioEndpoint(flow, flow == eCapture ? eCommunications : eMultimedia, out device) < 0 || device is null) return default;
            Guid iid = typeof(IAudioEndpointVolume).GUID;
            if (device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out epObj) < 0 || epObj is null) return default;

            string name = "";
            if (device.OpenPropertyStore(0 /* STGM_READ */, out var store) >= 0 && store != null)
            {
                name = NativeMethods.ReadString(store, PKEY_Device_FriendlyName) ?? "";
                Marshal.ReleaseComObject(store);
            }
            return action((IAudioEndpointVolume)epObj, name);
        }
        catch (COMException) { return default; } // sem dispositivo de saída, serviço de áudio parado etc.
        finally
        {
            if (epObj != null) Marshal.ReleaseComObject(epObj);
            if (device != null) Marshal.ReleaseComObject(device);
            if (enumObj != null) Marshal.ReleaseComObject(enumObj);
        }
    }
}
