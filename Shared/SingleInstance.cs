using System.Runtime.InteropServices;

namespace D4Overlay.Shared;

/// <summary>
/// Une seule instance de chaque application par session Windows. La seconde instance n'ouvre rien : elle
/// envoie à la première un message Windows « manifeste-toi » (celle-ci réaffiche sa fenêtre ou une bulle),
/// puis se termine. Le verrou (mutex) est libéré par Windows même si la première instance plante.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const int HwndBroadcast = 0xFFFF;
    private const int AsfwAny = -1;

    private readonly Mutex _mutex;

    /// <param name="appKey">Nom propre à l'application : "Overlay" ou "Config".</param>
    public SingleInstance(string appKey)
    {
        _mutex = new Mutex(initiallyOwned: true, name: $"D4Overlay.{appKey}.SingleInstance", out bool created);
        IsFirst = created;
        ActivateMessage = RegisterWindowMessage($"D4Overlay.{appKey}.Activate");
    }

    /// <summary>true : c'est la première instance, elle peut démarrer.</summary>
    public bool IsFirst { get; }

    /// <summary>Numéro du message Windows que la première instance doit écouter dans son WndProc.</summary>
    public uint ActivateMessage { get; }

    /// <summary>Depuis la seconde instance : demande à la première de se manifester.</summary>
    public void SignalFirst()
    {
        try
        {
            AllowSetForegroundWindow(AsfwAny); // sinon Windows peut refuser de passer la fenêtre de la première instance au premier plan
            PostMessage((IntPtr)HwndBroadcast, ActivateMessage, IntPtr.Zero, IntPtr.Zero);
        }
        catch { }
    }

    public void Dispose()
    {
        if (IsFirst)
        {
            try { _mutex.ReleaseMutex(); } catch { }
        }
        _mutex.Dispose();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);
}
