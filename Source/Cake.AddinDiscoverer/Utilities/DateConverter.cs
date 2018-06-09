using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.TypeConversion;
using System;
using System.Globalization;

namespace Cake.AddinDiscoverer.Utilities
{
	internal class DateConverter : ITypeConverter
	{
		private readonly string _dateFormat;

		public DateConverter(string dateFormat)
		{
			_dateFormat = dateFormat;
		}

		public object ConvertFromString(string text, IReaderRow row, MemberMapData memberMapData)
		{
			if (!string.IsNullOrEmpty(text))
			{
				DateTime dt;
				DateTime.TryParseExact(
					text,
					_dateFormat,
					CultureInfo.InvariantCulture,
					DateTimeStyles.AssumeUniversal,
					out dt);
				return dt;
			}

			return null;
		}

		public string ConvertToString(object value, IWriterRow row, MemberMapData memberMapData)
		{
			return ObjectToDateString(value, _dateFormat);
		}

		public string ObjectToDateString(object o, string dateFormat)
		{
			if (o == null) return string.Empty;

			if (o is DateTime dt)
			{
				return dt.ToString(dateFormat);
			}
			else
			{
				return string.Empty;
			}
		}
	}
}
