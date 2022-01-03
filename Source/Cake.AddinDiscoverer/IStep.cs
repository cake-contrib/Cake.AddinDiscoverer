using Cake.AddinDiscoverer.Models;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer
{
	/// <summary>
	/// Interface that describes the steps to be executed by the Addin Discoverer.
	/// </summary>
	internal interface IStep
	{
		/// <summary>
		/// Indicates if the pre-condition is met and therefore this step should be executed.
		/// </summary>
		/// <param name="context">The context.</param>
		/// <returns>A value indicating if the pre-condition is met.</returns>
		bool PreConditionIsMet(DiscoveryContext context);

		/// <summary>
		/// Gets the short sentence describing this step.
		/// </summary>
		/// <param name="context">The context.</param>
		/// <returns>The description.</returns>
		string GetDescription(DiscoveryContext context);

		/// <summary>
		/// Executes the step.
		/// </summary>
		/// <param name="context">The context.</param>
		/// <param name="log">The log.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <returns>The asynchronous task.</returns>
		Task ExecuteAsync(DiscoveryContext context, TextWriter log, CancellationToken cancellationToken);
	}
}
