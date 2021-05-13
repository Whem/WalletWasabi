﻿using WalletWasabi.Blockchain.TransactionBroadcasting;
using WalletWasabi.Gui;
using WalletWasabi.Services;
using WalletWasabi.Stores;
using WalletWasabi.Tor;
using WalletWasabi.Wallets;
using WalletWasabi.WebClients.Wasabi;

namespace WalletWasabi.Fluent
{
	public static class Services
	{
		public static string DataDir { get; private set; } = null!;
		public static TorSettings TorSettings { get; private set; } = null!;
		public static BitcoinStore BitcoinStore { get; private set; } = null!;
		public static HttpClientFactory ExternalHttpClientFactory { get; private set; } = null!;
		public static LegalChecker LegalChecker { get; private set; } = null!;
		public static Config Config { get; private set; } = null!;
		public static WasabiSynchronizer Synchronizer { get; private set; } = null!;
		public static WalletManager WalletManager { get; private set; } = null!;
		public static TransactionBroadcaster TransactionBroadcaster { get; private set; } = null!;
		public static HostedServices HostedServices { get; private set; } = null!;
		public static UiConfig UiConfig { get; private set; } = null!;
		public static bool IsInitialized { get; private set; }

		/// <summary>
		/// Initializes global services used by fluent project.
		/// </summary>
		/// <param name="global">The global instance.</param>
		public static void Initialize(Global global)
		{
			DataDir = global.DataDir;
			TorSettings = global.TorSettings;
			BitcoinStore = global.BitcoinStore;
			ExternalHttpClientFactory = global.ExternalHttpClientFactory;
			LegalChecker = global.LegalChecker;
			Config = global.Config;
			Synchronizer = global.Synchronizer;
			WalletManager = global.WalletManager;
			TransactionBroadcaster = global.TransactionBroadcaster;
			HostedServices = global.HostedServices;
			UiConfig = global.UiConfig;
			IsInitialized = true;
		}
	}
}