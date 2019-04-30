using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Cake.AddinDiscoverer.Utilities
{
	internal class XDocumentFormatPreserved
	{
		public XDocument Document { get; private set; }

		public static XDocumentFormatPreserved Parse(string text)
		{
			// Converting the text into an array of bytes might seem unnecessary but it actually
			// serves a very important purpose: ensure the data can be parsed into a XDocument
			// despite the presence of a BOM (byte order mark). You get an exception if you
			// attempt to parse a string containing a BOM into a XDocument but, surprisingly,
			// this problem goes away if you load a byte array.
			var bytes = Encoding.UTF8.GetBytes(text);

			var document = new XDocumentFormatPreserved();
			using (var stream = new MemoryStream(bytes))
			{
				document.Document = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
			}

			return document;
		}

		public override string ToString()
		{
			using (var sw = new StringWriter(CultureInfo.InvariantCulture))
			{
				var ws = new XmlWriterSettings()
				{
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
