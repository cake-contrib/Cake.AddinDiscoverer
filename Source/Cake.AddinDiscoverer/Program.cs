using Cake.AddinDiscoverer.Utilities;
using CommandLine;
using System;
using System.IO;

namespace Cake.AddinDiscoverer
{
	/// <summary>
	/// Program entry point.
	/// </summary>
	public class Program
	{
		/// <summary>
		/// Main method.
		/// </summary>
		/// <param name="args">Command line arguments.</param>
		/// <returns>Result code (0 indicates success; non-zero indicates error).</returns>
		///
		public static int Main(string[] args)
		{
			// Parse command line arguments and proceed with analysis if parsing was successful
			var result = Parser.Default.ParseArguments<Options>(args)
				.MapResult(
					opts =>
					{
						if (string.IsNullOrEmpty(opts.GithubUsername)) opts.GithubUsername = Environment.GetEnvironmentVariable("GITHUB_USERNAME");
						if (string.IsNullOrEmpty(opts.GithuPassword)) opts.GithuPassword = Environment.GetEnvironmentVariable("GITHUB_PASSWORD");
						if (string.IsNullOrEmpty(opts.GithubToken)) opts.GithubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
						if (string.IsNullOrEmpty(opts.TemporaryFolder)) opts.TemporaryFolder = Path.GetTempPath();

						// Make sure this is an absolute path
						opts.TemporaryFolder = Path.GetFullPath(opts.TemporaryFolder);

						return OnSuccessfulParse(opts);
					},
					_ => ResultCode.Error);
#if DEBUG
			// Flush the console key buffer
			while (Console.KeyAvailable) Console.ReadKey(true);

			// Wait for user to press a key
			Console.WriteLine("\r\nPress any key to exit...");
			Console.ReadKey();
#endif
			return (int)result;
		}

		private static ResultCode OnSuccessfulParse(Options options)
		{
			var addinDiscoverer = new AddinDiscoverer(options);
			var result = addinDiscoverer.LaunchDiscoveryAsync().GetAwaiter().GetResult();
			return result;
		}
	}
}
