using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NuGet.Versioning;
using System;

namespace Cake.AddinDiscoverer
{
	internal class NugetVersionConverter : JsonConverter
	{
		public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
		{
			var versionAsString = ((NuGetVersion)value).ToNormalizedString();
			var token = JToken.FromObject(versionAsString);
			token.WriteTo(writer);
		}

		public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
		{
			var versionAsString = (string)reader.Value;
			return NuGetVersion.Parse(versionAsString);
		}

		public override bool CanConvert(Type objectType)
		{
			return objectType == typeof(NuGetVersion);
		}
	}
}
