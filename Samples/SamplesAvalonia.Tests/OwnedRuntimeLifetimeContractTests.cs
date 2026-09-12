namespace StockSharp.Samples.Tests;

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Samples.Candles.CombineHistoryRealtime;

[TestClass]
public class OwnedRuntimeLifetimeContractTests : BaseTestClass
{
	private sealed class ReleaseProbe(Action release, Func<ValueTask> releaseAsync) : IDisposable, IAsyncDisposable
	{
		public void Dispose() => release();

		public ValueTask DisposeAsync() => releaseAsync();
	}

	private static OwnedRuntimeLifetime CreateLifetime(ReleaseProbe[] resources)
		=> new(resources[0], resources[1], resources[2], resources[3], resources[4]);

	private static ReleaseProbe[] CreateResources(ConcurrentQueue<int> calls, Func<int, Exception> failure)
		=> [.. Enumerable.Range(0, 5).Select(index =>
		{
			void release()
			{
				calls.Enqueue(index);
				if (failure(index) is { } error)
					throw error;
			}

			return new ReleaseProbe(release, () =>
			{
				release();
				return default;
			});
		})];

	[TestMethod]
	[DataRow(0, "connectorContext")]
	[DataRow(1, "entityRegistry")]
	[DataRow(2, "storageRegistry")]
	[DataRow(3, "snapshotRegistry")]
	[DataRow(4, "executor")]
	[Timeout(10_000)]
	public void ConstructorRejectsMissingDependencyWithoutDisposingBorrowedArguments(int missingIndex, string parameterName)
	{
		var calls = new ConcurrentQueue<int>();
		var resources = CreateResources(calls, _ => null);
		resources[missingIndex] = null;

		var error = ThrowsExactly<ArgumentNullException>(() => CreateLifetime(resources));

		AreEqual(parameterName, error.ParamName);
		AreEqual(0, calls.Count);
	}

	[TestMethod]
	[DataRow(0)]
	[DataRow(1)]
	[DataRow(2)]
	[DataRow(3)]
	[DataRow(4)]
	[Timeout(10_000)]
	public void SingleReleaseFailureDoesNotAbandonLaterDependenciesOrRetryReleasedOnes(int failingIndex)
	{
		var calls = new ConcurrentQueue<int>();
		var expectedError = new InvalidOperationException("Release failed.");
		var lifetime = CreateLifetime(CreateResources(calls, index => index == failingIndex ? expectedError : null));

		var error = ThrowsExactly<InvalidOperationException>(lifetime.Dispose);
		lifetime.Dispose();

		AreSame(expectedError, error);
		AreEqual(new[] { 0, 1, 2, 3, 4 }, calls.ToArray());
	}

	[TestMethod]
	[Timeout(10_000)]
	public void MultipleReleaseFailuresAreAllReportedAfterEveryDependencyWasReleased()
	{
		var calls = new ConcurrentQueue<int>();
		var failures = Enumerable.Range(0, 5)
			.Select(index => new InvalidOperationException($"Release {index} failed."))
			.ToArray();
		var lifetime = CreateLifetime(CreateResources(calls, index => failures[index]));

		var error = ThrowsExactly<AggregateException>(lifetime.Dispose);
		lifetime.Dispose();

		AreEqual(new[] { 0, 1, 2, 3, 4 }, calls.ToArray());
		AreEqual(failures.Length, error.InnerExceptions.Count);
		for (var index = 0; index < failures.Length; index++)
			AreSame(failures[index], error.InnerExceptions[index]);
	}

	[TestMethod]
	[DataRow(1, false)]
	[DataRow(1, true)]
	[DataRow(4, false)]
	[DataRow(4, true)]
	[Timeout(10_000)]
	public async Task PendingAsyncReleaseCompletesBeforeDisposalAdvances(int pendingIndex, bool isFaulted)
	{
		var calls = new ConcurrentQueue<int>();
		var resources = CreateResources(calls, _ => null);
		var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var expectedError = new InvalidOperationException("Asynchronous release failed.");
		resources[pendingIndex] = new ReleaseProbe(
			() => Fail("An asynchronous dependency must be released asynchronously."),
			async () =>
			{
				calls.Enqueue(pendingIndex);
				started.SetResult();
				await release.Task;
				if (isFaulted)
					throw expectedError;
			});
		var lifetime = CreateLifetime(resources);
		var disposal = Task.Run(lifetime.Dispose, CancellationToken);

		try
		{
			await started.Task.WaitAsync(CancellationToken);
			IsFalse(disposal.IsCompleted);
			AreEqual(Enumerable.Range(0, pendingIndex + 1).ToArray(), calls.ToArray());
		}
		finally
		{
			release.TrySetResult();
			if (isFaulted)
			{
				var error = await ThrowsExactlyAsync<InvalidOperationException>(() => disposal.WaitAsync(CancellationToken));
				AreSame(expectedError, error);
			}
			else
				await disposal.WaitAsync(CancellationToken);
		}

		lifetime.Dispose();
		AreEqual(new[] { 0, 1, 2, 3, 4 }, calls.ToArray());
	}

	[TestMethod]
	[Timeout(10_000)]
	public async Task ConcurrentDisposalReleasesEachOwnedResourceExactlyOnce()
	{
		var calls = new ConcurrentQueue<int>();
		var lifetime = CreateLifetime(CreateResources(calls, _ => null));
		var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var callers = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
		{
			await start.Task.WaitAsync(CancellationToken);
			lifetime.Dispose();
		}, CancellationToken)).ToArray();

		start.SetResult();
		await Task.WhenAll(callers).WaitAsync(CancellationToken);

		AreEqual(new[] { 0, 1, 2, 3, 4 }, calls.ToArray());
	}

	[TestMethod]
	[Timeout(10_000)]
	public void ReentrantDisposalFromAnOwnedResourceDoesNotReleaseAnythingTwice()
	{
		var calls = new ConcurrentQueue<int>();
		var resources = CreateResources(calls, _ => null);
		OwnedRuntimeLifetime lifetime = null;
		resources[0] = new ReleaseProbe(() =>
		{
			calls.Enqueue(0);
			lifetime.Dispose();
		}, () => throw new InvalidOperationException("Connector context uses synchronous disposal."));
		lifetime = CreateLifetime(resources);

		lifetime.Dispose();

		AreEqual(new[] { 0, 1, 2, 3, 4 }, calls.ToArray());
	}
}
