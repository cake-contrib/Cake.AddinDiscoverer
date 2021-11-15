using Cake.AddinDiscoverer.Models;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer.Steps
{
	internal class ExclusionlistStep : IStep
	{
		public bool PreConditionIsMet(DiscoveryContext context) => true;

		public string GetDescription(DiscoveryContext context) => "Filter out addins that are on the exclusion list";

		public Task ExecuteAsync(DiscoveryContext context, TextWriter log)
		{
			context.Addins = context.Addins
				.Where(addin => !context.ExcludedAddins.Any(excludedAddinName => addin.Name.IsMatch(excludedAddinName)))
				.OrderBy(addin => addin.Name)
				.ToArray();

			return Task.CompletedTask;
		}
	}
}
