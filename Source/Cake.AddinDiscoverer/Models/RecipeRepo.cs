using System.Diagnostics;

namespace Cake.AddinDiscoverer.Models
{
	[DebuggerDisplay("{Owner}/{Name}")]
	internal class RecipeRepo
	{
		public string Owner { get; set; }

		public string Name { get; set; }

		public string VersionFilePath { get; set; }

		public string ContentFolderPath { get; set; }

		public RecipeRepo(string owner, string name, string versionFilePath, string contentFolderPath)
		{
			Owner = owner;
			Name = name;
			VersionFilePath = versionFilePath;
			ContentFolderPath = contentFolderPath;
		}
	}
}
