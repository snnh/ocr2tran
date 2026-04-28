namespace Ocr2Tran.Windows;

public static class AppIcon
{
    private static readonly object Gate = new();
    private static Icon? _icon;

    public static Icon Create()
    {
        lock (Gate)
        {
            _icon ??= Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
            return _icon;
        }
    }

    public static void ApplyTo(Form form)
    {
        form.Icon = Create();
    }

    public static void Dispose()
    {
        lock (Gate)
        {
            if (!ReferenceEquals(_icon, SystemIcons.Application))
            {
                _icon?.Dispose();
            }

            _icon = null;
        }
    }
}
