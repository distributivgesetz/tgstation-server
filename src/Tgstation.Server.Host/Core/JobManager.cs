﻿using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tgstation.Server.Host.Models;

namespace Tgstation.Server.Host.Core
{
	/// <inheritdoc />
	sealed class JobManager : IJobManager, IDisposable
	{
		/// <summary>
		/// The <see cref="IServiceProvider"/> for the <see cref="JobManager"/>
		/// </summary>
		readonly IServiceProvider serviceProvider;

		/// <summary>
		/// The <see cref="ILogger"/> for the <see cref="JobManager"/>
		/// </summary>
		readonly ILogger<JobManager> logger;

		/// <summary>
		/// <see cref="Dictionary{TKey, TValue}"/> of <see cref="Api.Models.Internal.Job.Id"/> to running <see cref="JobHandler"/>s
		/// </summary>
		readonly Dictionary<long, JobHandler> jobs;

		/// <summary>
		/// Construct a <see cref="JobManager"/>
		/// </summary>
		/// <param name="serviceProvider">The value of <see cref="serviceProvider"/></param>
		/// <param name="logger">The value of <see cref="logger"/></param>
		public JobManager(IServiceProvider serviceProvider, ILogger<JobManager> logger)
		{
			this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
			this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
			jobs = new Dictionary<long, JobHandler>();
		}

		/// <inheritdoc />
		public void Dispose()
		{
			foreach (var job in jobs)
				job.Value.Dispose();
		}

		/// <summary>
		/// Gets the <see cref="JobHandler"/> for a given <paramref name="job"/> if it exists
		/// </summary>
		/// <param name="job">The <see cref="Job"/> to get the <see cref="JobHandler"/> for</param>
		/// <returns>The <see cref="JobHandler"/></returns>
		JobHandler CheckGetJob(Job job)
		{
			lock (this)
			{
				if (!jobs.TryGetValue(job.Id, out JobHandler jobHandler))
					throw new InvalidOperationException("Job not running!");
				return jobHandler;
			}
		}

		/// <summary>
		/// Runner for <see cref="JobHandler"/>s
		/// </summary>
		/// <param name="job">The <see cref="Job"/> being run</param>
		/// <param name="operation">The operation for the <paramref name="job"/></param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task"/> representing the running operation</returns>
		async Task RunJob(Job job, Func<Job, IServiceProvider, CancellationToken, Task> operation, CancellationToken cancellationToken)
		{			
			try
			{
				using (var scope = serviceProvider.CreateScope())
				{
					async Task HandleExceptions(Task task)
					{
						try
						{
							await task.ConfigureAwait(false);
						}
						catch (OperationCanceledException)
						{
							logger.LogDebug("Job {0} cancelled!", job.Id);
							job.Cancelled = true;
						}
						catch (Exception e)
						{
							job.ExceptionDetails = e is JobException ? e.Message : e.ToString();
							logger.LogDebug("Job {0} exited with error! Exception: {1}", job.Id, job.ExceptionDetails);
						}
						finally
						{
							job.StoppedAt = DateTimeOffset.Now;
						}
					}

					IDatabaseContext databaseContext = null;
					async Task RunJobInternal()
					{
						var oldJob = job;
						job = new Job { Id = oldJob.Id };
						databaseContext = scope.ServiceProvider.GetRequiredService<IDatabaseContext>();
						databaseContext.Jobs.Attach(job);

						await operation(job, scope.ServiceProvider, cancellationToken).ConfigureAwait(false);

						logger.LogDebug("Job {0} completed!", job.Id);
					};

					await HandleExceptions(RunJobInternal()).ConfigureAwait(false);

					await databaseContext.Save(default).ConfigureAwait(false);

					bool JobErroredOrCancelled() => job.ExceptionDetails != null || job.Cancelled.Value;

					//ok so, now it's time for the post commit step if it exists
					if (!JobErroredOrCancelled() && job.PostComplete != null)
					{
						await HandleExceptions(job.PostComplete(cancellationToken)).ConfigureAwait(false);
						if (JobErroredOrCancelled())
							await databaseContext.Save(default).ConfigureAwait(false);
					}
				}
			}
			finally
			{
				lock (this)
				{
					var handler = jobs[job.Id];
					jobs.Remove(job.Id);
					handler.Dispose();
				}
			}
		}

		/// <inheritdoc />
		public async Task RegisterOperation(Job job, Func<Job, IServiceProvider, Action<int>, CancellationToken, Task> operation, CancellationToken cancellationToken)
		{
			using (var scope = serviceProvider.CreateScope())
			{
				var databaseContext = scope.ServiceProvider.GetRequiredService<IDatabaseContext>();
				job.StartedAt = DateTimeOffset.Now;
				job.Cancelled = false;
				job.Instance = new Instance
				{
					Id = job.Instance.Id
				};
				databaseContext.Instances.Attach(job.Instance);
				if (job.StartedBy != null)
				{
					job.StartedBy = new User
					{
						Id = job.StartedBy.Id
					};
					databaseContext.Users.Attach(job.StartedBy);
				}
				databaseContext.Jobs.Add(job);
				await databaseContext.Save(cancellationToken).ConfigureAwait(false);
				logger.LogDebug("Starting job {0}: {1}...", job.Id, job.Description);
				var jobHandler = JobHandler.Create(x => RunJob(job, (jobParam, serviceProvider, ct) =>
				operation(jobParam, serviceProvider, y =>
				{
					lock (this)
						if (jobs.TryGetValue(job.Id, out var handler))
							handler.Progress = y;
				}, ct),
				x));
				lock (this)
					jobs.Add(job.Id, jobHandler);
			}
		}

		/// <inheritdoc />
		public async Task StartAsync(CancellationToken cancellationToken)
		{
			logger.LogTrace("Starting job manager...");
			using (var scope = serviceProvider.CreateScope())
			{
				var databaseContext = scope.ServiceProvider.GetRequiredService<IDatabaseContext>();

				//mark all jobs as cancelled
				var badJobs = await databaseContext.Jobs.Where(y => !y.StoppedAt.HasValue).Select(y => y.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
				if (badJobs.Count > 0)
				{
					logger.LogTrace("Cleaning {0} unfinished jobs...", badJobs.Count);
					foreach (var I in badJobs)
					{
						var job = new Job { Id = I };
						databaseContext.Jobs.Attach(job);
						job.Cancelled = true;
						job.StoppedAt = DateTimeOffset.Now;
					}
					await databaseContext.Save(cancellationToken).ConfigureAwait(false);
				}
			}
			logger.LogDebug("Job manager started!");
		}

		/// <inheritdoc />
		public async Task StopAsync(CancellationToken cancellationToken)
		{
			var joinTasks = jobs.Select(x =>
			{
				x.Value.Cancel();
				return x.Value.Wait(cancellationToken);
			});
			await Task.WhenAll(joinTasks).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<bool> CancelJob(Job job, User user, bool blocking, CancellationToken cancellationToken)
		{
			if (job == null)
				throw new ArgumentNullException(nameof(job));
			if (user == null)
				throw new ArgumentNullException(nameof(user));
			JobHandler handler;
			try
			{
				handler = CheckGetJob(job);
			}
			catch (InvalidOperationException)
			{
				//this is fine
				return false;
			}
			handler.Cancel();  //this will ensure the db update is only done once
			using (var scope = serviceProvider.CreateScope())
			{
				var databaseContext = scope.ServiceProvider.GetRequiredService<IDatabaseContext>();
				job = new Job { Id = job.Id };
				databaseContext.Jobs.Attach(job);
				user = new User { Id = user.Id };
				databaseContext.Users.Attach(user);
				job.CancelledBy = user;
				//let either startup or cancellation set job.cancelled
				await databaseContext.Save(cancellationToken).ConfigureAwait(false);
			}
			if (blocking)
				await handler.Wait(cancellationToken).ConfigureAwait(false);
			return true;
		}

		/// <inheritdoc />
		public int? JobProgress(Job job)
		{
			lock (this)
			{
				if (!jobs.TryGetValue(job.Id, out var handler))
					return null;
				return handler.Progress;
			}
		}
	}
}
