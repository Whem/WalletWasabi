using System.Collections.Generic;
using NBitcoin;
using System.Linq;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Wallets;
using System.Diagnostics.CodeAnalysis;

namespace WalletWasabi.WabiSabi.Client;

public class KeyChain : BaseKeyChain
{
	public KeyChain(KeyManager keyManager, Kitchen kitchen) : base(kitchen)
	{
		if (keyManager.IsWatchOnly)
		{
			throw new ArgumentException("A watch-only keymanager cannot be used to initialize a keychain.");
		}
		KeyManager = keyManager;
	}

	private KeyManager KeyManager { get; }

	protected override Key GetMasterKey()
	{
		return KeyManager.GetMasterExtKey(Kitchen.SaltSoup()).PrivateKey;
	}

	public override void TrySetScriptStates(KeyState state, IEnumerable<Script> scripts)
	{
		foreach (var hdPubKey in KeyManager.GetKeys(key => scripts.Any(key.ContainsScript)))
		{
			KeyManager.SetKeyState(state, hdPubKey);
		}
	}

	protected override BitcoinSecret GetBitcoinSecret(Script scriptPubKey)
	{
		var hdKey = KeyManager.GetSecrets(Kitchen.SaltSoup(), scriptPubKey).Single();
		if (hdKey is null)
		{
			throw new InvalidOperationException($"The signing key for '{scriptPubKey}' was not found.");
		}

		var derivedScriptPubKeyType = scriptPubKey switch
		{
			_ when scriptPubKey.IsScriptType(ScriptType.P2WPKH) => ScriptPubKeyType.Segwit,
			_ when scriptPubKey.IsScriptType(ScriptType.Taproot) => ScriptPubKeyType.TaprootBIP86,
			_ => throw new NotSupportedException("Not supported script type.")
		};

		if (hdKey.PrivateKey.PubKey.GetScriptPubKey(derivedScriptPubKeyType) != scriptPubKey)
		{
			throw new InvalidOperationException("The key cannot generate the utxo scriptpubkey. This could happen if the wallet password is not the correct one.");
		}
		var secret = hdKey.PrivateKey.GetBitcoinSecret(KeyManager.GetNetwork());
		return secret;
	}
}
