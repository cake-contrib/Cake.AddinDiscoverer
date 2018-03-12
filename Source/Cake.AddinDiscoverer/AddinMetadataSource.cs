using System;

namespace Cake.AddinDiscoverer
{
	[Flags]
	public enum AddinMetadataSource
	{
		None = 0,
		WebsiteList = 1 << 0,
		Yaml = 1 << 1
	}
}
