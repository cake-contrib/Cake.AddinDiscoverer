namespace Cake.AddinDiscoverer.Utilities
{
	internal enum IconAnalysisResult
	{
		Unspecified = 0,
		RawgitUrl = 1,
		JsDelivrUrl = 2,
		CustomUrl = 3,
		EmbeddedCustom = 4,
		EmbeddedCakeContrib = 5,
		EmbeddedFancyCakeContrib = 6
	}
}
