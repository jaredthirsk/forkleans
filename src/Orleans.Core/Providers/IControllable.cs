using System.Threading.Tasks;

namespace Forkleans.Providers
{
    /// <summary>
    /// A general interface for controllable components inside Orleans runtime.
    /// </summary>
    public interface IControllable
    {
        /// <summary>
        /// A function to execute a control command.
        /// </summary>
        /// <param name="command">A serial number of the command.</param>
        /// <param name="arg">An opaque command argument.</param>
        /// <returns>The value returned from the command handler.</returns>
        Task<object> ExecuteCommand(int command, object arg);
    }
}
