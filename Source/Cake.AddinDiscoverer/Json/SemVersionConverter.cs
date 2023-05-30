using Cake.AddinDiscoverer.Utilities;
using Cake.Incubator.StringExtensions;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cake.AddinDiscoverer.Json
{
	/// <summary>
	/// Converts a <see cref="SemVersion"/> to or from JSON.
	/// </summary>
	/// <seealso cref="JsonConverter" />
	internal class SemVersionConverter : JsonConverter<SemVersion>
	{
		public override SemVersion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{

			if (reader.TokenType == JsonTokenType.String)
			{
				var version = reader.GetString();
				return SemVersion.Parse(version);
			}
			else if (reader.TokenType == JsonTokenType.StartObject)
			{
				var major = 0;
				var minor = 0;
				var patch = 0;
				var prerelease = string.Empty;
				var build = string.Empty;

				while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
				{
					if (reader.TokenType == JsonTokenType.PropertyName)
					{
						var propertyName = reader.GetString();
						reader.Read();

						if (propertyName.EqualsIgnoreCase("Major")) major = reader.GetInt32();
						else if (propertyName.EqualsIgnoreCase("Minor")) minor = reader.GetInt32();
						else if (propertyName.EqualsIgnoreCase("Patch")) patch = reader.GetInt32();
						else if (propertyName.EqualsIgnoreCase("Prerelease")) prerelease = reader.GetString();
						else if (propertyName.EqualsIgnoreCase("build")) build = reader.GetString();
					}
				}

				return new SemVersion(major, minor, patch, prerelease, build);
			}

			throw new JsonException($"Unable to convert the content of the JSON node into a SemVersion value");
		}

		public override void Write(Utf8JsonWriter writer, SemVersion value, JsonSerializerOptions options)
		{
			writer.WriteStringValue(value.ToString());
		}
	}
}
