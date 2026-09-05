using Microsoft.Extensions.Logging;
using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;

namespace BluetoothComm;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		// 注册蓝牙适配器（Plugin.BLE）
		builder.Services.AddSingleton<IBluetoothLE>(CrossBluetoothLE.Current);
		builder.Services.AddSingleton<IAdapter>(CrossBluetoothLE.Current.Adapter);

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
