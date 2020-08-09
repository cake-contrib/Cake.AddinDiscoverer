namespace Cake.AddinDiscoverer.Models
{
	internal enum PdbStatus
	{
		NotAvailable = 0,
		Embedded = 1,
		IncludedInPackage = 2,
		IncludedInSymbolsPackage = 3
	}
}
