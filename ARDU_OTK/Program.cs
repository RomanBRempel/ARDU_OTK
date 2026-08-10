using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Velopack;

namespace ARDU_OTK;

/// <summary>
/// Собственная точка входа вместо генерируемой XAML.
/// </summary>
/// <remarks>
/// Нужна из-за Velopack: <see cref="VelopackApp.Run"/> обязан отработать
/// самой первой строкой процесса. Установщик и апдейтер перезапускают тот же
/// exe со служебными аргументами (установка, первый запуск, удаление), Velopack
/// перехватывает их и завершает процесс. Если до этого успеет подняться XAML,
/// служебные запуски будут показывать окно приложения.
///
/// Генерируемый Main отключён через DISABLE_XAML_GENERATED_MAIN в csproj.
/// </remarks>
public static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(initParams =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }
}
