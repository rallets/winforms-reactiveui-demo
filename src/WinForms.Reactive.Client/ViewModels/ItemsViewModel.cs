using System.Reactive;
using System.Reactive.Linq;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Splat;
using WinForms.Reactive.Client.Helpers;
using WinForms.Reactive.Client.Interactions;
using WinForms.Reactive.Client.Services;

namespace WinForms.Reactive.Client.ViewModels;

/*
 * Here we are using Fody syntax `[Reactive]` & `[ObservableAsProperty]` to avoid boilerplate code.
 * Ref: https://www.reactiveui.net/docs/handbook/view-models/boilerplate-code
 *
 * PS. Fody implements this pattern, but it hides `ThrownException`.
 *
 * Fody:
 * [Reactive] public object SelectedItem { get; set; } = null;
 *
 * Explicit way:
 * private object _selectedItem;
 * public object SelectedItem
 * {
 * 	get => _selectedItem;
 * 	set => this.RaiseAndSetIfChanged(ref _selectedItem, value);
 * }
 *
 * Fody:
 * [ObservableAsProperty] public IEnumerable<ItemDto> SearchResults { get; }
 * [ObservableAsProperty] public string FirstName { get; }
 *
 * Explicit way:
 * private readonly ObservableAsPropertyHelper<IEnumerable<ItemDto>> _searchResults;
 * public IEnumerable<ItemDto> SearchResults => _searchResults.Value;
 *
 * readonly ObservableAsPropertyHelper<string> _firstName;
 * public string FirstName => _firstName.Value;
 */

public class ItemsViewModel : ReactiveObject, IRoutableViewModel
{
	private IItemsService _itemsService;

	public string UrlPathSegment => nameof(ItemsViewModel);
	public IScreen HostScreen { get; protected set; }

	// Commands
	public ReactiveCommand<Unit, IEnumerable<ItemDto>> LoadItemsCommand { get; }
	public ReactiveCommand<Unit, Unit> ShowDetailsCommand { get; }

	// Input
	[Reactive] public string SearchStartsWith { get; set; } = string.Empty;
	[Reactive] public string SearchEndsWith { get; set; } = string.Empty;
	[Reactive] public (Guid? tagId, int index) SelectedItem { get; set; }

	// Output
	[ObservableAsProperty] public IEnumerable<ItemDto> Items { get; } = new List<ItemDto>();
	[ObservableAsProperty] public IEnumerable<ItemDto> ItemsFiltered { get; } = new List<ItemDto>();
	[ObservableAsProperty] public IEnumerable<ItemTagDto> SelectedItemTags { get; }
	[ObservableAsProperty] public bool IsLoading { get; } = false;
	[ObservableAsProperty] public bool HasItems { get; } = false;
	[ObservableAsProperty] public bool HasItemSelection { get; } = false;
	[ObservableAsProperty] public bool HasSubItems { get; } = false;

	public ItemsViewModel(
		IItemsService? itemsService = null
		)
	{
		_itemsService = itemsService ?? Locator.Current.GetService<IItemsService>()!;

		/*
		 * `LoadItemsCommand` is a command that returns a list from an async call (from an asyc backend, for example).
		 *
		 * We guard agains multiple execution via the `canExecute` condition.
		 * We set a flag `IsExecuting` to show a progressbar/spinner.
		 * We can handle exceptions if needed.
		 * We refresh automatically the list every 5 minutes.
		 */
		LoadItemsCommand = ReactiveCommand.CreateFromTask(LoadItems, LoadItemsCommand?.IsExecuting.Select(x => !x));
		LoadItemsCommand.IsExecuting.ToPropertyEx(this, x => x.IsLoading);
		//LoadItemsCommand.ThrownExceptions.Subscribe(error => { /* Handle errors here */ });
		LoadItemsCommand
			.ObserveOn(RxApp.MainThreadScheduler)
			.ToPropertyEx(this, x => x.Items);

		var interval = TimeSpan.FromMinutes(5);
		Observable.Timer(interval, interval)
			.Select(time => Unit.Default)
			.InvokeCommand(this, x => x.LoadItemsCommand);

		ShowDetailsCommand = ReactiveCommand.CreateFromTask(ShowDetails, this.WhenAnyValue(x => x.HasItemSelection));

		/*
		 * `WhenAny` is he same as `Observable.CombineLatest`.
		 * Hence this is equal to
		 *
		 * Observable.CombineLatest(
		 *	this.WhenAnyValue(x => x.Items),
		 *	this.WhenAnyValue(x => x.SearchStartsWith),
		 *		this.WhenAnyValue(x => x.SearchEndsWith),
		 *	(items, startsWith, endsWith) => (items, startsWith, endsWith))
		 * .Select(...)
		 * .etc
		 */
		this.WhenAny(x => x.Items, x => x.SearchStartsWith, x => x.SearchEndsWith, (items, startsWith, endsWith) => (items, startsWith, endsWith))
			.Throttle(TimeSpan.FromMilliseconds(800))
			.DistinctUntilChanged()
			.Select(x =>
			{
				// uncomment to check how Catch() works
				//throw new Exception("ahah");

				var r = (x.items.Value ?? Enumerable.Empty<ItemDto>())
					.Where(i =>
						(string.IsNullOrEmpty(x.startsWith.Value) || i.Name.StartsWith(x.startsWith.Value))
						&&
						(string.IsNullOrEmpty(x.endsWith.Value) || i.Name.EndsWith(x.endsWith.Value)
						))
					.ToList();
				return r;
			})
			.Catch(Observable.Return(Enumerable.Empty<ItemDto>()))
			.ObserveOn(RxApp.MainThreadScheduler)
			.ToPropertyEx(this, x => x.ItemsFiltered);

		this
			.WhenAnyValue(x => x.Items)
			.Select(items => items != null && items.Any())
			.ToPropertyEx(this, x => x.HasItems);

		this
			.WhenAnyValue(x => x.SelectedItem)
			.Select(x => x.tagId != null)
			.ToPropertyEx(this, x => x.HasItemSelection);

		this
			.WhenAny(x => x.SelectedItem, x => x.Items, (selectedItem, items) => (selectedItem, items))
			.Select(x => (Items?.FirstOrDefault(item => item.ItemId == x.selectedItem.Value.tagId)?.Tags?.Count() ?? 0) > 0)
			.ToPropertyEx(this, x => x.HasSubItems);

		this.WhenAnyValue(x => x.SelectedItem)
			.Throttle(TimeSpan.FromMilliseconds(800))
			.Where(x => x.tagId != null)
			.Select(x => x.tagId!.Value)
			.SelectMany(FetchDetailAsync)
			.ObserveOn(RxApp.MainThreadScheduler)
			.ToPropertyEx(this, x => x.SelectedItemTags);
	}

	private async Task<Unit> ShowDetails(CancellationToken token)
	{
		var itemId = this.SelectedItem.tagId;

		var confirm = await MessageInteractions.AskConfirmation.Handle("Are you sure?");
		if (confirm)
		{
			// TODO: is this the right way to spin a new indipendent form?
			var tags = this.Items.First(x => x.ItemId == itemId).Tags;
			var vm = new ItemTagsViewModel(tags);
			var v = vm.GetView();
			v.Show();

			await Task.Delay(2000);
			await MessageInteractions.ShowMessage.Handle("Details were shown");
		}

		return await Task.FromResult(Unit.Default);
	}

	private async Task<IEnumerable<ItemDto>> LoadItems()
	{
		var items = await _itemsService.GetAll();
		return items;
	}

	private async Task<IEnumerable<ItemTagDto>> FetchDetailAsync(Guid id, CancellationToken token)
	{
		var tags = this.Items.Where(x => x.ItemId == id).SelectMany(x => x.Tags).ToList();
		return await Task.FromResult(tags);
	}

}
