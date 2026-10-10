using System.Collections.Generic;
using System.Threading.Tasks;

namespace AgyCliDashboard.Services;

public interface IFileService
{
    List<string> GetDirectories(string dirPath, bool showHidden = false);
    List<string> GetFiles(string dirPath, bool showHidden = false);
}
