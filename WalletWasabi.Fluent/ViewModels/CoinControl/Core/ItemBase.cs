using System.Collections.Generic;
using NBitcoin;
using WalletWasabi.Blockchain.Analysis.Clustering;
using WalletWasabi.Fluent.Helpers;

namespace WalletWasabi.Fluent.ViewModels.CoinControl.Core;

public abstract class ItemBase
{
	protected ItemBase() : this(new List<ItemBase>()) { }

	protected ItemBase(IReadOnlyCollection<ItemBase> children)
	{
		Children = children;
	}

	public bool IsPrivate => Labels == CoinPocketHelper.PrivateFundsText;

	public bool IsSemiPrivate => Labels == CoinPocketHelper.SemiPrivateFundsText;

	public bool IsNonPrivate => !IsSemiPrivate && !IsPrivate;

	public IReadOnlyCollection<ItemBase> Children { get; }

	public abstract bool IsConfirmed { get; }

	public abstract bool IsCoinjoining { get; }

	public abstract bool IsBanned { get; }

	public abstract string ConfirmationStatus { get; }

	public abstract Money Amount { get; }

	public abstract string BannedUntilUtcToolTip { get; }

	public abstract int? AnonymityScore { get; }

	public bool IsExpanded { get; set; } = true;

	public abstract SmartLabel Labels { get; }

	public abstract DateTimeOffset? BannedUntilUtc { get; }
}
