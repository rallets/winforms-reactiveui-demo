using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Splat;
using WinForms.Reactive.Client.Services;

namespace WinForms.Reactive.Client.ViewModels;

/*
 * Here we are using Dynamic Data, because WinWorms doesn't support `ReadOnlyObservableCollection`
 * but actually the constant SUPPORTS_BINDINGLIST is not defined for net6.0-windows
 * see: https://github.com/reactivemarbles/DynamicData/blob/ec02c3b84d6272b812c3ad5c21b2ee30ce8b41e7/src/DynamicData/List/ObservableListEx.cs#L349
 * so this is not working yet.
 */

public enum ItemsOrderBy
{
	None = 0,
	Id,
	Name
}

public class ItemsDDViewModel : ReactiveObject, IRoutableViewModel
{
	private IItemsService _itemsService;

	public string UrlPathSegment => nameof(ItemsDDViewModel);
	public IScreen HostScreen { get; protected set; }

	// Commands
	public ReactiveCommand<Unit, IEnumerable<ItemDto>> LoadItemsCommand { get; }

	// Inputs
	[Reactive] public string SearchStartsWith { get; set; } = string.Empty;
	[Reactive] public string SearchEndsWith { get; set; } = string.Empty;
	[Reactive] public ItemsOrderBy OrderBy { get; set; } = ItemsOrderBy.Name;

	private readonly SourceCache<ItemDto, Guid> _items;
	public ReadOnlyObservableCollection<ItemDto> Items;

	public ItemsDDViewModel(
		IItemsService? itemsService = null
		)
	{
		_itemsService = itemsService ?? Locator.Current.GetService<IItemsService>()!;

		/*
		 * We use the DynamicData preferred method with `SourceCache` as source of the streaming.
		 * SourceCache needs a unique key, otherwise use SourceList.
		 */
		_items = new SourceCache<ItemDto, Guid>(o => o.ItemId);

		Func<ItemDto, bool> FilterItem(string startsWith, string endsWith)
		{
			return item =>
			{
				return item.Name.StartsWith(startsWith) && item.Name.EndsWith(endsWith);
			};
		}

		// more complex filter, not tested yet
		// var filter = this.WhenAny(
		//		t => t.SearchStartsWith,
		//		t => t.SearchEndsWith,
		//		//(startsWith, endsWith) => FilterItem(startsWith.Value, endsWith.Value)
		//		(startsWith, endsWith) => (startsWith: startsWith.Value, endsWith: endsWith.Value)
		//	)
		//	.Throttle(TimeSpan.FromMilliseconds(800))
		//	.Select(term => (startsWith: term.startsWith.Trim(), endsWith: term.endsWith.Trim()))
		//	.DistinctUntilChanged()
		//	.Select(x => FilterItem(x.startsWith, x.endsWith));

		// basic filter
		var filter = this.WhenAny(
			t => t.SearchStartsWith,
			t => t.SearchEndsWith,
			(startsWith, endsWith) => FilterItem(startsWith.Value, endsWith.Value)
		);

		IObservable<IComparer<ItemDto>> sortBy = this.WhenAnyValue(x => x.OrderBy)
			.Select(x =>
			{
				return x switch {
					ItemsOrderBy.Id => SortExpressionComparer<ItemDto>.Ascending(t => t.ItemId),
					ItemsOrderBy.Name => SortExpressionComparer<ItemDto>.Ascending(t => t.Name),
					_ => SortExpressionComparer<ItemDto>.Ascending(t => t.Name)
				};
			});

		_items
			.Connect()
			.AutoRefreshOnObservable(x => this.WhenAnyValue(y => y.OrderBy))
			.Filter(filter)
			.Sort(sortBy)
			.ObserveOn(RxApp.MainThreadScheduler)
			.Bind(out Items)
			.Subscribe(x =>
			{
				var changeSet = x;
			});

		/*
		 * NB. I'm emitting here in the costructor only for debugging purposes, as it's not yet supported in winforms.
		 */
		var staticItems = new List<ItemDto> {
			new (Guid.NewGuid(), "Name 1", new List<ItemTagDto> {
				new (Guid.NewGuid(), "Sub 1"),
				new (Guid.NewGuid(), "Sub 2")
			}),
			new (Guid.NewGuid(), "Name 2", new List<ItemTagDto>()),
		};
		_items.Edit(innerCache => innerCache.AddOrUpdate(staticItems));

		/*
		 * `LoadItemsCommand` is a command that returns a list from an async call (from an asyc backend, for example).
		 *
		 * We guard agains multiple execution via the `canExecute` condition.
		 * We set a flag `IsExecuting` to show a progressbar/spinner.
		 * We can handle exceptions if needed.
		 * We refresh automatically the list every 5 minutes.
		 */
		LoadItemsCommand = ReactiveCommand.CreateFromTask(LoadItems);
		LoadItemsCommand.ObserveOn(RxApp.MainThreadScheduler).Subscribe(x =>
		{
			/*
			* Here we are using `Edit` that supports atomic updates, so we change the dataset with only one emit.
			*/
			_items.Edit(innerCache => innerCache.AddOrUpdate(x));
		});

	}

	private async Task<IEnumerable<ItemDto>> LoadItems()
	{
		var items = await _itemsService.GetAll();
		return items;
	}

}
