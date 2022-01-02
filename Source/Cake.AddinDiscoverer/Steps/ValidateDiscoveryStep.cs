using Cake.AddinDiscoverer.Models;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer.Steps
{
	internal class ValidateDiscoveryStep : IStep
	{
		public bool PreConditionIsMet(DiscoveryContext context) => true;

		public string GetDescription(DiscoveryContext context)
		{
			if (string.IsNullOrEmpty(context.Options.AddinName)) return "Making sure we found at least one addin";
			else return $"Making sure we found {context.Options.AddinName}";
		}

		public Task ExecuteAsync(DiscoveryContext context, TextWriter log, CancellationToken cancellationToken)
		{
			if (!context.Addins.Any())
			{
				if (string.IsNullOrEmpty(context.Options.AddinName))
				{
					throw new Exception($"Unable to find any addin");
				}
				else
				{
					throw new Exception($"Unable to find '{context.Options.AddinName}'");
				}
			}

			return Task.CompletedTask;
		}
	}
}
