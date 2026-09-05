using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;

namespace BluetoothComm;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // 浅色状态栏背景 + 深色图标（Android 6.0+）
#pragma warning disable CA1416, CS0618
        if (Build.VERSION.SdkInt >= BuildVersionCodes.R && Window?.InsetsController is not null)
        {
            Window.InsetsController.SetSystemBarsAppearance(
                (int)WindowInsetsControllerAppearance.LightStatusBars,
                (int)WindowInsetsControllerAppearance.LightStatusBars);
        }
        else if (Build.VERSION.SdkInt >= BuildVersionCodes.M && Window?.DecorView is not null)
        {
            Window.DecorView.SystemUiVisibility = (StatusBarVisibility)SystemUiFlags.LightStatusBar;
        }
#pragma warning restore CA1416
    }
}
