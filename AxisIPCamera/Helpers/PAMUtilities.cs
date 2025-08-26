using Keyfactor.Orchestrators.Extensions.Interfaces;
using Microsoft.Extensions.Logging;

namespace Keyfactor.Extensions.Orchestrator.AxisIPCamera.Helpers
{
    public static class PAMUtilities
    {
        public static string ResolvePAMField(IPAMSecretResolver resolver, ILogger logger, string name, string key)
        {
            logger.LogDebug($"Attempting to resolve PAM eligible field {name}");
            return resolver.Resolve(key);
        }
    }
}
