using System;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Cake.AddinDiscoverer.Utilities
{
	internal sealed class SemVersionConverter : IYamlTypeConverter
	{
		public bool Accepts(Type type) => type == typeof(SemVersion);

		public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
		{
			var versionAsString = parser.Consume<Scalar>().Value;
			return SemVersion.Parse(versionAsString);
		}

		public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer serializer)
		{
			var semVersion = (SemVersion)value;
			emitter.Emit(new Scalar(semVersion.ToString()));
		}
	}
}
