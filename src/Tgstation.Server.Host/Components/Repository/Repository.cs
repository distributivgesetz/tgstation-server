﻿using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tgstation.Server.Host.IO;

namespace Tgstation.Server.Host.Components.Repository
{
	/// <inheritdoc />
	sealed class Repository : IRepository
	{
		const string UnknownReference = "<UNKNOWN>";

		/// <summary>
		/// The branch name used for publishing testmerge commits
		/// </summary>
		public const string RemoteTemporaryBranchName = "___TGSTempBranch";

		/// <inheritdoc />
		public bool IsGitHubRepository { get; }

		/// <inheritdoc />
		public string Head => repository.Head.Tip.Sha;

		/// <inheritdoc />
		public string Reference => repository.Head.FriendlyName;

		/// <inheritdoc />
		public string Origin => repository.Network.Remotes.First().Url;

		/// <summary>
		/// The <see cref="LibGit2Sharp.IRepository"/> for the <see cref="Repository"/>
		/// </summary>
		readonly LibGit2Sharp.IRepository repository;

		/// <summary>
		/// The <see cref="IIOManager"/> for the <see cref="Repository"/>
		/// </summary>
		readonly IIOManager ioMananger;

		/// <summary>
		/// The <see cref="IEventConsumer"/> for the <see cref="Repository"/>
		/// </summary>
		readonly IEventConsumer eventConsumer;

		/// <summary>
		/// <see cref="Action"/> to be taken when <see cref="Dispose"/> is called
		/// </summary>
		readonly Action onDispose;

		/// <summary>
		/// Construct a <see cref="Repository"/>
		/// </summary>
		/// <param name="repository">The value of <see cref="repository"/></param>
		/// <param name="ioMananger">The value of <see cref="ioMananger"/></param>
		/// <param name="eventConsumer">The value of <see cref="eventConsumer"/></param>
		/// <param name="onDispose">The value if <see cref="onDispose"/></param>
		public Repository(LibGit2Sharp.IRepository repository, IIOManager ioMananger, IEventConsumer eventConsumer, Action onDispose)
		{
			this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
			this.ioMananger = ioMananger ?? throw new ArgumentNullException(nameof(ioMananger));
			this.eventConsumer = eventConsumer ?? throw new ArgumentNullException(nameof(eventConsumer));
			this.onDispose = onDispose ?? throw new ArgumentNullException(nameof(onDispose));
			IsGitHubRepository = Origin.ToUpperInvariant().Contains("://GITHUB.COM/");
		}

		/// <inheritdoc />
		public void Dispose()
		{
			repository.Dispose();
			onDispose.Invoke();
		}
		/// <summary>
		/// Convert <paramref name="url"/> to an "https://<paramref name="accessString"/>@{url} equivalent
		/// </summary>
		/// <param name="url">The URL to convert</param>
		/// <param name="accessString">The <see cref="string"/> containing authentication info for the remote repository</param>
		/// <returns>An authenticated URL for accessing the remote repository</returns>
		public static string GenerateAuthUrl(string url, string accessString)
		{
			if (url == null)
				throw new ArgumentNullException(nameof(url));
			if (String.IsNullOrWhiteSpace(accessString))
				return url;
			const string HttProtocolSecure = "HTTPS://";
			if (!url.ToUpperInvariant().StartsWith(HttProtocolSecure, StringComparison.InvariantCulture))
				throw new InvalidOperationException("Cannot use access string without HTTPS remote!");
			//ONLY support https urls
			return url.ToUpperInvariant().Replace(HttProtocolSecure, String.Concat(HttProtocolSecure, accessString, '@'));
		}

		/// <summary>
		/// Runs a blocking force checkout to <paramref name="committish"/>
		/// </summary>
		/// <param name="committish">The committish to checkout</param>
		void RawCheckout(string committish)
		{
			Commands.Checkout(repository, committish, new CheckoutOptions
			{
				CheckoutModifiers = CheckoutModifiers.Force
			});
			repository.RemoveUntrackedFiles();
		}

		/// <inheritdoc />
		public async Task<string> AddTestMerge(int pullRequestNumber, string targetCommit, string committerName, string committerEmail, string accessString, string mergerIdentifier, CancellationToken cancellationToken)
		{

			if (!IsGitHubRepository)
				throw new InvalidOperationException("Test merging is only available on GitHub hosted origin repositories!");

			var Refspec = new List<string>();
			var prBranchName = String.Format(CultureInfo.InvariantCulture, "pr-{0}", pullRequestNumber);
			var localBranchName = String.Format(CultureInfo.InvariantCulture, "pull/{0}/headrefs/heads/{1}", pullRequestNumber, prBranchName);
			Refspec.Add(String.Format(CultureInfo.InvariantCulture, "pull/{0}/head:{1}", pullRequestNumber, prBranchName));
			var logMessage = String.Format(CultureInfo.InvariantCulture, "Merge remote pull request #{0}", pullRequestNumber);

			var originalCommit = repository.Head;

			MergeResult result = null;
			await Task.Factory.StartNew(() =>
			{
				var remote = repository.Network.Remotes.Add("temp_pr_fetch", GenerateAuthUrl(Origin, accessString));
				try
				{
					cancellationToken.ThrowIfCancellationRequested();
					Commands.Fetch((LibGit2Sharp.Repository)repository, remote.Name, Refspec, new FetchOptions
					{
						Prune = true,
						OnProgress = (a) => !cancellationToken.IsCancellationRequested,
						OnTransferProgress = (a) => !cancellationToken.IsCancellationRequested,
						OnUpdateTips = (a, b, c) => !cancellationToken.IsCancellationRequested
					}, logMessage);
				}
				catch (UserCancelledException) { }
				finally
				{
					repository.Network.Remotes.Remove(remote.Name);
					//commit is there and we never gc so
					repository.Branches.Remove(localBranchName);
					repository.Branches.Remove(prBranchName);
				}

				cancellationToken.ThrowIfCancellationRequested();

				result = repository.Merge(targetCommit, new Signature(new Identity(committerName, committerEmail), DateTimeOffset.Now), new MergeOptions
				{
					CommitOnSuccess = true,
					FailOnConflict = true,
					FastForwardStrategy = FastForwardStrategy.NoFastForward,
					SkipReuc = true
				});

				cancellationToken.ThrowIfCancellationRequested();

				if (result.Status == MergeStatus.Conflicts)
				{
					RawCheckout(originalCommit.CanonicalName ?? originalCommit.Tip.Sha);
					cancellationToken.ThrowIfCancellationRequested();
				}

				repository.RemoveUntrackedFiles();
			}, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Current).ConfigureAwait(false);

			if (result.Status == MergeStatus.Conflicts)
			{
				await eventConsumer.HandleEvent(EventType.RepoMergeConflict, new List<string> { originalCommit.Tip.Sha, targetCommit, originalCommit.FriendlyName ?? UnknownReference, prBranchName }, cancellationToken).ConfigureAwait(false);
				return null;
			}

			return result.Commit.Sha;
		}

		/// <inheritdoc />
		public async Task CheckoutObject(string committish, CancellationToken cancellationToken)
		{
			if (committish == null)
				throw new ArgumentNullException(nameof(committish));
			await eventConsumer.HandleEvent(EventType.RepoCheckout, new List<string> { committish }, cancellationToken).ConfigureAwait(false);
			await Task.Factory.StartNew(() => RawCheckout(committish), cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Current).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public Task FetchOrigin(string accessString, CancellationToken cancellationToken) => Task.WhenAll(
			eventConsumer.HandleEvent(EventType.RepoFetch, Array.Empty<string>(), cancellationToken),
			Task.Factory.StartNew(() =>
			{
				var remote = repository.Network.Remotes.First();
				try
				{
					Commands.Fetch((LibGit2Sharp.Repository)repository, remote.Name, remote.FetchRefSpecs.Select(x => x.Specification), new FetchOptions
					{
						Prune = true,
						OnProgress = (a) => !cancellationToken.IsCancellationRequested,
						OnTransferProgress = (a) => !cancellationToken.IsCancellationRequested,
						OnUpdateTips = (a, b, c) => !cancellationToken.IsCancellationRequested
					}, "Fetch origin commits");
				}
				catch (UserCancelledException)
				{
					cancellationToken.ThrowIfCancellationRequested();
				}
			}, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Current));

		/// <inheritdoc />
		public Task PushHeadToTemporaryBranch(string accessString, CancellationToken cancellationToken) => Task.Factory.StartNew(() =>
		{
			var branch = repository.CreateBranch(RemoteTemporaryBranchName);
			try
			{
				cancellationToken.ThrowIfCancellationRequested();
				var remote = repository.Network.Remotes.Add("temp_push", GenerateAuthUrl(Origin, accessString));
				try
				{
					cancellationToken.ThrowIfCancellationRequested();
					try
					{
						repository.Network.Push(remote, String.Format(CultureInfo.InvariantCulture, "+{0}:{0}", branch.CanonicalName), new PushOptions
						{
							OnPackBuilderProgress = (a, b, c) => !cancellationToken.IsCancellationRequested,
							OnNegotiationCompletedBeforePush = (a) => !cancellationToken.IsCancellationRequested,
							OnPushTransferProgress = (a, b, c) => !cancellationToken.IsCancellationRequested
						});
					}
					catch (UserCancelledException)
					{
						cancellationToken.ThrowIfCancellationRequested();
					}
				}
				finally
				{
					repository.Network.Remotes.Remove(remote.Name);
				}
			}
			finally
			{
				repository.Branches.Remove(branch);
			}
		}, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Current);

		/// <inheritdoc />
		public async Task<string> ResetToOrigin(CancellationToken cancellationToken)
		{
			if (!repository.Head.IsTracking)
				throw new InvalidOperationException("Cannot reset to origin while not on a tracked reference!");
			var trackedBranch = repository.Head.TrackedBranch;
			await eventConsumer.HandleEvent(EventType.RepoResetOrigin, new List<string> { trackedBranch.FriendlyName, trackedBranch.Tip.Sha }, cancellationToken).ConfigureAwait(false);
			return await Task.Factory.StartNew(() =>
			{
				Commands.Checkout((LibGit2Sharp.Repository)repository, repository.Head.TrackedBranch, new CheckoutOptions
				{
					CheckoutModifiers = CheckoutModifiers.Force
				});
				cancellationToken.ThrowIfCancellationRequested();
				repository.RemoveUntrackedFiles();
				return trackedBranch.Tip.Sha;
			}, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Current).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task CopyTo(string path, CancellationToken cancellationToken)
		{
			if (path == null)
				throw new ArgumentNullException(nameof(path));
			await ioMananger.CopyDirectory(".", path, new List<string> { ".git" }, cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<string> MergeOrigin(string committerName, string committerEmail, CancellationToken cancellationToken)
		{
			MergeResult result = null;
			Branch trackedBranch = null;

			var oldHead = repository.Head;

			await Task.Factory.StartNew(() =>
			{
				if (!repository.Head.IsTracking)
					throw new InvalidOperationException("Cannot reset to origin while not on a tracked reference!");
				trackedBranch = repository.Head.TrackedBranch;

				result = repository.Merge(trackedBranch, new Signature(new Identity(committerName, committerEmail), DateTimeOffset.Now), new MergeOptions
				{
					CommitOnSuccess = true,
					FailOnConflict = true,
					FastForwardStrategy = FastForwardStrategy.Default,
					SkipReuc = true,
				});

				cancellationToken.ThrowIfCancellationRequested();

				if (result.Status == MergeStatus.Conflicts)
				{
					RawCheckout(oldHead.CanonicalName);
					cancellationToken.ThrowIfCancellationRequested();
				}

				repository.RemoveUntrackedFiles();
			}, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Current).ConfigureAwait(false);

			if (result.Status == MergeStatus.Conflicts)
			{
				await eventConsumer.HandleEvent(EventType.RepoMergeConflict, new List<string> { oldHead.Tip.Sha, trackedBranch.Tip.Sha, oldHead.FriendlyName ?? UnknownReference, trackedBranch.FriendlyName }, cancellationToken).ConfigureAwait(false);
				return null;
			}

			return Head;
		}

		/// <inheritdoc />
		public async Task Sychronize(string accessString, CancellationToken cancellationToken)
		{
			var startHead = Head;

			if (!await eventConsumer.HandleEvent(EventType.RepoPreSynchronize, new List<string> { ioMananger.ResolvePath(".") }, cancellationToken).ConfigureAwait(false))
				return;

			if (Head == startHead || !repository.Head.IsTracking)
				return;

			await Task.Factory.StartNew(() =>
			{
				cancellationToken.ThrowIfCancellationRequested();
				var remote = repository.Network.Remotes.Add("temp_push", GenerateAuthUrl(Origin, accessString));
				try
				{
					cancellationToken.ThrowIfCancellationRequested();
					try
					{
						repository.Network.Push(repository.Head, new PushOptions
						{
							OnPackBuilderProgress = (a, b, c) => !cancellationToken.IsCancellationRequested,
							OnNegotiationCompletedBeforePush = (a) => !cancellationToken.IsCancellationRequested,
							OnPushTransferProgress = (a, b, c) => !cancellationToken.IsCancellationRequested
						});
					}
					catch (UserCancelledException)
					{
						cancellationToken.ThrowIfCancellationRequested();
					}
				}
				finally
				{
					repository.Network.Remotes.Remove(remote.Name);
				}
			}, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Current).ConfigureAwait(false);
		}
	}
}
