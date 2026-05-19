using System;
using System.Collections.Generic;
using System.IO;

/// <summary>Admin accounts live in admins.json only — never in users.json.</summary>
public class AdminAccount
{
    public const int DefaultAdminTuioId = 900000;

    public int TuioId { get; set; }
    public string Name { get; set; }
    public string CreatedAt { get; set; }
}

public class AdminManager
{
    private readonly string filePath;
    private readonly List<AdminAccount> admins = new List<AdminAccount>();

    public AdminManager(string jsonPath)
    {
        filePath = jsonPath;
        Load();
    }

    public static string ResolveDefaultPath()
    {
        string exeDir = System.Windows.Forms.Application.StartupPath ?? ".";
        return Path.Combine(exeDir, "admins.json");
    }

    public void Load()
    {
        admins.Clear();
        if (!File.Exists(filePath)) return;

        string json = File.ReadAllText(filePath);
        int idx = json.IndexOf("\"admins\"");
        if (idx < 0) return;
        int arrStart = json.IndexOf('[', idx);
        if (arrStart < 0) return;
        int arrEnd = json.LastIndexOf(']');
        if (arrEnd < 0) return;
        string arrContent = json.Substring(arrStart + 1, arrEnd - arrStart - 1);

        string[] parts = arrContent.Split(new[] { "}," }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string part in parts)
        {
            string obj = part.Trim().TrimStart('{').TrimEnd('}').Trim();
            if (string.IsNullOrWhiteSpace(obj)) continue;
            AdminAccount a = ParseAdminObject(obj);
            if (a != null) admins.Add(a);
        }
    }

    private static AdminAccount ParseAdminObject(string obj)
    {
        try
        {
            var a = new AdminAccount();
            a.TuioId = GetIntValue(obj, "tuioId");
            a.Name = GetStringValue(obj, "name");
            a.CreatedAt = GetStringValue(obj, "createdAt");
            if (string.IsNullOrEmpty(a.Name)) a.Name = "admin";
            if (a.TuioId > 0) return a;
        }
        catch { }
        return null;
    }

    private static string GetStringValue(string json, string key)
    {
        int ki = json.IndexOf("\"" + key + "\"");
        if (ki < 0) return "";
        int colon = json.IndexOf(':', ki);
        if (colon < 0) return "";
        int q1 = json.IndexOf('"', colon + 1);
        if (q1 < 0) return "";
        int q2 = json.IndexOf('"', q1 + 1);
        if (q2 < 0) return "";
        return json.Substring(q1 + 1, q2 - q1 - 1);
    }

    private static int GetIntValue(string json, string key)
    {
        int ki = json.IndexOf("\"" + key + "\"");
        if (ki < 0) return 0;
        int colon = json.IndexOf(':', ki);
        if (colon < 0) return 0;
        string rest = json.Substring(colon + 1).Trim();
        string numStr = "";
        foreach (char c in rest)
        {
            if (char.IsDigit(c)) numStr += c;
            else if (numStr.Length > 0) break;
        }
        int val;
        int.TryParse(numStr, out val);
        return val;
    }

    public void Save()
    {
        var lines = new List<string>();
        lines.Add("{");
        lines.Add("  \"admins\": [");
        for (int i = 0; i < admins.Count; i++)
        {
            var a = admins[i];
            string comma = (i < admins.Count - 1) ? "," : "";
            lines.Add("    {");
            lines.Add("      \"tuioId\": " + a.TuioId + ",");
            lines.Add("      \"name\": \"" + EscapeJson(a.Name) + "\",");
            lines.Add("      \"createdAt\": \"" + EscapeJson(a.CreatedAt) + "\"");
            lines.Add("    }" + comma);
        }
        lines.Add("  ]");
        lines.Add("}");
        File.WriteAllText(filePath, string.Join("\n", lines));
    }

    private static string EscapeJson(string s)
    {
        if (s == null) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    public AdminAccount FindByTuioId(int tuioId)
    {
        foreach (var a in admins)
            if (a.TuioId == tuioId) return a;
        return null;
    }

    public bool IsAdminTuioId(int tuioId)
    {
        return FindByTuioId(tuioId) != null;
    }

    public void EnsureDefaultAdmin()
    {
        Load();
        if (FindByTuioId(AdminAccount.DefaultAdminTuioId) != null) return;
        admins.Add(new AdminAccount
        {
            TuioId = AdminAccount.DefaultAdminTuioId,
            Name = "admin",
            CreatedAt = DateTime.Now.ToString("o")
        });
        Save();
    }

    public List<AdminAccount> AllAdmins { get { return admins; } }
}
