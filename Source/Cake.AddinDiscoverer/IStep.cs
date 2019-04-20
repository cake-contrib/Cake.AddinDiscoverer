using System.Threading.Tasks;

namespace Cake.AddinDiscoverer
{
	internal interface IStep
	{
		bool PreConditionIsMet(DiscoveryContext context);
		string GetDescription(DiscoveryContext context);
		Task ExecuteAsync(DiscoveryContext context);
	}
}
