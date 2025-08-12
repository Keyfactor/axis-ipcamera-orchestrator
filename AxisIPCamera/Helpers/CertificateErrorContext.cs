using System.Collections.Generic;
using System.Linq;

namespace Keyfactor.Extensions.Orchestrator.AxisIPCamera.Helpers
{
    public class CertificateErrorContext
    {
        public List<string> Errors { get; } = new List<string>();

        public void Add(string error)
        {
            Errors.Add(error);
        }

        public bool HasErrors => Errors.Any();
    }
}