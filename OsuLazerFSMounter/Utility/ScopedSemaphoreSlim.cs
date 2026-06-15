namespace OsuLazerFSMounter.Utility;

public class ScopedSemaphoreSlim : IDisposable
{
	public sealed class Scope(ScopedSemaphoreSlim parent) : IDisposable
	{
		private readonly ScopedSemaphoreSlim _parent = parent;
		private volatile int _isDisposed = 0;

		public bool IsDisposed => this._isDisposed != 0;

		public void Dispose()
		{
			if (Interlocked.CompareExchange(ref this._isDisposed, 1, 0) != 0) // no bool overload :(
				return;

			GC.SuppressFinalize(this);
			this._parent.Semaphore.Release();
		}
	}

	public SemaphoreSlim Semaphore { get; set; }

	public ScopedSemaphoreSlim(SemaphoreSlim semaphore)
	{
		this.Semaphore = semaphore;
	}
	public ScopedSemaphoreSlim(int initialCount, int maxCount)
		: this(new(initialCount, maxCount)) { }

	public Scope Enter()
	{
		this.Semaphore.Wait();
		return new(this);
	}
	public async Task<Scope> EnterAsync(CancellationToken cancellationToken = default)
	{
		await this.Semaphore.WaitAsync(cancellationToken);
		return new(this);
	}

	public void Dispose()
	{
		this.Semaphore.Dispose();
		GC.SuppressFinalize(this);
	}
}
