using System.Collections.Generic;
using System.Threading.Tasks;

namespace AgyCliDashboard.Services;

public interface ISystemCheckService
{
    List<Task<SystemRequirementItem>> CreateCheckTasks();
}
