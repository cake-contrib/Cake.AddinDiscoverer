using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Cake.AddinDiscoverer.Utilities
{
	internal class XDocumentFormatPreserved
	{
		public bool Utf8OrderMarkPresent { get; private set; }

		public XDocument Document { get; private set; }

		private static readonly string BYTE_ORDER_MARK_UTF8 = Encoding.UTF8.GetString(Encoding.UTF8.GetPreamble());

		public static XDocumentFormatPreserved Parse(string text)
		{
			var document = new XDocumentFormatPreserved();
			document.Utf8OrderMarkPresent = text.StartsWith(BYTE_ORDER_MARK_UTF8);
			if (document.Utf8OrderMarkPresent)
			{
				document.Document = XDocument.Parse(text.Remove(0, BYTE_ORDER_MARK_UTF8.Length), LoadOptions.PreserveWhitespace);
			}
			else
			{
				document.Document = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
			}

			return document;
		}

		public override string ToString()
		{
			using (var sw = new StringWriter(CultureInfo.InvariantCulture))
			{
				var ws = new XmlWriterSettings()
				{
					OmitXmlDeclaration = true,
					Indent = true,
					NewLineHandling = NewLineHandling.None
				};

				using (var w = XmlWriter.Create(sw, ws))
				{
					Document.WriteTo(w);
				}

				return Utf8OrderMarkPresent ? $"{BYTE_ORDER_MARK_UTF8}{sw.ToString()}" : sw.ToString();
			}
		}
	}
}
