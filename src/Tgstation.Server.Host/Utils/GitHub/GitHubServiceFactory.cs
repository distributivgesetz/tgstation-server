﻿using System;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Octokit;

using Tgstation.Server.Host.Configuration;

namespace Tgstation.Server.Host.Utils.GitHub
{
	/// <inheritdoc />
	sealed class GitHubServiceFactory : IGitHubServiceFactory
	{
		/// <summary>
		/// The <see cref="IGitHubClientFactory"/> for the <see cref="GitHubServiceFactory"/>.
		/// </summary>
		readonly IGitHubClientFactory gitHubClientFactory;

		/// <summary>
		/// The <see cref="ILoggerFactory"/> for the <see cref="GitHubServiceFactory"/>.
		/// </summary>
		readonly ILoggerFactory loggerFactory;

		/// <summary>
		/// The <see cref="UpdatesConfiguration"/> for the <see cref="GitHubServiceFactory"/>.
		/// </summary>
		readonly UpdatesConfiguration updatesConfiguration;

		/// <summary>
		/// Initializes a new instance of the <see cref="GitHubServiceFactory"/> class.
		/// </summary>
		/// <param name="gitHubClientFactory">The value of <see cref="gitHubClientFactory"/>.</param>
		/// <param name="loggerFactory">The value of <see cref="loggerFactory"/>.</param>
		/// <param name="updatesConfigurationOptions">The <see cref="IOptions{TOptions}"/> containing value of <see cref="updatesConfiguration"/>.</param>
		public GitHubServiceFactory(
			IGitHubClientFactory gitHubClientFactory,
			ILoggerFactory loggerFactory,
			IOptions<UpdatesConfiguration> updatesConfigurationOptions)
		{
			this.gitHubClientFactory = gitHubClientFactory ?? throw new ArgumentNullException(nameof(gitHubClientFactory));
			this.loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
			updatesConfiguration = updatesConfigurationOptions?.Value ?? throw new ArgumentNullException(nameof(updatesConfigurationOptions));
		}

		/// <inheritdoc />
		public IGitHubService CreateService() => CreateServiceImpl(gitHubClientFactory.CreateClient());

		/// <inheritdoc />
		public IAuthenticatedGitHubService CreateService(string accessToken)
			=> CreateServiceImpl(
				gitHubClientFactory.CreateClient(
					accessToken ?? throw new ArgumentNullException(nameof(accessToken))));

		/// <summary>
		/// Create a <see cref="GitHubService"/>.
		/// </summary>
		/// <param name="gitHubClient">The <see cref="IGitHubClient"/> for the <see cref="GitHubService"/>.</param>
		/// <returns>A new <see cref="GitHubService"/>.</returns>
		GitHubService CreateServiceImpl(IGitHubClient gitHubClient)
			=> new (
				gitHubClient,
				loggerFactory.CreateLogger<GitHubService>(),
				updatesConfiguration);
	}
}
