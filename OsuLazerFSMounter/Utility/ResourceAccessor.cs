namespace OsuLazerFSMounter.Utility;

public class ResourceAccessor<T> : IDisposable
{
	public sealed class AccessorScope : IDisposable
	{
		private readonly ResourceAccessor<T> _accessor;
		private volatile int _hasDisposed = 0;

		public bool HasDisposed => this._hasDisposed != 0;

		public T Value
		{
			get
			{
				ObjectDisposedException.ThrowIf(this._hasDisposed != 0, this);
				return this._accessor._value;
			}
			set
			{
				ObjectDisposedException.ThrowIf(this._hasDisposed != 0, this);
				this._accessor._value = value;
			}
		}

		/// <summary>
		/// you are supposed to wait in the caller method, this class only release
		/// </summary>
		/// <param name="accessor"></param>
		internal AccessorScope(ResourceAccessor<T> accessor)
		{
			this._accessor = accessor;
		}

		public void Dispose()
		{
			if (Interlocked.Exchange(ref this._hasDisposed, 1) == 0)
				this._accessor._semaphore.Semaphore.Release();
		}
	}

	public delegate void Accessor(ref T resource);
	public delegate Task<T> AsyncAccessor(T resource);

	private readonly ScopedSemaphoreSlim _semaphore;

	private T _value;

	public ResourceAccessor(int initialCount, int maxCount, T initialValue)
	{
		this._semaphore = new(initialCount, maxCount);
		this._value = initialValue;
	}

	public void Dispose()
	{
		GC.SuppressFinalize(this);
		this._semaphore.Dispose();
	}

	public void Access(Accessor accessor)
	{
		using ScopedSemaphoreSlim.Scope _ = this._semaphore.Enter();
		accessor.Invoke(ref this._value);
	}
	public async Task AccessAsync(AsyncAccessor accessor, CancellationToken ct = default)
	{
		using ScopedSemaphoreSlim.Scope _ = await this._semaphore.EnterAsync(ct);
		this._value = await accessor.Invoke(this._value);
	}

	public AccessorScope EnterAccessorScope()
	{
		this._semaphore.Semaphore.Wait();
		return new(this);
	}
	public async Task<AccessorScope> EnterAccessorScopeAsync(CancellationToken ct = default)
	{
		await this._semaphore.Semaphore.WaitAsync(ct);
		return new(this);
	}
}
