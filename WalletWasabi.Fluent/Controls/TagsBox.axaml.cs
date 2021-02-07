using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Metadata;
using Avalonia.Threading;
using WalletWasabi.Helpers;

namespace WalletWasabi.Fluent.Controls
{
	public class TagsBox : TemplatedControl
	{
		public static readonly StyledProperty<bool> RestrictInputToSuggestionsProperty =
			AvaloniaProperty.Register<TagsBox, bool>(nameof(RestrictInputToSuggestions));

		public static readonly StyledProperty<int> ItemCountLimitProperty =
			AvaloniaProperty.Register<TagsBox, int>(nameof(ItemCountLimit));

		public static readonly StyledProperty<char> TagSeparatorProperty =
			AvaloniaProperty.Register<TagsBox, char>(nameof(TagSeparator), defaultValue: ' ');

		public static readonly StyledProperty<bool> SuggestionsAreCaseSensitiveProperty =
			AvaloniaProperty.Register<TagsBox, bool>(nameof(SuggestionsAreCaseSensitive), defaultValue: true);

		public static readonly DirectProperty<TagsBox, IEnumerable<string>> ItemsProperty =
			AvaloniaProperty.RegisterDirect<TagsBox, IEnumerable<string>>(nameof(Items),
				o => o.Items,
				(o, v) => o.Items = v,
				enableDataValidation: true);

		public static readonly DirectProperty<TagsBox, IEnumerable?> SuggestionsProperty =
			AvaloniaProperty.RegisterDirect<TagsBox, IEnumerable?>(
				nameof(Suggestions),
				o => o.Suggestions,
				(o, v) => o.Suggestions = v);

		private CompositeDisposable? _compositeDisposable;
		private AutoCompleteBox? _autoCompleteBox;
		private TextBox _internalTextBox;
		private StringComparison _stringComparison;
		private bool _backspaceEmptyField1;
		private bool _backspaceEmptyField2;
		private bool _isFocused;
		private bool _isInputEnabled = true;
		private IEnumerable? _suggestions;
		private ICommand? _completedCommand;
		private IEnumerable<string> _items;

		public static readonly DirectProperty<TagsBox, ICommand?> CompletedCommandProperty =
			AvaloniaProperty.RegisterDirect<TagsBox, ICommand?>(
				nameof(CompletedCommand),
				o => o.CompletedCommand,
				(o, v) => o.CompletedCommand = v);

		public static readonly StyledProperty<bool> IsReadOnlyProperty =
			AvaloniaProperty.Register<TagsBox, bool>("IsReadOnly");

		[Content]
		public IEnumerable<string> Items
		{
			get => _items;
			set => SetAndRaise(ItemsProperty, ref _items, value);
		}

		public bool RestrictInputToSuggestions
		{
			get => GetValue(RestrictInputToSuggestionsProperty);
			set => SetValue(RestrictInputToSuggestionsProperty, value);
		}

		public int ItemCountLimit
		{
			get => GetValue(ItemCountLimitProperty);
			set => SetValue(ItemCountLimitProperty, value);
		}

		public char TagSeparator
		{
			get => GetValue(TagSeparatorProperty);
			set => SetValue(TagSeparatorProperty, value);
		}

		public IEnumerable? Suggestions
		{
			get => _suggestions;
			set => SetAndRaise(SuggestionsProperty, ref _suggestions, value);
		}

		public ICommand? CompletedCommand
		{
			get => _completedCommand;
			set => SetAndRaise(CompletedCommandProperty, ref _completedCommand, value);
		}

		public bool IsReadOnly
		{
			get => GetValue(IsReadOnlyProperty);
			set => SetValue(IsReadOnlyProperty, value);
		}

		public bool SuggestionsAreCaseSensitive
		{
			get => GetValue(SuggestionsAreCaseSensitiveProperty);
			set => SetValue(SuggestionsAreCaseSensitiveProperty, value);
		}

		protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
		{
			base.OnApplyTemplate(e);

			_compositeDisposable?.Dispose();

			_compositeDisposable = new CompositeDisposable();

			var Presenter = e.NameScope.Find<ItemsPresenter>("PART_ItemsPresenter");

			Presenter.ApplyTemplate();

			_autoCompleteBox = (Presenter.Panel as ConcatenatingWrapPanel)?.ConcatenatedChildren
				.OfType<AutoCompleteBox>().FirstOrDefault();

			if (_autoCompleteBox is null)
			{
				return;
			}

			_autoCompleteBox.TextChanged += OnAutoCompleteBoxTextChanged;
			_autoCompleteBox.DropDownClosed += OnAutoCompleteBoxDropDownClosed;
			_autoCompleteBox.TemplateApplied += OnAutoCompleteBoxTemplateApplied;

			Disposable.Create(
					() =>
					{
						_autoCompleteBox.TextChanged -= OnAutoCompleteBoxTextChanged;
						_autoCompleteBox.DropDownClosed -= OnAutoCompleteBoxDropDownClosed;
					})
				.DisposeWith(_compositeDisposable);

			_autoCompleteBox
				.AddDisposableHandler(TextInputEvent, OnTextInput, RoutingStrategies.Tunnel)
				.DisposeWith(_compositeDisposable);

			_autoCompleteBox
				.AddDisposableHandler(KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel)
				.DisposeWith(_compositeDisposable);

			if (_isFocused)
			{
				Dispatcher.UIThread.Post(() => _autoCompleteBox.Focus());
			}
		}

		private void OnAutoCompleteBoxTemplateApplied(object? sender, TemplateAppliedEventArgs e)
		{
			_internalTextBox = e.NameScope.Find<TextBox>("PART_TextBox");
		}

		private void CheckIsInputEnabled()
		{
			if (Items is IList x && ItemCountLimit > 0)
			{
				_isInputEnabled = x.Count < ItemCountLimit;
			}
		}

		private void OnTextInput(object? sender, TextInputEventArgs e)
		{
			if (sender is not AutoCompleteBox autoCompleteBox)
			{
				return;
			}

			if (!_isInputEnabled)
			{
				e.Handled = true;
				return;
			}

			if (RestrictInputToSuggestions &&
			    Suggestions is IList<string> suggestions &&
			    !suggestions.Any(x =>
				    x.StartsWith(autoCompleteBox.SearchText, StringComparison.InvariantCultureIgnoreCase)))
			{
				e.Handled = true;
			}
		}

		protected override void UpdateDataValidation<T>(AvaloniaProperty<T> property, BindingValue<T> value)
		{
			if (property == ItemsProperty)
			{
				DataValidationErrors.SetError(this, value.Error);
			}
		}

		protected override void OnGotFocus(GotFocusEventArgs e)
		{
			base.OnGotFocus(e);
			FocusChanged();
		}

		protected override void OnLostFocus(RoutedEventArgs e)
		{
			base.OnLostFocus(e);
			FocusChanged();
		}

		private void FocusChanged()
		{
			if (_autoCompleteBox is { })
			{
				if (IsKeyboardFocusWithin && !_autoCompleteBox.IsKeyboardFocusWithin)
				{
					_autoCompleteBox?.Focus();
				}
			}

			_isFocused = IsKeyboardFocusWithin;
		}

		private void OnAutoCompleteBoxDropDownClosed(object? sender, EventArgs e)
		{
			if (sender is not AutoCompleteBox autoCompleteBox)
			{
				return;
			}

			if (_internalTextBox is null)
			{
				return;
			}


			var currentText = (autoCompleteBox.Text ?? "").Trim();
			var selectedTextLength = Math.Max(0, _internalTextBox.SelectionEnd - _internalTextBox.SelectionStart);


			if (currentText.Length == 0 ||
			    selectedTextLength == 0 ||
			    autoCompleteBox.SelectedItem is not string selItem ||
			    selItem.Length == 0 ||
			    currentText != selItem)
			{
				return;
			}

			AddTag(currentText);
			BackspaceLogicClear();
			autoCompleteBox.ClearValue(AutoCompleteBox.SelectedItemProperty);
			Dispatcher.UIThread.Post(() => autoCompleteBox.ClearValue(AutoCompleteBox.TextProperty));
		}

		private void BackspaceLogicClear()
		{
			_backspaceEmptyField2 = _backspaceEmptyField1 = true;
		}

		private void OnAutoCompleteBoxTextChanged(object? sender, EventArgs e)
		{
			if (sender is not AutoCompleteBox autoCompleteBox ||
			    string.IsNullOrEmpty(Guard.Correct(autoCompleteBox.Text)))
			{
				return;
			}

			var currentText = autoCompleteBox.Text ?? "";
			var endsWithSeparator = currentText.EndsWith(TagSeparator);
			currentText = currentText.Trim();

			var splitTags = currentText.Split(TagSeparator);

			if (splitTags.Length <= 1)
			{
				var tag = splitTags[0];

				if (!_isInputEnabled ||
				    !endsWithSeparator)
				{
					return;
				}

				if (RestrictInputToSuggestions && Suggestions is { } &&
				    !Suggestions.Cast<string>().Any(
					    x => x.Equals(tag, StringComparison.InvariantCultureIgnoreCase)))
				{
					return;
				}

				AddTag(tag);
				Dispatcher.UIThread.Post(() => autoCompleteBox.ClearValue(AutoCompleteBox.TextProperty));
			}
			else
			{
				foreach (var tag in splitTags)
				{
					if (RestrictInputToSuggestions && Suggestions is { } &&
					    !Suggestions.Cast<string>().Any(
						    x => x.Equals(tag, StringComparison.InvariantCultureIgnoreCase)))
					{
						continue;
					}

					AddTag(tag);
					Dispatcher.UIThread.Post(() => autoCompleteBox.ClearValue(AutoCompleteBox.TextProperty));
				}
			}
		}

		private void OnKeyUp(object? sender, KeyEventArgs e)
		{
			if (sender is not AutoCompleteBox autoCompleteBox)
			{
				return;
			}

			var currentText = autoCompleteBox.Text ?? "";

			_backspaceEmptyField2 = _backspaceEmptyField1;
			_backspaceEmptyField1 = currentText.Length == 0;

			currentText = currentText.Trim();

			switch (e.Key)
			{
				case Key.Back when _backspaceEmptyField1 && _backspaceEmptyField2:
					RemoveLastTag();
					break;
				case Key.Enter when _isInputEnabled && !string.IsNullOrEmpty(currentText):
					// Reject entry of the tag when user pressed enter and
					// the input tag is not on the suggestions list.
					if (RestrictInputToSuggestions && Suggestions is { } &&
					    !Suggestions.Cast<string>().Any(
						    x => x.Equals(currentText, StringComparison.InvariantCultureIgnoreCase)))
					{
						break;
					}

					BackspaceLogicClear();
					AddTag(currentText);
					ExecuteCompletedCommand();

					Dispatcher.UIThread.Post(() => autoCompleteBox.ClearValue(AutoCompleteBox.TextProperty));
					break;

				case Key.Enter:
					ExecuteCompletedCommand();
					break;
			}
		}

		private void ExecuteCompletedCommand()
		{
			if (Items is IList x && x.Count >= ItemCountLimit)
			{
				if (CompletedCommand is { } && CompletedCommand.CanExecute(null))
				{
					CompletedCommand.Execute(null);
				}
			}
		}

		private void RemoveLastTag()
		{
			if (Items is IList x && x.Count > 0)
			{
				x.RemoveAt(Math.Max(0, x.Count - 1));
			}

			CheckIsInputEnabled();
		}

		protected override void OnPropertyChanged<T>(AvaloniaPropertyChangedEventArgs<T> e)
		{
			base.OnPropertyChanged(e);

			if (e.Property == IsReadOnlyProperty)
			{
				PseudoClasses.Set(":readonly", IsReadOnly);
			}
			else if (e.Property == SuggestionsAreCaseSensitiveProperty)
			{
				_stringComparison = SuggestionsAreCaseSensitive
					? StringComparison.InvariantCultureIgnoreCase
					: StringComparison.InvariantCulture;
			}
		}

		internal void RemoveTargetTag(object? tag)
		{
			if (Items is IList {Count: > 0} x && tag is { })
			{
				x.RemoveAt(x.IndexOf(tag));
			}

			CheckIsInputEnabled();
		}

		private void AddTag(string tag)
		{
			if (Items is IList x)
			{
				if (ItemCountLimit > 0 && x.Count + 1 >= ItemCountLimit)
				{
					return;
				}

				x.Add(tag);
			}

			CheckIsInputEnabled();
		}
	}
}