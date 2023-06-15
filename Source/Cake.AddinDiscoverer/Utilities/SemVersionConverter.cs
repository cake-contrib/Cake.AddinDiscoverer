using System;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Cake.AddinDiscoverer.Utilities
{
	internal sealed class SemVersionConverter : IYamlTypeConverter
	{
		public bool Accepts(Type type) => type == typeof(SemVersion);

		public object ReadYaml(IParser parser, Type type)
		{
			var versionAsString = parser.Consume<Scalar>().Value;
			return SemVersion.Parse(versionAsString);
		}

		public void WriteYaml(IEmitter emitter, object value, Type type)
		{
			var semVersion = (SemVersion)value;
			emitter.Emit(new Scalar(semVersion.ToString(3)));
		}
	}
}
