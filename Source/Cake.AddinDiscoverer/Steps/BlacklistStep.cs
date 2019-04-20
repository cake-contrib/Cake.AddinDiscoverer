using System.Linq;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer.Steps
{
	internal class BlacklistStep : IStep
	{
		public bool PreConditionIsMet(DiscoveryContext context) => true;

		public string GetDescription(DiscoveryContext context)
		{
			if (string.IsNullOrEmpty(context.Options.AddinName)) return "Download latest packages from NuGet";
			else return $"Download latest pakage for {context.Options.AddinName}";
		}

		public async Task ExecuteAsync(DiscoveryContext context)
		{
			context.Addins = context.Addins
				.Where(addin => !context.BlacklistedAddins.Any(blackListedAddinName => addin.Name.IsMatch(blackListedAddinName)))
				.OrderBy(addin => addin.Name)
				.ToArray();

			await Task.Delay(1).ConfigureAwait(false);
		}
	}
}
