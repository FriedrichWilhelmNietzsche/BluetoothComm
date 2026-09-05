using Microsoft.Extensions.DependencyInjection;

namespace BluetoothComm;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();

		// 全局异常捕获：写 crash.log + Android Logcat，用于定位闪退
		AppDomain.CurrentDomain.UnhandledException += (_, e) =>
			CrashLogger.Log("UnhandledException", e.ExceptionObject as Exception);
		TaskScheduler.UnobservedTaskException += (_, e) =>
		{
			CrashLogger.Log("UnobservedTaskException", e.Exception);
			e.SetObserved();
		};
#if ANDROID
		Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (_, e) =>
		{
			CrashLogger.Log("AndroidEnvironment", e.Exception);
			e.Handled = false;
		};
#endif
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}
}
