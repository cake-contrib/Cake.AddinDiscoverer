using System;

namespace Cake.AddinDiscoverer
{
	[Flags]
	internal enum AddinMetadataSource
	{
		None = 0,
		WebsiteList = 1 << 0,
		Yaml = 1 << 1,
		Nuget = 1 << 2
	}
}
