using System.Diagnostics;

namespace Ocr2Tran.Windows;

public static class ProjectHomepage
{
    public const string Url = "https://github.com/snnh/ocr2tran";

    public static void Open(IWin32Window? owner = null)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, ex.Message, "打开项目主页失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
