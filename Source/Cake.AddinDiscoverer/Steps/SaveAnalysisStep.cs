using Cake.AddinDiscoverer.Models;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer.Steps
{
	internal class SaveAnalysisStep : IStep
	{
		public bool PreConditionIsMet(DiscoveryContext context) => true;

		public string GetDescription(DiscoveryContext context) => "Save the result of the analysis";

		public async Task ExecuteAsync(DiscoveryContext context, TextWriter log, CancellationToken cancellationToken)
		{
			/*
			 * This method uses a two step approach:
			 *    - Step 1: serialize to memory
			 *    - Step 2: write to disk
			 * rather than combining both operations in a single step like so:
				using FileStream jsonFileStream = File.Create(context.AnalysisResultSaveLocation);
				await JsonSerializer.SerializeAsync(jsonFileStream, context.Addins, typeof(AddinMetadata[]), new JsonSerializerOptions { WriteIndented = true }, cancellationToken).ConfigureAwait(false);
			 * The reason is that the single step approach is causing `Timeout` exception on AppVeyor:
				System.InvalidOperationException: Timeouts are not supported on this stream.
					at int System.IO.Stream.get_ReadTimeout()
					at bool System.Text.Json.Serialization.Metadata.JsonPropertyInfo<T>.GetMemberAndWriteJson(object obj, ref WriteStack state, Utf8JsonWriter writer)
					at bool System.Text.Json.Serialization.Converters.ObjectDefaultConverter<T>.OnTryWrite(Utf8JsonWriter writer, T value, JsonSerializerOptions options, ref WriteStack state)
					at bool System.Text.Json.Serialization.JsonConverter<T>.TryWrite(Utf8JsonWriter writer, in T value, JsonSerializerOptions options, ref WriteStack state)
					at bool System.Text.Json.Serialization.Converters.DictionaryDefaultConverter<TDictionary, TKey, TValue>.OnWriteResume(Utf8JsonWriter writer, TDictionary value, JsonSerializerOptions options, ref WriteStack state)
					at bool System.Text.Json.Serialization.JsonDictionaryConverter<TDictionary, TKey, TValue>.OnTryWrite(Utf8JsonWriter writer, TDictionary dictionary, JsonSerializerOptions options, ref WriteStack state)
					at bool System.Text.Json.Serialization.JsonConverter<T>.TryWrite(Utf8JsonWriter writer, in T value, JsonSerializerOptions options, ref WriteStack state)
					at bool System.Text.Json.Serialization.Metadata.JsonPropertyInfo<T>.GetMemberAndWriteJson(object obj, ref WriteStack state, Utf8JsonWriter writer)
					at bool System.Text.Json.Serialization.Converters.ObjectDefaultConverter<T>.OnTryWrite(Utf8JsonWriter writer, T value, JsonSerializerOptions options, ref WriteStack state)
					at bool System.Text.Json.Serialization.JsonConverter<T>.TryWrite(Utf8JsonWriter writer, in T value, JsonSerializerOptions options, ref WriteStack state)
					at bool System.Text.Json.Serialization.Converters.ArrayConverter<TCollection, TElement>.OnWriteResume(Utf8JsonWriter writer, TElement[] array, JsonSerializerOptions options, ref WriteStack state)
					at bool System.Text.Json.Serialization.JsonCollectionConverter<TCollection, TElement>.OnTryWrite(Utf8JsonWriter writer, TCollection value, JsonSerializerOptions options, ref WriteStack state)
					at bool System.Text.Json.Serialization.JsonConverter<T>.TryWrite(Utf8JsonWriter writer, in T value, JsonSerializerOptions options, ref WriteStack state)
					at bool System.Text.Json.Serialization.JsonConverter<T>.WriteCore(Utf8JsonWriter writer, in T value, JsonSerializerOptions options, ref WriteStack state)
					at bool System.Text.Json.Serialization.JsonConverter<T>.WriteCoreAsObject(Utf8JsonWriter writer, object value, JsonSerializerOptions options, ref WriteStack state)
					at bool System.Text.Json.JsonSerializer.WriteCore<TValue>(Utf8JsonWriter writer, in TValue value, JsonTypeInfo jsonTypeInfo, ref WriteStack state)
					at async Task System.Text.Json.JsonSerializer.WriteStreamAsync<TValue>(Stream utf8Json, TValue value, JsonTypeInfo jsonTypeInfo, CancellationToken cancellationToken) x 3
					at async Task Cake.AddinDiscoverer.Steps.SaveAnalysisStep.ExecuteAsync(DiscoveryContext context, TextWriter log, CancellationToken cancellationToken) in C:/projects/cake-addindiscoverer/Source/Cake.AddinDiscoverer/Steps/SaveAnalysisStep.cs:line 19
					at async Task<ResultCode> Cake.AddinDiscoverer.AddinDiscoverer.LaunchDiscoveryAsync() in C:/projects/cake-addindiscoverer/Source/Cake.AddinDiscoverer/AddinDiscoverer.cs:line 161
			*/

			// Serialize the addins to memory
			using Stream ms = new MemoryStream();
			await JsonSerializer.SerializeAsync(ms, context.Addins, typeof(AddinMetadata[]), new JsonSerializerOptions { WriteIndented = true }, cancellationToken).ConfigureAwait(false);
			ms.Position = 0;

			// Write the content of the memory stream to a file on disk
			using FileStream fs = File.Create(context.AnalysisResultSaveLocation);
			await ms.CopyToAsync(fs).ConfigureAwait(false);

			// Clear the temporary files
			Directory.Delete(context.AnalysisFolder, true);
		}
	}
}
