using Ocr2Tran.App;
using Ocr2Tran.Windows;

namespace Ocr2Tran;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var context = new TrayAppContext(ConfigStore.Load());
        Application.Run(context);
    }
}
