namespace Cake.AddinDiscoverer.Models
{
	internal enum PdbStatus
	{
		Unknown = 0,
		Embedded = 1,
		IncludedInPackage = 2,
		IncludedInSymbolsPackage = 3
	}
}
