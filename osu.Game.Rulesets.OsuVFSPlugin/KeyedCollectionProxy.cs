using System.Collections.ObjectModel;

namespace osu.Game.Rulesets.OsuVFSPlugin;
public class KeyedCollectionProxy<TKey, TItem> : KeyedCollection<TKey, TItem>
	where TKey : notnull
{
	private readonly Func<TItem, TKey> _keySelector;

	public KeyedCollectionProxy(Func<TItem, TKey> keySelector)
	{
		this._keySelector = keySelector;
	}

	protected override TKey GetKeyForItem(TItem item)
	{
		return this._keySelector(item);
	}
}