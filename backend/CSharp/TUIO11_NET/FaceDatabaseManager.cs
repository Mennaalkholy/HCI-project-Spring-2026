using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

/// <summary>Read/write face_database.json (same file as face_auth_service.py).</summary>
public class FaceDatabaseManager
{
    private readonly string filePath;
    private readonly JavaScriptSerializer serializer;

    public FaceDatabaseManager(string jsonPath)
    {
        filePath = jsonPath;
        serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
    }

    public static string ResolveDefaultPath()
    {
        return ResolvePathInPythonFolder("face_database.json");
    }

    public static string ResolveAdminFacePath()
    {
        return ResolvePathInPythonFolder("admin_face_database.json");
    }

    private static string ResolvePathInPythonFolder(string fileName)
    {
        string exeDir = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath) ?? ".";
        string candidate = Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", "..", "..", "Python", fileName));
        if (File.Exists(candidate)) return candidate;
        string alt = Path.Combine(
            Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", "..")),
            "Python", fileName);
        return Path.GetFullPath(alt);
    }

    public bool ProfileExists(int tuioId)
    {
        return FindProfile(tuioId) != null;
    }

    public Dictionary<string, object> FindProfile(int tuioId)
    {
        string uid = tuioId.ToString();
        foreach (var row in LoadProfiles())
        {
            if (row == null) continue;
            if (string.Equals(Convert.ToString(row.GetValueOrDefault("id", "")), uid, StringComparison.Ordinal))
                return row;
        }
        return null;
    }

    public List<Dictionary<string, object>> LoadProfiles()
    {
        if (!File.Exists(filePath)) return new List<Dictionary<string, object>>();
        try
        {
            string json = File.ReadAllText(filePath);
            var data = serializer.DeserializeObject(json);
            var list = data as ArrayList;
            if (list == null) return new List<Dictionary<string, object>>();
            var outList = new List<Dictionary<string, object>>();
            foreach (object item in list)
            {
                var dict = item as Dictionary<string, object>;
                if (dict != null) outList.Add(dict);
            }
            return outList;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[FaceDB] Load failed: " + ex.Message);
            return new List<Dictionary<string, object>>();
        }
    }

    public bool SaveProfiles(List<Dictionary<string, object>> profiles)
    {
        try
        {
            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            string json = serializer.Serialize(profiles);
            File.WriteAllText(filePath, json);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[FaceDB] Save failed: " + ex.Message);
            return false;
        }
    }

    public bool DeleteProfile(int tuioId)
    {
        var profiles = LoadProfiles();
        string uid = tuioId.ToString();
        int removed = profiles.RemoveAll(p =>
            string.Equals(Convert.ToString(p.GetValueOrDefault("id", "")), uid, StringComparison.Ordinal));
        if (removed == 0) return true;
        return SaveProfiles(profiles);
    }

    public bool UpdateProfileName(int tuioId, string name)
    {
        var profiles = LoadProfiles();
        string uid = tuioId.ToString();
        bool found = false;
        foreach (var p in profiles)
        {
            if (!string.Equals(Convert.ToString(p.GetValueOrDefault("id", "")), uid, StringComparison.Ordinal))
                continue;
            p["name"] = name ?? ("User " + tuioId);
            found = true;
            break;
        }
        if (!found) return false;
        return SaveProfiles(profiles);
    }

    /// <summary>Copy encoding from source tuio id to target (Option B admin seed).</summary>
    public bool CopyProfile(int fromTuioId, int toTuioId, string displayName)
    {
        var profiles = LoadProfiles();
        string fromUid = fromTuioId.ToString();
        string toUid = toTuioId.ToString();
        Dictionary<string, object> source = null;
        foreach (var p in profiles)
        {
            if (string.Equals(Convert.ToString(p.GetValueOrDefault("id", "")), fromUid, StringComparison.Ordinal))
            {
                source = p;
                break;
            }
        }
        if (source == null || !source.ContainsKey("encoding"))
            return false;

        object enc = source["encoding"];
        int existingIdx = -1;
        for (int i = 0; i < profiles.Count; i++)
        {
            if (string.Equals(Convert.ToString(profiles[i].GetValueOrDefault("id", "")), toUid, StringComparison.Ordinal))
            {
                existingIdx = i;
                break;
            }
        }

        string now = DateTime.Now.ToString("o");
        var row = new Dictionary<string, object>
        {
            { "id", toUid },
            { "name", displayName ?? ("User " + toTuioId) },
            { "encoding", enc },
            { "created_at", now },
            { "last_seen", null },
            { "recognition_count", 0 }
        };

        if (existingIdx >= 0)
            profiles[existingIdx] = row;
        else
            profiles.Add(row);

        return SaveProfiles(profiles);
    }
}

static class DictionaryExtensions
{
    public static object GetValueOrDefault(this Dictionary<string, object> dict, string key, object defaultValue)
    {
        if (dict != null && dict.TryGetValue(key, out object v)) return v;
        return defaultValue;
    }
}
