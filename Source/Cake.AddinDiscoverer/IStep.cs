using System.Threading.Tasks;

namespace Cake.AddinDiscoverer
{
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
		/// <returns>The asynchronous task.</returns>
		Task ExecuteAsync(DiscoveryContext context);
	}
}
