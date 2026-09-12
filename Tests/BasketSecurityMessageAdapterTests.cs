namespace StockSharp.Tests;

/// <summary>
/// A basket security is one subscription to whoever asked for it and several to the venue. The
/// client knows only the transaction it sent, so every answer about the basket has to come back
/// under that number: a client told nothing about its own subscription waits on a stream it cannot
/// tell apart from one that is merely quiet.
/// </summary>
[TestClass]
public class BasketSecurityMessageAdapterTests : BaseTestClass
{
	private static readonly SecurityId _riu = new() { SecurityCode = "RIU8", BoardCode = "FORTS" };
	private static readonly SecurityId _riz = new() { SecurityCode = "RIZ8", BoardCode = "FORTS" };

	/// <summary>The venue underneath: it keeps what it was asked and answers only when told to.</summary>
	private sealed class LegAdapter : MessageAdapter
	{
		public LegAdapter()
			: base(new IncrementalIdGenerator())
		{
		}

		public List<Message> InMessages { get; } = [];

		/// <summary>The legs the basket was decomposed into, in the order they were asked for.</summary>
		public long[] LegSubscriptions => [.. InMessages.OfType<MarketDataMessage>().Where(m => m.IsSubscribe).Select(m => m.TransactionId)];

		protected override ValueTask OnSendInMessageAsync(Message message, CancellationToken cancellationToken)
		{
			InMessages.Add(message);
			return default;
		}

		/// <summary>Pushes a message up the pipeline, as the venue link does.</summary>
		public ValueTask AnswerAsync(Message message, CancellationToken cancellationToken)
			=> SendOutMessageAsync(message, cancellationToken);

		public override IMessageAdapter Clone() => new LegAdapter();
	}

	private static ExpirationContinuousSecurity Basket()
	{
		var basket = new ExpirationContinuousSecurity
		{
			Id = "RI@FORTS",
			Board = ExchangeBoard.Forts,
		};

		basket.ExpirationJumps.Add(_riu, new DateTime(2024, 9, 15, 0, 0, 0, DateTimeKind.Utc));
		basket.ExpirationJumps.Add(_riz, new DateTime(2024, 12, 15, 0, 0, 0, DateTimeKind.Utc));

		return basket;
	}

	private static (BasketSecurityMessageAdapter adapter, LegAdapter inner, List<Message> output) CreateSut(Security basket)
	{
		var securities = new CollectionSecurityProvider([basket]);
		var inner = new LegAdapter();

		var adapter = new BasketSecurityMessageAdapter(
			inner,
			securities,
			new BasketSecurityProcessorProvider(),
			new InMemoryExchangeInfoProvider());

		var output = new List<Message>();
		adapter.NewOutMessageAsync += (m, _) => { output.Add(m); return default; };

		return (adapter, inner, output);
	}

	private static MarketDataMessage SubscribeTo(Security basket, long transactionId)
		=> new()
		{
			TransactionId = transactionId,
			SecurityId = basket.ToSecurityId(),
			DataType2 = DataType.Ticks,
			IsSubscribe = true,
		};

	/// <summary>
	/// A user is entitled to be told their basket subscription is live once every leg behind it is.
	/// Told only about the legs - under transactions they never issued - they have no way to know
	/// their own subscription has started, and a client that waits for online before trading waits
	/// for good.
	/// </summary>
	[TestMethod]
	[Timeout(10_000, CooperativeCancellation = true)]
	public async Task ParentSubscription_GoesOnlineWhenEveryLegIsOnline()
	{
		const long parentTx = 4001;

		var basket = Basket();
		var (adapter, inner, output) = CreateSut(basket);

		await adapter.SendInMessageAsync(SubscribeTo(basket, parentTx), CancellationToken);

		var legs = inner.LegSubscriptions;

		legs.Length.AssertEqual(2, "the basket is asked of the venue one leg at a time");

		foreach (var leg in legs)
			await inner.AnswerAsync(new SubscriptionResponseMessage { OriginalTransactionId = leg }, CancellationToken);

		foreach (var leg in legs)
			await inner.AnswerAsync(new SubscriptionOnlineMessage { OriginalTransactionId = leg }, CancellationToken);

		IsTrue(output.OfType<SubscriptionOnlineMessage>().Any(m => m.OriginalTransactionId == parentTx),
			$"every leg is online and the client was never told its own subscription is: it was handed " +
			$"[{output.OfType<SubscriptionOnlineMessage>().Select(m => m.OriginalTransactionId.ToString()).JoinComma()}], " +
			$"which are the venue's leg transactions, not the one the client sent");
	}

	/// <summary>
	/// And the same when a leg cannot be served: a basket one of whose legs the venue refused is a
	/// basket that will never be complete, and the client is entitled to hear that under its own
	/// transaction rather than to keep waiting.
	/// </summary>
	[TestMethod]
	[Timeout(10_000, CooperativeCancellation = true)]
	public async Task ParentSubscription_IsToldWhenALegIsRefused()
	{
		const long parentTx = 4002;

		var basket = Basket();
		var (adapter, inner, output) = CreateSut(basket);

		await adapter.SendInMessageAsync(SubscribeTo(basket, parentTx), CancellationToken);

		var legs = inner.LegSubscriptions;

		legs.Length.AssertEqual(2, "the basket is asked of the venue one leg at a time");

		await inner.AnswerAsync(new SubscriptionResponseMessage { OriginalTransactionId = legs[0] }, CancellationToken);

		await inner.AnswerAsync(new SubscriptionResponseMessage
		{
			OriginalTransactionId = legs[1],
			Error = new InvalidOperationException("this contract is not served here"),
		}, CancellationToken);

		IsTrue(output.OfType<SubscriptionResponseMessage>().Any(m => m.OriginalTransactionId == parentTx && m.Error is not null),
			"a leg the venue refused leaves the basket incomplete, and the client was told nothing about it");
	}

	/// <summary>
	/// A copy of an adapter is a second adapter, and a second adapter needs a link of its own. Given
	/// the very object the original wraps, the two become two heads on one connection: every leg
	/// either of them asks the venue for is answered to both, and disposing or reconnecting one
	/// disturbs the other.
	/// </summary>
	[TestMethod]
	public void ACopyOfTheAdapterWrapsACopyOfTheLink()
	{
		var basket = Basket();
		var (adapter, inner, _) = CreateSut(basket);

		var clone = (BasketSecurityMessageAdapter)adapter.Clone();

		AreNotSame(inner, clone.InnerAdapter, "the copy has to have a link of its own, not the original's");
	}

	/// <summary>
	/// The other half of the same promise, and the one a client notices: work handed to the copy stays
	/// with the copy. A client subscribed through the original is entitled to receive what it asked
	/// for and nothing else - leg answers belonging to somebody else's basket arrive under transaction
	/// ids it never issued, and it has no way to tell them from its own.
	/// </summary>
	[TestMethod]
	[Timeout(10_000, CooperativeCancellation = true)]
	public async Task WorkGivenToTheCopyStaysWithTheCopy()
	{
		const long cloneTx = 4003;

		var basket = Basket();
		var (adapter, inner, output) = CreateSut(basket);

		var clone = (BasketSecurityMessageAdapter)adapter.Clone();

		await clone.SendInMessageAsync(SubscribeTo(basket, cloneTx), CancellationToken);

		foreach (var leg in inner.LegSubscriptions)
			await inner.AnswerAsync(new SubscriptionResponseMessage { OriginalTransactionId = leg }, CancellationToken);

		IsEmpty(output, "the original was asked for nothing, so its client must be handed nothing");
	}
}
