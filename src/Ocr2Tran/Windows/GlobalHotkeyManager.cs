using System.Runtime.InteropServices;
using Ocr2Tran.App;

namespace Ocr2Tran.Windows;

public sealed class GlobalHotkeyManager : NativeWindow, IDisposable
{
    private const int WmHotkey = 0x0312;
    private readonly Dictionary<int, HotkeyAction> _actions = new();
    private bool _disposed;

    public GlobalHotkeyManager()
    {
        CreateHandle(new CreateParams());
    }

    public event EventHandler<HotkeyAction>? HotkeyPressed;

    public void Register(HotkeyAction action, string value)
    {
        var spec = HotkeyParser.Parse(value);
        var id = (int)action;
        if (!RegisterHotKey(Handle, id, (uint)spec.Modifiers, (uint)spec.Key))
        {
            throw new InvalidOperationException($"Unable to register hotkey {value} for {action}.");
        }

        _actions[id] = action;
    }

    public void Clear()
    {
        foreach (var id in _actions.Keys)
        {
            UnregisterHotKey(Handle, id);
        }

        _actions.Clear();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey && _actions.TryGetValue(m.WParam.ToInt32(), out var action))
        {
            HotkeyPressed?.Invoke(this, action);
            return;
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Clear();

        DestroyHandle();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
