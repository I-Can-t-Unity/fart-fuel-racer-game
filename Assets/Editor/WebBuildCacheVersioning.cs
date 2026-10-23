using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

// Give every generated game file a content-specific URL. A new deployment cannot
// combine an old browser-cached data file with a newer framework or WebAssembly file.
public static class WebBuildCacheVersioning
{
    [PostProcessBuild(100)]
    public static void OnBuild(BuildTarget target, string outputPath)
    {
        if (target == BuildTarget.WebGL) VersionIndex(outputPath);
    }

    public static void VersionIndex(string outputPath)
    {
        string indexPath = Path.Combine(outputPath, "index.html");
        if (!File.Exists(indexPath)) return;
        string html = File.ReadAllText(indexPath);
        int versioned = 0;
        html = Regex.Replace(html, @"buildUrl\s*\+\s*""/([^""?]+)(?:\?v=[a-f0-9]+)?""", match =>
        {
            string fileName = match.Groups[1].Value;
            string path = Path.Combine(outputPath, "Build", fileName);
            if (!File.Exists(path)) return match.Value;
            string hash;
            using (var algorithm = SHA256.Create())
            using (var file = File.OpenRead(path))
                hash = System.BitConverter.ToString(algorithm.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
            versioned++;
            return "buildUrl + \"/" + fileName + "?v=" + hash.Substring(0, 16) + "\"";
        });
        File.WriteAllText(indexPath, html);
        Debug.Log("Versioned " + versioned + " Web build URLs for cache consistency.");
    }
}
