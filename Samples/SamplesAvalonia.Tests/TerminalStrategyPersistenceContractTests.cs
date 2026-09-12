namespace StockSharp.Samples.Tests;

using System;
using System.Collections.Generic;
using System.Linq;

using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Algo.Strategies;
using StockSharp.BusinessEntities;
using StockSharp.Samples.Strategies.LiveTerminal;

[TestClass]
[DoNotParallelize]
public class TerminalStrategyPersistenceContractTests : BaseTestClass
{
	public sealed class PersistedStrategy : Strategy
	{
		public static PersistedStrategy LastCreated { get; set; }

		public PersistedStrategy()
		{
			HedgeSecurity = Param<Security>(nameof(HedgeSecurity));
			HedgePortfolio = Param<Portfolio>(nameof(HedgePortfolio));
			Threshold = Param(nameof(Threshold), 12m);
			LastCreated = this;
		}

		public StrategyParam<Security> HedgeSecurity { get; }
		public StrategyParam<Portfolio> HedgePortfolio { get; }
		public StrategyParam<decimal> Threshold { get; }
		public int ReleaseCount { get; private set; }

		public override void Load(SettingsStorage storage)
		{
			base.Load(storage);
			if (storage.GetValue("IsLoadFailureRequested", false))
				throw new InvalidOperationException("Strategy settings could not be restored.");
		}

		protected override void DisposeManaged()
		{
			ReleaseCount++;
			base.DisposeManaged();
		}
	}

	[TestCleanup]
	public void ReleaseUnreturnedStrategy()
	{
		PersistedStrategy.LastCreated?.Dispose();
		PersistedStrategy.LastCreated = null;
	}

	[TestMethod]
	[DataRow(0, "envelope")]
	[DataRow(1, "resolveSecurity")]
	[DataRow(2, "resolvePortfolio")]
	[Timeout(10_000)]
	public void MissingArgumentIsRejectedBeforeAStrategyIsCreated(int missingIndex, string parameterName)
	{
		using var source = new PersistedStrategy();
		var envelope = source.SaveEntire(false);
		PersistedStrategy.LastCreated = null;
		Func<string, Security> resolveSecurity = _ => throw new InvalidOperationException("Resolver must not run.");
		Func<string, Portfolio> resolvePortfolio = _ => throw new InvalidOperationException("Resolver must not run.");

		var error = ThrowsExactly<ArgumentNullException>(() => TerminalStrategyPersistence.Load(
			missingIndex == 0 ? null : envelope,
			missingIndex == 1 ? null : resolveSecurity,
			missingIndex == 2 ? null : resolvePortfolio));

		AreEqual(parameterName, error.ParamName);
		IsNull(PersistedStrategy.LastCreated);
		AreEqual(0, source.ReleaseCount);
	}

	[TestMethod]
	[Timeout(10_000)]
	public void EmptyEntityParametersDoNotInvokeResolvers()
	{
		using var source = new PersistedStrategy();
		var securityCalls = 0;
		var portfolioCalls = 0;

		using var restored = TerminalStrategyPersistence.Load(source.SaveEntire(false), _ =>
		{
			securityCalls++;
			return null;
		}, _ =>
		{
			portfolioCalls++;
			return null;
		});

		IsNull(restored.Security);
		IsNull(restored.Portfolio);
		AreEqual(0, securityCalls);
		AreEqual(0, portfolioCalls);
		AreEqual(0, ((PersistedStrategy)restored).ReleaseCount);
	}

	[TestMethod]
	[Timeout(10_000)]
	public void CustomEntityParametersUseLocalObjectsAndScalarParametersSurvive()
	{
		using var source = new PersistedStrategy
		{
			Security = new Security { Id = "MAIN@TEST" },
			Portfolio = new Portfolio { Name = "PRIMARY" },
			Name = "Persisted strategy",
		};
		source.HedgeSecurity.Value = new Security { Id = "HEDGE@TEST" };
		source.HedgePortfolio.Value = new Portfolio { Name = "HEDGE" };
		source.Threshold.Value = 37.5m;
		var envelope = source.SaveEntire(false);
		var securities = new Dictionary<string, Security>
		{
			["MAIN@TEST"] = new() { Id = "MAIN@TEST" },
			["HEDGE@TEST"] = new() { Id = "HEDGE@TEST" },
		};
		var portfolios = new Dictionary<string, Portfolio>
		{
			["PRIMARY"] = new() { Name = "PRIMARY" },
			["HEDGE"] = new() { Name = "HEDGE" },
		};
		var securityCalls = new List<string>();
		var portfolioCalls = new List<string>();

		using var restored = (PersistedStrategy)TerminalStrategyPersistence.Load(envelope, id =>
		{
			securityCalls.Add(id);
			return securities[id];
		}, name =>
		{
			portfolioCalls.Add(name);
			return portfolios[name];
		});

		AreSame(securities["MAIN@TEST"], restored.Security);
		AreSame(securities["HEDGE@TEST"], restored.HedgeSecurity.Value);
		AreSame(portfolios["PRIMARY"], restored.Portfolio);
		AreSame(portfolios["HEDGE"], restored.HedgePortfolio.Value);
		AreEqual(new[] { "HEDGE@TEST", "MAIN@TEST" }, securityCalls.OrderBy(id => id).ToArray());
		AreEqual(new[] { "HEDGE", "PRIMARY" }, portfolioCalls.OrderBy(name => name).ToArray());
		AreEqual(37.5m, restored.Threshold.Value);
		AreEqual(source.Name, restored.Name);
		AreEqual(0, restored.ReleaseCount);
		AreEqual(0, source.ReleaseCount);
		var savedParameters = envelope.GetValue<SettingsStorage>("settings")
			.GetValue<SettingsStorage[]>(nameof(Strategy.Parameters));
		AreEqual("HEDGE@TEST", savedParameters.Single(parameter =>
			parameter.GetValue<string>(nameof(IStrategyParam.Id)) == nameof(PersistedStrategy.HedgeSecurity))
			.GetValue<string>(nameof(IStrategyParam.Value)));
	}

	[TestMethod]
	[DataRow(false)]
	[DataRow(true)]
	[Timeout(10_000)]
	public void UnavailableLocalEntityDisposesOnlyTheUnreturnedStrategy(bool isPortfolio)
	{
		using var source = new PersistedStrategy
		{
			Security = isPortfolio ? null : new Security { Id = "MISSING@TEST" },
			Portfolio = isPortfolio ? new Portfolio { Name = "MISSING" } : null,
		};
		var envelope = source.SaveEntire(false);

		ThrowsExactly<InvalidOperationException>(() => TerminalStrategyPersistence.Load(envelope, _ => null, _ => null));

		IsNotNull(PersistedStrategy.LastCreated);
		IsFalse(ReferenceEquals(source, PersistedStrategy.LastCreated));
		AreEqual(1, PersistedStrategy.LastCreated.ReleaseCount);
		AreEqual(0, source.ReleaseCount);
	}

	[TestMethod]
	[DataRow(false)]
	[DataRow(true)]
	[Timeout(10_000)]
	public void ResolverExceptionIsPropagatedAndTheUnreturnedStrategyIsDisposed(bool isPortfolio)
	{
		using var source = new PersistedStrategy
		{
			Security = isPortfolio ? null : new Security { Id = "MAIN@TEST" },
			Portfolio = isPortfolio ? new Portfolio { Name = "PRIMARY" } : null,
		};
		var expectedError = new InvalidOperationException("Local entity lookup failed.");

		var error = ThrowsExactly<InvalidOperationException>(() => TerminalStrategyPersistence.Load(
			source.SaveEntire(false), _ => throw expectedError, _ => throw expectedError));

		AreSame(expectedError, error);
		IsNotNull(PersistedStrategy.LastCreated);
		IsFalse(ReferenceEquals(source, PersistedStrategy.LastCreated));
		AreEqual(1, PersistedStrategy.LastCreated.ReleaseCount);
		AreEqual(0, source.ReleaseCount);
	}

	[TestMethod]
	[Timeout(10_000)]
	public void UnknownSavedParameterDoesNotInvokeEntityResolvers()
	{
		using var source = new PersistedStrategy();
		var envelope = source.SaveEntire(false);
		var settings = envelope.GetValue<SettingsStorage>("settings");
		var parameters = settings.GetValue<SettingsStorage[]>(nameof(Strategy.Parameters));
		settings.Set(nameof(Strategy.Parameters), parameters.Append(new SettingsStorage()
			.Set(nameof(IStrategyParam.Id), "RemovedSecurityParameter")
			.Set(nameof(IStrategyParam.Value), "UNKNOWN@TEST")).ToArray());
		var calls = 0;

		using var restored = TerminalStrategyPersistence.Load(envelope, _ =>
		{
			calls++;
			return null;
		}, _ =>
		{
			calls++;
			return null;
		});

		AreEqual(0, calls);
		IsFalse(restored.Parameters.ContainsKey("RemovedSecurityParameter"));
		AreEqual(12m, ((PersistedStrategy)restored).Threshold.Value);
	}

	[TestMethod]
	[Timeout(10_000)]
	public void MissingSavedParametersPreserveConstructorDefaultsWithoutEntityLookup()
	{
		using var source = new PersistedStrategy();
		var envelope = source.SaveEntire(false);
		envelope.GetValue<SettingsStorage>("settings").Remove(nameof(Strategy.Parameters));
		var calls = 0;

		using var restored = TerminalStrategyPersistence.Load(envelope, _ =>
		{
			calls++;
			return null;
		}, _ =>
		{
			calls++;
			return null;
		});

		AreEqual(0, calls);
		AreEqual(12m, ((PersistedStrategy)restored).Threshold.Value);
		IsNull(restored.Security);
		IsNull(restored.Portfolio);
	}

	[TestMethod]
	[Timeout(10_000)]
	public void SettingsLoadFailureDisposesTheCreatedButUnreturnedStrategy()
	{
		using var source = new PersistedStrategy();
		var envelope = source.SaveEntire(false);
		envelope.GetValue<SettingsStorage>("settings").Set("IsLoadFailureRequested", true);
		PersistedStrategy.LastCreated = null;

		ThrowsExactly<InvalidOperationException>(() => TerminalStrategyPersistence.Load(envelope, _ => null, _ => null));

		IsNotNull(PersistedStrategy.LastCreated);
		IsFalse(ReferenceEquals(source, PersistedStrategy.LastCreated));
		AreEqual(0, source.ReleaseCount);
		AreEqual(1, PersistedStrategy.LastCreated.ReleaseCount,
			"A failed load must not abandon the disposable instance created by the persistence factory.");
	}
}
