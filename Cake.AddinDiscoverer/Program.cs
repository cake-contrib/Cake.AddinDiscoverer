using CommandLine;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer
{
	class Program
	{
		public static void Main(string[] args)
		{
			// Parse comand line arguments and proceed with analysis if parsing was succesfull
			var parserResult = Parser.Default.ParseArguments<Options>(args)
				.WithParsed<Options>(opts =>
				{
					if (string.IsNullOrEmpty(opts.GithubUsername)) opts.GithubUsername = Environment.GetEnvironmentVariable("GITHUB_USERNAME");
					if (string.IsNullOrEmpty(opts.GithuPassword)) opts.GithuPassword = Environment.GetEnvironmentVariable("GITHUB_PASSWORD");
					if (string.IsNullOrEmpty(opts.TemporaryFolder)) opts.TemporaryFolder = Path.GetTempPath();

					OnSuccessfulParse(opts);
				});
#if DEBUG
			// Flush the console key buffer
			while (Console.KeyAvailable) Console.ReadKey(true);

			// Wait for user to press a key
			Console.WriteLine("\r\nPress any key to exit...");
			Console.ReadKey();
#endif
		}
		
		private static void OnSuccessfulParse(Options options)
		{
			var addinDiscoverer = new AddinDiscoverer(options);
			addinDiscoverer.LaunchDiscoveryAsync().GetAwaiter().GetResult();
		}
	}
}
