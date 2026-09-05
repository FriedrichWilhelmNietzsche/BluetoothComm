namespace BluetoothComm;

/// <summary>
/// 全局未捕获异常记录：写入应用私有目录 crash.log，同时输出到 Android Logcat
/// （标签 BluetoothComm），便于定位闪退原因。
/// </summary>
public static class CrashLogger
{
    private static readonly object Lock = new();

    public static void Log(string source, Exception? ex)
    {
        try
        {
            var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {ex}";
            lock (Lock)
            {
                File.AppendAllText(
                    Path.Combine(FileSystem.AppDataDirectory, "crash.log"),
                    msg + Environment.NewLine + Environment.NewLine);
            }
#if ANDROID
            Android.Util.Log.Error("BluetoothComm", msg);
#endif
        }
        catch
        {
            // 日志失败不影响主流程
        }
    }
}
