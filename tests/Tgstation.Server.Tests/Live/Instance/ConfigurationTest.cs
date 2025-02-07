﻿using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Tgstation.Server.Api.Models;
using Tgstation.Server.Api.Models.Request;
using Tgstation.Server.Client;
using Tgstation.Server.Client.Components;
using Tgstation.Server.Host.IO;

namespace Tgstation.Server.Tests.Live.Instance
{
	sealed class ConfigurationTest
	{
		readonly IConfigurationClient configurationClient;
		readonly Api.Models.Instance instance;

		public ConfigurationTest(IConfigurationClient configurationClient, Api.Models.Instance instance)
		{
			this.configurationClient = configurationClient ?? throw new ArgumentNullException(nameof(configurationClient));
			this.instance = instance ?? throw new ArgumentNullException(nameof(instance));
		}

		bool FileExists(IConfigurationFile file)
		{
			var tmp = file.Path?.StartsWith('/') ?? false ? '.' + file.Path : file.Path;
			var path = Path.Combine(instance.Path, "Configuration", tmp);
			var result = File.Exists(path);
			return result;
		}

		async Task TestUploadDownloadAndDeleteDirectory(CancellationToken cancellationToken)
		{
			//try to delete non-existent
			var TestDir = new ConfigurationFileRequest
			{
				Path = "/TestDeleteDir"
			};

			await configurationClient.DeleteEmptyDirectory(TestDir, cancellationToken);

			//try to delete non-empty
			const string TestString = "Hello world!";
			await using var uploadMs = new MemoryStream(Encoding.UTF8.GetBytes(TestString));
			var file = await configurationClient.Write(new ConfigurationFileRequest
			{
				Path = TestDir.Path + "/test.txt"
			}, uploadMs, cancellationToken);

			Assert.IsTrue(FileExists(file));
			Assert.IsNull(file.LastReadHash);

			var updatedFileTuple = await configurationClient.Read(file, cancellationToken);
			var updatedFile = updatedFileTuple.Item1;
			Assert.IsNotNull(updatedFile.LastReadHash);
			await using (var downloadMemoryStream = new MemoryStream())
			{
				await using (var downloadStream = updatedFileTuple.Item2)
				{
					await downloadStream.CopyToAsync(downloadMemoryStream, cancellationToken);
				}
				Assert.AreEqual(TestString, Encoding.UTF8.GetString(downloadMemoryStream.ToArray()).Trim());
			}

			await ApiAssert.ThrowsException<ConflictException>(() => configurationClient.DeleteEmptyDirectory(TestDir, cancellationToken), ErrorCode.ConfigurationDirectoryNotEmpty);

			file.FileTicket = null;
			await configurationClient.Write(new ConfigurationFileRequest
			{
				Path = updatedFile.Path,
				LastReadHash = updatedFile.LastReadHash
			}, null, cancellationToken);
			Assert.IsFalse(FileExists(file));

			await configurationClient.DeleteEmptyDirectory(TestDir, cancellationToken);

			var tmp = TestDir.Path?.StartsWith('/') ?? false ? '.' + TestDir.Path : TestDir.Path;
			var path = Path.Combine(instance.Path, "Configuration", tmp);
			Assert.IsFalse(Directory.Exists(path));

			// leave a directory there to test the deployment process
			var staticDir = new ConfigurationFileRequest
			{
				Path = "/GameStaticFiles/data"
			};

			await configurationClient.CreateDirectory(staticDir, cancellationToken);
		}

		public Task SetupDMApiTests(CancellationToken cancellationToken)
		{
			// just use an I/O manager here
			var ioManager = new DefaultIOManager();
			return Task.WhenAll(
				ioManager.CopyDirectory(
					Enumerable.Empty<string>(),
					null,
					"../../../../DMAPI",
					ioManager.ConcatPath(instance.Path, "Repository", "tests", "DMAPI"),
					null,
					cancellationToken),
				ioManager.CopyDirectory(
					Enumerable.Empty<string>(),
					null,
					"../../../../../src/DMAPI",
					ioManager.ConcatPath(instance.Path, "Repository", "src", "DMAPI"),
					null,
					cancellationToken)
				);
		}

		Task TestPregeneratedFilesExist(CancellationToken cancellationToken) => Task.Factory.StartNew(
			_ =>
			{
				var configDir = Path.Combine(instance.Path, "Configuration");
				var baseDir = Path.Combine(configDir, "CodeModifications");
				var path = Path.Combine(baseDir, "HeadInclude.dm");
				var result = File.Exists(path);
				Assert.IsTrue(result);
				path = Path.Combine(baseDir, "TailInclude.dm");
				result = File.Exists(path);
				Assert.IsTrue(result);
				var tgsIgnore = Path.Combine(configDir, "GameStaticFiles", ".tgsignore");
				result = File.Exists(tgsIgnore);
				Assert.IsTrue(result);
			},
			null,
			cancellationToken,
			TaskCreationOptions.LongRunning,
			TaskScheduler.Current);

		async Task TestListing(CancellationToken cancellationToken)
		{
			await using var uploadMs = new MemoryStream(Encoding.UTF8.GetBytes("Hello world!"));
			await configurationClient.Write(
				new ConfigurationFileRequest
				{
					Path = "f-TestFile.txt"
				},
				uploadMs,
				cancellationToken);
			uploadMs.Seek(0, SeekOrigin.Begin);
			await configurationClient.Write(
				new ConfigurationFileRequest
				{
					Path = "f-TestDir/f-TestFile.txt"
				},
				uploadMs,
				cancellationToken);

			var baseList = await configurationClient.List(null, ".", cancellationToken);
			Assert.AreEqual(5, baseList.Count);

			Assert.AreEqual($"/CodeModifications", baseList[0].Path);
			Assert.IsTrue(baseList[0].IsDirectory);
			Assert.AreEqual($"/EventScripts", baseList[1].Path);
			Assert.IsTrue(baseList[1].IsDirectory);
			Assert.AreEqual($"/f-TestDir", baseList[2].Path);
			Assert.IsTrue(baseList[2].IsDirectory);
			Assert.AreEqual($"/GameStaticFiles", baseList[3].Path);
			Assert.IsTrue(baseList[3].IsDirectory);
			Assert.AreEqual($"/f-TestFile.txt", baseList[4].Path);
			Assert.IsFalse(baseList[4].IsDirectory);
		}

		async Task SequencedApiTests(CancellationToken cancellationToken)
		{
			await TestUploadDownloadAndDeleteDirectory(cancellationToken);
			await TestListing(cancellationToken);
		}

		public Task RunPreWatchdog(CancellationToken cancellationToken) => Task.WhenAll(
			SequencedApiTests(cancellationToken),
			SetupDMApiTests(cancellationToken),
			TestPregeneratedFilesExist(cancellationToken));
	}
}
