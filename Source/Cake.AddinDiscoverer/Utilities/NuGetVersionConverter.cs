using NuGet.Versioning;
using System;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Cake.AddinDiscoverer.Utilities
{
	internal sealed class NuGetVersionConverter : IYamlTypeConverter
	{
		public bool Accepts(Type type) => type == typeof(NuGetVersion);

		public object ReadYaml(IParser parser, Type type)
		{
			var versionAsString = parser.Consume<Scalar>().Value;
			return NuGetVersion.Parse(versionAsString);
		}

		public void WriteYaml(IEmitter emitter, object value, Type type)
		{
			var nugetVersion = (NuGetVersion)value;
			emitter.Emit(new Scalar(nugetVersion.ToNormalizedString()));
		}
	}
}
