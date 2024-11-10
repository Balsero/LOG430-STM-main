using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Interfaces
{
    public interface ICompareTimes
    {
        Task<string> SaveStateAsync(string key, string state);
        Task<string> GetStateAsync(string key);
    }
}
