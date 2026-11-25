using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls.Primitives;
using NovaTaskbar.Windows.Interop;

namespace NovaTaskbar.App.Widgets;

/// <summary>
/// O Dock é uma janela que não recebe foco (WS_EX_NOACTIVATE), então o "light dismiss" do WinUI não percebe
/// cliques em outros apps ou na área de trabalho. Enquanto um painel/menu está aberto, observamos os botões do mouse
/// (50 ms) e fechamos ao detectar um clique novo sobre uma janela que não é deste processo.
/// </summary>
internal static class OutsideClickDismiss
{
    private static readonly uint OwnPid = (uint)Environment.ProcessId;
    private static readonly int[] Buttons = [0x01, 0x02, 0x04, 0x05, 0x06]; // esquerdo, direito, meio, X1, X2

    /// <summary>Liga o fechamento por clique externo neste flyout (chame antes do ShowAt).</summary>
    public static T Attach<T>(T flyout) where T : FlyoutBase
    {
        DispatcherQueueTimer? timer = null;
        bool[] wasDown = new bool[Buttons.Length];
        flyout.Opened += (_, _) =>
        {
            for (int i = 0; i < Buttons.Length; i++) wasDown[i] = IsDown(Buttons[i]); // ignora o clique que abriu
            timer ??= DispatcherQueue.GetForCurrentThread().CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(50);
            timer.Tick += Tick;
            timer.Start();
        };
        flyout.Closed += (_, _) =>
        {
            if (timer is null) return;
            timer.Stop();
            timer.Tick -= Tick;
        };
        return flyout;

        void Tick(DispatcherQueueTimer t, object o)
        {
            bool pressed = false;
            for (int i = 0; i < Buttons.Length; i++)
            {
                bool down = IsDown(Buttons[i]);
                if (down && !wasDown[i]) pressed = true;
                wasDown[i] = down;
            }
            if (pressed && !OverOwnWindow()) flyout.Hide();
        }
    }

    private static bool IsDown(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>O cursor está sobre o Dock, o vidro ou um painel/menu aberto (todos deste processo)?</summary>
    internal static bool OverOwnWindow()
    {
        if (!NativeMethods.GetCursorPos(out var p)) return true;
        IntPtr h = NativeMethods.WindowFromPoint(p);
        if (h == IntPtr.Zero) return false;
        IntPtr root = NativeMethods.GetAncestor(h, 2 /* GA_ROOT */);
        NativeMethods.GetWindowThreadProcessId(root == IntPtr.Zero ? h : root, out uint pid);
        return pid == OwnPid;
    }
}
