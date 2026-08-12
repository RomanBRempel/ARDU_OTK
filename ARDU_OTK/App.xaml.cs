using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ARDU_OTK;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private Window? _window;

    /// <summary>
    /// Главное окно приложения.
    /// </summary>
    /// <remarks>
    /// Нужно файловым диалогам: приложение unpackaged, и пикеры обязаны
    /// получить окно-владелец через WinRT-интероп, иначе падают в рантайме.
    /// </remarks>
    public static Window MainWindow =>
        Current is App { _window: { } window }
            ? window
            : throw new InvalidOperationException("Главное окно ещё не создано.");


    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        InitializeComponent();

        // Необработанное исключение в WinUI гасит процесс без следов: Windows
        // показывает только 0xC000027B. Пишем причину в файл, иначе на цеховом
        // ПК разобраться невозможно.
        UnhandledException += (_, e) =>
        {
            LogFatal("UnhandledException", e.Exception);
            e.Handled = false;
        };
    }

    /// <summary>Путь к журналу аварий. Рядом с данными, а не в каталоге установки.</summary>
    internal static string CrashLogPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ARDU_OTK.Data",
        "startup-errors.log");

    internal static void LogFatal(string context, Exception? ex)
    {
        try
        {
            var path = CrashLogPath;
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.AppendAllText(
                path,
                $"[{DateTimeOffset.Now:O}] {context}: {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Журнал — вспомогательный. Его отказ не должен подменять исходную ошибку.
        }
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        try
        {
            // Масштаб применяется до создания окна: размеры подставляются в
            // разметку при её загрузке, и страница, построенная раньше вызова,
            // осталась бы с исходными шрифтами.
            Services.UiScale.Apply(Services.UiScale.Load());

            _window = new MainWindow();
            _window.Activate();
        }
        catch (Exception ex)
        {
            LogFatal("OnLaunched", ex);
            throw;
        }
    }
}
