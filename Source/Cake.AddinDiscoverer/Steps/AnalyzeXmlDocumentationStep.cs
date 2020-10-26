using Cake.AddinDiscoverer.Models;
using NuDoq;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Exception = System.Exception;

namespace Cake.AddinDiscoverer.Steps
{
	internal class AnalyzeXmlDocumentationStep : IStep
	{
		public bool PreConditionIsMet(DiscoveryContext context) => true;

		public string GetDescription(DiscoveryContext context) => "Analyze XML documentation";

		public async Task ExecuteAsync(DiscoveryContext context)
		{
			const int maxDregreeOfParallelism = 25;

			context.Addins = await context.Addins
				.ForEachAsync(
					async addin =>
					{
						var tempFileName = Path.GetTempFileName();

						try
						{
							if (!string.IsNullOrEmpty(addin.XmlDocumentationFilePath))
							{
								await using var stream = addin.NuGetPackage.LoadFile(addin.XmlDocumentationFilePath);

								// Create a temporary copy of the xml file because NuDoq cannot read a stream.
								await using (FileStream fs = File.OpenWrite(tempFileName))
								{
									await stream.CopyToAsync(fs).ConfigureAwait(false);
								}

								// Read the XML documentation file
								var documentedMembers = DocReader.Read(tempFileName);

								// Map types and members to IDs that can be used for searching the XML documentation
								var map = new MemberIdMap();
								map.AddRange(addin.DecoratedMethods.Select(dm => dm.DeclaringType));

								// Analyze the decorated methods
								foreach (var decoratedMethod in addin.DecoratedMethods)
								{
									var methodId = map.FindId(decoratedMethod);

									var member = documentedMembers.Elements
										.OfType<Member>()
										.FirstOrDefault(x => x.Id == methodId);

									if (member == null)
									{
										addin.AnalysisResult.XmlDocumentationAnalysisNotes.Add($"{methodId} is not documented");
									}
								}
							}
						}
						catch (Exception e)
						{
							addin.AnalysisResult.Notes += $"AnalyzeXmlDocumentation: {e.GetBaseException().Message}{Environment.NewLine}";
						}
						finally
						{
							File.Delete(tempFileName);
						}

						return addin;
					}, maxDregreeOfParallelism)
				.ConfigureAwait(false);
		}
	}
}
