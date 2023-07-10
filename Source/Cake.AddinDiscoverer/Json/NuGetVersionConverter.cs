using Cake.Incubator.StringExtensions;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cake.AddinDiscoverer.Json
{
	/// <summary>
	/// Converts a <see cref="NuGetVersion"/> to or from JSON.
	/// </summary>
	/// <seealso cref="JsonConverter" />
	internal class NuGetVersionConverter : JsonConverter<NuGetVersion>
	{
		public override NuGetVersion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			if (reader.TokenType == JsonTokenType.String)
			{
				var version = reader.GetString();
				return NuGetVersion.Parse(version);
			}
			else if (reader.TokenType == JsonTokenType.StartObject)
			{
				var major = 0;
				var minor = 0;
				var patch = 0;
				var revision = 0;
				var metadata = string.Empty;
				var releaseLabels = Array.Empty<string>();

				while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
				{
					if (reader.TokenType == JsonTokenType.PropertyName)
					{
						var propertyName = reader.GetString();
						reader.Read();

						if (propertyName.EqualsIgnoreCase("Major")) { major = reader.GetInt32(); }
						else if (propertyName.EqualsIgnoreCase("Minor")) { minor = reader.GetInt32(); }
						else if (propertyName.EqualsIgnoreCase("Patch")) { patch = reader.GetInt32(); }
						else if (propertyName.EqualsIgnoreCase("Revision")) { revision = reader.GetInt32(); }
						else if (propertyName.EqualsIgnoreCase("Metadata")) { metadata = reader.GetString(); }
						else if (propertyName.EqualsIgnoreCase("ReleaseLabels"))
						{
							if (reader.TokenType == JsonTokenType.StartArray)
							{
								reader.Read();

								var labels = new List<string>();
								while (reader.TokenType != JsonTokenType.EndArray)
								{
									labels.Add(reader.GetString());
									reader.Read();
								}

								releaseLabels = labels.ToArray();
							}
							else
							{
								releaseLabels = new[] { reader.GetString() };
							}
						}
					}
				}

				return new NuGetVersion(major, minor, patch, revision, releaseLabels, metadata);
			}

			throw new JsonException($"Unable to convert the content of the JSON node into a NuGetVersion value");
		}

		public override void Write(Utf8JsonWriter writer, NuGetVersion value, JsonSerializerOptions options)
		{
			writer.WriteStringValue(value.ToNormalizedString());
		}
	}
}
