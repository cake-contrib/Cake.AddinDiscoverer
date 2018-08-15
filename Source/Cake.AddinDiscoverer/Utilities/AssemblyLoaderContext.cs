using System.Reflection;
using System.Runtime.Loader;

namespace Cake.AddinDiscoverer.Utilities
{
	internal class AssemblyLoaderContext : AssemblyLoadContext
	{
		public AssemblyLoaderContext()
		{
		}

		protected override Assembly Load(AssemblyName assemblyName)
		{
			return Assembly.Load(assemblyName);
		}
	}
}
