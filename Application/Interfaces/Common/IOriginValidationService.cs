using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Interfaces.Common
{
    public interface IOriginValidationService
    {
        /// <summary>
        /// Determines if the given origin is from a frontend application
        /// </summary>
        /// <param name="origin">The origin URL to validate</param>
        /// <returns>True if the origin is from a frontend, false if from backend or unknown</returns>
        bool IsFrontendOrigin(string origin);
    }
}