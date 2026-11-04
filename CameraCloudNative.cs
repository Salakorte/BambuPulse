using System.Runtime.InteropServices;
using System.Text;

static class CameraCloudNative
{
    const string Library = "CameraCloudBridge.dll";
    internal const int DispatchCallbacksMessage = 0x8000 + 0x431;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void CameraProgressCallback(int stage);

    [DllImport(Library, EntryPoint = "CameraCloud_DispatchCallbacks", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void DispatchCallbacks();

    [DllImport(Library, EntryPoint = "CameraCloud_GetCameraUrl", CallingConvention = CallingConvention.Cdecl)]
    static extern int GetCameraUrlNative(
        [MarshalAs(UnmanagedType.LPWStr)] string pluginDirectory,
        [MarshalAs(UnmanagedType.LPWStr)] string studioDirectory,
        [MarshalAs(UnmanagedType.LPWStr)] string configDirectory,
        [MarshalAs(UnmanagedType.LPWStr)] string certDirectory,
        IntPtr callbackWindow,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string country,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string userInfo,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string serial,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string devVersion,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string studioVersion,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string deviceId,
        CameraProgressCallback progress,
        [Out] byte[] output,
        int outputCapacity,
        [Out] byte[] error,
        int errorCapacity);

    [DllImport(Library, EntryPoint = "CameraCloud_StartVideo", CallingConvention = CallingConvention.Cdecl)]
    static extern int StartVideoNative(
        [MarshalAs(UnmanagedType.LPWStr)] string pluginDirectory,
        [MarshalAs(UnmanagedType.LPWStr)] string studioDirectory,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string url,
        IntPtr parent,
        int width,
        int height,
        out IntPtr session,
        [Out] byte[] error,
        int errorCapacity);

    [DllImport(Library, EntryPoint = "CameraCloud_ResizeVideo", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void ResizeVideo(IntPtr session, int width, int height);

    [DllImport(Library, EntryPoint = "CameraCloud_GetVideoSize", CallingConvention = CallingConvention.Cdecl)]
    static extern int GetVideoSizeNative(IntPtr session, out int width, out int height);

    internal static bool TryGetVideoSize(IntPtr session, out int width, out int height)
    {
        width = 0;
        height = 0;
        return session != IntPtr.Zero && GetVideoSizeNative(session, out width, out height) == 0 &&
            width > 0 && height > 0;
    }

    [DllImport(Library, EntryPoint = "CameraCloud_StopVideo", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void StopVideo(IntPtr session);

    internal static string GetCameraUrl(
        string pluginDirectory,
        string studioDirectory,
        string configDirectory,
        string certDirectory,
        IntPtr callbackWindow,
        string country,
        string userInfo,
        string serial,
        string devVersion,
        string studioVersion,
        string deviceId,
        CameraProgressCallback progress)
    {
        EnsureInstall(pluginDirectory, studioDirectory, certDirectory);
        var output = new byte[16384];
        var error = new byte[2048];
        var result = GetCameraUrlNative(pluginDirectory, studioDirectory, configDirectory,
            certDirectory, callbackWindow, country, userInfo, serial, devVersion, studioVersion, deviceId,
            progress, output, output.Length, error, error.Length);
        if (result != 0)
            throw new InvalidOperationException(ReadUtf8(error));
        return ReadUtf8(output);
    }

    internal static IntPtr StartVideo(
        string pluginDirectory,
        string studioDirectory,
        string url,
        IntPtr parent,
        int width,
        int height)
    {
        EnsureInstall(pluginDirectory, studioDirectory, null);
        var error = new byte[2048];
        var result = StartVideoNative(pluginDirectory, studioDirectory, url, parent, width, height,
            out var session, error, error.Length);
        if (result != 0)
            throw new InvalidOperationException(ReadUtf8(error));
        return session;
    }

    static void EnsureBridgeExists()
    {
        var path = Path.Combine(AppContext.BaseDirectory, Library);
        if (!File.Exists(path))
            throw new FileNotFoundException("CameraCloudBridge.dll is missing. Build or publish the app again.", path);
    }

    static void EnsureInstall(string pluginDirectory, string studioDirectory, string? certDirectory)
    {
        EnsureBridgeExists();
        if (!Directory.Exists(pluginDirectory) ||
            !File.Exists(Path.Combine(pluginDirectory, "bambu_networking.dll")) ||
            !File.Exists(Path.Combine(pluginDirectory, "BambuSource.dll")))
            throw new DirectoryNotFoundException("Bambu Studio's Cloud networking and camera plugins were not found.");
        if (!Directory.Exists(studioDirectory))
            throw new DirectoryNotFoundException("The Bambu Studio installation directory was not found.");
        if (certDirectory is not null &&
            !File.Exists(Path.Combine(certDirectory, "slicer_base64.cer")))
            throw new FileNotFoundException("Bambu Studio's Cloud certificate was not found.",
                Path.Combine(certDirectory, "slicer_base64.cer"));
    }

    static string ReadUtf8(byte[] value)
    {
        var length = Array.IndexOf(value, (byte)0);
        if (length < 0)
            length = value.Length;
        return Encoding.UTF8.GetString(value, 0, length);
    }
}
