namespace StockSharp.Algo.Statistics;

/// <summary>
/// The interface, describing statistic parameter, calculated based on the profit-loss value (maximal contraction, Sharp coefficient etc.).
/// </summary>
public interface IPnLStatisticParameter : IStatisticParameter
{
	/// <summary>
	/// To add new data to the parameter.
	/// </summary>
	/// <param name="marketTime">The exchange time.</param>
	/// <param name="pnl">The profit-loss value.</param>
	/// <param name="commission">Commission.</param>
	void Add(DateTimeOffset marketTime, decimal pnl, decimal? commission);
}

/// <summary>
/// The base profit-loss statistics parameter.
/// </summary>
/// <typeparam name="TValue">The type of the parameter value.</typeparam>
/// <remarks>
/// Initialize <see cref="BasePnLStatisticParameter{TValue}"/>.
/// </remarks>
/// <param name="type"><see cref="IStatisticParameter.Type"/></param>
public abstract class BasePnLStatisticParameter<TValue>(StatisticParameterTypes type) : BaseStatisticParameter<TValue>(type), IPnLStatisticParameter
	where TValue : IComparable<TValue>
{
	/// <inheritdoc />
	public virtual void Add(DateTimeOffset marketTime, decimal pnl, decimal? commission)
		=> throw new NotSupportedException();
}

/// <summary>
/// Net profit for whole time period.
/// </summary>
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.NetProfitKey,
	Description = LocalizedStrings.NetProfitWholeTimeKey,
	GroupName = LocalizedStrings.PnLKey,
	Order = 0
)]
public class NetProfitParameter : BasePnLStatisticParameter<decimal>
{
	/// <summary>
	/// Initialize <see cref="NetProfitParameter"/>.
	/// </summary>
	public NetProfitParameter()
		: base(StatisticParameterTypes.NetProfit)
	{
	}

	/// <inheritdoc />
	public override void Add(DateTimeOffset marketTime, decimal pnl, decimal? commission)
	{
		Value = pnl;
	}
}

/// <summary>
/// Net profit for whole time period in percent.
/// </summary>
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.NetProfitPercentKey,
	Description = LocalizedStrings.NetProfitPercentDescKey,
	GroupName = LocalizedStrings.PnLKey,
	Order = 1
)]
public class NetProfitPercentParameter : BasePnLStatisticParameter<decimal>
{
	private decimal _beginValue;

	/// <summary>
	/// Initialize <see cref="NetProfitPercentParameter"/>.
	/// </summary>
	public NetProfitPercentParameter()
		: base(StatisticParameterTypes.NetProfitPercent)
	{
	}

	/// <inheritdoc />
	public override void Init(decimal beginValue)
	{
		base.Init(beginValue);

		_beginValue = beginValue;
	}

	/// <inheritdoc />
	public override void Add(DateTimeOffset marketTime, decimal pnl, decimal? commission)
	{
		if (_beginValue == 0)
			return;

		Value = pnl * 100m / _beginValue;
	}
}

/// <summary>
/// The maximal profit value for the entire period.
/// </summary>
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.MaxProfitKey,
	Description = LocalizedStrings.MaxProfitWholePeriodKey,
	GroupName = LocalizedStrings.PnLKey,
	Order = 2
)]
public class MaxProfitParameter : BasePnLStatisticParameter<decimal>
{
	/// <summary>
	/// Initialize <see cref="MaxProfitParameter"/>.
	/// </summary>
	public MaxProfitParameter()
		: base(StatisticParameterTypes.MaxProfit)
	{
	}

	/// <inheritdoc />
	public override void Add(DateTimeOffset marketTime, decimal pnl, decimal? commission)
	{
		Value = Math.Max(Value, pnl);
	}
}

/// <summary>
/// Date of maximum profit value for the entire period.
/// </summary>
/// <remarks>
/// Initialize <see cref="MaxProfitDateParameter"/>.
/// </remarks>
/// <param name="underlying"><see cref="MaxProfitParameter"/></param>
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.MaxProfitDateKey,
	Description = LocalizedStrings.MaxProfitDateDescKey,
	GroupName = LocalizedStrings.PnLKey,
	Order = 3
)]
public class MaxProfitDateParameter(MaxProfitParameter underlying) : BasePnLStatisticParameter<DateTimeOffset>(StatisticParameterTypes.MaxProfitDate)
{
	private readonly MaxProfitParameter _underlying = underlying ?? throw new ArgumentNullException(nameof(underlying));
	private decimal _prevValue;

	/// <inheritdoc />
	public override void Reset()
	{
		_prevValue = default;
		base.Reset();
	}

	/// <inheritdoc />
	public override void Add(DateTimeOffset marketTime, decimal pnl, decimal? commission)
	{
		if (_prevValue < _underlying.Value)
		{
			_prevValue = _underlying.Value;
			Value = marketTime;
		}
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		storage.SetValue("PrevValue", _prevValue);
		base.Save(storage);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		_prevValue = storage.GetValue<decimal>("PrevValue");
		base.Load(storage);
	}
}

/// <summary>
/// Maximum absolute drawdown during the whole period.
/// </summary>
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.MaxDrawdownKey,
	Description = LocalizedStrings.MaxDrawdownDescKey,
	GroupName = LocalizedStrings.PnLKey,
	Order = 4
)]
public class MaxDrawdownParameter : BasePnLStatisticParameter<decimal>
{
	/// <summary>
	/// Initialize <see cref="MaxDrawdownParameter"/>.
	/// </summary>
	public MaxDrawdownParameter()
		: base(StatisticParameterTypes.MaxDrawdown)
	{
	}

	private decimal _maxEquity = decimal.MinValue;

	/// <inheritdoc />
	public override void Reset()
	{
		_maxEquity = decimal.MinValue;
		base.Reset();
	}

	/// <inheritdoc />
	public override void Add(DateTimeOffset marketTime, decimal pnl, decimal? commission)
	{
		_maxEquity = Math.Max(_maxEquity, pnl);
		Value = Math.Max(Value, _maxEquity - pnl);
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		storage.SetValue("MaxEquity", _maxEquity);
		base.Save(storage);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		_maxEquity = storage.GetValue<decimal>("MaxEquity");
		base.Load(storage);
	}
}

/// <summary>
/// Maximum absolute drawdown during the whole period in percent.
/// </summary>
/// <remarks>
/// Initialize <see cref="MaxDrawdownPercentParameter"/>.
/// </remarks>
/// <param name="underlying"><see cref="MaxDrawdownParameter"/></param>
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.MaxDrawdownPercentKey,
	Description = LocalizedStrings.MaxDrawdownPercentKey,
	GroupName = LocalizedStrings.PnLKey,
	Order = 5
)]
public class MaxDrawdownPercentParameter(MaxDrawdownParameter underlying) : BasePnLStatisticParameter<decimal>(StatisticParameterTypes.MaxDrawdownPercent)
{
	private readonly MaxDrawdownParameter _underlying = underlying ?? throw new ArgumentNullException(nameof(underlying));
	private decimal _beginValue;

	/// <inheritdoc />
	public override void Init(decimal beginValue)
	{
		base.Init(beginValue);

		_beginValue = beginValue;
	}

	/// <inheritdoc />
	public override void Add(DateTimeOffset marketTime, decimal pnl, decimal? commission)
	{
		if (pnl == 0 || _beginValue == 0)
			return;

		Value = _underlying.Value * 100m / _beginValue;
	}
}

/// <summary>
/// Date of maximum absolute drawdown during the whole period.
/// </summary>
/// <remarks>
/// Initialize <see cref="MaxDrawdownDateParameter"/>.
/// </remarks>
/// <param name="underlying"><see cref="MaxDrawdownParameter"/></param>
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.MaxDrawdownDateKey,
	Description = LocalizedStrings.MaxDrawdownDateDescKey,
	GroupName = LocalizedStrings.PnLKey,
	Order = 6
)]
public class MaxDrawdownDateParameter(MaxDrawdownParameter underlying) : BasePnLStatisticParameter<DateTimeOffset>(StatisticParameterTypes.MaxDrawdownDate)
{
	private readonly MaxDrawdownParameter _underlying = underlying ?? throw new ArgumentNullException(nameof(underlying));
	private decimal _prevValue;

	/// <inheritdoc />
	public override void Reset()
	{
		_prevValue = default;
		base.Reset();
	}

	/// <inheritdoc />
	public override void Add(DateTimeOffset marketTime, decimal pnl, decimal? commission)
	{
		if (_prevValue < _underlying.Value)
		{
			_prevValue = _underlying.Value;
			Value = marketTime;
		}
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		storage.SetValue("PrevValue", _prevValue);
		base.Save(storage);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		_prevValue = storage.GetValue<decimal>("PrevValue");
		base.Load(storage);
	}
}

/// <summary>
/// Maximum relative equity drawdown during the whole period.
/// </summary>
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.RelativeDrawdownKey,
	Description = LocalizedStrings.MaxRelativeDrawdownKey,
	GroupName = LocalizedStrings.PnLKey,
	Order = 7
)]
public class MaxRelativeDrawdownParameter : BasePnLStatisticParameter<decimal>
{
	/// <summary>
	/// Initialize <see cref="MaxRelativeDrawdownParameter"/>.
	/// </summary>
	public MaxRelativeDrawdownParameter()
		: base(StatisticParameterTypes.MaxRelativeDrawdown)
	{
	}

	private decimal _maxEquity = decimal.MinValue;

	/// <inheritdoc />
	public override void Reset()
	{
		_maxEquity = decimal.MinValue;
		base.Reset();
	}

	/// <inheritdoc />
	public override void Add(DateTimeOffset marketTime, decimal pnl, decimal? commission)
	{
		_maxEquity = Math.Max(_maxEquity, pnl);

		var drawdown = _maxEquity - pnl;
		Value = Math.Max(Value, _maxEquity != 0 ? drawdown / _maxEquity : 0);
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		storage.SetValue("MaxEquity", _maxEquity);
		base.Save(storage);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		_maxEquity = storage.GetValue<decimal>("MaxEquity");
		base.Load(storage);
	}
}

/// <summary>
/// Relative income for the whole time period.
/// </summary>
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.RelativeIncomeKey,
	Description = LocalizedStrings.RelativeIncomeWholePeriodKey,
	GroupName = LocalizedStrings.PnLKey,
	Order = 8
)]
public class ReturnParameter : BasePnLStatisticParameter<decimal>
{
	/// <summary>
	/// Initialize <see cref="ReturnParameter"/>.
	/// </summary>
	public ReturnParameter()
		: base(StatisticParameterTypes.Return)
	{
	}

	private decimal _minEquity = decimal.MaxValue;

	/// <inheritdoc />
	public override void Reset()
	{
		_minEquity = decimal.MaxValue;
		base.Reset();
	}

	/// <inheritdoc />
	public override void Add(DateTimeOffset marketTime, decimal pnl, decimal? commission)
	{
		_minEquity = Math.Min(_minEquity, pnl);

		var profit = pnl - _minEquity;
		Value = Math.Max(Value, _minEquity != 0 ? profit / _minEquity : 0);
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		storage.SetValue("MinEquity", _minEquity);
		base.Save(storage);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		_minEquity = storage.GetValue<decimal>("MinEquity");
		base.Load(storage);
	}
}

/// <summary>
/// Recovery factor (net profit / maximum drawdown).
/// </summary>
/// <remarks>
/// Initialize <see cref="RecoveryFactorParameter"/>.
/// </remarks>
/// <param name="maxDrawdown"><see cref="MaxDrawdownParameter"/></param>
/// <param name="netProfit"><see cref="NetProfitParameter"/></param>
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.RecoveryFactorKey,
	Description = LocalizedStrings.RecoveryFactorDescKey,
	GroupName = LocalizedStrings.PnLKey,
	Order = 9
)]
public class RecoveryFactorParameter(MaxDrawdownParameter maxDrawdown, NetProfitParameter netProfit) : BasePnLStatisticParameter<decimal>(StatisticParameterTypes.RecoveryFactor)
{
	private readonly MaxDrawdownParameter _maxDrawdown = maxDrawdown ?? throw new ArgumentNullException(nameof(maxDrawdown));
	private readonly NetProfitParameter _netProfit = netProfit ?? throw new ArgumentNullException(nameof(netProfit));

	/// <inheritdoc />
	public override void Add(DateTimeOffset marketTime, decimal pnl, decimal? commission)
	{
		if (_maxDrawdown.Value != 0)
			Value = _netProfit.Value / _maxDrawdown.Value;
	}
}

/// <summary>
/// Total commission.
/// </summary>
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.CommissionKey,
	Description = LocalizedStrings.TotalCommissionDescKey,
	GroupName = LocalizedStrings.PnLKey,
	Order = 10
)]
public class CommissionParameter : BasePnLStatisticParameter<decimal>
{
	/// <summary>
	/// Initialize <see cref="CommissionParameter"/>.
	/// </summary>
	public CommissionParameter()
		: base(StatisticParameterTypes.Commission)
	{
	}

	/// <inheritdoc />
	public override void Add(DateTimeOffset marketTime, decimal pnl, decimal? commission)
	{
		if (commission is not null)
			Value = commission.Value;
	}
}