using System.Runtime.InteropServices;

namespace wpfhw;

public static class NativeDownload
{
    [DllImport("download_engine.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int DownloadFile(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string url,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string savePath);
}
