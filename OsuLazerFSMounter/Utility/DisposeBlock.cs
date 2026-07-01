namespace OsuLazerFSMounter.Utility;
public class DisposeBlock : IDisposable
{
	private volatile int _hasDisposed = 0;

	public bool HasDisposed => this._hasDisposed != 0;
	public Action Disposer { get; set; }

	public DisposeBlock(Action disposer)
	{
		this.Disposer = disposer;
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref this._hasDisposed, 1) == 0)
			this.Disposer.Invoke();
	}
}
