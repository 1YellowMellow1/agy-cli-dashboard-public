using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AgyCliDashboard.Services;

public class FileService
{
    public List<string> GetDirectories(string dirPath, bool showHidden = false)
    {
        if (!Directory.Exists(dirPath)) return new List<string>();

        try
        {
            var query = Directory.GetDirectories(dirPath)
                .Select(d => Path.GetFileName(d));

            if (!showHidden)
            {
                query = query.Where(d => !d.StartsWith(".") &&
                                         !d.Equals("node_modules", StringComparison.OrdinalIgnoreCase) &&
                                         !d.Equals("bin", StringComparison.OrdinalIgnoreCase) &&
                                         !d.Equals("obj", StringComparison.OrdinalIgnoreCase));
            }

            return query.OrderBy(d => d).ToList();
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"FileService.GetDirectories Error: {ex.Message}");
            return new List<string>();
        }
    }

    public List<string> GetFiles(string dirPath, bool showHidden = false)
    {
        if (!Directory.Exists(dirPath)) return new List<string>();

        try
        {
            var query = Directory.GetFiles(dirPath)
                .Select(f => Path.GetFileName(f));

            if (!showHidden)
            {
                query = query.Where(f => (!f.StartsWith(".") ||
                                         f.Equals(".gitignore", StringComparison.OrdinalIgnoreCase) ||
                                         f.Equals(".geminiignore", StringComparison.OrdinalIgnoreCase) ||
                                         f.Equals(".cursorrules", StringComparison.OrdinalIgnoreCase) ||
                                         f.Equals(".clinerules", StringComparison.OrdinalIgnoreCase) ||
                                         f.Equals(".editorconfig", StringComparison.OrdinalIgnoreCase) ||
                                         f.Equals(".env.example", StringComparison.OrdinalIgnoreCase)) &&
                                        !f.EndsWith(".user", StringComparison.OrdinalIgnoreCase));
            }

            return query.OrderBy(f => f).ToList();
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"FileService.GetFiles Error: {ex.Message}");
            return new List<string>();
        }
    }


}
