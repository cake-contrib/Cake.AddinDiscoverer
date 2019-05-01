using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Cake.AddinDiscoverer.Utilities
{
	internal class XDocumentFormatPreserved
	{
		public XDocument Document { get; private set; }

		public XDocumentFormatPreserved(string text)
		{
			// Get rid of the Byte order mark and the ZERO WIDTH SPACE U+200B
			var safeText = text.Trim('\uFEFF', '\u200B');

			// Parse the safe text, making sure to preserve whitespace
			this.Document = XDocument.Parse(safeText, LoadOptions.PreserveWhitespace);
		}

		public override string ToString()
		{
			using (var sw = new StringWriterWithEncoding(Encoding.UTF8))
			{
				var ws = new XmlWriterSettings()
				{
					Encoding = Encoding.UTF8,
					OmitXmlDeclaration = Document.Declaration == null,
					Indent = true,
					NewLineHandling = NewLineHandling.None
				};

				using (var w = XmlWriter.Create(sw, ws))
				{
					Document.WriteTo(w);
				}

				return sw.ToString();
			}
		}
	}
}
