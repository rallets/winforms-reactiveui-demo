# winforms-reactiveui-demo

Winforms .net Core 6 + ReactiveUI and DynamicData

## Goals

The goal of this demo is to show how to deal with the most common scenarios for a Desktop application.
The application implements:

- Dependency injection with Splat, views, and services.
- How to use a main entry-point with Routing and UserControls.
- How to open a new Form (outside the main routing).
- Routing between screens/views.
- Binding data and execute commands between View and ViewModel.
- Bind to reactive properties (as calculated fields).
- How to bind data to a ListBox via DataSource.
- How to deal with UI events (via `ReactiveMarbles.ObservableEvents.SourceGenerator`). See the note below.
- How to use Interactions to show Modal form with or without DialogResult.
- How to subsribe to UI changes like TextBox and ComboBox with sort info, and refresh the data in the UI.
- How to use Dynamic Data and deal with Collections. See the note below.
- etc.

### NOte related to RxUi WinForms events

RxUI WinForms doesn't support `Bind` yet.
So there is a temporary solution to support events that don't implement INotifyPropertyChanged.

### Note related to Dynamin Data

The demo is using Dynamic Data, but it doesn't work yet, because WinWorms doesn't support `ReadOnlyObservableCollection`.

Technical info:

- Actually the constant SUPPORTS_BINDINGLIST is not defined for net6.0-windows
- see: <https://github.com/reactivemarbles/DynamicData/blob/ec02c3b84d6272b812c3ad5c21b2ee30ce8b41e7/src/DynamicData/List/ObservableListEx.cs#L349>
