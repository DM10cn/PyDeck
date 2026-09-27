namespace PimGui.Core;

/// <summary>Coalesce producer notifications into one pending dispatcher callback.
/// The callback reads current state, so obsolete intermediate updates need no queue.</summary>
public sealed class CoalescedAction(Func<Action, bool> enqueue, Action update)
{
    private int pending;

    public void Request()
    {
        if (Interlocked.CompareExchange(ref pending, 1, 0) != 0) return;
        try
        {
            if (!enqueue(() =>
            {
                // Release before reading state: a concurrent producer must get a later turn.
                Interlocked.Exchange(ref pending, 0);
                update();
            })) Interlocked.Exchange(ref pending, 0);
        }
        catch { Interlocked.Exchange(ref pending, 0); throw; }
    }
}
