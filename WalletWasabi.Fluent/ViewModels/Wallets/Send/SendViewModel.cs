using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Threading;
using Avalonia.Media.Imaging;
using DynamicData;
using DynamicData.Binding;
using NBitcoin;
using NBitcoin.Payment;
using ReactiveUI;
using WalletWasabi.Blockchain.Analysis.Clustering;
using WalletWasabi.Fluent.Models;
using WalletWasabi.Fluent.Validation;
using WalletWasabi.Fluent.ViewModels.Navigation;
using WalletWasabi.Logging;
using WalletWasabi.Models;
using WalletWasabi.Tor.Http;
using WalletWasabi.Tor.Socks5.Pool.Circuits;
using WalletWasabi.Userfacing;
using WalletWasabi.Wallets;
using WalletWasabi.WebClients.PayJoin;
using Constants = WalletWasabi.Helpers.Constants;
using System.Reactive.Concurrency;

namespace WalletWasabi.Fluent.ViewModels.Wallets.Send
{
	[NavigationMetaData(
		Title = "Send",
		Caption = "",
		IconName = "wallet_action_send",
		NavBarPosition = NavBarPosition.None,
		Searchable = false,
		NavigationTarget = NavigationTarget.DialogScreen)]
	public partial class SendViewModel : RoutableViewModel
	{
		private readonly Wallet _wallet;
		private readonly TransactionInfo _transactionInfo;
		[AutoNotify] private string _to;
		[AutoNotify] private decimal _amountBtc;
		[AutoNotify] private decimal _exchangeRate;
		[AutoNotify] private bool _isFixedAmount;
		[AutoNotify] private ObservableCollection<string> _priorLabels;
		[AutoNotify] private ObservableCollection<string> _labels;
		[AutoNotify] private bool _isPayJoin;
		[AutoNotify] private string? _payJoinEndPoint;
		[AutoNotify] private WriteableBitmap? _qrImage;
		[AutoNotify] private bool _isQrPanelVisible;
		[AutoNotify] private bool _isCameraLoadingAnimationVisible;

		private WebcamQrReader _qrReader;
		private bool _parsingUrl;

		public SendViewModel(Wallet wallet)
		{
			_to = "";
			_wallet = wallet;
			_transactionInfo = new TransactionInfo();
			_labels = new ObservableCollection<string>();
			_isQrPanelVisible = false;
			_isCameraLoadingAnimationVisible = false;
			_qrReader = new(_wallet.Network);
			ExchangeRate = _wallet.Synchronizer.UsdExchangeRate;
			PriorLabels = new();

			Observable.FromEventPattern<WriteableBitmap>(_qrReader, nameof(_qrReader.NewImageArrived))
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(args =>
				{
					if (IsCameraLoadingAnimationVisible == true)
					{
						IsCameraLoadingAnimationVisible = false;
					}
					QrImage = args.EventArgs;
				});

			Observable.FromEventPattern<string>(_qrReader, nameof(_qrReader.CorrectAddressFound))
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(async args =>
				{
					To = args.EventArgs;
					await _qrReader.StopScanningAsync();
					IsQrPanelVisible = false;
				});

			Observable.FromEventPattern<string>(_qrReader, nameof(_qrReader.InvalidAddressFound))
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(args => To = args.EventArgs);

			Observable.FromEventPattern<Exception>(_qrReader, nameof(_qrReader.ErrorOccured))
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(async args =>
			   {
				   IsCameraLoadingAnimationVisible = false;
				   IsQrPanelVisible = false;
				   await _qrReader.StopScanningAsync();
				   await ShowErrorAsync(Title, args.EventArgs.Message, "Something went wrong");
			   });

			this.ValidateProperty(x => x.To, ValidateToField);
			this.ValidateProperty(x => x.AmountBtc, ValidateAmount);

			this.WhenAnyValue(x => x.To)
				.Subscribe(ParseToField);

			this.WhenAnyValue(x => x.AmountBtc)
				.Subscribe(x => _transactionInfo.Amount = new Money(x, MoneyUnit.BTC));

			this.WhenAnyValue(x => x.PayJoinEndPoint)
				.Subscribe(endPoint =>
				{
					if (endPoint is { })
					{
						_transactionInfo.PayJoinClient = GetPayjoinClient(endPoint);
						IsPayJoin = true;
					}
					else
					{
						IsPayJoin = false;
					}
				});

			Labels.ToObservableChangeSet().Subscribe(x => _transactionInfo.UserLabels = new SmartLabel(_labels.ToArray()));

			SetupCancel(enableCancel: false, enableCancelOnEscape: true, enableCancelOnPressed: false);
			EnableBack = true;

			PasteCommand = ReactiveCommand.CreateFromTask(async () => await OnPasteAsync());
			AutoPasteCommand = ReactiveCommand.CreateFromTask(async () => await OnAutoPasteAsync());
			QRCommand = ReactiveCommand.Create(async () =>
			{
				if (!_qrReader.IsRunning)
				{
					IsCameraLoadingAnimationVisible = true;
					IsQrPanelVisible = true;
					await _qrReader.StartScanningAsync();
				}
			});

			var nextCommandCanExecute =
				this.WhenAnyValue(x => x.Labels, x => x.AmountBtc, x => x.To).Select(_ => Unit.Default)
					.Merge(Observable.FromEventPattern(Labels, nameof(Labels.CollectionChanged)).Select(_ => Unit.Default))
					.Select(_ =>
					{
						var allFilled = !string.IsNullOrEmpty(To) && AmountBtc > 0 && Labels.Any();
						var hasError = Validations.Any;

						return allFilled && !hasError;
					});

			NextCommand = ReactiveCommand.Create(() =>
			{
				Navigate().To(new SendFeeViewModel(_wallet, _transactionInfo));
			}, nextCommandCanExecute);

			EnableAutoBusyOn(NextCommand);
		}

		public ICommand PasteCommand { get; }

		public ICommand AutoPasteCommand { get; }

		public ICommand QRCommand { get; }

		private async Task OnAutoPasteAsync()
		{
			var isAutoPasteEnabled = Services.UiConfig.Autocopy;

			if (string.IsNullOrEmpty(To) && isAutoPasteEnabled)
			{
				await OnPasteAsync(pasteIfInvalid: false);
			}
		}

		private async Task OnPasteAsync(bool pasteIfInvalid = true)
		{
			var text = await Application.Current.Clipboard.GetTextAsync();

			_parsingUrl = true;

			if (!TryParseUrl(text) && pasteIfInvalid)
			{
				To = text;
			}

			_parsingUrl = false;
		}

		private IPayjoinClient? GetPayjoinClient(string endPoint)
		{
			if (!string.IsNullOrWhiteSpace(endPoint) &&
				Uri.IsWellFormedUriString(endPoint, UriKind.Absolute))
			{
				var payjoinEndPointUri = new Uri(endPoint);
				if (!Services.Config.UseTor)
				{
					if (payjoinEndPointUri.DnsSafeHost.EndsWith(".onion", StringComparison.OrdinalIgnoreCase))
					{
						Logger.LogWarning("PayJoin server is an onion service but Tor is disabled. Ignoring...");
						return null;
					}

					if (Services.Config.Network == Network.Main && payjoinEndPointUri.Scheme != Uri.UriSchemeHttps)
					{
						Logger.LogWarning("PayJoin server is not exposed as an onion service nor https. Ignoring...");
						return null;
					}
				}

				IHttpClient httpClient =
					Services.ExternalHttpClientFactory.NewHttpClient(() => payjoinEndPointUri, Mode.DefaultCircuit);
				return new PayjoinClient(payjoinEndPointUri, httpClient);
			}

			return null;
		}

		private void ValidateAmount(IValidationErrors errors)
		{
			if (AmountBtc > Constants.MaximumNumberOfBitcoins)
			{
				errors.Add(ErrorSeverity.Error, "Amount must be less than the total supply of BTC.");
			}
			else if (AmountBtc > _wallet.Coins.TotalAmount().ToDecimal(MoneyUnit.BTC))
			{
				errors.Add(ErrorSeverity.Error, "Insufficient funds to cover the amount requested.");
			}
			else if (AmountBtc <= 0)
			{
				errors.Add(ErrorSeverity.Error, "Amount must be more than 0 BTC");
			}
		}

		private void ValidateToField(IValidationErrors errors)
		{
			if (!string.IsNullOrWhiteSpace(To) &&
				!AddressStringParser.TryParse(To, _wallet.Network, out _))
			{
				errors.Add(ErrorSeverity.Error, "Input a valid BTC address or URL.");
			}
			else if (IsPayJoin && _wallet.KeyManager.IsHardwareWallet)
			{
				errors.Add(ErrorSeverity.Error, "PayJoin is not possible with hardware wallets.");
			}
		}

		private void ParseToField(string s)
		{
			if (_parsingUrl)
			{
				return;
			}

			_parsingUrl = true;

			Dispatcher.UIThread.Post(() =>
			{
				TryParseUrl(s);

				_parsingUrl = false;
			});
		}

		private bool TryParseUrl(string text)
		{
			bool result = false;

			if (AddressStringParser.TryParse(text, _wallet.Network, out BitcoinUrlBuilder? url))
			{
				result = true;
				SmartLabel label = url.Label;

				if (!label.IsEmpty)
				{
					Labels.Clear();

					foreach (var labelString in label.Labels)
					{
						Labels.Add(labelString);
					}
				}

				if (url.UnknowParameters.TryGetValue("pj", out var endPoint))
				{
					PayJoinEndPoint = endPoint;
				}
				else
				{
					PayJoinEndPoint = null;
				}

				if (url.Address is { })
				{
					_transactionInfo.Address = url.Address;
					To = url.Address.ToString();
				}

				if (url.Amount is { })
				{
					AmountBtc = url.Amount.ToDecimal(MoneyUnit.BTC);
					IsFixedAmount = true;
				}
				else
				{
					IsFixedAmount = false;
				}
			}
			else
			{
				IsFixedAmount = false;
				PayJoinEndPoint = null;
			}

			return result;
		}

		protected override void OnNavigatedTo(bool inHistory, CompositeDisposable disposables)
		{
			if (!inHistory)
			{
				To = "";
				AmountBtc = 0;
				Labels.Clear();
				ClearValidations();
			}

			_wallet.Synchronizer.WhenAnyValue(x => x.UsdExchangeRate)
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(x => ExchangeRate = x)
				.DisposeWith(disposables);

			_wallet.TransactionProcessor.WhenAnyValue(x => x.Coins).Select(_ => Unit.Default)
				.Merge(this.WhenAnyValue(x => x.Labels.Count).Select(_ => Unit.Default))
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(_ => UpdateSuggestedLabels())
				.DisposeWith(disposables);

			RxApp.MainThreadScheduler.Schedule(async () => await OnAutoPasteAsync());

			base.OnNavigatedTo(inHistory, disposables);
		}

		protected override void OnNavigatedFrom(bool isInHistory)
		{
			try
			{
				RxApp.MainThreadScheduler.Schedule(async () => await _qrReader.StopScanningAsync());
				base.OnNavigatedFrom(isInHistory);
			}
			catch (Exception exc)
			{
				Logger.LogError(exc);
			}
		}

		private void UpdateSuggestedLabels()
		{
			var enteredLabels = Labels;
			var allLabels = WalletHelpers.GetLabels();
			var newSuggestedLabels = allLabels.Except(enteredLabels).Distinct();

			PriorLabels = new ObservableCollection<string>(newSuggestedLabels);
		}
	}
}
