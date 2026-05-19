using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.ComponentModel;
using System.Collections.Generic;
using System.Threading;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Diagnostics;
using System.Globalization;
using TUIO;


public class UserData
{
    public const int AdminTuioId = 900000;

    public int TuioId { get; set; }
    public string Name { get; set; }
    public string Gender { get; set; }
    public string AgeGroup { get; set; }
    public int MaxCompletedLevel { get; set; }
    public string CreatedAt { get; set; }
    /// <summary>"admin" for dashboard access; empty or "user" for players.</summary>
    public string Role { get; set; }

    public bool IsAdmin()
    {
        if (TuioId == AdminTuioId) return true;
        return string.Equals(Role ?? "", "admin", StringComparison.OrdinalIgnoreCase);
    }
}

public class UserManager
{
    private string filePath;
    private List<UserData> users = new List<UserData>();

    public UserManager(string jsonPath)
    {
        filePath = jsonPath;
        Load();
    }

    public void Load()
    {
        users.Clear();
        if (!File.Exists(filePath)) return;

        string json = File.ReadAllText(filePath);
        int idx = json.IndexOf("\"users\"");
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
            UserData u = ParseUserObject(obj);
            if (u != null) users.Add(u);
        }
    }

    private UserData ParseUserObject(string obj)
    {
        try
        {
            UserData u = new UserData();
            u.TuioId = GetIntValue(obj, "tuioId");
            u.Name = GetStringValue(obj, "name");
            u.Gender = GetStringValue(obj, "gender");
            u.MaxCompletedLevel = GetIntValue(obj, "maxCompletedLevel");
            string explicitAgeGroup = GetStringValue(obj, "ageGroup");
            if (string.IsNullOrWhiteSpace(explicitAgeGroup))
                explicitAgeGroup = AgeGroupFromAge(GetIntValue(obj, "age")); // backward compatibility with old users.json
            u.AgeGroup = NormalizeAgeGroup(explicitAgeGroup);
            if (string.IsNullOrEmpty(u.Name)) u.Name = "User " + u.TuioId;
            u.CreatedAt = GetStringValue(obj, "createdAt");
            u.Role = GetStringValue(obj, "role");
            if (u.TuioId > 0) return u;
        }
        catch { }
        return null;
    }

    private string GetStringValue(string json, string key)
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

    private int GetIntValue(string json, string key)
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
        lines.Add("  \"users\": [");
        for (int i = 0; i < users.Count; i++)
        {
            var u = users[i];
            string comma = (i < users.Count - 1) ? "," : "";
            lines.Add("    {");
            lines.Add("      \"tuioId\": " + u.TuioId + ",");
            lines.Add("      \"name\": \"" + EscapeJson(u.Name) + "\",");
            lines.Add("      \"gender\": \"" + EscapeJson(u.Gender) + "\",");
            lines.Add("      \"ageGroup\": \"" + EscapeJson(u.AgeGroup) + "\",");
            lines.Add("      \"maxCompletedLevel\": " + u.MaxCompletedLevel + ",");
            lines.Add("      \"createdAt\": \"" + EscapeJson(u.CreatedAt) + "\",");
            string role = string.IsNullOrWhiteSpace(u.Role) ? "user" : u.Role.Trim();
            lines.Add("      \"role\": \"" + EscapeJson(role) + "\"");
            lines.Add("    }" + comma);
        }
        lines.Add("  ]");
        lines.Add("}");
        File.WriteAllText(filePath, string.Join("\n", lines));
    }

    private string EscapeJson(string s)
    {
        if (s == null) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    public UserData FindByTuioId(int tuioId)
    {
        foreach (var u in users)
            if (u.TuioId == tuioId) return u;
        return null;
    }

    public static bool IsReservedControlTuioId(int tuioId)
    {
        // TUIO controls used by the app.
        int[] reserved = { 0, 1, 2, 4, 5, 6, 7, 10 };
        for (int i = 0; i < reserved.Length; i++)
            if (reserved[i] == tuioId) return true;
        return false;
    }

    private static string NormalizeAgeGroup(string ageGroup)
    {
        string g = (ageGroup ?? "").Trim().ToLowerInvariant();
        if (g == "child" || g == "teen" || g == "adult") return g;
        return "adult";
    }

    private static string AgeGroupFromAge(int age)
    {
        if (age > 0 && age < 13) return "child";
        if (age > 0 && age < 20) return "teen";
        return "adult";
    }

    public UserData Signup(int tuioId, string gender, string ageGroup)
    {
        UserData u = new UserData
        {
            TuioId = tuioId,
            Name = "User " + tuioId,
            Gender = string.IsNullOrEmpty(gender) ? "unspecified" : gender,
            AgeGroup = NormalizeAgeGroup(ageGroup),
            MaxCompletedLevel = 0,
            CreatedAt = DateTime.Now.ToString("o"),
            Role = "user"
        };
        users.Add(u);
        Save();
        return u;
    }

    public bool AddUser(UserData u)
    {
        if (u == null || u.TuioId <= 0 || FindByTuioId(u.TuioId) != null) return false;
        if (IsReservedControlTuioId(u.TuioId)) return false;
        if (string.IsNullOrWhiteSpace(u.Role)) u.Role = "user";
        users.Add(u);
        Save();
        return true;
    }

    public bool DeleteUser(int tuioId)
    {
        if (tuioId == AdminAccount.DefaultAdminTuioId) return false;
        UserData u = FindByTuioId(tuioId);
        if (u == null) return false;
        if (u.IsAdmin()) return false;
        users.Remove(u);
        Save();
        return true;
    }

    /// <summary>Removes any row from users.json (e.g. migrate admin out of player list).</summary>
    public bool RemoveFromUsersList(int tuioId)
    {
        UserData u = FindByTuioId(tuioId);
        if (u == null) return true;
        users.Remove(u);
        Save();
        return true;
    }

    public static bool IsAdminUser(UserData u)
    {
        return u != null && u.IsAdmin();
    }

    public void MarkLevelCompleted(int tuioId, int level)
    {
        if (level <= 0) return;
        UserData u = FindByTuioId(tuioId);
        if (u == null) return;
        if (u.MaxCompletedLevel < level)
        {
            u.MaxCompletedLevel = level;
            Save();
        }
    }

    /// <summary>Copy of the in-memory list (safe to enumerate while the manager reloads).</summary>
    public List<UserData> AllUsers { get { return new List<UserData>(users); } }

    /// <summary>Reserved range for MediaPipe / keyboard sign-up (avoids clashing with physical TUIO markers).</summary>
    public int NextSyntheticTuioId()
    {
        Load();
        for (int id = 900001; id < int.MaxValue - 1; id++)
        {
            if (FindByTuioId(id) == null) return id;
        }
        return 900001;
    }
}
 
public class ThemeManager
{
    public bool IsDarkMode { get; private set; }
    public string AgeGroup { get; private set; }

    public Color Background { get; private set; }
    public Color CardBackground { get; private set; }
    public Color TextPrimary { get; private set; }
    public Color TextSecondary { get; private set; }
    public Color Accent { get; private set; }
    public Color AccentSecondary { get; private set; }
    public Color BarBackground { get; private set; }
    public Color BarSelectedBackground { get; private set; }
    public Color BorderColor { get; private set; }
    public Color SelectedBorder { get; private set; }
    public Color SuccessColor { get; private set; }
    public Color ErrorColor { get; private set; }
    public Color ButtonBackground { get; private set; }
    public Color ButtonText { get; private set; }

    public float TitleFontSize { get; private set; }
    public float BodyFontSize { get; private set; }
    public float SmallFontSize { get; private set; }
    public float GameItemFontSize { get; private set; }

    public ThemeManager()
    {
        AgeGroup = "adult";
        Update();
    }

    public void SetAgeGroup(string ageGroup)
    {
        AgeGroup = ageGroup ?? "adult";
        Update();
    }

    public void Update()
    {
        int hour = DateTime.Now.Hour;
        IsDarkMode = (hour < 6 || hour >= 18);
        ApplyAgeTheme();
    }

    private void ApplyAgeTheme()
    {
        switch (AgeGroup)
        {
            case "child":
                TitleFontSize = 28f; BodyFontSize = 18f; SmallFontSize = 13f; GameItemFontSize = 22f;
                break;
            case "teen":
                TitleFontSize = 24f; BodyFontSize = 16f; SmallFontSize = 11f; GameItemFontSize = 18f;
                break;
            default:
                TitleFontSize = 22f; BodyFontSize = 14f; SmallFontSize = 10f; GameItemFontSize = 16f;
                break;
        }
        if (IsDarkMode) ApplyDarkColors();
        else ApplyLightColors();
    }

    private void ApplyLightColors()
    {
        switch (AgeGroup)
        {
            case "child":
                Background = Color.FromArgb(255, 250, 240);
                CardBackground = Color.White;
                TextPrimary = Color.FromArgb(50, 50, 70);
                TextSecondary = Color.FromArgb(120, 120, 140);
                Accent = Color.FromArgb(255, 100, 50);
                AccentSecondary = Color.FromArgb(50, 200, 100);
                BarBackground = Color.FromArgb(255, 180, 80);
                BarSelectedBackground = Color.FromArgb(255, 120, 60);
                BorderColor = Color.FromArgb(255, 160, 60);
                SelectedBorder = Color.FromArgb(50, 200, 100);
                SuccessColor = Color.FromArgb(30, 180, 60);
                ErrorColor = Color.FromArgb(230, 60, 60);
                ButtonBackground = Color.FromArgb(255, 130, 50);
                ButtonText = Color.White;
                break;
            case "teen":
                Background = Color.FromArgb(240, 242, 248);
                CardBackground = Color.White;
                TextPrimary = Color.FromArgb(40, 40, 60);
                TextSecondary = Color.FromArgb(100, 100, 130);
                Accent = Color.FromArgb(80, 100, 220);
                AccentSecondary = Color.FromArgb(0, 180, 200);
                BarBackground = Color.FromArgb(60, 70, 110);
                BarSelectedBackground = Color.FromArgb(80, 100, 180);
                BorderColor = Color.FromArgb(120, 130, 180);
                SelectedBorder = Color.FromArgb(0, 200, 220);
                SuccessColor = Color.FromArgb(0, 180, 100);
                ErrorColor = Color.FromArgb(220, 50, 70);
                ButtonBackground = Color.FromArgb(80, 100, 220);
                ButtonText = Color.White;
                break;
            default:
                Background = Color.FromArgb(245, 245, 248);
                CardBackground = Color.White;
                TextPrimary = Color.FromArgb(30, 30, 40);
                TextSecondary = Color.FromArgb(100, 100, 110);
                Accent = Color.FromArgb(40, 80, 140);
                AccentSecondary = Color.FromArgb(60, 140, 120);
                BarBackground = Color.FromArgb(50, 55, 70);
                BarSelectedBackground = Color.FromArgb(40, 80, 130);
                BorderColor = Color.FromArgb(160, 160, 175);
                SelectedBorder = Color.FromArgb(40, 160, 80);
                SuccessColor = Color.FromArgb(30, 130, 60);
                ErrorColor = Color.FromArgb(180, 40, 40);
                ButtonBackground = Color.FromArgb(40, 80, 140);
                ButtonText = Color.White;
                break;
        }
    }

    private void ApplyDarkColors()
    {
        switch (AgeGroup)
        {
            case "child":
                Background = Color.FromArgb(35, 30, 50);
                CardBackground = Color.FromArgb(55, 50, 70);
                TextPrimary = Color.FromArgb(255, 240, 220);
                TextSecondary = Color.FromArgb(180, 170, 160);
                Accent = Color.FromArgb(255, 140, 60);
                AccentSecondary = Color.FromArgb(80, 220, 130);
                BarBackground = Color.FromArgb(80, 50, 30);
                BarSelectedBackground = Color.FromArgb(140, 70, 30);
                BorderColor = Color.FromArgb(120, 80, 50);
                SelectedBorder = Color.FromArgb(80, 220, 130);
                SuccessColor = Color.FromArgb(80, 220, 100);
                ErrorColor = Color.FromArgb(255, 80, 80);
                ButtonBackground = Color.FromArgb(255, 130, 50);
                ButtonText = Color.White;
                break;
            case "teen":
                Background = Color.FromArgb(22, 24, 35);
                CardBackground = Color.FromArgb(35, 38, 55);
                TextPrimary = Color.FromArgb(230, 230, 245);
                TextSecondary = Color.FromArgb(140, 140, 170);
                Accent = Color.FromArgb(120, 100, 255);
                AccentSecondary = Color.FromArgb(0, 220, 240);
                BarBackground = Color.FromArgb(30, 32, 55);
                BarSelectedBackground = Color.FromArgb(60, 55, 120);
                BorderColor = Color.FromArgb(70, 70, 110);
                SelectedBorder = Color.FromArgb(0, 220, 240);
                SuccessColor = Color.FromArgb(0, 220, 140);
                ErrorColor = Color.FromArgb(255, 70, 90);
                ButtonBackground = Color.FromArgb(120, 100, 255);
                ButtonText = Color.White;
                break;
            default:
                Background = Color.FromArgb(28, 28, 35);
                CardBackground = Color.FromArgb(40, 40, 50);
                TextPrimary = Color.FromArgb(220, 220, 230);
                TextSecondary = Color.FromArgb(140, 140, 155);
                Accent = Color.FromArgb(70, 130, 220);
                AccentSecondary = Color.FromArgb(60, 180, 140);
                BarBackground = Color.FromArgb(35, 38, 48);
                BarSelectedBackground = Color.FromArgb(40, 70, 120);
                BorderColor = Color.FromArgb(70, 70, 85);
                SelectedBorder = Color.FromArgb(60, 180, 100);
                SuccessColor = Color.FromArgb(60, 180, 100);
                ErrorColor = Color.FromArgb(200, 60, 60);
                ButtonBackground = Color.FromArgb(70, 130, 220);
                ButtonText = Color.White;
                break;
        }
    }
}
 
public class GameContentManager
{
    public string[] CountryNames { get; private set; }
    public string[] FlagPaths { get; private set; }
    public string LevelLabel { get; private set; }
    public string DifficultyLabel { get; private set; }
    public string[] LandmarkPaths { get; private set; }

    public void LoadForAgeGroup(string ageGroup, int level = 1)
    {
        switch (ageGroup)
        {
            case "child":
                CountryNames = new string[] { "France", "Italy", "Spain", "Egypt", "Germany", "England", "Japan", "Saudi Arabia" };
                FlagPaths = new string[] { "france_flag.png", "italy_flag.png", "spain_flag.png", "egypt_flag.png", "germany_flag.png", "england_flag.jpg", "japan_flag.png", "saudi_arabia_flag.png" };
                LandmarkPaths = new string[] { "france_landM.jpg", "italy_landM.jpg", "spain_landM.jpg", "egypt_landM.jpg", "germany_landM.jpg", "england_landM.jpg", "japan_landM.jpg", "Saudi_Arabia_landM2.jpg" };
                if (level == 1)
                {
                    LevelLabel = "Level 1 — Easy";
                    DifficultyLabel = "Match the country to its flag!";
                }
                else
                {
                    LevelLabel = "Level 2 — Easy";
                    DifficultyLabel = "Match the landmark to its flag!";
                }
                break;
            case "teen":
                CountryNames = new string[] { "Turkey", "India", "South Korea", "Portugal", "Greece", "Mexico", "Norway", "Thailand", "UAE", "Saudi Arabia" };
                FlagPaths = new string[] { "turkey_flag.png", "india-flag.png", "Korea_flag.png", "portugal_flag.png", "greece_flag.png", "mexico_flag.png", "norway_flag.png", "thiland_flag.png" , "UAE_flag.png", "saudi_arabia_flag.png"};
                LandmarkPaths = new string[] { "turkey_landM.jpg", "india_landM.jpg", "Korea-landM.jpg", "portugal_landM.jpg", "greece_landM.jpg", "mexico_landM.jpg" , "norway_landM.jpg", "thiland_landM.jpg", "UAE_landM.jpg", "saudi_arabia_landM.jpg" };
                if (level == 1)
                {
                    LevelLabel = "Level 1 — Medium";
                    DifficultyLabel = "Match countries with their flags. Use TUIO 1 to scroll, TUIO 0 to switch, TUIO 2 to confirm.";
                }
                else
                {
                    LevelLabel = "Level 2 — Medium";
                    DifficultyLabel = "Match landmarks with their flags. Use TUIO 1 to scroll, TUIO 0 to switch, TUIO 2 to confirm.";
                }
                break;
            default:
                CountryNames = new string[] { "Denmark", "Netherlands", "Ghana", "Madagascar", "Indonesia", "Belgium", "Sweden", "Malaysia" };
                FlagPaths = new string[] { "denmark_flag.png", "netherlands_flag.png", "ghana_flag.png", "madagascar_flag.png", "Indonesia_flag.png", "belgium_flag.png", "sweden_flag.png", "Malaysia_flag.png" };
                LandmarkPaths = new string[] { "denmark_landM.jpg", "netherland-landM.jpg", "ghana_landM.jpg", "madagascar_landM.jpg", "Indonesia_landM.jpg", "belgium_landM.jpg", "sweden_landM.jpg", "Malaysia_landM.jpg" };
                if (level == 1)
                {
                    LevelLabel = "Level 1 — Hard";
                    DifficultyLabel = "Match all countries to flags. Rotate TUIO 1 to navigate, TUIO 0 to switch bar, TUIO 2 to confirm.";
                }
                else
                {
                    LevelLabel = "Level 2 — Hard";
                    DifficultyLabel = "Match landmarks to flags. Rotate TUIO 1 to navigate, TUIO 0 to switch bar, TUIO 2 to confirm.";
                }
                break;
        }
    }
}

/* TUIO MAIN*/
/// <summary>One key on the sign-up name virtual keyboard (layout rebuilt each frame).</summary>
internal sealed class SignupKeyboardKey
{
    public string Action;
    public string Primary;
    public string Secondary;
    public float Weight = 1f;
    public Rectangle Bounds;
}

public class TuioDemo : Form, TuioListener
{
    private TuioClient client;
    private Dictionary<long, TuioDemoObject> objectList;
    private Dictionary<long, TuioCursor> cursorList;
    private Dictionary<long, TuioBlob> blobList;
    private object cursorSync = new object();
    private object objectSync = new object();
    private object blobSync = new object();

    public static int width, height;
    private int window_width = 1280;
    private int window_height = 800;
    private int window_left = 0;
    private int window_top = 0;
    private int screen_width = Screen.PrimaryScreen.Bounds.Width;
    private int screen_height = Screen.PrimaryScreen.Bounds.Height;
    private bool fullscreen;
    private bool verbose;
 
    private UserManager userManager;
    private AdminManager adminManager;
    private FaceDatabaseManager faceDatabaseManager;
    private FaceDatabaseManager adminFaceDatabaseManager;
    private AdminAccount currentAdmin;
    private ThemeManager theme;
    private GameContentManager gameContent;
    private GameSoundManager gameSounds;
    private UserData currentUser;

 
    private const int ScreenModeSelect = 0;
    private const int ScreenSignup = 1;
    private const int ScreenGreeting = 2;
    private const int ScreenGame = 3;
    private const int ScreenLevelTransition = 4;
    /// <summary>Waiting for face_auth (Camo) — no TUIO marker login.</summary>
    private const int ScreenFaceIdentify = 10;
    private const int ScreenAdminUsers = 11;
    /// <summary>On-screen keyboard name entry after gender / age.</summary>
    private const int ScreenSignupName = 12;
    private const int ScreenAdminEditName = 13;

    private int screenState = ScreenModeSelect;
    private bool mediapipeSocketConnected = false;
    private bool mediapipeCameraActive = false;
    private Process mediapipeCameraProcess;
    private bool mediapipeCameraProcessStartedByApp = false;
    private bool modeAutoSelected = false;
    private string modeDetectStatus = "Detecting input mode...";
    private bool hasPendingMediapipeUser = false;
    private int pendingMediapipeTuioId = 0;
    private string pendingMediapipeName = "";
    private string pendingMediapipeAgeGroup = "adult";
    private string pendingMediapipeGender = "unspecified";
    private bool pendingMediapipeExists = false;
    private bool pendingMediapipeIsAdmin = false;

 
    private string loginError = "";
    private SocketClient socketClient;

    /// <summary>When true, sign-up uses keyboard only (no dial). MediaPipe path uses mediapipeSignupHybrid instead.</summary>
    private bool inputModeMediapipe = false;

    /// <summary>Option 2: same dial sign-up as TUIO, but dial rotates via MediaPipe (virtual TUIO 4); keyboard replaces TUIO 5–7.</summary>
    private bool mediapipeSignupHybrid = false;

    /// <summary>True after choosing MediaPipe (option 2); used to lock out keyboard during play.</summary>
    private bool sessionMediapipeMode = false;

   
    private int signupTuioId = 0;
    private string signupGender = "";
    private string signupAgeGroup = "";
    private int signupField = 0;        // 0 = gender, 1 = age group
    private string signupError = "";
    private bool signupFaceReady = false;
    private string signupFaceStatus = "";
    private string signupPhysicalTuioStatus = "";
    private string faceIdentifyHint = "";
    private int dialIndex = 0;            
    private float lastTuio4Angle = float.MinValue;
    private float dialRotationAccum = 0f;
    private const float dialRotationThreshold = 0.22f;
    private bool tuio5WasPresent = false; // add character
    private bool tuio6WasPresent = false; // backspace
    private bool tuio7WasPresent = false; // switch field / submit

    private const int SignupNameMaxLength = 24;
    private string signupName = "";
    private bool signupKeyboardCapsLock = false;
    private readonly List<SignupKeyboardKey> signupKeyboardKeys = new List<SignupKeyboardKey>(80);
    private int signupKeyboardHoveredIndex = -1;
    private int signupKeyboardLayoutSignature = -1;
    private Rectangle signupNameTextBoxRect = Rectangle.Empty;
    private Rectangle signupKeyboardPanelRect = Rectangle.Empty;
    private bool signupNameTuio6WasPresent = false;
    private bool signupNameTuio7WasPresent = false;
    private const int SignupKeyHoldMs = 4000;
    private int signupKeyHoldTargetIndex = -1;
    private DateTime signupKeyHoldSince = DateTime.MinValue;
    private DateTime signupKeyHoldCooldownUntil = DateTime.MinValue;

    private static readonly Color SignupKbPanelBg = Color.FromArgb(255, 26, 29, 33);
    private static readonly Color SignupKbKeyBg = Color.FromArgb(255, 60, 76, 85);
    private static readonly Color SignupKbKeyHoverBg = Color.FromArgb(255, 82, 98, 108);
    private static readonly Color SignupKbCapsBg = Color.FromArgb(255, 98, 0, 238);
    private static readonly Color SignupKbKeyText = Color.White;

  
    private static readonly string[] SIGNUP_GENDERS = { "Male", "Female" };
    private static readonly string[] SIGNUP_AGE_GROUPS = { "Child (9-12)", "Teen (13-19)", "Adult (20-50)" };

   
    private System.Windows.Forms.Timer greetingTimer;
    private const int greetingDurationMs = 1000;

 
    private List<string> remainingCountries = new List<string>();
    private List<string> remainingFlags = new List<string>();
    private List<string> remainingLandmarks = new List<string>();
    private int countryNameIndex = 0;
    private int flagIndex = 0;
    private int landmarkIndex = 0;
    private Dictionary<string, string> countryToFlagMap = new Dictionary<string, string>();
    private Dictionary<string, string> landmarkToFlagMap = new Dictionary<string, string>();
    private Dictionary<string, Image> flagImageCacheByPath = new Dictionary<string, Image>();
    private Dictionary<string, Point> childFlagRandomPositions = new Dictionary<string, Point>();
    private int childFlagLayoutSignature = -1;
    private Dictionary<string, Point> level2LandmarkRandomPositions = new Dictionary<string, Point>();
    private int level2LandmarkLayoutSignature = -1;
    private const int ChildFlagWidth = 110;
    private const int ChildFlagHeight = 80;
    private const int ChildFlagMargin = 24;
    private const int PlaneHoverSelectMs = 3000;
    private string hoveredFlagForPlane = "";
    private DateTime hoveredFlagSince = DateTime.MinValue;
    private string level2PlaneFlagImagePath = "";
    private static Random rng = new Random();
    private const int TuioMouseSymbolId = 10;
    private const int TuioGazeSymbolId = 11;
    private const int GazeMarkerDiameter = 34;
    private const float GazeMarkerSmooth = 0.16f;
    private float gazeMarkerDrawX = -1f;
    private float gazeMarkerDrawY = -1f;
    /// <summary>Latest gaze dot center in client pixels (for country hint bubble during sad / confused).</summary>
    private int gazeHintScreenX = -1;
    private int gazeHintScreenY = -1;
    /// <summary>Hub debouncer does not re-send the same label; moods can last minutes while timestamps go stale.</summary>
    private const double FacialHintMoodStaleSeconds = 300.0;
    /// <summary>Gaze TUIO briefly drops when face track is lost; keep hint anchored on last good dot.</summary>
    private const double GazeHintLatchSeconds = 8.0;
    private const int HintPoint8FallbackHoldMs = 900;
    private int gazeLatchScreenX = -1;
    private int gazeLatchScreenY = -1;
    private DateTime gazeLatchUpdatedAt = DateTime.MinValue;
    /// <summary>After a wrong match on level 1, show the complementary visual hint.</summary>
    private bool countryHintAfterLifeLoss = false;
    private const string TuioMouseImagePath = "tayara.png";
    private bool tuioMouseImageMissingLogged = false;
    /// <summary>True after we queue a delayed start of reacTIVision (reset on Logout).</summary>
    private bool reacTIVisionLaunchScheduled = false;
    private bool mediapipePoint8Active = false;
    private float mediapipePoint8X = 0.5f;
    private float mediapipePoint8Y = 0.5f;
    private DateTime mediapipePoint8UpdatedAt = DateTime.MinValue;
    private float mediapipeLastActivePoint8X = 0.5f;
    private float mediapipeLastActivePoint8Y = 0.5f;
    private DateTime mediapipeLastActivePoint8At = DateTime.MinValue;
    /// <summary>True when name keyboard is driven by webcam laser dot (hub keyboard_pointer_source).</summary>
    private bool keyboardPointerIsLaser = false;
    private const int MediapipePointerFreshMs = 250;
    private const int MediapipePointerHoldMs = 120;
    private const int MediapipePointerHoldSignupNameMs = 450;
    /// <summary>Latest mood from tuio_only_service Face Mesh (hub event user_facial_emotion).</summary>
    private string facialEmotionMood = "neutral";
    private float facialEmotionScore = 0f;
    private string facialEmotionContext = "";
    /// <summary>Short banner e.g. HAPPY MODE / CALM MODE.</summary>
    private string facialEmotionModeLabel = "";
    private DateTime facialEmotionUpdatedAt = DateTime.MinValue;
    private System.Windows.Forms.Timer emotionPaintTimer;
    private bool levelFinished = false;
    private int activeBar = 0;
    private bool tuio0WasPresent = false;
    private bool tuio2WasPresent = false;
    private bool adminTuio0WasPresent = false;
    private bool adminSessionActive = false;
    private int adminSelectedTuioId = -1;
    private bool adminCreatingNew = false;
    private string adminEditName = "";
    private string adminEditGender = "unspecified";
    private string adminEditAgeGroup = "adult";
    private int adminEditMaxLevel = 0;
    private string adminStatusMessage = "";
    private readonly List<KeyValuePair<Rectangle, int>> adminUserCardHits = new List<KeyValuePair<Rectangle, int>>();
    private Rectangle adminBtnNewRect = Rectangle.Empty;
    private Rectangle adminBtnSaveRect = Rectangle.Empty;
    private Rectangle adminBtnDeleteRect = Rectangle.Empty;
    private Rectangle adminBtnExitRect = Rectangle.Empty;
    private Rectangle adminGenderMaleRect = Rectangle.Empty;
    private Rectangle adminGenderFemaleRect = Rectangle.Empty;
    private Rectangle adminAgeChildRect = Rectangle.Empty;
    private Rectangle adminAgeTeenRect = Rectangle.Empty;
    private Rectangle adminAgeAdultRect = Rectangle.Empty;
    private Rectangle adminLevelMinusRect = Rectangle.Empty;
    private Rectangle adminLevelPlusRect = Rectangle.Empty;
    private Rectangle adminBtnEditNameRect = Rectangle.Empty;
    private const int AdminPointerHoldMs = 4000;
    private string adminPointerHoverTarget = "";
    private string adminPointerHoldTarget = "";
    private DateTime adminPointerHoldSince = DateTime.MinValue;
    private DateTime adminPointerHoldCooldownUntil = DateTime.MinValue;

    private float lastTuio1Angle = float.MinValue;
    private const int barWidth = 180;
    private const float rotationThreshold = 0.35f;
    private float rotationAccum = 0f;

    private int currentLevel = 1;
    private int transitionPhase = 0;
    private System.Windows.Forms.Timer levelTransitionTimer;

    private int maxLives = 3;
    private int currentLives = 3;
    private bool gameOver = false;

    // Theme refresh timer
    private System.Windows.Forms.Timer themeRefreshTimer;

    public TuioDemo(int port)
    {
        verbose = true;
        fullscreen = false;
        width = window_width + 30;
        height = window_height + 30;

        this.ClientSize = new Size(width, height);
        this.Name = "TuioDemo";
        this.Text = "Matching Countries";

        this.Closing += new CancelEventHandler(Form_Closing);
        this.KeyDown += new KeyEventHandler(Form_KeyDown);
        this.MouseClick += new MouseEventHandler(Form_MouseClick);

        this.SetStyle(ControlStyles.AllPaintingInWmPaint |
                      ControlStyles.UserPaint |
                      ControlStyles.DoubleBuffer, true);
        this.KeyPreview = true;

        objectList = new Dictionary<long, TuioDemoObject>(128);
        cursorList = new Dictionary<long, TuioCursor>(128);
        blobList = new Dictionary<long, TuioBlob>(128);
         
        string jsonPath = Path.Combine(
            Path.GetDirectoryName(Application.ExecutablePath) ?? ".", "users.json");
        userManager = new UserManager(jsonPath);
        adminManager = new AdminManager(AdminManager.ResolveDefaultPath());
        faceDatabaseManager = new FaceDatabaseManager(FaceDatabaseManager.ResolveDefaultPath());
        adminFaceDatabaseManager = new FaceDatabaseManager(FaceDatabaseManager.ResolveAdminFacePath());
        EnsureAdminAccountSeeded();
        theme = new ThemeManager();
        gameContent = new GameContentManager();
        gameSounds = new GameSoundManager(this);

        // Timers
        greetingTimer = new System.Windows.Forms.Timer();
        greetingTimer.Interval = greetingDurationMs;
        greetingTimer.Tick += (s, ev) =>
        {
            greetingTimer.Stop();
            if (screenState != ScreenGreeting) return;
            StartGameFromSavedProgress();
            if (sessionMediapipeMode && currentUser != null)
                socketClient.SendEvent("mediapipe_game_started", "\"level\": " + currentLevel);
            Invalidate();
        };

        levelTransitionTimer = new System.Windows.Forms.Timer();
        levelTransitionTimer.Interval = 1000;
        levelTransitionTimer.Tick += (s, ev) =>
        {
            transitionPhase++;
            if (transitionPhase >= 6)
            {
                levelTransitionTimer.Stop();
                if (gameSounds != null)
                    gameSounds.StopLevelTransition();
                StartLevel2();
            }
            else
            {
                Invalidate();
            }
        };

        themeRefreshTimer = new System.Windows.Forms.Timer();
        themeRefreshTimer.Interval = 60000;
        themeRefreshTimer.Tick += (s, ev) => { theme.Update(); Invalidate(); };
        themeRefreshTimer.Start();

        emotionPaintTimer = new System.Windows.Forms.Timer();
        emotionPaintTimer.Interval = 380;
        emotionPaintTimer.Tick += EmotionPaintTimer_Tick;

        client = new TuioClient(port);
        client.addTuioListener(this);
        client.connect();

        string notifyHost = System.Environment.GetEnvironmentVariable("MEDIAPIPE_NOTIFY_HOST") ?? "127.0.0.1";
        string notifyPortRaw = System.Environment.GetEnvironmentVariable("MEDIAPIPE_NOTIFY_PORT");
        int notifyPort = 5000;
        if (!string.IsNullOrEmpty(notifyPortRaw))
            int.TryParse(notifyPortRaw, out notifyPort);
        if (notifyPort <= 0) notifyPort = 5000;
        Console.WriteLine("[Socket] Connecting to MediaPipe notify server " + notifyHost + ":" + notifyPort);
        socketClient = new SocketClient(notifyHost, notifyPort, this);
        socketClient.OnLineReceived += HandleSocketLine;
        socketClient.OnConnectionStateChanged += (ok) =>
        {
            mediapipeSocketConnected = ok;
            if (!ok)
            {
                // The notify socket dropped (e.g., face_recognition_test.py closed).
                // Mark camera inactive so handoff logic can launch the main MediaPipe script.
                mediapipeCameraActive = false;

                // If we already recognized a user, continue the flow by launching camera mode.
                if (hasPendingMediapipeUser)
                {
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        Thread.Sleep(350);
                        EnsureMediapipeCameraRunning();
                    });
                }
            }
            if (!modeAutoSelected && screenState == ScreenModeSelect)
            {
                if (ok)
                {
                    modeAutoSelected = true;
                    BeginFaceIdentificationFlow();
                }
                else
                {
                    modeDetectStatus = "No TCP hub (127.0.0.1:5000). Start: from hci2\\backend run python Server\\notification_hub.py — then run face_auth_service.py.";
                    Invalidate();
                }
            }
        };
        socketClient.ConnectAsync();

        if (IsPerfTestSkipAuth())
        {
            modeAutoSelected = true;
            Console.WriteLine("[TuioDemo] TUIO_DEMO_SKIP_AUTH is set — skipping face login/sign-up; opening game for performance test.");
            this.Load += TuioDemo_OnLoadPerfTestSkipAuth;
        }
    }

    /// <summary>
    /// When TUIO_DEMO_SKIP_AUTH=1 (or true), skip mode select / face identify / greeting and jump straight into the game.
    /// Optional: TUIO_DEMO_PERF_AGE_GROUP=child|teen|adult, TUIO_DEMO_PERF_FROM_LEVEL=2 to start at level 2.
    /// </summary>
    private static bool IsPerfTestSkipAuth()
    {
        string v = System.Environment.GetEnvironmentVariable("TUIO_DEMO_SKIP_AUTH");
        if (string.IsNullOrWhiteSpace(v)) return false;
        v = v.Trim();
        return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private void TuioDemo_OnLoadPerfTestSkipAuth(object sender, EventArgs e)
    {
        this.Load -= TuioDemo_OnLoadPerfTestSkipAuth;
        EnterPerfTestGame();
    }

    private void EnterPerfTestGame()
    {
        loginError = "";
        faceIdentifyHint = "";
        string age = System.Environment.GetEnvironmentVariable("TUIO_DEMO_PERF_AGE_GROUP");
        if (string.IsNullOrWhiteSpace(age)) age = "adult";
        age = age.Trim().ToLowerInvariant();
        if (age != "child" && age != "teen" && age != "adult") age = "adult";

        currentUser = new UserData
        {
            TuioId = 999998,
            Name = "PerfTest",
            Gender = "unspecified",
            AgeGroup = age,
            MaxCompletedLevel = 0
        };
        string fromLevel = System.Environment.GetEnvironmentVariable("TUIO_DEMO_PERF_FROM_LEVEL");
        if (!string.IsNullOrWhiteSpace(fromLevel) && fromLevel.Trim() == "2")
            currentUser.MaxCompletedLevel = 1;

        theme.SetAgeGroup(currentUser.AgeGroup);
        theme.Update();
        sessionMediapipeMode = false;
        inputModeMediapipe = false;
        mediapipeSignupHybrid = false;
        StartGameFromSavedProgress();
        Invalidate();
        // No face_auth in TUIO_DEMO_SKIP_AUTH mode — start table tracker after a short delay.
        ScheduleReacTIVisionAfterFaceAuthExit();
    }

    private void HandleSocketLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        Console.WriteLine("[Socket] Received: " + line);

        string lower = line.ToLowerInvariant();
        string hubEvent = ExtractStringField(line, "event");

        if (lower.Contains("\"event\"") && lower.Contains("mediapipe_camera_active"))
        {
            BeginInvoke(new Action(() =>
            {
                if (IsPerfTestSkipAuth() && screenState == ScreenGame)
                {
                    Invalidate();
                    return;
                }
                modeAutoSelected = true;
                sessionMediapipeMode = false;
                mediapipeCameraActive = true;
                modeDetectStatus = "Face camera active (Camo).";
                if (screenState == ScreenModeSelect)
                {
                    screenState = ScreenFaceIdentify;
                    faceIdentifyHint = "Look at the camera. Your face identifies your account.";
                }
                TryConsumePendingMediapipeUser();
                Invalidate();
            }));
            return;
        }

        if (lower.Contains("\"event\"") && lower.Contains("mediapipe_user_ready"))
        {
            int tuioId = ExtractIntField(line, "tuioId");
            if (tuioId <= 0) return;
            string name = ExtractStringField(line, "name");
            string ageGroup = ExtractStringField(line, "ageGroup");
            string gender = ExtractStringField(line, "gender");
            if (string.IsNullOrWhiteSpace(ageGroup)) ageGroup = "adult";
            if (string.IsNullOrWhiteSpace(gender)) gender = "unspecified";
            bool exists = lower.Contains("\"exists\":true") || lower.Contains("\"exists\": true");
            bool isAdmin = lower.Contains("\"isadmin\":true") || lower.Contains("\"isadmin\": true");

            BeginInvoke(new Action(() =>
            {
                if (IsPerfTestSkipAuth() && screenState == ScreenGame)
                {
                    Invalidate();
                    return;
                }
                modeAutoSelected = true;
                mediapipeCameraActive = true; // Force true if we got a user event
                
                hasPendingMediapipeUser = true;
                pendingMediapipeTuioId = tuioId;
                pendingMediapipeName = name;
                pendingMediapipeAgeGroup = ageGroup;
                pendingMediapipeGender = gender;
                pendingMediapipeExists = exists;
                pendingMediapipeIsAdmin = isAdmin;
                
                // If we were on the mode selection or identity screen, move forward
                if (screenState == ScreenModeSelect || screenState == ScreenFaceIdentify)
                {
                    // TryConsume will set screenState to Greeting
                }
                
                TryConsumePendingMediapipeUser();
                Invalidate();
            }));
            return;
        }

        if (string.Equals(hubEvent, "user_facial_emotion", StringComparison.OrdinalIgnoreCase))
        {
            string em = ExtractStringField(line, "emotion").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(em)) em = "neutral";
            if (em != "happy" && em != "sad" && em != "neutral" && em != "confused") em = "neutral";
            float sc = ExtractFloatField(line, "score");
            if (sc <= 0f || sc > 1.05f) sc = 0.65f;
            string ctx = "";
            string modeLabel = "";
            if (em == "happy")
            {
                modeLabel = "HAPPY MODE";
                ctx = "Warm sunshine palette — enjoy the game!";
            }
            else if (em == "sad")
            {
                modeLabel = "CALM MODE";
                ctx = "Soft twilight colors — take this round at your own pace.";
            }
            else if (em == "confused")
            {
                modeLabel = "THINKING MODE";
                ctx = "Take your time — a gentle hint may appear near your gaze.";
            }
            BeginInvoke(new Action(() =>
            {
                facialEmotionMood = em;
                facialEmotionScore = sc;
                facialEmotionModeLabel = modeLabel;
                facialEmotionContext = ctx;
                facialEmotionUpdatedAt = DateTime.Now;
                if ((em == "happy" || em == "sad" || em == "confused") && emotionPaintTimer != null)
                    emotionPaintTimer.Start();
                else if (emotionPaintTimer != null)
                    emotionPaintTimer.Stop();
                Invalidate();
            }));
            return;
        }

        if (string.Equals(hubEvent, "keyboard_pointer_source", StringComparison.OrdinalIgnoreCase))
        {
            string src = ExtractStringField(line, "source");
            bool isLaser = string.Equals(src, "laser", StringComparison.OrdinalIgnoreCase);
            BeginInvoke(new Action(() =>
            {
                keyboardPointerIsLaser = isLaser;
                if (IsVirtualKeyboardScreen())
                    Invalidate();
            }));
            return;
        }

        if (lower.Contains("\"event\"") && lower.Contains("point8_position"))
        {
            float x = ExtractFloatField(line, "x");
            float y = ExtractFloatField(line, "y");
            bool active = !(lower.Contains("\"active\":false") || lower.Contains("\"active\": false"));
            BeginInvoke(new Action(() =>
            {
                // Any point8 stream means MediaPipe pointer is alive; prefer it over stale TUIO plane objects.
                sessionMediapipeMode = true;
                mediapipePoint8Active = active;
                mediapipePoint8X = Math.Max(0f, Math.Min(1f, x));
                mediapipePoint8Y = Math.Max(0f, Math.Min(1f, y));
                mediapipePoint8UpdatedAt = DateTime.Now;
                if (active)
                {
                    mediapipeLastActivePoint8X = mediapipePoint8X;
                    mediapipeLastActivePoint8Y = mediapipePoint8Y;
                    mediapipeLastActivePoint8At = mediapipePoint8UpdatedAt;
                }
                if (screenState == ScreenSignupName || screenState == ScreenAdminUsers || screenState == ScreenAdminEditName)
                    Invalidate();
            }));
            return;
        }

        // tuio_only_service.py sends this after swipe / fist / peace (not substring-matched hub noise).
        if (string.Equals(hubEvent, "mediapipe_signup_gesture", StringComparison.OrdinalIgnoreCase))
        {
            string sig = ExtractStringField(line, "signal");
            if (string.IsNullOrEmpty(sig)) return;
            string sigNorm = sig.Trim().ToLowerInvariant();
            bool isRotateLeft = sigNorm == "rotateleft";
            bool isRotateRight = sigNorm == "rotateright";
            bool isSelect = sigNorm == "select";
            bool isSubmit = sigNorm == "submit";
            if (!isRotateLeft && !isRotateRight && !isSelect && !isSubmit) return;

            BeginInvoke(new Action(() =>
            {
                if (!mediapipeSignupHybrid) return;

                if (screenState == ScreenSignupName)
                {
                    if (isSubmit && screenState == ScreenSignupName)
                        CompleteSignupWithName();
                    Invalidate();
                    return;
                }

                if (screenState != ScreenSignup) return;
                string[] dialOptions = GetCurrentDialOptions();
                if (dialOptions == null || dialOptions.Length == 0) return;

                if (isRotateLeft)
                    dialIndex = (dialIndex - 1 + dialOptions.Length) % dialOptions.Length;
                else if (isRotateRight)
                    dialIndex = (dialIndex + 1) % dialOptions.Length;
                else if (isSelect)
                {
                    string selected = dialOptions[dialIndex % dialOptions.Length];
                    if (signupField == 0) signupGender = selected;
                    else if (signupField == 1) signupAgeGroup = selected;
                }
                else if (isSubmit)
                {
                    if (signupField == 0)
                    {
                        if (BlockSignupFieldSwitchIfNeeded()) { Invalidate(); return; }
                        signupError = "";
                        signupField = 1;
                        dialIndex = 0;
                        dialRotationAccum = 0f;
                        lastTuio4Angle = float.MinValue;
                    }
                    else if (signupField == 1)
                    {
                        if (BlockSignupDialSubmitIfNeeded()) { Invalidate(); return; }
                        ProceedToSignupName();
                    }
                }

                Invalidate();
            }));
            return;
        }
    }

    /// <summary>
    /// After face_auth exits, the USB camera needs a moment before tuio_only_service can reopen it (SingleWebcam / TUIO_SKIP_CAMERA).
    /// Runs on a thread pool thread so the UI thread is not blocked and the hub can flush face_auth_shutdown first.
    /// Sends a second resume ping for slow Windows USB handoff.
    /// </summary>
    private void BroadcastTuioResumeHandCameraAfterUsbDelay()
    {
        try
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    Thread.Sleep(900);
                    socketClient.SendEvent("tuio_resume_hand_camera", "\"reason\": \"post_face_login\"");
                    Thread.Sleep(1400);
                    socketClient.SendEvent("tuio_resume_hand_camera", "\"reason\": \"post_face_login_retry\"");
                }
                catch { }
            });
        }
        catch { }
    }

    private static string FindReacTIVisionExecutable()
    {
        string env = System.Environment.GetEnvironmentVariable("REACTIVISION_EXE");
        if (!string.IsNullOrWhiteSpace(env))
        {
            string t = env.Trim();
            if (File.Exists(t)) return t;
        }
        try
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            DirectoryInfo d = new DirectoryInfo(baseDir);
            for (int i = 0; i < 14 && d != null; i++)
            {
                string candidate = Path.Combine(d.FullName, "reacTIVision-1.5.1-win64", "reacTIVision.exe");
                if (File.Exists(candidate))
                    return candidate;
                d = d.Parent;
            }
        }
        catch { }
        return null;
    }

    private static bool IsReacTIVisionProcessRunning()
    {
        try
        {
            Process[] arr = Process.GetProcessesByName("reacTIVision");
            if (arr != null && arr.Length > 0)
            {
                for (int i = 0; i < arr.Length; i++)
                {
                    try { arr[i].Dispose(); } catch { }
                }
                return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// After face_auth exits, start bundled reacTIVision so the table camera can take the USB device.
    /// Disable with env TUIO_DEMO_AUTO_REACTIVISION=0 or set REACTIVISION_EXE to a custom path.
    /// </summary>
    private void ScheduleReacTIVisionAfterFaceAuthExit()
    {
        string disable = System.Environment.GetEnvironmentVariable("TUIO_DEMO_AUTO_REACTIVISION");
        if (disable != null && disable.Trim() == "0")
            return;

        lock (this)
        {
            if (reacTIVisionLaunchScheduled)
                return;
            reacTIVisionLaunchScheduled = true;
        }

        ThreadPool.QueueUserWorkItem(delegate
        {
            int delayMs = IsPerfTestSkipAuth() ? 600 : 900;
            string delayEnv = System.Environment.GetEnvironmentVariable("TUIO_DEMO_REACTIVISION_DELAY_MS");
            if (!string.IsNullOrWhiteSpace(delayEnv))
            {
                if (int.TryParse(delayEnv.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed >= 0)
                    delayMs = parsed;
            }
            Thread.Sleep(delayMs);
            try
            {
                if (IsReacTIVisionProcessRunning())
                {
                    Console.WriteLine("[ReacTIVision] Process already running — skip start.");
                    return;
                }
                string exe = FindReacTIVisionExecutable();
                if (string.IsNullOrEmpty(exe))
                {
                    Console.WriteLine("[ReacTIVision] Bundle not found. Place reacTIVision-1.5.1-win64 next to hci2 or set REACTIVISION_EXE.");
                    lock (this) { reacTIVisionLaunchScheduled = false; }
                    return;
                }
                string workDir = Path.GetDirectoryName(exe) ?? ".";
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = exe;
                psi.WorkingDirectory = workDir;
                psi.UseShellExecute = true;
                Process.Start(psi);
                Console.WriteLine("[ReacTIVision] Started: " + exe);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ReacTIVision] Start failed: " + ex.Message);
                lock (this) { reacTIVisionLaunchScheduled = false; }
            }
        });
    }

    private void NotifySignupGestureMode(bool active)
    {
        if (socketClient == null || !socketClient.IsConnected) return;
        socketClient.SendEvent("mediapipe_signup_gestures", "\"active\": " + (active ? "true" : "false"));
    }

    /// <summary>Tells tuio_only: name keyboard uses laser only (MediaPipe Hands off).</summary>
    private void NotifySignupPointerMode(bool active)
    {
        keyboardPointerIsLaser = active;
        if (active)
        {
            mediapipePoint8Active = false;
            mediapipeLastActivePoint8At = DateTime.MinValue;
        }
        if (socketClient == null || !socketClient.IsConnected) return;
        socketClient.SendEvent("mediapipe_signup_pointer", "\"active\": " + (active ? "true" : "false"));
    }

    private void TryConsumePendingMediapipeUser()
    {
        if (!hasPendingMediapipeUser) return;
        if (pendingMediapipeTuioId <= 0) return;

        NotifySignupGestureMode(false);

        Console.WriteLine("[FaceAuth] Session: id=" + pendingMediapipeTuioId + " exists=" + pendingMediapipeExists + " isAdmin=" + pendingMediapipeIsAdmin);
        
        inputModeMediapipe = false;
        mediapipeSignupHybrid = true;
        sessionMediapipeMode = true;
        loginError = "";

        // Admin face matched admin_face_database.json + admins.json — never users.json / game.
        if (pendingMediapipeIsAdmin)
        {
            adminManager.Load();
            AdminAccount admin = adminManager.FindByTuioId(pendingMediapipeTuioId);
            if (admin == null)
            {
                loginError = "Admin face recognized but admins.json has no entry for id " + pendingMediapipeTuioId + ".";
                hasPendingMediapipeUser = false;
                pendingMediapipeIsAdmin = false;
                Invalidate();
                return;
            }
            if (!string.IsNullOrWhiteSpace(pendingMediapipeName))
                admin.Name = pendingMediapipeName;
            adminManager.Save();
            EnterAdminDashboard(admin);
            hasPendingMediapipeUser = false;
            pendingMediapipeTuioId = 0;
            pendingMediapipeName = "";
            pendingMediapipeIsAdmin = false;
            pendingMediapipeExists = false;
            Invalidate();
            return;
        }

        userManager.Load();
        UserData found = userManager.FindByTuioId(pendingMediapipeTuioId);

        // Brand-new account from face_auth: go to signup screen to pick gender/age
        if (!pendingMediapipeExists && pendingMediapipeTuioId > 0)
        {
            signupTuioId = pendingMediapipeTuioId;
            screenState = ScreenSignup;
            signupField = 0; // 0=gender, 1=age
            signupGender = "";
            signupAgeGroup = "";
            signupError = "";
            // face_auth only sends mediapipe_user_ready after enroll_profile_for_tuio succeeds.
            signupFaceReady = true;
            signupFaceStatus = "Face enrolled at sign-in — pick gender and age, then submit.";

            hasPendingMediapipeUser = false;
            pendingMediapipeTuioId = 0;
            pendingMediapipeName = "";
            pendingMediapipeExists = false;
            
            Invalidate();
            // Pause auth so we can focus on signup without interference
            socketClient.SendEvent("pause_auth", "\"reason\": \"signup_started\"", "face_auth");
            NotifySignupGestureMode(true);
            NotifySignupPointerMode(false);
            // While paused, face_auth still holds the USB webcam; tuio_only cannot open it for hand poses.
            // Mirror the known-user path: exit face_auth, then ask tuio_only to resume after USB handoff delay.
            socketClient.SendEvent("face_auth_shutdown", "\"reason\": \"signup_release_camera_for_hand\"", "face_auth");
            Thread.Sleep(40);
            BroadcastTuioResumeHandCameraAfterUsbDelay();
            return;
        }

        if (found == null)
            currentUser = userManager.Signup(pendingMediapipeTuioId, pendingMediapipeGender, pendingMediapipeAgeGroup);
        else
            currentUser = found;

        if (currentUser != null)
        {
            if (!string.IsNullOrWhiteSpace(pendingMediapipeName))
                currentUser.Name = pendingMediapipeName;
            userManager.Save();

            theme.SetAgeGroup(currentUser.AgeGroup);
            theme.Update();
            screenState = ScreenGreeting;
            greetingTimer.Interval = 800; // Even faster login for known users
            greetingTimer.Start();

            socketClient.SendEvent(
                "login",
                "\"tuioId\": " + currentUser.TuioId +
                ", \"name\": \"" + currentUser.Name + "\"" +
                ", \"ageGroup\": \"" + currentUser.AgeGroup + "\"" +
                ", \"source\": \"mediapipe_face_db\"" +
                ", \"exists\": " + (pendingMediapipeExists ? "true" : "false")
            );
            // Brief pause so the hub can deliver the login event before face_auth exits.
            Thread.Sleep(40);
            // Release webcam for table / reacTIVision; Python face_auth process exits.
            socketClient.SendEvent("face_auth_shutdown", "\"reason\": \"user_logged_in_release_camera\"", "face_auth");
            BroadcastTuioResumeHandCameraAfterUsbDelay();
            ScheduleReacTIVisionAfterFaceAuthExit();
        }

        hasPendingMediapipeUser = false;
        pendingMediapipeTuioId = 0;
        pendingMediapipeName = "";
        pendingMediapipeAgeGroup = "adult";
        pendingMediapipeGender = "unspecified";
        pendingMediapipeExists = false;
    }

    private void EnsureMediapipeCameraRunning()
    {
        try
        {
            if (mediapipeCameraProcess != null && !mediapipeCameraProcess.HasExited)
                return;
        }
        catch { }

        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string[] candidateScripts = new[]
        {
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "Task 5 _ 2 WorkOnrealCamera.py")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Task 5 _ 2 WorkOnrealCamera.py")),
            Path.GetFullPath(Path.Combine(baseDir, "Task 5 _ 2 WorkOnrealCamera.py")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "WorkOnrealCamera.py")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "WorkOnrealCamera.py")),
            Path.GetFullPath(Path.Combine(baseDir, "WorkOnrealCamera.py")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "Task 5_2 WorkOnrealCamera.py")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Task 5_2 WorkOnrealCamera.py")),
            Path.GetFullPath(Path.Combine(baseDir, "Task 5_2 WorkOnrealCamera.py")),
        };

        string scriptPath = "";
        for (int i = 0; i < candidateScripts.Length; i++)
        {
            if (File.Exists(candidateScripts[i]))
            {
                scriptPath = candidateScripts[i];
                break;
            }
        }
        if (string.IsNullOrWhiteSpace(scriptPath))
        {
            Console.WriteLine("[MediaPipe] Camera script not found. Tried:");
            for (int i = 0; i < candidateScripts.Length; i++)
                Console.WriteLine("  - " + candidateScripts[i]);
            return;
        }

        string envPython = System.Environment.GetEnvironmentVariable("MEDIAPIPE_PYTHON");
        string[,] launchers = new string[,]
        {
            { string.IsNullOrWhiteSpace(envPython) ? "" : envPython, "\"" + scriptPath + "\"" },
            { "python", "\"" + scriptPath + "\"" },
            { "py", "-3 \"" + scriptPath + "\"" },
        };

        for (int i = 0; i < launchers.GetLength(0); i++)
        {
            string fileName = launchers[i, 0];
            string args = launchers[i, 1];
            if (string.IsNullOrWhiteSpace(fileName)) continue;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = fileName;
                psi.Arguments = args;
                psi.WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? ".";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                Process p = Process.Start(psi);
                if (p == null) continue;

                Thread.Sleep(300);
                if (!p.HasExited)
                {
                    mediapipeCameraProcess = p;
                    mediapipeCameraProcessStartedByApp = true;
                    Console.WriteLine("[MediaPipe] Started camera process using: " + fileName + " " + args);
                    return;
                }
                Console.WriteLine("[MediaPipe] Launcher exited immediately: " + fileName + " " + args);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[MediaPipe] Launcher failed: " + fileName + " -> " + ex.Message);
            }
        }

        Console.WriteLine("[MediaPipe] Failed to start camera process with all launchers.");
    }

    private int ExtractIntField(string json, string key)
    {
        try
        {
            int ki = json.IndexOf("\"" + key + "\"", StringComparison.OrdinalIgnoreCase);
            if (ki < 0) return 0;
            int colon = json.IndexOf(':', ki);
            if (colon < 0) return 0;
            string rest = json.Substring(colon + 1).Trim();
            string num = "";
            foreach (char c in rest)
            {
                if (char.IsDigit(c)) num += c;
                else if (num.Length > 0) break;
            }
            int v;
            int.TryParse(num, out v);
            return v;
        }
        catch { return 0; }
    }

    private float ExtractFloatField(string json, string key)
    {
        try
        {
            int ki = json.IndexOf("\"" + key + "\"", StringComparison.OrdinalIgnoreCase);
            if (ki < 0) return 0f;
            int colon = json.IndexOf(':', ki);
            if (colon < 0) return 0f;
            int i = colon + 1;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            int j = i;
            while (j < json.Length)
            {
                char c = json[j];
                if (char.IsDigit(c) || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E')
                    j++;
                else
                    break;
            }
            string num = json.Substring(i, j - i);
            float v;
            if (float.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                return v;
            return 0f;
        }
        catch { return 0f; }
    }

    private string ExtractStringField(string json, string key)
    {
        try
        {
            int ki = json.IndexOf("\"" + key + "\"", StringComparison.OrdinalIgnoreCase);
            if (ki < 0) return "";
            int colon = json.IndexOf(':', ki);
            if (colon < 0) return "";
            int q1 = json.IndexOf('"', colon + 1);
            if (q1 < 0) return "";
            int q2 = json.IndexOf('"', q1 + 1);
            if (q2 < 0) return "";
            return json.Substring(q1 + 1, q2 - q1 - 1);
        }
        catch { return ""; }
    }

    private static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            T tmp = list[i];
            list[i] = list[j];
            list[j] = tmp;
        }
    }

    private int GetLivesForAgeGroup(string ageGroup)
    {
        switch (ageGroup)
        { 
            case "child": return 5;
            case "teen": return 4;
            default: return 3;
        }
    }

    private void StartGameFromSavedProgress()
    {
        if (currentUser != null && currentUser.MaxCompletedLevel >= 1)
            StartLevel2();
        else
            StartGame();
    }

    private void SaveLevelProgress(int level)
    {
        if (currentUser == null) return;
        if (level <= 0) return;
        userManager.MarkLevelCompleted(currentUser.TuioId, level);
        if (currentUser.MaxCompletedLevel < level)
            currentUser.MaxCompletedLevel = level;
    }


    private void Logout()
    {
        lock (this)
        {
            reacTIVisionLaunchScheduled = false;
        }
        currentUser = null;
        screenState = ScreenModeSelect;
        NotifySignupGestureMode(false);
        NotifySignupPointerMode(false);
        socketClient.SendEvent("resume_auth", "\"reason\": \"logout\"", "face_auth");
        Invalidate();
    }

    private void StartGame()
    {
        currentLevel = 1;
        gameContent.LoadForAgeGroup(currentUser.AgeGroup, 1);

        maxLives = GetLivesForAgeGroup(currentUser.AgeGroup);
        currentLives = maxLives;
        gameOver = false;

        countryToFlagMap.Clear();
        for (int i = 0; i < gameContent.CountryNames.Length; i++)
            countryToFlagMap[gameContent.CountryNames[i]] = gameContent.FlagPaths[i];

        remainingCountries = new List<string>(gameContent.CountryNames);
        remainingFlags = new List<string>(gameContent.FlagPaths);
        Shuffle(remainingCountries);
        Shuffle(remainingFlags);

        countryNameIndex = 0;
        flagIndex = 0;
        levelFinished = false;
        countryHintAfterLifeLoss = false;

        activeBar = 0;
        tuio0WasPresent = false;
        tuio2WasPresent = false;
        lastTuio1Angle = float.MinValue;
        rotationAccum = 0f;
        hoveredFlagForPlane = "";
        hoveredFlagSince = DateTime.MinValue;
        level2PlaneFlagImagePath = "";
        screenState = ScreenGame;
        if (sessionMediapipeMode)
            socketClient.SendEvent("mediapipe_game_started", "\"level\": 1");
        Invalidate();
    }

    private void EnterLevelTransitionScreen()
    {
        transitionPhase = 0;
        screenState = ScreenLevelTransition;
        levelTransitionTimer.Start();
        if (gameSounds != null)
            gameSounds.PlayLevelTransition();
        Invalidate();
    }

    private void StartLevel2()
    {
        if (gameSounds != null)
            gameSounds.StopLevelTransition();

        // Reaching level 2 means level 1 was completed; persist it defensively.
        SaveLevelProgress(1);

        currentLevel = 2;
        gameContent.LoadForAgeGroup(currentUser.AgeGroup, 2);

        maxLives = GetLivesForAgeGroup(currentUser.AgeGroup);
        currentLives = maxLives;
        gameOver = false;

        landmarkToFlagMap.Clear();
        for (int i = 0; i < gameContent.LandmarkPaths.Length; i++)
            landmarkToFlagMap[gameContent.LandmarkPaths[i]] = gameContent.FlagPaths[i];

        remainingLandmarks = new List<string>(gameContent.LandmarkPaths);
        remainingFlags = new List<string>(gameContent.FlagPaths);
        Shuffle(remainingLandmarks);
        Shuffle(remainingFlags);
        hoveredFlagForPlane = "";
        hoveredFlagSince = DateTime.MinValue;
        level2PlaneFlagImagePath = remainingFlags.Count > 0 ? remainingFlags[rng.Next(remainingFlags.Count)] : "";

        landmarkIndex = 0;
        flagIndex = 0;
        levelFinished = false;

        activeBar = 0;
        tuio0WasPresent = false;
        tuio2WasPresent = false;
        lastTuio1Angle = float.MinValue;
        rotationAccum = 0f;
        screenState = ScreenGame;
        if (sessionMediapipeMode)
            socketClient.SendEvent("mediapipe_game_started", "\"level\": 2");
        Invalidate();
    }

    private string[] GetCurrentDialOptions()
    {
        return (signupField == 0) ? SIGNUP_GENDERS : SIGNUP_AGE_GROUPS;
    }

    private static bool IsSignupGenderFilled(string gender)
    {
        return !string.IsNullOrWhiteSpace(gender);
    }

    private static bool IsSignupAgeFilled(string ageGroup)
    {
        return !string.IsNullOrWhiteSpace(ageGroup);
    }

    private bool IsCurrentSignupDialFieldFilled()
    {
        return signupField == 0 ? IsSignupGenderFilled(signupGender) : IsSignupAgeFilled(signupAgeGroup);
    }

    /// <summary>True if field switch was blocked; sets signupError.</summary>
    private bool BlockSignupFieldSwitchIfNeeded()
    {
        if (IsCurrentSignupDialFieldFilled()) return false;
        signupError = signupField == 0
            ? "Select Male or Female before switching to Age Group."
            : "Select Child, Teen, or Adult before switching back.";
        return true;
    }

    /// <summary>True if continue/submit was blocked; sets signupError.</summary>
    private bool BlockSignupDialSubmitIfNeeded()
    {
        signupError = "";
        if (!IsSignupGenderFilled(signupGender))
        {
            signupError = "Select Male or Female first.";
            return true;
        }
        if (!IsSignupAgeFilled(signupAgeGroup))
        {
            signupError = "Select Child, Teen, or Adult first.";
            return true;
        }
        return false;
    }

    private string NormalizeAgeGroupFromLabel(string label)
    {
        if (label == SIGNUP_AGE_GROUPS[0]) return "child";
        if (label == SIGNUP_AGE_GROUPS[1]) return "teen";
        return "adult";
    }

    private bool ValidateSignupGenderAge()
    {
        if (BlockSignupDialSubmitIfNeeded()) return false;
        if (!signupFaceReady)
        { signupError = "Face is not enrolled yet. Look at camera and try again."; return false; }
        userManager.Load();
        if (UserManager.IsReservedControlTuioId(signupTuioId))
        {
            signupError = "This profile id is reserved for in-game controls.";
            return false;
        }
        return true;
    }

    private void ProceedToSignupName()
    {
        if (!ValidateSignupGenderAge()) { Invalidate(); return; }
        signupName = "";
        signupKeyboardCapsLock = false;
        signupKeyboardLayoutSignature = -1;
        signupKeyboardHoveredIndex = -1;
        signupNameTuio6WasPresent = false;
        signupNameTuio7WasPresent = false;
        signupKeyHoldTargetIndex = -1;
        signupKeyHoldSince = DateTime.MinValue;
        signupKeyHoldCooldownUntil = DateTime.MinValue;
        screenState = ScreenSignupName;
        signupError = "";
        NotifySignupGestureMode(false);
        NotifySignupPointerMode(true);
            Invalidate();
    }

    private void TrySignup()
    {
        ProceedToSignupName();
    }

    private void CompleteSignupWithName()
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(CompleteSignupWithName));
            return;
        }
        if (screenState == ScreenGreeting || screenState == ScreenGame || screenState == ScreenLevelTransition)
            return;

        signupError = "";
        string trimmed = (signupName ?? "").Trim();
        if (trimmed.Length < 1)
        {
            signupError = "Type your name, then press Enter or TUIO 7.";
            Invalidate();
            return;
        }
        if (trimmed.Length > SignupNameMaxLength)
            trimmed = trimmed.Substring(0, SignupNameMaxLength);

        if (!ValidateSignupGenderAge()) { Invalidate(); return; }

        string ageGroupValue = NormalizeAgeGroupFromLabel(signupAgeGroup);
        UserData existing = userManager.FindByTuioId(signupTuioId);
        if (existing != null)
        {
            existing.Gender = signupGender;
            existing.AgeGroup = ageGroupValue;
            existing.Name = trimmed;
            userManager.Save();
            currentUser = existing;
        }
        else
        {
            currentUser = userManager.Signup(signupTuioId, signupGender, ageGroupValue);
            currentUser.Name = trimmed;
            userManager.Save();
        }

            if (gameSounds != null) gameSounds.PlaySignupSuccess();
            mediapipeSignupHybrid = false;
        sessionMediapipeMode = true;
            NotifySignupGestureMode(false);
        NotifySignupPointerMode(false);
            signupFaceReady = false;
            signupFaceStatus = "";

            theme.SetAgeGroup(currentUser.AgeGroup);
            theme.Update();
            screenState = ScreenGreeting;
        greetingTimer.Stop();
        greetingTimer.Interval = greetingDurationMs;
            greetingTimer.Start();

        socketClient.SendEvent("signup",
            "\"tuioId\": " + signupTuioId +
            ", \"gender\": \"" + signupGender +
            "\", \"ageGroup\": \"" + currentUser.AgeGroup +
            ", \"name\": \"" + EscapeJson(currentUser.Name) + "\"");
        socketClient.SendEvent("login",
            "\"tuioId\": " + currentUser.TuioId +
            ", \"name\": \"" + EscapeJson(currentUser.Name) + "\"" +
            ", \"ageGroup\": \"" + currentUser.AgeGroup + "\"" +
            ", \"source\": \"signup_complete\"" +
            ", \"exists\": false");
            socketClient.SendEvent("face_auth_shutdown", "\"reason\": \"signup_complete_release_camera\"", "face_auth");
            BroadcastTuioResumeHandCameraAfterUsbDelay();
            ScheduleReacTIVisionAfterFaceAuthExit();
            Invalidate();
    }

    private static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private void EnsureAdminAccountSeeded()
    {
        adminManager.EnsureDefaultAdmin();

        // Admin must not appear in users.json (players only).
        userManager.Load();
        if (userManager.FindByTuioId(AdminAccount.DefaultAdminTuioId) != null)
        {
            userManager.RemoveFromUsersList(AdminAccount.DefaultAdminTuioId);
            Console.WriteLine("[Admin] Removed admin from users.json — use admins.json only.");
        }
        var strayAdmins = new List<int>();
        foreach (UserData u in userManager.AllUsers)
        {
            if (u != null && u.IsAdmin())
                strayAdmins.Add(u.TuioId);
        }
        foreach (int id in strayAdmins)
        {
            userManager.RemoveFromUsersList(id);
            Console.WriteLine("[Admin] Removed role=admin from users.json id=" + id);
        }

        // Admin face lives in admin_face_database.json only.
        if (faceDatabaseManager.ProfileExists(AdminAccount.DefaultAdminTuioId))
        {
            faceDatabaseManager.DeleteProfile(AdminAccount.DefaultAdminTuioId);
            Console.WriteLine("[Admin] Removed admin id from player face_database.json.");
        }

        if (!adminFaceDatabaseManager.ProfileExists(AdminAccount.DefaultAdminTuioId))
        {
            if (adminFaceDatabaseManager.CopyProfile(900001, AdminAccount.DefaultAdminTuioId, "admin"))
                Console.WriteLine("[Admin] Seeded admin_face_database.json from player 900001.");
            else if (faceDatabaseManager.ProfileExists(900001))
            {
                if (adminFaceDatabaseManager.CopyProfile(900001, AdminAccount.DefaultAdminTuioId, "admin"))
                    Console.WriteLine("[Admin] Seeded admin face from player profile 900001.");
            }
            else
                Console.WriteLine("[Admin] No admin face yet — copy your face into admin_face_database.json (id 900000).");
        }
    }

    private void EnterAdminDashboard(AdminAccount admin)
    {
        currentAdmin = admin;
        currentUser = null;
        adminSessionActive = true;
        sessionMediapipeMode = false;
        mediapipeSignupHybrid = false;
        adminStatusMessage = "Signed in as admin. Select a user or create new.";
        adminSelectedTuioId = -1;
        adminCreatingNew = false;
        ClearAdminEditForm();
        screenState = ScreenAdminUsers;

        socketClient.SendEvent("pause_auth", "\"reason\": \"admin_dashboard\"", "face_auth");
        Thread.Sleep(40);
        socketClient.SendEvent("face_auth_shutdown", "\"reason\": \"admin_release_camera\"", "face_auth");

        sessionMediapipeMode = true;
        mediapipeSignupHybrid = true;
        NotifySignupPointerMode(true);
        BroadcastTuioResumeHandCameraAfterUsbDelay();

        adminPointerHoverTarget = "";
        adminPointerHoldTarget = "";
        adminPointerHoldSince = DateTime.MinValue;
        adminPointerHoldCooldownUntil = DateTime.MinValue;

        userManager.Load();
        Invalidate();
    }

    private void ExitAdminDashboard()
    {
        adminSessionActive = false;
        adminSelectedTuioId = -1;
        adminCreatingNew = false;
        adminStatusMessage = "";
        currentAdmin = null;
        currentUser = null;
        screenState = ScreenFaceIdentify;
        faceIdentifyHint = "Face login — players use users.json; admin uses admins.json (id " + AdminAccount.DefaultAdminTuioId + ").";
        NotifySignupPointerMode(false);
        sessionMediapipeMode = false;
        mediapipeSignupHybrid = false;
        socketClient.SendEvent("resume_auth", "\"reason\": \"admin_exit\"", "face_auth");
        Invalidate();
    }

    private void ClearAdminEditForm()
    {
        adminEditName = "";
        adminEditGender = "unspecified";
        adminEditAgeGroup = "adult";
        adminEditMaxLevel = 0;
    }

    private void AdminSelectUser(int tuioId)
    {
        if (tuioId == AdminAccount.DefaultAdminTuioId) return;
        UserData u = userManager.FindByTuioId(tuioId);
        if (u == null) return;
        adminCreatingNew = false;
        adminSelectedTuioId = tuioId;
        adminEditName = u.Name ?? "";
        adminEditGender = string.IsNullOrWhiteSpace(u.Gender) ? "unspecified" : u.Gender;
        adminEditAgeGroup = string.IsNullOrWhiteSpace(u.AgeGroup) ? "adult" : u.AgeGroup;
        adminEditMaxLevel = u.MaxCompletedLevel;
        adminStatusMessage = "Editing user " + tuioId;
        Invalidate();
    }

    private void AdminBeginCreateUser()
    {
        userManager.Load();
        int newId = userManager.NextSyntheticTuioId();
        adminCreatingNew = true;
        adminSelectedTuioId = newId;
        adminEditName = "User " + newId;
        adminEditGender = "unspecified";
        adminEditAgeGroup = "adult";
        adminEditMaxLevel = 0;
        adminStatusMessage = "Hold pointer on Save (4s) after setting gender/age. Face enrolls at first login.";
        Invalidate();
    }

    private void AdminSaveCurrent()
    {
        userManager.Load();
        string name = (adminEditName ?? "").Trim();
        if (name.Length < 1)
        {
            adminStatusMessage = "Name cannot be empty.";
            Invalidate();
            return;
        }

        if (adminCreatingNew)
        {
            if (userManager.FindByTuioId(adminSelectedTuioId) != null)
            {
                adminStatusMessage = "TUIO id already exists.";
                Invalidate();
                return;
            }
            var nu = new UserData
            {
                TuioId = adminSelectedTuioId,
                Name = name,
                Gender = adminEditGender,
                AgeGroup = adminEditAgeGroup,
                MaxCompletedLevel = Math.Max(0, adminEditMaxLevel),
                CreatedAt = DateTime.Now.ToString("o"),
                Role = "user"
            };
            if (!userManager.AddUser(nu))
            {
                adminStatusMessage = "Could not create user.";
                Invalidate();
                return;
            }
            adminCreatingNew = false;
            adminStatusMessage = "Created user " + adminSelectedTuioId + ".";
            userManager.Load();
        }
        else
        {
            UserData u = userManager.FindByTuioId(adminSelectedTuioId);
            if (u == null || adminManager.IsAdminTuioId(u.TuioId))
            {
                adminStatusMessage = "Select a player user to save (not admin).";
                Invalidate();
                return;
            }
            u.Name = name;
            u.Gender = adminEditGender;
            u.AgeGroup = adminEditAgeGroup;
            u.MaxCompletedLevel = Math.Max(0, adminEditMaxLevel);
            userManager.Save();
            if (faceDatabaseManager.ProfileExists(u.TuioId))
                faceDatabaseManager.UpdateProfileName(u.TuioId, name);
            adminStatusMessage = "Saved user " + u.TuioId + ".";
            userManager.Load();
        }
        Invalidate();
    }

    private void AdminDeleteCurrent()
    {
        if (adminSelectedTuioId <= 0 || adminCreatingNew)
        {
            adminStatusMessage = "Select an existing user to delete.";
            Invalidate();
            return;
        }
        if (adminSelectedTuioId == AdminAccount.DefaultAdminTuioId)
        {
            adminStatusMessage = "Cannot delete the admin account.";
            Invalidate();
            return;
        }
        if (!userManager.DeleteUser(adminSelectedTuioId))
        {
            adminStatusMessage = "Delete failed.";
            Invalidate();
            return;
        }
        faceDatabaseManager.DeleteProfile(adminSelectedTuioId);
        adminSelectedTuioId = -1;
        adminCreatingNew = false;
        ClearAdminEditForm();
        adminStatusMessage = "User deleted.";
        userManager.Load();
        Invalidate();
    }

    private void BeginFaceIdentificationFlow()
    {
        modeAutoSelected = true;
        sessionMediapipeMode = false;
        inputModeMediapipe = false;
        mediapipeSignupHybrid = false;
        loginError = "";
        NotifySignupGestureMode(false);
        NotifySignupPointerMode(false);
        signupFaceReady = false;
        signupFaceStatus = "";
        screenState = ScreenFaceIdentify;
        faceIdentifyHint = "Camo / phone camera: your face signs you in. After login, hand gestures (MediaPipe) drive the game pointer via TUIO.";
        Invalidate();
    }

    private void HandleMediapipeSignupKeyDown(KeyEventArgs e)
    {
        // Sign-up is TUIO-only by request; ignore keyboard input here.
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    //Key handling (only fullscreen/exit) ───────────────────────
    private void Form_KeyDown(object sender, KeyEventArgs e)
    {
        // MediaPipe session: no keyboard during levels (TUIO/UDP only). TUIO path keeps keys (e.g. V). F1 + Escape always.
        if (sessionMediapipeMode && (screenState == ScreenGame || screenState == ScreenLevelTransition))
        {
            if (e.KeyData != Keys.Escape && e.KeyData != Keys.F1)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
        }

        if (screenState == ScreenSignup && e.KeyData != Keys.Escape && e.KeyData != Keys.F1)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if ((screenState == ScreenAdminUsers || screenState == ScreenAdminEditName) && e.KeyData != Keys.Escape && e.KeyData != Keys.F1)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if (screenState == ScreenSignupName)
        {
            HandleSignupNameKeyDown(e);
            return;
        }

        if (e.KeyData == Keys.F1)
        {
            if (!fullscreen)
            {
                width = screen_width; height = screen_height;
                window_left = this.Left; window_top = this.Top;
                this.FormBorderStyle = FormBorderStyle.None;
                this.Left = 0; this.Top = 0;
                this.Width = screen_width; this.Height = screen_height;
                fullscreen = true;
            }
            else
            {
                width = window_width; height = window_height;
                this.FormBorderStyle = FormBorderStyle.Sizable;
                this.Left = window_left; this.Top = window_top;
                this.Width = window_width; this.Height = window_height;
                fullscreen = false;
            }
        }
        else if (e.KeyData == Keys.Escape)
            this.Close();
        else if (e.KeyData == Keys.V)
            verbose = !verbose;
        else if (e.KeyData == Keys.L)
            Logout();
    }

    private void Form_Closing(object sender, CancelEventArgs e)
    {
        if (gameSounds != null) { gameSounds.Dispose(); gameSounds = null; }
        if (emotionPaintTimer != null)
        {
            emotionPaintTimer.Stop();
            emotionPaintTimer.Dispose();
            emotionPaintTimer = null;
        }
        socketClient.Disconnect();
        client.removeTuioListener(this);
        client.disconnect();
        if (mediapipeCameraProcessStartedByApp && mediapipeCameraProcess != null)
        {
            try
            {
                if (!mediapipeCameraProcess.HasExited)
                    mediapipeCameraProcess.Kill();
            }
            catch { }
        }
        System.Environment.Exit(0);
    }

    //  TUIO callbacks 
    public void addTuioObject(TuioObject o)
    {
        lock (objectSync) { objectList.Add(o.SessionID, new TuioDemoObject(o)); }
        if (verbose) Console.WriteLine("add obj " + o.SymbolID + " (" + o.SessionID + ") " + o.X + " " + o.Y + " " + o.Angle);
        if (!IsMediapipeBridgeSession(o.SessionID) && (screenState == ScreenSignup || screenState == ScreenGame))
            Console.WriteLine("[TuioDemo] Physical marker " + o.SymbolID + " session " + o.SessionID);
    }

    public void updateTuioObject(TuioObject o)
    {
        lock (objectSync) { objectList[o.SessionID].update(o); }
    }

    public void removeTuioObject(TuioObject o)
    {
        lock (objectSync) { objectList.Remove(o.SessionID); }
    }

    public void addTuioCursor(TuioCursor c)
    {
        lock (cursorSync) { cursorList.Add(c.SessionID, c); }
    }

    public void updateTuioCursor(TuioCursor c) { }

    public void removeTuioCursor(TuioCursor c)
    {
        lock (cursorSync) { cursorList.Remove(c.SessionID); }
    }

    public void addTuioBlob(TuioBlob b)
    {
        lock (blobSync) { blobList.Add(b.SessionID, b); }
    }

    public void updateTuioBlob(TuioBlob b) { }

    public void removeTuioBlob(TuioBlob b)
    {
        lock (blobSync) { blobList.Remove(b.SessionID); }
    }

    // Refresh (all logic) 
    public void refresh(TuioTime frameTime)
    {
        lock (objectSync)
        {
            switch (screenState)
            {
                case ScreenModeSelect: RefreshModeDetection(); break;
                case ScreenFaceIdentify: RefreshFaceIdentify(); break;
                case ScreenAdminUsers: RefreshAdminUsers(); break;
                case ScreenSignup: RefreshSignup(); break;
                case ScreenSignupName: RefreshSignupName(); break;
                case ScreenAdminEditName: RefreshAdminEditName(); break;
                case ScreenGame: RefreshGame(); break;
            }
        }
        Invalidate();
    }

    private void RefreshModeDetection()
    {
        if (modeAutoSelected) return;

        // Admin shortcut from detection screen: TUIO 0 opens users admin page directly.
        foreach (TuioDemoObject t in objectList.Values)
        {
            if (IsMediapipeBridgeSession(t.SessionID)) continue;
            if (t.SymbolID == 0)
            {
                modeAutoSelected = true;
                sessionMediapipeMode = false;
                userManager.Load();
                screenState = ScreenAdminUsers;
                adminTuio0WasPresent = true;
                modeDetectStatus = "Admin marker detected.";
                Invalidate();
                return;
            }
        }

        // reacTIVision TUIO is for gameplay only (separate camera). Identity is always face + hub.
        bool hasReactiVisionObject = false;
        foreach (TuioDemoObject tobject in objectList.Values)
        {
            if (IsMediapipeBridgeSession(tobject.SessionID)) continue;
            if (tobject.SymbolID > 0)
            {
                hasReactiVisionObject = true;
                break;
            }
        }
        if (hasReactiVisionObject)
            modeDetectStatus = "reacTIVision ready for gameplay (markers do not log you in).";

        if (mediapipeSocketConnected)
        {
            modeDetectStatus = (hasReactiVisionObject ? modeDetectStatus + " " : "") + "Starting face login…";
            BeginInvoke(new Action(() => { BeginFaceIdentificationFlow(); }));
            return;
        }

        modeDetectStatus = "No hub on port 5000. In a terminal: cd hci2\\backend → python Server\\notification_hub.py (leave running). Then cd Python and run python face_auth_service.py.";
    }

    // ── Login logic ──
    /// <summary>Synthetic TUIO sessions from mediapipe_tuio_bridge.py (ignored during TUIO login).</summary>
    private static bool IsMediapipeBridgeSession(long sessionId)
    {
        return sessionId >= 910000 && sessionId <= 910099;
    }

    /// <summary>Python bridge sends sign-up dial updates on this session (symbol 4).</summary>
    private const long MediapipeDialSessionId = 910004L;

    private void RefreshFaceIdentify()
    {
        bool tuio0Now = false;
        foreach (TuioDemoObject t in objectList.Values)
        {
            if (IsMediapipeBridgeSession(t.SessionID)) continue;
            if (t.SymbolID == 0) { tuio0Now = true; break; }
        }
        if (tuio0Now && !adminTuio0WasPresent)
        {
            userManager.Load();
            screenState = ScreenAdminUsers;
            adminTuio0WasPresent = true;
            Invalidate();
            return;
        }
        if (!tuio0Now) adminTuio0WasPresent = false;
    }

    private void RefreshAdminUsers()
    {
        bool tuio0Now = false;
        foreach (TuioDemoObject t in objectList.Values)
        {
            if (IsMediapipeBridgeSession(t.SessionID)) continue;
            if (t.SymbolID == 0) { tuio0Now = true; break; }
        }

        // Place TUIO 0 again to return to login (or exit admin session).
        if (tuio0Now && !adminTuio0WasPresent)
        {
            if (adminSessionActive)
                ExitAdminDashboard();
            else
            screenState = ScreenFaceIdentify;
            adminTuio0WasPresent = true;
            Invalidate();
            return;
        }
        if (!tuio0Now) adminTuio0WasPresent = false;

        LayoutAdminScreen();
        UpdateAdminPointerHold();
    }

    // ── Signup  (TUIO 4=dial rotate, 5=add char, 6=backspace, 7=switch/submit) ──
    private void RefreshSignup()
    {
        if (inputModeMediapipe) return;
        // Physical reacTIVision markers (4–7) and hybrid bottle/hand (hub) both work on this screen.

        string[] dialOptions = GetCurrentDialOptions();

        // TUIO 4: rotate the dial — prefer physical fiducial (reacTIVision), then Python virtual dial session.
        TuioDemoObject dialTuio = FindSignupDialTuioObject();

        bool foundTuio4 = dialTuio != null;
        if (foundTuio4)
        {
            float angle = dialTuio.Angle;
            if (lastTuio4Angle != float.MinValue)
            {
                float delta = angle - lastTuio4Angle;
                if (delta > (float)Math.PI) delta -= (float)(2 * Math.PI);
                if (delta < -(float)Math.PI) delta += (float)(2 * Math.PI);
                dialRotationAccum += delta;

                if (dialRotationAccum >= dialRotationThreshold)
                {
                    dialIndex = (dialIndex + 1) % dialOptions.Length;
                    dialRotationAccum = 0f;
                }
                else if (dialRotationAccum <= -dialRotationThreshold)
                {
                    dialIndex = (dialIndex - 1 + dialOptions.Length) % dialOptions.Length;
                    dialRotationAccum = 0f;
                }
            }
            lastTuio4Angle = angle;
        }
        else
        {
            lastTuio4Angle = float.MinValue;
            dialRotationAccum = 0f;
        }

        // TUIO 5: choose current option (physical markers only — ignore Python bridge sessions)
        bool tuio5Now = AnyPhysicalTuioSymbolPresent(5);
        if (tuio5Now && !tuio5WasPresent)
        {
            string selected = dialOptions[dialIndex % dialOptions.Length];
            if (signupField == 0) signupGender = selected;
            else if (signupField == 1) signupAgeGroup = selected;
        }
        tuio5WasPresent = tuio5Now;

        // TUIO 6: go back to previous field
        bool tuio6Now = AnyPhysicalTuioSymbolPresent(6);
        if (tuio6Now && !tuio6WasPresent)
        {
            if (signupField > 0)
            {
                if (!BlockSignupFieldSwitchIfNeeded())
                {
                    signupError = "";
                signupField--;
                dialIndex = 0;
                dialRotationAccum = 0f;
                lastTuio4Angle = float.MinValue;
                }
                Invalidate();
            }
        }
        tuio6WasPresent = tuio6Now;

        // TUIO 7:switch field or submit
        bool tuio7Now = AnyPhysicalTuioSymbolPresent(7);
        if (tuio7Now && !tuio7WasPresent)
        {
            if (signupField == 0)
            {
                if (!BlockSignupFieldSwitchIfNeeded())
                {
                    signupError = "";
                signupField = 1;
                dialIndex = 0;
                dialRotationAccum = 0f;
                lastTuio4Angle = float.MinValue;
                }
                Invalidate();
            }
            else if (signupField == 1)
            {
                if (!BlockSignupDialSubmitIfNeeded())
                    BeginInvoke(new Action(() => { ProceedToSignupName(); }));
                else
                    Invalidate();
            }
        }
        tuio7WasPresent = tuio7Now;

        signupPhysicalTuioStatus = BuildPhysicalTuioStatusLine();
    }

    private string BuildPhysicalTuioStatusLine()
    {
        var ids = new List<int>();
        lock (objectSync)
        {
            foreach (TuioDemoObject t in objectList.Values)
            {
                if (!IsMediapipeBridgeSession(t.SessionID) && !ids.Contains(t.SymbolID))
                    ids.Add(t.SymbolID);
            }
        }
        ids.Sort();
        if (ids.Count == 0)
            return "reacTIVision: no markers detected — use PRINTED amoeba markers (not phone screen); check reacTIVision window shows ID circles";
        var parts = new List<string>();
        for (int i = 0; i < ids.Count; i++)
            parts.Add(ids[i].ToString());
        return "reacTIVision markers visible: " + string.Join(", ", parts.ToArray());
    }

    private void RefreshSignupName()
    {
        EnsureSignupKeyboardLayout();
        UpdateSignupKeyboardHover();

        bool holdActive = signupKeyboardHoveredIndex >= 0 && HasPlanePointer();
        UpdateSignupKeyHold(holdActive);

        bool tuio6Now = AnyPhysicalTuioSymbolPresent(6);
        if (tuio6Now && !signupNameTuio6WasPresent)
            ApplySignupKeyboardAction("backspace");
        signupNameTuio6WasPresent = tuio6Now;

        bool tuio7Now = AnyPhysicalTuioSymbolPresent(7);
        if (tuio7Now && !signupNameTuio7WasPresent)
            BeginInvoke(new Action(() => { CompleteSignupWithName(); }));
        signupNameTuio7WasPresent = tuio7Now;
    }

    // Game logic (TUIO 0=switch, 1=scroll, 2=confirm) ──
    private void RefreshGame()
    {
        if (levelFinished || gameOver) return;

        // TUIO 0: toggle active bar (physical markers; not Python bridge)
        bool tuio0Now = AnyPhysicalTuioSymbolPresent(0);
        if (tuio0Now && !tuio0WasPresent)
            activeBar = 1 - activeBar;
        tuio0WasPresent = tuio0Now;

        // TUIO 2: confirm match (physical markers)
        bool tuio2Now = AnyPhysicalTuioSymbolPresent(2);

        if (currentLevel == 1)
        {
            // Plane (TUIO 10) selects flag after hovering for 3 seconds (all age groups).
            if (currentUser != null)
            {
                Rectangle randomArea = new Rectangle(16, 72, width - 32, height - 120);
                EnsureChildFlagPositions(randomArea);

                int px, py;
                float planeAngle;
                bool hasPlane = TryGetPlaneScreenPoint(out px, out py, out planeAngle);
                string hoveredFlagNow = "";
                if (hasPlane)
                {
                    foreach (string flagName in remainingFlags)
                    {
                        Point p;
                        if (!childFlagRandomPositions.TryGetValue(flagName, out p)) continue;
                        Rectangle r = new Rectangle(p.X, p.Y, ChildFlagWidth, ChildFlagHeight);
                        if (r.Contains(px, py))
                        {
                            hoveredFlagNow = flagName;
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(hoveredFlagNow))
                {
                    hoveredFlagForPlane = "";
                    hoveredFlagSince = DateTime.MinValue;
                }
                else
                {
                    if (hoveredFlagNow != hoveredFlagForPlane)
                    {
                        hoveredFlagForPlane = hoveredFlagNow;
                        hoveredFlagSince = DateTime.Now;
                    }
                    else if (hoveredFlagSince != DateTime.MinValue &&
                             (DateTime.Now - hoveredFlagSince).TotalMilliseconds >= PlaneHoverSelectMs &&
                             remainingCountries.Count > 0)
                    {
                        string selectedCountry = remainingCountries[countryNameIndex];
                        string selectedFlag = hoveredFlagNow;
                         int selectedFlagIndex = remainingFlags.IndexOf(selectedFlag);
                        if (selectedFlagIndex >= 0 &&
                            countryToFlagMap.ContainsKey(selectedCountry) &&
                            countryToFlagMap[selectedCountry] == selectedFlag)
                        {
                            remainingCountries.RemoveAt(countryNameIndex);
                            remainingFlags.RemoveAt(selectedFlagIndex);
                            countryNameIndex = Math.Max(0, Math.Min(countryNameIndex, remainingCountries.Count - 1));
                            flagIndex = Math.Max(0, Math.Min(flagIndex, remainingFlags.Count - 1));

                            childFlagLayoutSignature = -1;
                            if (remainingCountries.Count == 0)
                            {
                                levelFinished = true;
                                SaveLevelProgress(1);
                                socketClient.SendEvent("level_complete", "\"level\": 1, \"user\": \"" + currentUser.Name + "\", \"ageGroup\": \"" + currentUser.AgeGroup + "\"");
                                BeginInvoke(new Action(() => { EnterLevelTransitionScreen(); }));
                            }
                            ClearCountryHintAfterLifeLoss();
                            socketClient.SendEvent("correct_match", "\"country\": \"" + selectedCountry + "\", \"flag\": \"" + selectedFlag + "\", \"remaining\": " + remainingCountries.Count);
                        }
                        else
                        {

                            currentLives--;
                            bool nowGameOver = currentLives <= 0;
                            if (nowGameOver) { currentLives = 0; gameOver = true; }
                            if (gameSounds != null && currentUser != null)
                                gameSounds.OnLifeLost(nowGameOver, currentUser.AgeGroup);
                            ArmCountryHintAfterLifeLoss();
                            socketClient.SendEvent("wrong_match", "\"country\": \"" + selectedCountry + "\", \"flag\": \"" + selectedFlag + "\", \"livesLeft\": " + currentLives);
                        }

                        hoveredFlagForPlane = "";
                        hoveredFlagSince = DateTime.MinValue;
                    }
                }
            }

            if (tuio2Now && !tuio2WasPresent && remainingCountries.Count > 0 && remainingFlags.Count > 0 && !HasPlanePointer())
            {
                string selectedCountry = remainingCountries[countryNameIndex];
                string selectedFlag = remainingFlags[flagIndex];
                if (countryToFlagMap.ContainsKey(selectedCountry) && countryToFlagMap[selectedCountry] == selectedFlag)
                {
                    remainingCountries.RemoveAt(countryNameIndex);
                    remainingFlags.RemoveAt(flagIndex);
                    countryNameIndex = Math.Max(0, Math.Min(countryNameIndex, remainingCountries.Count - 1));
                    flagIndex = Math.Max(0, Math.Min(flagIndex, remainingFlags.Count - 1));

                    if (remainingCountries.Count == 0)
                    {
                        levelFinished = true;
                        SaveLevelProgress(1);
                        socketClient.SendEvent("level_complete", "\"level\": 1, \"user\": \"" + currentUser.Name + "\", \"ageGroup\": \"" + currentUser.AgeGroup + "\"");
                        BeginInvoke(new Action(() => { EnterLevelTransitionScreen(); }));
                    }
                    ClearCountryHintAfterLifeLoss();
                    socketClient.SendEvent("correct_match", "\"country\": \"" + selectedCountry + "\", \"flag\": \"" + selectedFlag + "\", \"remaining\": " + remainingCountries.Count);
                }
                else
                {

                    currentLives--;
                    bool nowGameOver = currentLives <= 0;
                    if (nowGameOver) { currentLives = 0; gameOver = true; }
                    if (gameSounds != null && currentUser != null)
                        gameSounds.OnLifeLost(nowGameOver, currentUser.AgeGroup);
                    ArmCountryHintAfterLifeLoss();
                    socketClient.SendEvent("wrong_match", "\"country\": \"" + selectedCountry + "\", \"flag\": \"" + selectedFlag + "\", \"livesLeft\": " + currentLives);
                }
            }
        }
        else
        {
            // Plane (TUIO 10) selects landmark after hovering for 3 seconds (level 2 style).
            Rectangle randomArea = new Rectangle(16, 72, width - 32, height - 120);
            EnsureLevel2LandmarkPositions(randomArea);

            int px, py;
            float planeAngle;
            bool hasPlane = TryGetPlaneScreenPoint(out px, out py, out planeAngle);
            string hoveredLandmarkNow = "";

            if (hasPlane)
            {
                foreach (string lmName in remainingLandmarks)
                {
                    Point p;
                    if (!level2LandmarkRandomPositions.TryGetValue(lmName, out p)) continue;
                    Rectangle r = new Rectangle(p.X, p.Y, ChildFlagWidth, ChildFlagHeight);
                    if (r.Contains(px, py))
                    {
                        hoveredLandmarkNow = lmName;
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(hoveredLandmarkNow))
            {
                hoveredFlagForPlane = "";
                hoveredFlagSince = DateTime.MinValue;
            }
            else
            {
                if (hoveredLandmarkNow != hoveredFlagForPlane)
                {
                    hoveredFlagForPlane = hoveredLandmarkNow;
                    hoveredFlagSince = DateTime.Now;
                }
                else if (hoveredFlagSince != DateTime.MinValue &&
                         (DateTime.Now - hoveredFlagSince).TotalMilliseconds >= PlaneHoverSelectMs &&
                         remainingLandmarks.Count > 0 &&
                         !string.IsNullOrEmpty(level2PlaneFlagImagePath))
                {
                    string selectedLandmark = hoveredLandmarkNow;
                    string mappedFlag = landmarkToFlagMap.ContainsKey(selectedLandmark) ? landmarkToFlagMap[selectedLandmark] : "";

                    if (!string.IsNullOrEmpty(mappedFlag) && mappedFlag == level2PlaneFlagImagePath)
                    {
                        int lmIndex = remainingLandmarks.IndexOf(selectedLandmark);
                        if (lmIndex >= 0) remainingLandmarks.RemoveAt(lmIndex);

                        int mappedFlagIndex = remainingFlags.IndexOf(mappedFlag);
                        if (mappedFlagIndex >= 0) remainingFlags.RemoveAt(mappedFlagIndex);

                        landmarkIndex = Math.Max(0, Math.Min(landmarkIndex, remainingLandmarks.Count - 1));
                        flagIndex = Math.Max(0, Math.Min(flagIndex, remainingFlags.Count - 1));



                        socketClient.SendEvent("correct_match", "\"landmark\": \"" + selectedLandmark + "\", \"flag\": \"" + mappedFlag + "\", \"remaining\": " + remainingLandmarks.Count);

                        if (remainingLandmarks.Count == 0)
                        {
                            levelFinished = true;
                            SaveLevelProgress(2);
                            socketClient.SendEvent("level_complete", "\"level\": 2, \"user\": \"" + currentUser.Name + "\", \"ageGroup\": \"" + currentUser.AgeGroup + "\"");
                        }
                        else
                        {
                            level2PlaneFlagImagePath = remainingFlags.Count > 0 ? remainingFlags[rng.Next(remainingFlags.Count)] : "";
                        }
                    }
                    else
                    {

                        currentLives--;
                        bool nowGameOver = currentLives <= 0;
                        if (nowGameOver) { currentLives = 0; gameOver = true; }
                        if (gameSounds != null && currentUser != null)
                            gameSounds.OnLifeLost(nowGameOver, currentUser.AgeGroup);

                        socketClient.SendEvent(
                            "wrong_match",
                            "\"landmark\": \"" + selectedLandmark + "\", \"flag\": \"" + level2PlaneFlagImagePath + "\", \"livesLeft\": " + currentLives
                        );
                    }

                    hoveredFlagForPlane = "";
                    hoveredFlagSince = DateTime.MinValue;
                }
            }

            // Optional fallback: keep old TUIO-2 confirm only when plane is NOT present.
            if (!hasPlane && tuio2Now && !tuio2WasPresent && remainingLandmarks.Count > 0 && remainingFlags.Count > 0)
            {
                string selectedLandmark = remainingLandmarks[landmarkIndex];
                string selectedFlag = remainingFlags[flagIndex];
                if (landmarkToFlagMap.ContainsKey(selectedLandmark) && landmarkToFlagMap[selectedLandmark] == selectedFlag)
                {
                    remainingLandmarks.RemoveAt(landmarkIndex);
                    remainingFlags.RemoveAt(flagIndex);
                    landmarkIndex = Math.Max(0, Math.Min(landmarkIndex, remainingLandmarks.Count - 1));
                    flagIndex = Math.Max(0, Math.Min(flagIndex, remainingFlags.Count - 1));

                    if (remainingLandmarks.Count == 0)
                    {
                        levelFinished = true;
                        SaveLevelProgress(2);
                        socketClient.SendEvent("level_complete", "\"level\": 2, \"user\": \"" + currentUser.Name + "\", \"ageGroup\": \"" + currentUser.AgeGroup + "\"");
                    }
                    socketClient.SendEvent("correct_match", "\"landmark\": \"" + selectedLandmark + "\", \"flag\": \"" + selectedFlag + "\", \"remaining\": " + remainingLandmarks.Count);
                }
                else
                {

                    currentLives--;
                    bool nowGameOver = currentLives <= 0;
                    if (nowGameOver) { currentLives = 0; gameOver = true; }
                    if (gameSounds != null && currentUser != null)
                        gameSounds.OnLifeLost(nowGameOver, currentUser.AgeGroup);
                    socketClient.SendEvent("wrong_match", "\"landmark\": \"" + selectedLandmark + "\", \"flag\": \"" + selectedFlag + "\", \"livesLeft\": " + currentLives);
                }
            }
        }

        if (!tuio2Now) { tuio2WasPresent = false; }
        else tuio2WasPresent = true;

        // TUIO 1: rotate to scroll (physical markers only)
        bool foundTuio1 = false;
        foreach (TuioDemoObject t in objectList.Values)
        {
            if (t.SymbolID != 1 || IsMediapipeBridgeSession(t.SessionID)) continue;
            foundTuio1 = true;
            float angle = t.Angle;
            if (lastTuio1Angle != float.MinValue)
            {
                float delta = angle - lastTuio1Angle;
                if (delta > (float)Math.PI) delta -= (float)(2 * Math.PI);
                if (delta < -(float)Math.PI) delta += (float)(2 * Math.PI);
                rotationAccum += delta;
                if (rotationAccum >= rotationThreshold)
                {
                    if (currentLevel == 1)
                    {
                        if (activeBar == 0 && remainingCountries.Count > 0)
                            countryNameIndex = (countryNameIndex + 1) % remainingCountries.Count;
                        else if (activeBar == 1 && remainingFlags.Count > 0)
                            flagIndex = (flagIndex + 1) % remainingFlags.Count;
                    }
                    else
                    {
                        if (activeBar == 0 && remainingLandmarks.Count > 0)
                            landmarkIndex = (landmarkIndex + 1) % remainingLandmarks.Count;
                        else if (activeBar == 1 && remainingFlags.Count > 0)
                            flagIndex = (flagIndex + 1) % remainingFlags.Count;
                    }
                    rotationAccum = 0f;
                }
                else if (rotationAccum <= -rotationThreshold)
                {
                    if (currentLevel == 1)
                    {
                        if (activeBar == 0 && remainingCountries.Count > 0)
                            countryNameIndex = (countryNameIndex - 1 + remainingCountries.Count) % remainingCountries.Count;
                        else if (activeBar == 1 && remainingFlags.Count > 0)
                            flagIndex = (flagIndex - 1 + remainingFlags.Count) % remainingFlags.Count;
                    }
                    else
                    {
                        if (activeBar == 0 && remainingLandmarks.Count > 0)
                            landmarkIndex = (landmarkIndex - 1 + remainingLandmarks.Count) % remainingLandmarks.Count;
                        else if (activeBar == 1 && remainingFlags.Count > 0)
                            flagIndex = (flagIndex - 1 + remainingFlags.Count) % remainingFlags.Count;
                    }
                    rotationAccum = 0f;
                }
            }
            lastTuio1Angle = angle;
            break;
        }
        if (!foundTuio1) lastTuio1Angle = float.MinValue;
    }

    // Image cache 
    private Image GetFlagImageByPath(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return null;
        if (flagImageCacheByPath.ContainsKey(fileName)) return flagImageCacheByPath[fileName];
        string baseDir = Path.GetDirectoryName(Application.ExecutablePath) ?? ".";
        string[] fullPaths = new string[]
        {
            Path.Combine(baseDir, fileName),
            Path.Combine(baseDir, "Flages", fileName),
            Path.Combine(baseDir, "Flags", fileName),
            Path.Combine(baseDir, "LandMarks", fileName),
            Path.Combine(baseDir, "Landmarks", fileName)
        };

        for (int i = 0; i < fullPaths.Length; i++)
        {
            string fullPath = fullPaths[i];
            try
            {
                if (File.Exists(fullPath))
                {
                    Image img = Image.FromFile(fullPath);
                    flagImageCacheByPath[fileName] = img;
                    return img;
                }
            }
            catch { }
        }
        return null;
    }

    private void EnsureChildFlagPositions(Rectangle area)
    {
        if (remainingFlags == null || remainingFlags.Count == 0) return;

        int signature = remainingFlags.Count;
        for (int i = 0; i < remainingFlags.Count; i++)
            signature = (signature * 31) + (remainingFlags[i] != null ? remainingFlags[i].GetHashCode() : 0);

        if (signature == childFlagLayoutSignature) return;

        childFlagLayoutSignature = signature;
        childFlagRandomPositions.Clear();

        int minX = area.Left + 10;
        int minY = area.Top + 10;
        int maxX = Math.Max(minX, area.Right - ChildFlagWidth - 10);
        int maxY = Math.Max(minY, area.Bottom - ChildFlagHeight - 10);
        List<Rectangle> placedRects = new List<Rectangle>();

        foreach (string flag in remainingFlags)
        {
            Rectangle candidate = new Rectangle(minX, minY, ChildFlagWidth, ChildFlagHeight);
            bool placed = false;

            for (int attempt = 0; attempt < 300; attempt++)
            {
                int x = (maxX > minX) ? rng.Next(minX, maxX + 1) : minX;
                int y = (maxY > minY) ? rng.Next(minY, maxY + 1) : minY;
                candidate = new Rectangle(x, y, ChildFlagWidth, ChildFlagHeight);

                Rectangle withMargin = Rectangle.Inflate(candidate, ChildFlagMargin, ChildFlagMargin);
                bool overlaps = false;
                for (int j = 0; j < placedRects.Count; j++)
                {
                    if (withMargin.IntersectsWith(placedRects[j]))
                    {
                        overlaps = true;
                        break;
                    }
                }
                if (!overlaps)
                {
                    placed = true;
                    break;
                }
            }

            if (!placed && placedRects.Count > 0)
            {
                // Fallback if area is tight: still keep random location.
                int x = (maxX > minX) ? rng.Next(minX, maxX + 1) : minX;
                int y = (maxY > minY) ? rng.Next(minY, maxY + 1) : minY;
                candidate = new Rectangle(x, y, ChildFlagWidth, ChildFlagHeight);
            }

            placedRects.Add(candidate);
            childFlagRandomPositions[flag] = new Point(candidate.X, candidate.Y);
        }
    }

    private void EnsureLevel2LandmarkPositions(Rectangle area)
    {
        if (remainingLandmarks == null || remainingLandmarks.Count == 0) return;

        int signature = remainingLandmarks.Count;
        for (int i = 0; i < remainingLandmarks.Count; i++)
            signature = (signature * 31) + (remainingLandmarks[i] != null ? remainingLandmarks[i].GetHashCode() : 0);

        if (signature == level2LandmarkLayoutSignature) return;

        level2LandmarkLayoutSignature = signature;
        level2LandmarkRandomPositions.Clear();

        int minX = area.Left + 10;
        int minY = area.Top + 10;
        int maxX = Math.Max(minX, area.Right - ChildFlagWidth - 10);
        int maxY = Math.Max(minY, area.Bottom - ChildFlagHeight - 10);

        List<Rectangle> placedRects = new List<Rectangle>();

        foreach (string lm in remainingLandmarks)
        {
            Rectangle candidate = new Rectangle(minX, minY, ChildFlagWidth, ChildFlagHeight);
            bool placed = false;

            for (int attempt = 0; attempt < 300; attempt++)
            {
                int x = (maxX > minX) ? rng.Next(minX, maxX + 1) : minX;
                int y = (maxY > minY) ? rng.Next(minY, maxY + 1) : minY;
                candidate = new Rectangle(x, y, ChildFlagWidth, ChildFlagHeight);

                Rectangle withMargin = Rectangle.Inflate(candidate, ChildFlagMargin, ChildFlagMargin);
                bool overlaps = false;
                for (int j = 0; j < placedRects.Count; j++)
                {
                    if (withMargin.IntersectsWith(placedRects[j]))
                    {
                        overlaps = true;
                        break;
                    }
                }

                if (!overlaps)
                {
                    placed = true;
                    break;
                }
            }

            if (!placed && placedRects.Count > 0)
            {
                int x = (maxX > minX) ? rng.Next(minX, maxX + 1) : minX;
                int y = (maxY > minY) ? rng.Next(minY, maxY + 1) : minY;
                candidate = new Rectangle(x, y, ChildFlagWidth, ChildFlagHeight);
            }

            placedRects.Add(candidate);
            level2LandmarkRandomPositions[lm] = new Point(candidate.X, candidate.Y);
        }
    }

    private Image GetImageFromCommonPaths(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return null;
        if (flagImageCacheByPath.ContainsKey(fileName)) return flagImageCacheByPath[fileName];

        string exeDir = Path.GetDirectoryName(Application.ExecutablePath) ?? ".";
        string startupDir = Application.StartupPath ?? ".";
        string cwd = Environment.CurrentDirectory ?? ".";
        string debugDir = Path.GetFullPath(Path.Combine(exeDir, ".."));
        string binDir = Path.GetFullPath(Path.Combine(exeDir, "..", ".."));

        string[] candidates = new string[]
        {
            Path.Combine(exeDir, fileName),
            Path.Combine(startupDir, fileName),
            Path.Combine(cwd, fileName),
            Path.Combine(debugDir, fileName),
            Path.Combine(binDir, fileName),
            Path.Combine(exeDir, "Assets", fileName),
            Path.Combine(startupDir, "Assets", fileName),
            Path.Combine(cwd, "Assets", fileName),
            Path.Combine(debugDir, "Assets", fileName),
            Path.Combine(binDir, "Assets", fileName)
        };

        for (int i = 0; i < candidates.Length; i++)
        {
            string fullPath = candidates[i];
            try
            {
                if (!File.Exists(fullPath)) continue;
                Image img = Image.FromFile(fullPath);
                flagImageCacheByPath[fileName] = img;
                return img;
            }
            catch { }
        }

        return null;
    }

    /// <summary>True if a reacTIVision (non-bridge) object with this symbol is on the table.</summary>
    private bool AnyPhysicalTuioSymbolPresent(int symbolId)
    {
        lock (objectSync)
        {
            foreach (TuioDemoObject t in objectList.Values)
            {
                if (t.SymbolID == symbolId && !IsMediapipeBridgeSession(t.SessionID))
                    return true;
            }
        }
        return false;
    }

    private TuioDemoObject FindSignupDialTuioObject()
    {
        TuioDemoObject physical = null;
        TuioDemoObject bridgeDial = null;
        lock (objectSync)
        {
            foreach (TuioDemoObject t in objectList.Values)
            {
                if (t.SymbolID != 4) continue;
                if (IsMediapipeBridgeSession(t.SessionID))
                {
                    if (t.SessionID == MediapipeDialSessionId)
                        bridgeDial = t;
                }
                else
                    physical = t;
            }
        }
        if (physical != null) return physical;
        if (bridgeDial != null) return bridgeDial;
        return null;
    }

    private TuioDemoObject GetPhysicalTuioObjectBySymbol(int symbolId)
    {
        lock (objectSync)
        {
            foreach (TuioDemoObject t in objectList.Values)
            {
                if (t.SymbolID == symbolId && !IsMediapipeBridgeSession(t.SessionID))
                    return t;
            }
        }
        return null;
    }

    private TuioDemoObject GetTuioObjectBySymbol(int symbolId)
    {
        lock (objectSync)
        {
            TuioDemoObject bridgeFallback = null;
            foreach (TuioDemoObject t in objectList.Values)
            {
                if (t.SymbolID != symbolId) continue;
                if (!IsMediapipeBridgeSession(t.SessionID))
                    return t;
                if (bridgeFallback == null)
                    bridgeFallback = t;
            }
            return bridgeFallback;
        }
    }

    private bool TryGetPlaneScreenPoint(out int px, out int py, out float angleRad)
    {
        // Admin dashboard + name keyboard: webcam laser dot via TUIO plane (phase 3 hci).
        // Ignore MediaPipe finger/point8 so a stale hand cursor does not block the laser.
        if (keyboardPointerIsLaser || IsVirtualKeyboardScreen())
        {
            TuioDemoObject laserPlane = GetTuioObjectBySymbol(TuioMouseSymbolId);
            if (laserPlane != null)
            {
                px = laserPlane.getScreenX(width);
                py = laserPlane.getScreenY(height);
                angleRad = 0f;
                return true;
            }
            px = 0;
            py = 0;
            angleRad = 0f;
            return false;
        }

        // Physical reacTIVision marker 10 (table fiducial) beats virtual bottle/finger when present.
        TuioDemoObject physicalPlane = GetPhysicalTuioObjectBySymbol(TuioMouseSymbolId);
        if (physicalPlane != null)
        {
            px = physicalPlane.getScreenX(width);
            py = physicalPlane.getScreenY(height);
            angleRad = physicalPlane.Angle;
            return true;
        }

        // Prefer live MediaPipe pointer whenever updates are recent.
        if (mediapipePoint8Active &&
            (DateTime.Now - mediapipePoint8UpdatedAt).TotalMilliseconds <= MediapipePointerFreshMs)
        {
            px = (int)(mediapipePoint8X * width);
            py = (int)(mediapipePoint8Y * height);
            angleRad = 0f;
            return true;
        }

        int holdMs = (screenState == ScreenAdminUsers)
            ? MediapipePointerHoldSignupNameMs : MediapipePointerHoldMs;
        if (mediapipeLastActivePoint8At != DateTime.MinValue &&
            (DateTime.Now - mediapipeLastActivePoint8At).TotalMilliseconds <= holdMs)
        {
            px = (int)(mediapipeLastActivePoint8X * width);
            py = (int)(mediapipeLastActivePoint8Y * height);
            angleRad = 0f;
            return true;
        }

        TuioDemoObject plane = GetTuioObjectBySymbol(TuioMouseSymbolId);
        if (plane != null)
        {
            px = plane.getScreenX(width);
            py = plane.getScreenY(height);
            angleRad = plane.Angle;
            return true;
        }

        px = 0;
        py = 0;
        angleRad = 0f;
        return false;
    }

    /// <summary>Soft, low-contrast gaze marker from Python (TUIO symbol 11); drawn under the plane overlay.</summary>
    private void DrawGazeSoftMarker(Graphics g)
    {
        gazeHintScreenX = -1;
        gazeHintScreenY = -1;

        TuioDemoObject gazeObj = GetTuioObjectBySymbol(TuioGazeSymbolId);
        bool gazeTuioLive = gazeObj != null && IsMediapipeBridgeSession(gazeObj.SessionID);

        void CommitLatch(int cx, int cy)
        {
            gazeLatchScreenX = cx;
            gazeLatchScreenY = cy;
            gazeLatchUpdatedAt = DateTime.Now;
        }

        bool UseLatchedGazeDot()
        {
            if (!IsSadOrConfusedHintMood()) return false;
            if (gazeLatchScreenX < 0 || gazeLatchScreenY < 0) return false;
            if ((DateTime.Now - gazeLatchUpdatedAt).TotalSeconds > GazeHintLatchSeconds) return false;
            gazeHintScreenX = gazeLatchScreenX;
            gazeHintScreenY = gazeLatchScreenY;
            DrawSubtleAnchorDot(gazeLatchScreenX, gazeLatchScreenY, 28, 55);
            return true;
        }

        void TryHintAnchorFromPointer8()
        {
            if (!IsSadOrConfusedHintMood()) return;
            if (mediapipeLastActivePoint8At == DateTime.MinValue) return;
            if ((DateTime.Now - mediapipeLastActivePoint8At).TotalMilliseconds > HintPoint8FallbackHoldMs) return;
            int ax = (int)Math.Round(Math.Max(0f, Math.Min(1f, mediapipeLastActivePoint8X)) * width);
            int ay = (int)Math.Round(Math.Max(0f, Math.Min(1f, mediapipeLastActivePoint8Y)) * height);
            gazeHintScreenX = ax;
            gazeHintScreenY = ay;
        }

        void DrawSubtleAnchorDot(int cx, int cy, int fillA, int edgeA)
        {
            int d = GazeMarkerDiameter;
            int half = d / 2;
            Rectangle rect = new Rectangle(cx - half, cy - half, d, d);
            SmoothingMode oldSM = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(fillA, 125, 188, 228)))
                g.FillEllipse(fill, rect);
            using (Pen edge = new Pen(Color.FromArgb(edgeA, 200, 230, 255), 1.2f))
                g.DrawEllipse(edge, rect);
            g.SmoothingMode = oldSM;
        }

        if (!gazeTuioLive)
        {
            gazeMarkerDrawX = -1f;
            if (!UseLatchedGazeDot())
            {
                TryHintAnchorFromPointer8();
                if (gazeHintScreenX >= 0 && gazeHintScreenY >= 0)
                    DrawSubtleAnchorDot(gazeHintScreenX, gazeHintScreenY, 38, 62);
            }
            return;
        }

        int tx = gazeObj.getScreenX(width);
        int ty = gazeObj.getScreenY(height);

        if (gazeMarkerDrawX < 0f)
        {
            gazeMarkerDrawX = tx;
            gazeMarkerDrawY = ty;
        }
        else
        {
            gazeMarkerDrawX += (tx - gazeMarkerDrawX) * GazeMarkerSmooth;
            gazeMarkerDrawY += (ty - gazeMarkerDrawY) * GazeMarkerSmooth;
        }

        int lx = (int)Math.Round(gazeMarkerDrawX);
        int ly = (int)Math.Round(gazeMarkerDrawY);
        gazeHintScreenX = lx;
        gazeHintScreenY = ly;
        CommitLatch(lx, ly);
        DrawSubtleAnchorDot(lx, ly, 50, 70);
    }

    private void ArmCountryHintAfterLifeLoss()
    {
        if (screenState != ScreenGame || currentLevel != 1 || gameOver) return;
        countryHintAfterLifeLoss = true;
        if (emotionPaintTimer != null)
            emotionPaintTimer.Start();
        if (InvokeRequired)
            BeginInvoke(new Action(Invalidate));
        else
            Invalidate();
    }

    private void ClearCountryHintAfterLifeLoss()
    {
        if (!countryHintAfterLifeLoss) return;
        countryHintAfterLifeLoss = false;
        if (InvokeRequired)
            BeginInvoke(new Action(Invalidate));
        else
            Invalidate();
    }

    private bool TryGetCountryHintAnchor(out int anchorX, out int anchorY)
    {
        anchorX = gazeHintScreenX;
        anchorY = gazeHintScreenY;
        if (anchorX >= 0 && anchorY >= 0) return true;
        int px, py;
        float a;
        if (TryGetPlaneScreenPoint(out px, out py, out a))
        {
            anchorX = px;
            anchorY = py;
            return true;
        }
        return false;
    }

    private const float CountryHintBubbleMaxW = 420f;
    private const float CountryHintBubblePadX = 24f;
    private const float CountryHintBubblePadY = 14f;

    private SizeF MeasureCountryHintText(Graphics g, string text, float maxInnerW)
    {
        if (string.IsNullOrWhiteSpace(text)) return SizeF.Empty;
        using (Font hf = new Font("Arial", Math.Max(theme.SmallFontSize - 1f, 9f), FontStyle.Regular))
        using (StringFormat fmt = new StringFormat())
        {
            fmt.Trimming = StringTrimming.Word;
            return g.MeasureString(text, hf, (int)Math.Max(80f, maxInnerW), fmt);
        }
    }

    private void DrawCountryHintBubbleFrame(Graphics g, RectangleF bubble)
    {
        Rectangle shadow = new Rectangle((int)bubble.X + 2, (int)bubble.Y + 3, (int)bubble.Width, (int)bubble.Height);
        using (SolidBrush sb = new SolidBrush(Color.FromArgb(190, 8, 12, 24)))
            DrawRoundedRect(g, sb, null, shadow, 10);
        using (SolidBrush fill = new SolidBrush(Color.FromArgb(235, 32, 40, 58)))
        using (Pen border = new Pen(Color.FromArgb(210, 120, 200, 255), 1.2f))
            DrawRoundedRect(g, fill, border, Rectangle.Round(bubble), 10);
    }

    private void DrawHintWhiteText(Graphics g, string text, RectangleF textArea)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        using (Font hf = new Font("Arial", Math.Max(theme.SmallFontSize - 1f, 9f), FontStyle.Regular))
        using (SolidBrush wb = new SolidBrush(Color.FromArgb(255, 248, 250, 255)))
        using (StringFormat fmt = new StringFormat())
        {
            fmt.Alignment = StringAlignment.Near;
            fmt.LineAlignment = StringAlignment.Near;
            fmt.Trimming = StringTrimming.Word;
            g.DrawString(text, hf, wb, textArea, fmt);
        }
    }

    private RectangleF CountryHintInnerTextRect(RectangleF bubble)
    {
        return new RectangleF(
            bubble.X + CountryHintBubblePadX * 0.5f,
            bubble.Y + CountryHintBubblePadY * 0.5f,
            bubble.Width - CountryHintBubblePadX,
            bubble.Height - CountryHintBubblePadY);
    }

    private void DrawCountryHintTier1(Graphics g, RectangleF bubble, string country)
    {
        DrawHintWhiteText(g, CountryHintHelper.GetHintTier1Text(country), CountryHintInnerTextRect(bubble));
    }

    private void DrawCountryHintTier2(Graphics g, RectangleF bubble, string country)
    {
        DrawHintWhiteText(g, CountryHintHelper.GetHintTier2Text(country), CountryHintInnerTextRect(bubble));
    }

    private void DrawCountryHintBubble(Graphics g, int anchorX, int anchorY, string country, int tier, float stackIndex)
    {
        string hintText = tier == 1
            ? CountryHintHelper.GetHintTier1Text(country)
            : CountryHintHelper.GetHintTier2Text(country);
        float maxInnerW = Math.Min(CountryHintBubbleMaxW, width - 24f) - CountryHintBubblePadX;
        SizeF textSz = MeasureCountryHintText(g, hintText, maxInnerW);
        float bubbleW = Math.Min(CountryHintBubbleMaxW, Math.Max(120f, textSz.Width + CountryHintBubblePadX));
        float bubbleH = Math.Max(36f, textSz.Height + CountryHintBubblePadY);
        float halfDot = GazeMarkerDiameter / 2f + 10f;
        float x = anchorX - bubbleW * 0.5f;
        float y = anchorY + halfDot + stackIndex * (bubbleH + 6f);
        x = Math.Max(6f, Math.Min(x, width - bubbleW - 6f));
        y = Math.Max(6f, Math.Min(y, height - bubbleH - 6f));
        RectangleF bubble = new RectangleF(x, y, bubbleW, bubbleH);

        SmoothingMode prev = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        DrawCountryHintBubbleFrame(g, bubble);
        if (tier == 1)
            DrawCountryHintTier1(g, bubble, country);
        else
            DrawCountryHintTier2(g, bubble, country);
        g.SmoothingMode = prev;
    }

    private void DrawCountrySmartHint(Graphics g)
    {
        if (screenState != ScreenGame || currentLevel != 1 || gameOver) return;
        if (remainingCountries == null || remainingCountries.Count == 0) return;

        bool showTier1 = IsSadOrConfusedHintMood();
        bool showTier2 = countryHintAfterLifeLoss;
        if (!showTier1 && !showTier2) return;

        int anchorX, anchorY;
        if (!TryGetCountryHintAnchor(out anchorX, out anchorY)) return;

        string country = remainingCountries[countryNameIndex];
        float stack = 0f;
        if (showTier1)
        {
            DrawCountryHintBubble(g, anchorX, anchorY, country, 1, stack);
            stack += 1f;
        }
        if (showTier2)
        {
            DrawCountryHintBubble(g, anchorX, anchorY, country, 2, stack);
        }
    }

    private bool HasPlanePointer()
    {
        int px, py;
        float a;
        return TryGetPlaneScreenPoint(out px, out py, out a);
    }
 
    private void DrawRoundedRect(Graphics g, Brush fill, Pen border, Rectangle rect, int radius)
    {
        using (GraphicsPath path = new GraphicsPath())
        {
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            if (fill != null) g.FillPath(fill, path);
            if (border != null) g.DrawPath(border, path);
        }
    }

    private void DrawCenteredString(Graphics g, string text, Font font, Brush brush, Rectangle rect)
    {
        SizeF s = g.MeasureString(text, font);
        g.DrawString(text, font, brush, rect.X + (rect.Width - s.Width) / 2f, rect.Y + (rect.Height - s.Height) / 2f);
    }

 
    private void EmotionPaintTimer_Tick(object sender, EventArgs e)
    {
        bool ambientFresh = IsFacialEmotionVisualFresh();
        bool hintMoodLong =
            screenState == ScreenGame
            && currentLevel == 1
            && !gameOver
            && facialEmotionUpdatedAt != DateTime.MinValue
            && (facialEmotionMood == "sad" || facialEmotionMood == "confused")
            && (DateTime.Now - facialEmotionUpdatedAt).TotalSeconds < FacialHintMoodStaleSeconds;
        bool lifeLossHintActive =
            screenState == ScreenGame && currentLevel == 1 && !gameOver && countryHintAfterLifeLoss;
        if (!ambientFresh && !hintMoodLong && !lifeLossHintActive)
        {
            emotionPaintTimer.Stop();
            return;
        }
        Invalidate();
    }

    private bool IsFacialEmotionVisualFresh()
    {
        if (facialEmotionUpdatedAt == DateTime.MinValue) return false;
        return (DateTime.Now - facialEmotionUpdatedAt).TotalSeconds < 48.0;
    }

    private bool IsSadOrConfusedHintMood()
    {
        if (facialEmotionUpdatedAt == DateTime.MinValue) return false;
        if (facialEmotionMood != "sad" && facialEmotionMood != "confused") return false;
        return (DateTime.Now - facialEmotionUpdatedAt).TotalSeconds < FacialHintMoodStaleSeconds;
    }

    /// <summary>Full-screen mood wash + banner + context (hub: user_facial_emotion).</summary>
    private void DrawFacialEmotionAmbient(Graphics g)
    {
        if (!IsFacialEmotionVisualFresh()) return;
        if (facialEmotionMood != "happy" && facialEmotionMood != "sad" && facialEmotionMood != "confused") return;

        Rectangle full = new Rectangle(0, 0, width, height);
        int wash = (int)Math.Max(38, Math.Min(100, 50 + facialEmotionScore * 52));
        if (facialEmotionMood == "happy")
        {
            using (SolidBrush b0 = new SolidBrush(Color.FromArgb(wash, 255, 245, 175)))
                g.FillRectangle(b0, full);
            using (LinearGradientBrush b1 = new LinearGradientBrush(full,
                Color.FromArgb(78, 255, 205, 70),
                Color.FromArgb(0, 255, 255, 210),
                LinearGradientMode.ForwardDiagonal))
                g.FillRectangle(b1, full);
        }
        else if (facialEmotionMood == "confused")
        {
            using (SolidBrush b0 = new SolidBrush(Color.FromArgb(wash, 40, 52, 72)))
                g.FillRectangle(b0, full);
            using (LinearGradientBrush b1 = new LinearGradientBrush(full,
                Color.FromArgb(65, 92, 118, 148),
                Color.FromArgb(0, 26, 38, 62),
                LinearGradientMode.ForwardDiagonal))
                g.FillRectangle(b1, full);
        }
        else
        {
            using (SolidBrush b0 = new SolidBrush(Color.FromArgb(wash, 28, 38, 72)))
                g.FillRectangle(b0, full);
            using (LinearGradientBrush b1 = new LinearGradientBrush(full,
                Color.FromArgb(72, 48, 62, 118),
                Color.FromArgb(0, 18, 26, 52),
                LinearGradientMode.ForwardDiagonal))
                g.FillRectangle(b1, full);
        }

        int bandH = Math.Min(160, height / 4);
        Rectangle topBand = new Rectangle(0, 0, width, bandH);
        int edgeA = (int)Math.Max(30, Math.Min(118, 48 + facialEmotionScore * 72));
        if (facialEmotionMood == "happy")
        {
            using (LinearGradientBrush b = new LinearGradientBrush(topBand,
                Color.FromArgb(edgeA, 255, 215, 60),
                Color.FromArgb(0, 255, 250, 190),
                LinearGradientMode.Vertical))
                g.FillRectangle(b, topBand);
        }
        else if (facialEmotionMood == "confused")
        {
            using (LinearGradientBrush b = new LinearGradientBrush(topBand,
                Color.FromArgb(edgeA, 88, 120, 150),
                Color.FromArgb(0, 44, 62, 92),
                LinearGradientMode.Vertical))
                g.FillRectangle(b, topBand);
        }
        else
        {
            using (LinearGradientBrush b = new LinearGradientBrush(topBand,
                Color.FromArgb(edgeA, 68, 88, 158),
                Color.FromArgb(0, 38, 46, 92),
                LinearGradientMode.Vertical))
                g.FillRectangle(b, topBand);
        }

        if (!string.IsNullOrEmpty(facialEmotionModeLabel))
        {
            using (Font bf = new Font("Arial", theme.BodyFontSize + 11, FontStyle.Bold))
            {
                Color tc = facialEmotionMood == "happy"
                    ? Color.FromArgb(255, 145, 55, 0)
                    : Color.FromArgb(255, 220, 230, 255);
                using (SolidBrush bb = new SolidBrush(tc))
                {
                    SizeF sz = g.MeasureString(facialEmotionModeLabel, bf);
                    g.DrawString(facialEmotionModeLabel, bf, bb, (width - sz.Width) / 2f, 8f);
                }
            }
        }
        if (!string.IsNullOrEmpty(facialEmotionContext) && screenState != ScreenGame)
        {
            using (Font cf = new Font("Arial", theme.SmallFontSize + 4, FontStyle.Italic))
            {
                Color cc = facialEmotionMood == "happy"
                    ? Color.FromArgb(248, 62, 38, 8)
                    : Color.FromArgb(238, 195, 210, 245);
                using (SolidBrush cb = new SolidBrush(cc))
                {
                    SizeF cz = g.MeasureString(facialEmotionContext, cf);
                    g.DrawString(facialEmotionContext, cf, cb, Math.Max(14f, (width - cz.Width) / 2f), 22f + theme.BodyFontSize + 11f);
                }
            }
        }
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        Graphics g = pevent.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        theme.Update();

        using (SolidBrush bg = new SolidBrush(theme.Background))
            g.FillRectangle(bg, 0, 0, width, height);

        DrawFacialEmotionAmbient(g);

        switch (screenState)
        {
            case ScreenModeSelect: DrawModeSelectScreen(g); break;
            case ScreenFaceIdentify: DrawFaceIdentifyScreen(g); break;
            case ScreenAdminUsers: DrawAdminUsersScreen(g); break;
            case ScreenSignup: DrawSignupScreen(g); break;
            case ScreenSignupName: DrawSignupNameScreen(g); break;
            case ScreenAdminEditName: DrawSignupNameScreen(g); break;
            case ScreenGreeting: DrawGreetingScreen(g); break;
            case ScreenGame: DrawGameScreen(g); break;
            case ScreenLevelTransition: DrawLevelTransitionScreen(g); break;
        }

        DrawTuioMouseOverlay(g);
    }

    private void DrawModeSelectScreen(Graphics g)
    {
        int cx = width / 2;
        int y = 50;

        Color titleColor = theme.Accent;
        if (IsFacialEmotionVisualFresh() && facialEmotionMood == "happy")
            titleColor = Color.FromArgb(255, 220, 85, 0);
        else if (IsFacialEmotionVisualFresh() && facialEmotionMood == "sad")
            titleColor = Color.FromArgb(255, 150, 185, 255);

        using (Font f = new Font("Arial", theme.TitleFontSize, FontStyle.Bold))
        using (SolidBrush b = new SolidBrush(titleColor))
        {
            string title = "Matching Countries";
            SizeF s = g.MeasureString(title, f);
            g.DrawString(title, f, b, cx - s.Width / 2f, y);
            y += (int)s.Height + 30;
        }

        using (Font f = new Font("Arial", theme.BodyFontSize))
        using (SolidBrush b = new SolidBrush(theme.TextPrimary))
        {
            string line = "Detecting active input mode...";
            SizeF s = g.MeasureString(line, f);
            g.DrawString(line, f, b, cx - s.Width / 2f, y);
            y += (int)s.Height + 26;
        }

        using (Font f = new Font("Arial", theme.BodyFontSize))
        using (SolidBrush b = new SolidBrush(theme.TextSecondary))
        {
            string dots = new string('.', (DateTime.Now.Millisecond / 250) % 4);
            string status = modeDetectStatus + dots;
            SizeF s = g.MeasureString(status, f);
            g.DrawString(status, f, b, cx - s.Width / 2f, y);
            y += (int)s.Height + 20;
        }

        using (Font f = new Font("Arial", theme.SmallFontSize))
        using (SolidBrush b = new SolidBrush(theme.TextSecondary))
        {
            string hint = "Face login via hub  ·  reacTIVision = gameplay only  ·  TUIO 0: Admin  ·  Escape: quit  ·  F1: fullscreen";
            SizeF s = g.MeasureString(hint, f);
            g.DrawString(hint, f, b, cx - s.Width / 2f, y);
        }
    }

    private void DrawFaceIdentifyScreen(Graphics g)
    {
        int cx = width / 2;
        int y = 40;

        Color titleColor = theme.Accent;
        if (IsFacialEmotionVisualFresh() && facialEmotionMood == "happy")
            titleColor = Color.FromArgb(255, 230, 100, 15);
        else if (IsFacialEmotionVisualFresh() && facialEmotionMood == "sad")
            titleColor = Color.FromArgb(255, 165, 195, 255);

        using (Font f = new Font("Arial", theme.TitleFontSize, FontStyle.Bold))
        using (SolidBrush b = new SolidBrush(titleColor))
        {
            string title = "Matching Countries";
            SizeF s = g.MeasureString(title, f);
            g.DrawString(title, f, b, cx - s.Width / 2f, y);
            y += (int)s.Height + 12;
        }

        using (Font f = new Font("Arial", theme.BodyFontSize))
        using (SolidBrush b = new SolidBrush(theme.TextSecondary))
        {
            string sub = "Sign in with your face (Camo / phone camera via face_auth_service). TUIO markers are not used for login.";
            SizeF s = g.MeasureString(sub, f);
            g.DrawString(sub, f, b, cx - s.Width / 2f, y);
            y += (int)s.Height + 20;
        }

        using (Font f = new Font("Arial", theme.BodyFontSize))
        using (SolidBrush b = new SolidBrush(theme.TextPrimary))
        {
            if (!string.IsNullOrWhiteSpace(faceIdentifyHint))
            {
                SizeF s = g.MeasureString(faceIdentifyHint, f);
                g.DrawString(faceIdentifyHint, f, b, cx - s.Width / 2f, y);
                y += (int)s.Height + 16;
            }
        }

        using (Font f = new Font("Arial", theme.SmallFontSize))
        using (SolidBrush b = new SolidBrush(theme.TextSecondary))
        {
            string hub = mediapipeSocketConnected ? "Hub: connected" : "Hub: disconnected — start notification_hub";
            string cam = mediapipeCameraActive ? "Face camera: active" : "Face camera: waiting for face_auth_service…";
            g.DrawString(hub + "  ·  " + cam, f, b, cx - g.MeasureString(hub + "  ·  " + cam, f).Width / 2f, y);
            y += 36;
            string play = "After login: MediaPipe hand gestures (TUIO-only service) move the in-game pointer; optional reacTIVision TUIO or on-screen controls.";
            g.DrawString(play, f, b, cx - g.MeasureString(play, f).Width / 2f, y);
        }

        if (!string.IsNullOrEmpty(loginError))
        {
            using (Font ef = new Font("Arial", theme.BodyFontSize))
            using (SolidBrush eb = new SolidBrush(theme.ErrorColor))
            {
                SizeF es = g.MeasureString(loginError, ef);
                g.DrawString(loginError, ef, eb, cx - es.Width / 2f, height - 80);
            }
        }
    }

    private void DrawAdminButton(Graphics g, string label, Rectangle rect, bool primary, out Rectangle outRect)
    {
        outRect = rect;
        Color bg = primary ? theme.Accent : theme.CardBackground;
        using (SolidBrush fill = new SolidBrush(bg))
        using (Pen border = new Pen(theme.BorderColor, 2f))
            DrawRoundedRect(g, fill, border, rect, 8);
        using (Font f = new Font("Arial", theme.SmallFontSize + 1, FontStyle.Bold))
        using (SolidBrush tb = new SolidBrush(primary ? Color.White : theme.TextPrimary))
        {
            SizeF ts = g.MeasureString(label, f);
            g.DrawString(label, f, tb, rect.X + (rect.Width - ts.Width) / 2f, rect.Y + (rect.Height - ts.Height) / 2f);
        }
    }

    private void LayoutAdminScreen()
    {
        adminUserCardHits.Clear();
        int pad = 16;
        int top = 20;
        int btnW = 88;
        int btnH = 34;
        int btnGap = 8;
        int toolbarRight = width - pad;

        adminBtnExitRect = new Rectangle(toolbarRight - btnW, top, btnW, btnH);
        adminBtnDeleteRect = new Rectangle(adminBtnExitRect.X - btnGap - btnW, top, btnW, btnH);
        adminBtnSaveRect = new Rectangle(adminBtnDeleteRect.X - btnGap - btnW, top, btnW, btnH);
        adminBtnEditNameRect = new Rectangle(adminBtnSaveRect.X - btnGap - btnW, top, btnW, btnH);
        adminBtnNewRect = new Rectangle(adminBtnEditNameRect.X - btnGap - btnW, top, btnW, btnH);

        top += btnH + 12;
        int listLeft = pad;
        int listTop = top + 28;
        int panelLeft = width * 3 / 5;
        int listW = panelLeft - listLeft - pad;
        int listBottom = height - 50;

        List<UserData> users = userManager.AllUsers;
        var playerUsers = new List<UserData>();
        foreach (UserData u in users)
        {
            if (u != null && !adminManager.IsAdminTuioId(u.TuioId))
                playerUsers.Add(u);
        }

        int cardW = Math.Min(240, listW - 8);
        int cardH = 88;
        int gap = 10;
        int yy = listTop;

        foreach (UserData u in playerUsers)
        {
            if (yy + cardH > listBottom) break;
            Rectangle card = new Rectangle(listLeft, yy, cardW, cardH);
            adminUserCardHits.Add(new KeyValuePair<Rectangle, int>(card, u.TuioId));
            yy += cardH + gap;
        }

        int px = panelLeft + pad;
        int py = listTop + 36;
        int panelW = width - px - pad;

        if (adminSelectedTuioId > 0 || adminCreatingNew)
            LayoutAdminEditPanel(px, listTop, panelW, ref py);
    }

    /// <summary>Right-panel hit areas for edit form (must match DrawAdminUsersScreen).</summary>
    private void LayoutAdminEditPanel(int px, int listTop, int panelW, ref int py)
    {
        py = listTop + 36;
        py += 22;
        py += 22;
        py += 14;
        int chipW = 80;
        adminGenderMaleRect = new Rectangle(px, py, chipW, 30);
        adminGenderFemaleRect = new Rectangle(px + chipW + 8, py, chipW, 30);
        py += 42;
        adminAgeChildRect = new Rectangle(px, py, chipW, 30);
        adminAgeTeenRect = new Rectangle(px + chipW + 8, py, chipW, 30);
        adminAgeAdultRect = new Rectangle(px + 2 * (chipW + 8), py, chipW, 30);
        py += 42;
        adminLevelMinusRect = new Rectangle(px, py, 36, 30);
        adminLevelPlusRect = new Rectangle(px + 44, py, 36, 30);
    }

    private string HitTestAdminTarget(int x, int y)
    {
        Point p = new Point(x, y);
        if (adminBtnNewRect.Contains(p)) return "new";
        if (adminBtnEditNameRect.Contains(p)) return "editname";
        if (adminBtnSaveRect.Contains(p)) return "save";
        if (adminBtnDeleteRect.Contains(p)) return "delete";
        if (adminBtnExitRect.Contains(p)) return "exit";
        if (adminGenderMaleRect.Contains(p)) return "gender:male";
        if (adminGenderFemaleRect.Contains(p)) return "gender:female";
        if (adminAgeChildRect.Contains(p)) return "age:child";
        if (adminAgeTeenRect.Contains(p)) return "age:teen";
        if (adminAgeAdultRect.Contains(p)) return "age:adult";
        if (adminLevelMinusRect.Contains(p)) return "level:minus";
        if (adminLevelPlusRect.Contains(p)) return "level:plus";
        foreach (var hit in adminUserCardHits)
        {
            if (hit.Key.Contains(p))
                return "user:" + hit.Value;
        }
        return "";
    }

    private void UpdateAdminPointerHold()
    {
        int px, py;
        float angle;
        if (!TryGetPlaneScreenPoint(out px, out py, out angle))
        {
            adminPointerHoverTarget = "";
            adminPointerHoldTarget = "";
            adminPointerHoldSince = DateTime.MinValue;
            return;
        }

        adminPointerHoverTarget = HitTestAdminTarget(px, py);
        bool holdActive = !string.IsNullOrEmpty(adminPointerHoverTarget);

        if (!holdActive)
        {
            adminPointerHoldTarget = "";
            adminPointerHoldSince = DateTime.MinValue;
            return;
        }

        if (DateTime.Now < adminPointerHoldCooldownUntil)
            return;

        if (adminPointerHoldTarget != adminPointerHoverTarget)
        {
            adminPointerHoldTarget = adminPointerHoverTarget;
            adminPointerHoldSince = DateTime.Now;
            return;
        }

        double elapsedMs = (DateTime.Now - adminPointerHoldSince).TotalMilliseconds;
        if (elapsedMs >= AdminPointerHoldMs)
        {
            ApplyAdminHoldTarget(adminPointerHoldTarget);
            adminPointerHoldTarget = "";
            adminPointerHoldSince = DateTime.MinValue;
            adminPointerHoldCooldownUntil = DateTime.Now.AddMilliseconds(500);
        }
    }

    private float GetAdminHoldProgress()
    {
        if (string.IsNullOrEmpty(adminPointerHoldTarget) || adminPointerHoldSince == DateTime.MinValue)
            return 0f;
        double elapsedMs = (DateTime.Now - adminPointerHoldSince).TotalMilliseconds;
        return (float)Math.Max(0.0, Math.Min(1.0, elapsedMs / AdminPointerHoldMs));
    }

    private void ApplyAdminHoldTarget(string target)
    {
        if (string.IsNullOrEmpty(target)) return;
        if (target == "new") { AdminBeginCreateUser(); return; }
        if (target == "save") { AdminSaveCurrent(); return; }
        if (target == "delete") { AdminDeleteCurrent(); return; }
        if (target == "exit") { ExitAdminDashboard(); return; }
        if (target == "gender:male") { adminEditGender = "Male"; adminStatusMessage = "Gender: Male"; Invalidate(); return; }
        if (target == "gender:female") { adminEditGender = "Female"; adminStatusMessage = "Gender: Female"; Invalidate(); return; }
        if (target == "age:child") { adminEditAgeGroup = "child"; adminStatusMessage = "Age: child"; Invalidate(); return; }
        if (target == "age:teen") { adminEditAgeGroup = "teen"; adminStatusMessage = "Age: teen"; Invalidate(); return; }
        if (target == "age:adult") { adminEditAgeGroup = "adult"; adminStatusMessage = "Age: adult"; Invalidate(); return; }
        if (target == "level:minus") { adminEditMaxLevel = Math.Max(0, adminEditMaxLevel - 1); Invalidate(); return; }
        if (target == "level:plus") { adminEditMaxLevel = Math.Min(99, adminEditMaxLevel + 1); Invalidate(); return; }
        if (target == "editname")
        {
            if (adminSelectedTuioId <= 0 && !adminCreatingNew)
            {
                adminStatusMessage = "Select a user first, then hold Edit Name (4s).";
                Invalidate();
                return;
            }
            EnterAdminNameKeyboard();
            return;
        }
        if (target.StartsWith("user:", StringComparison.Ordinal))
        {
            string idStr = target.Substring(5);
            int tid;
            if (int.TryParse(idStr, out tid))
                AdminSelectUser(tid);
        }
    }

    private void DrawAdminHoldRingOnRect(Graphics g, Rectangle rect, float progress)
    {
        if (progress <= 0f) return;
        int cx = rect.X + rect.Width / 2;
        int cy = rect.Y + rect.Height / 2;
        DrawSignupKeyHoldRing(g, cx, cy, progress);
    }

    private bool IsAdminTargetHovered(string target)
    {
        return string.Equals(adminPointerHoverTarget, target, StringComparison.Ordinal);
    }

    private void DrawAdminUsersScreen(Graphics g)
    {
        LayoutAdminScreen();

        int pad = 16;
        int top = 20;
        int btnH = 34;

        using (Font tf = new Font("Arial", theme.TitleFontSize + 2, FontStyle.Bold))
        using (SolidBrush tb = new SolidBrush(theme.TextPrimary))
        {
            string adminName = currentAdmin != null ? currentAdmin.Name : "admin";
            string title = "Admin — " + adminName + " (manage players)";
            g.DrawString(title, tf, tb, pad, top);
        }

        Rectangle tmp;
        float holdProg = GetAdminHoldProgress();
        DrawAdminButton(g, "+ New", adminBtnNewRect, false, out tmp);
        if (IsAdminTargetHovered("new")) DrawAdminHoldRingOnRect(g, adminBtnNewRect, holdProg);
        bool canEditName = adminSelectedTuioId > 0 || adminCreatingNew;
        DrawAdminButton(g, "Edit Name", adminBtnEditNameRect, canEditName, out tmp);
        if (canEditName && IsAdminTargetHovered("editname")) DrawAdminHoldRingOnRect(g, adminBtnEditNameRect, holdProg);
        DrawAdminButton(g, "Save", adminBtnSaveRect, true, out tmp);
        if (IsAdminTargetHovered("save")) DrawAdminHoldRingOnRect(g, adminBtnSaveRect, holdProg);
        DrawAdminButton(g, "Delete", adminBtnDeleteRect, false, out tmp);
        if (IsAdminTargetHovered("delete")) DrawAdminHoldRingOnRect(g, adminBtnDeleteRect, holdProg);
        DrawAdminButton(g, "Exit", adminBtnExitRect, false, out tmp);
        if (IsAdminTargetHovered("exit")) DrawAdminHoldRingOnRect(g, adminBtnExitRect, holdProg);

        top += btnH + 12;
        using (Font hf = new Font("Arial", theme.SmallFontSize, FontStyle.Regular))
        using (SolidBrush hb = new SolidBrush(theme.TextSecondary))
        {
            string hint = "Aim laser on a button or user card and hold 4 seconds · Laser pointer (no mouse/keyboard)";
            g.DrawString(hint, hf, hb, pad, top);
        }

        int listLeft = pad;
        int listTop = top + 28;
        int panelLeft = width * 3 / 5;
        int listW = panelLeft - listLeft - pad;
        int listBottom = height - 50;

        List<UserData> users = userManager.AllUsers;
        var playerUsers = new List<UserData>();
        foreach (UserData u in users)
        {
            if (u != null && !adminManager.IsAdminTuioId(u.TuioId))
                playerUsers.Add(u);
        }

        int cardW = Math.Min(240, listW - 8);
        int cardH = 88;
        int gap = 10;
        int yy = listTop;

        if (playerUsers.Count == 0)
        {
            using (Font ef = new Font("Arial", theme.BodyFontSize + 1, FontStyle.Italic))
            using (SolidBrush eb = new SolidBrush(theme.TextSecondary))
            {
                string msg = "No users found";
                SizeF ms = g.MeasureString(msg, ef);
                g.DrawString(msg, ef, eb, listLeft + Math.Max(0, (listW - ms.Width) / 2f), listTop + 40);
                string sub = "Hold + New for 4 seconds to add a player";
                SizeF ss = g.MeasureString(sub, ef);
                g.DrawString(sub, ef, eb, listLeft + Math.Max(0, (listW - ss.Width) / 2f), listTop + 68);
            }
        }
        else
        {
            foreach (UserData u in playerUsers)
            {
                if (yy + cardH > listBottom) break;
                Rectangle card = new Rectangle(listLeft, yy, cardW, cardH);
                bool selected = u.TuioId == adminSelectedTuioId && !adminCreatingNew;
                bool hovered = IsAdminTargetHovered("user:" + u.TuioId);
                Color borderCol = selected ? theme.Accent : (hovered ? theme.AccentSecondary : theme.BorderColor);
                using (SolidBrush fill = new SolidBrush(selected ? theme.AccentSecondary : theme.CardBackground))
                using (Pen border = new Pen(borderCol, selected || hovered ? 3f : 2f))
                DrawRoundedRect(g, fill, border, card, 10);
                if (hovered) DrawAdminHoldRingOnRect(g, card, holdProg);

            using (Font nf = new Font("Arial", theme.BodyFontSize, FontStyle.Bold))
                using (Font df = new Font("Arial", theme.SmallFontSize, FontStyle.Regular))
            using (SolidBrush nb = new SolidBrush(theme.TextPrimary))
            using (SolidBrush db = new SolidBrush(theme.TextSecondary))
            {
                string n = string.IsNullOrWhiteSpace(u.Name) ? ("User " + u.TuioId) : u.Name;
                    g.DrawString(n, nf, nb, card.X + 10, card.Y + 8);
                    g.DrawString("ID " + u.TuioId + " · " + (u.AgeGroup ?? "adult"), df, db, card.X + 10, card.Y + 34);
                    bool hasFace = faceDatabaseManager.ProfileExists(u.TuioId);
                    g.DrawString(hasFace ? "Face enrolled" : "No face yet", df, db, card.X + 10, card.Y + 54);
                }
                yy += cardH + gap;
            }
        }

        int px = panelLeft + pad;
        int py = listTop;
        int panelW = width - px - pad;

        using (Font pf = new Font("Arial", theme.BodyFontSize + 2, FontStyle.Bold))
        using (SolidBrush pb = new SolidBrush(theme.Accent))
        {
            string panelTitle = adminCreatingNew ? "Create user" : (adminSelectedTuioId > 0 ? "Edit user" : "Select a user");
            g.DrawString(panelTitle, pf, pb, px, py);
        }
        py += 36;

        if (adminSelectedTuioId <= 0 && !adminCreatingNew)
        {
            using (Font ef = new Font("Arial", theme.BodyFontSize, FontStyle.Italic))
            using (SolidBrush eb = new SolidBrush(theme.TextSecondary))
                g.DrawString("Hold pointer on a user card or + New (4s)", ef, eb, px, py + 20);
        }
        else
        {
            using (Font lf = new Font("Arial", theme.SmallFontSize + 1, FontStyle.Regular))
            using (SolidBrush lb = new SolidBrush(theme.TextSecondary))
            {
                g.DrawString("TUIO ID: " + adminSelectedTuioId, lf, lb, px, py);
                py += 22;
                g.DrawString("Name: " + (string.IsNullOrEmpty(adminEditName) ? ("User " + adminSelectedTuioId) : adminEditName), lf, lb, px, py);
                py += 22;
                using (SolidBrush hintBrush = new SolidBrush(theme.Accent))
                    g.DrawString("Change name: hold Edit Name (top bar, 4s) then Save", lf, hintBrush, px, py);
                py += 28;
            }

            using (Font lf = new Font("Arial", theme.SmallFontSize + 1, FontStyle.Regular))
            using (SolidBrush lb = new SolidBrush(theme.TextSecondary))
            {
                g.DrawString("Gender (hold 4s):", lf, lb, px, py);
                py += 22;
                DrawAdminChip(g, "Male", adminGenderMaleRect, adminEditGender == "Male", IsAdminTargetHovered("gender:male"));
                if (IsAdminTargetHovered("gender:male")) DrawAdminHoldRingOnRect(g, adminGenderMaleRect, holdProg);
                DrawAdminChip(g, "Female", adminGenderFemaleRect, adminEditGender == "Female", IsAdminTargetHovered("gender:female"));
                if (IsAdminTargetHovered("gender:female")) DrawAdminHoldRingOnRect(g, adminGenderFemaleRect, holdProg);
                py += 42;

                g.DrawString("Age group (hold 4s):", lf, lb, px, py);
                py += 22;
                DrawAdminChip(g, "child", adminAgeChildRect, adminEditAgeGroup == "child", IsAdminTargetHovered("age:child"));
                if (IsAdminTargetHovered("age:child")) DrawAdminHoldRingOnRect(g, adminAgeChildRect, holdProg);
                DrawAdminChip(g, "teen", adminAgeTeenRect, adminEditAgeGroup == "teen", IsAdminTargetHovered("age:teen"));
                if (IsAdminTargetHovered("age:teen")) DrawAdminHoldRingOnRect(g, adminAgeTeenRect, holdProg);
                DrawAdminChip(g, "adult", adminAgeAdultRect, adminEditAgeGroup == "adult", IsAdminTargetHovered("age:adult"));
                if (IsAdminTargetHovered("age:adult")) DrawAdminHoldRingOnRect(g, adminAgeAdultRect, holdProg);
                py += 42;

                g.DrawString("Max level (hold 4s):", lf, lb, px, py);
                py += 22;
                DrawAdminChip(g, "-", adminLevelMinusRect, false, IsAdminTargetHovered("level:minus"));
                if (IsAdminTargetHovered("level:minus")) DrawAdminHoldRingOnRect(g, adminLevelMinusRect, holdProg);
                DrawAdminChip(g, "+", adminLevelPlusRect, false, IsAdminTargetHovered("level:plus"));
                if (IsAdminTargetHovered("level:plus")) DrawAdminHoldRingOnRect(g, adminLevelPlusRect, holdProg);
                using (Font vf = new Font("Arial", theme.BodyFontSize + 4, FontStyle.Bold))
                using (SolidBrush vb = new SolidBrush(theme.TextPrimary))
                {
                    string lv = adminEditMaxLevel.ToString();
                    g.DrawString(lv, vf, vb, px + 100, py + 4);
                }

                bool faceOk = faceDatabaseManager.ProfileExists(adminSelectedTuioId);
                py += 44;
                using (SolidBrush fb = new SolidBrush(faceOk ? Color.FromArgb(255, 80, 200, 120) : theme.TextSecondary))
                    g.DrawString(faceOk ? "Face profile: enrolled" : "Face profile: none (enrolls at login)", lf, fb, px, py);
            }
        }

        if (!string.IsNullOrEmpty(adminStatusMessage))
        {
            using (Font sf = new Font("Arial", theme.SmallFontSize + 1, FontStyle.Italic))
            using (SolidBrush sb = new SolidBrush(theme.Accent))
            {
                SizeF ss = g.MeasureString(adminStatusMessage, sf);
                g.DrawString(adminStatusMessage, sf, sb, (width - ss.Width) / 2f, height - 36);
            }
        }

        UpdateAdminPointerHold();
        if (!string.IsNullOrEmpty(adminPointerHoldTarget))
            Invalidate();
    }

    private void DrawAdminChip(Graphics g, string label, Rectangle rect, bool selected, bool hovered)
    {
        Color bg = selected ? theme.Accent : (hovered ? Color.FromArgb(255, 55, 68, 78) : theme.CardBackground);
        using (SolidBrush fill = new SolidBrush(bg))
        using (Pen border = new Pen(hovered ? theme.Accent : (selected ? theme.Accent : theme.BorderColor), hovered ? 3f : 2f))
            DrawRoundedRect(g, fill, border, rect, 6);
        using (Font f = new Font("Arial", theme.SmallFontSize, selected ? FontStyle.Bold : FontStyle.Regular))
        using (SolidBrush tb = new SolidBrush(selected ? Color.White : theme.TextPrimary))
        {
            SizeF ts = g.MeasureString(label, f);
            g.DrawString(label, f, tb, rect.X + (rect.Width - ts.Width) / 2f, rect.Y + (rect.Height - ts.Height) / 2f);
        }
    }

    private void Form_MouseClick(object sender, MouseEventArgs e)
    {
        // Mouse disabled for signup name and admin — use hand pointer hold only.
        if (screenState == ScreenAdminUsers || screenState == ScreenSignupName || screenState == ScreenAdminEditName)
            return;
    }

    private bool IsVirtualKeyboardScreen()
    {
        return screenState == ScreenSignupName || screenState == ScreenAdminEditName;
    }

    private string GetKeyboardNameValue()
    {
        return screenState == ScreenAdminEditName ? (adminEditName ?? "") : (signupName ?? "");
    }

    private void SetKeyboardNameValue(string value)
    {
        if (screenState == ScreenAdminEditName)
            adminEditName = value ?? "";
        else
            signupName = value ?? "";
    }

    private void EnterAdminNameKeyboard()
    {
        if (adminSelectedTuioId <= 0 && !adminCreatingNew) return;
        signupKeyboardLayoutSignature = -1;
        signupKeyboardHoveredIndex = -1;
        signupKeyHoldTargetIndex = -1;
        signupKeyHoldSince = DateTime.MinValue;
        signupKeyHoldCooldownUntil = DateTime.MinValue;
        screenState = ScreenAdminEditName;
        adminStatusMessage = "Laser only — aim on a key, hold 4s to type · Done returns to admin";
        NotifySignupPointerMode(true);
        Invalidate();
    }

    private void FinishAdminNameKeyboard()
    {
        adminEditName = (adminEditName ?? "").Trim();
        if (adminEditName.Length < 1 && adminSelectedTuioId > 0)
            adminEditName = "User " + adminSelectedTuioId;
        NotifySignupPointerMode(false);
        screenState = ScreenAdminUsers;
        adminStatusMessage = "Name updated — hold Save to store user.";
        Invalidate();
    }

    private void RefreshAdminEditName()
    {
        EnsureSignupKeyboardLayout();
        UpdateSignupKeyboardHover();

        bool holdActive = signupKeyboardHoveredIndex >= 0 && HasPlanePointer();
        UpdateSignupKeyHold(holdActive);

        bool tuio6Now = false;
        foreach (TuioDemoObject t in objectList.Values)
            if (t.SymbolID == 6) { tuio6Now = true; break; }
        if (tuio6Now && !signupNameTuio6WasPresent)
            ApplySignupKeyboardAction("backspace");
        signupNameTuio6WasPresent = tuio6Now;

        bool tuio7Now = false;
        foreach (TuioDemoObject t in objectList.Values)
            if (t.SymbolID == 7) { tuio7Now = true; break; }
        if (tuio7Now && !signupNameTuio7WasPresent)
            ApplySignupKeyboardAction("enter");
        signupNameTuio7WasPresent = tuio7Now;
    }

    private void HandleSignupNameKeyDown(KeyEventArgs e)
    {
        if (e.KeyData == Keys.Escape || e.KeyData == Keys.F1) return;

        Keys key = e.KeyCode;
        if (key == Keys.Back)
        {
            ApplySignupKeyboardAction("backspace");
            e.Handled = true;
            e.SuppressKeyPress = true;
            Invalidate();
            return;
        }
        if (key == Keys.Enter)
        {
            CompleteSignupWithName();
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }
        if (key == Keys.Space)
        {
            ApplySignupKeyboardAction("space");
            e.Handled = true;
            e.SuppressKeyPress = true;
            Invalidate();
            return;
        }
        if (key == Keys.CapsLock)
        {
            signupKeyboardCapsLock = !signupKeyboardCapsLock;
            e.Handled = true;
            e.SuppressKeyPress = true;
            Invalidate();
            return;
        }

        char ch = '\0';
        if (key >= Keys.A && key <= Keys.Z)
            ch = (char)('a' + (key - Keys.A));
        else if (key >= Keys.D0 && key <= Keys.D9)
            ch = (char)('0' + (key - Keys.D0));

        if (ch != '\0')
        {
            bool shift = (Control.ModifierKeys & Keys.Shift) != 0;
            if (signupKeyboardCapsLock) shift = !shift;
            if (shift && ch >= 'a' && ch <= 'z') ch = char.ToUpperInvariant(ch);
            AppendSignupNameChar(ch);
            e.Handled = true;
            e.SuppressKeyPress = true;
            Invalidate();
        }
        else
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private void EnsureSignupKeyboardLayout()
    {
        int screenPart = screenState == ScreenAdminEditName ? 2000000 : 0;
        int sig = screenPart + width * 10000 + height;
        if (sig == signupKeyboardLayoutSignature && signupKeyboardKeys.Count > 0) return;
        signupKeyboardLayoutSignature = sig;
        signupKeyboardKeys.Clear();

        int margin = Math.Max(16, width / 48);
        int textH = Math.Max(52, height / 14);
        signupNameTextBoxRect = new Rectangle(margin, margin + 36, width - margin * 2, textH);

        int panelTop = signupNameTextBoxRect.Bottom + 14;
        int panelH = height - panelTop - margin;
        signupKeyboardPanelRect = new Rectangle(margin, panelTop, width - margin * 2, panelH);

        int gap = Math.Max(4, width / 200);
        int rowH = Math.Max(36, (panelH - gap * 5) / 4);
        int y = signupKeyboardPanelRect.Y + gap;

        AddSignupKeyboardRow(y, rowH, gap, new[]
        {
            KbChar("q"), KbChar("w"), KbChar("e"), KbChar("r"), KbChar("t"), KbChar("y"),
            KbChar("u"), KbChar("i"), KbChar("o"), KbChar("p"),
            Kb("backspace", "⌫", null, 1.6f)
        });
        y += rowH + gap;

        AddSignupKeyboardRow(y, rowH, gap, new[]
        {
            Kb("caps", "Caps", null, 1.6f),
            KbChar("a"), KbChar("s"), KbChar("d"), KbChar("f"), KbChar("g"), KbChar("h"),
            KbChar("j"), KbChar("k"), KbChar("l"),
            Kb("char", "'", null, 1f),
            Kb("enter", screenState == ScreenAdminEditName ? "Done" : "Enter", null, 1.6f)
        });
        y += rowH + gap;

        AddSignupKeyboardRow(y, rowH, gap, new[]
        {
            KbChar("z"), KbChar("x"), KbChar("c"), KbChar("v"), KbChar("b"), KbChar("n"), KbChar("m"),
            Kb("char", ",", null, 1f), Kb("char", ".", null, 1f), Kb("char", "-", null, 1f)
        });
        y += rowH + gap;

        AddSignupKeyboardRow(y, rowH, gap, new[]
        {
            Kb("space", "Space", null, 8f)
        });
    }

    private static SignupKeyboardKey Kb(string action, string primary, string secondary, float weight)
    {
        return new SignupKeyboardKey { Action = action, Primary = primary, Secondary = secondary, Weight = weight };
    }

    private static SignupKeyboardKey KbChar(string letter)
    {
        return Kb("char", letter, null, 1f);
    }

    private void AddSignupKeyboardRow(int y, int rowH, int gap, SignupKeyboardKey[] keys)
    {
        float totalWeight = 0f;
        for (int i = 0; i < keys.Length; i++) totalWeight += keys[i].Weight;
        int innerW = signupKeyboardPanelRect.Width - gap * 2;
        int x = signupKeyboardPanelRect.X + gap;
        for (int i = 0; i < keys.Length; i++)
        {
            int w = Math.Max(8, (int)((innerW - gap * (keys.Length - 1)) * (keys[i].Weight / totalWeight)));
            keys[i].Bounds = new Rectangle(x, y, w, rowH);
            signupKeyboardKeys.Add(keys[i]);
            x += w + gap;
        }
    }

    private void UpdateSignupKeyboardHover()
    {
        signupKeyboardHoveredIndex = -1;
        int px, py;
        float angle;
        if (!TryGetPlaneScreenPoint(out px, out py, out angle)) return;
        signupKeyboardHoveredIndex = HitTestSignupKeyboard(px, py);
    }

    private int HitTestSignupKeyboard(int x, int y)
    {
        for (int i = 0; i < signupKeyboardKeys.Count; i++)
        {
            if (signupKeyboardKeys[i].Bounds.Contains(x, y))
                return i;
        }
        return -1;
    }

    private string GetSignupPointerImagePath()
    {
        string ag = NormalizeAgeGroupFromLabel(signupAgeGroup);
        if (ag == "child") return "child_pointer.png";
        if (ag == "teen") return "teen_pointer.png";
        return "adult_pointer.png";
    }

    private void UpdateSignupKeyHold(bool holdActive)
    {
        int idx = signupKeyboardHoveredIndex;
        if (!holdActive || idx < 0 || idx >= signupKeyboardKeys.Count)
        {
            signupKeyHoldTargetIndex = -1;
            signupKeyHoldSince = DateTime.MinValue;
            return;
        }

        SignupKeyboardKey key = signupKeyboardKeys[idx];
        if (key.Action == "noop" || key.Action == "tab")
        {
            signupKeyHoldTargetIndex = -1;
            signupKeyHoldSince = DateTime.MinValue;
            return;
        }

        if (DateTime.Now < signupKeyHoldCooldownUntil)
            return;

        if (signupKeyHoldTargetIndex != idx)
        {
            signupKeyHoldTargetIndex = idx;
            signupKeyHoldSince = DateTime.Now;
            return;
        }

        double elapsedMs = (DateTime.Now - signupKeyHoldSince).TotalMilliseconds;
        if (elapsedMs >= SignupKeyHoldMs)
        {
            ApplySignupKeyboardKey(key);
            signupKeyHoldTargetIndex = -1;
            signupKeyHoldSince = DateTime.MinValue;
            signupKeyHoldCooldownUntil = DateTime.Now.AddMilliseconds(450);
        }
    }

    private float GetSignupKeyHoldProgress()
    {
        if (signupKeyHoldTargetIndex < 0 || signupKeyHoldSince == DateTime.MinValue)
            return 0f;
        double elapsedMs = (DateTime.Now - signupKeyHoldSince).TotalMilliseconds;
        return (float)Math.Max(0.0, Math.Min(1.0, elapsedMs / SignupKeyHoldMs));
    }

    private void DrawSignupKeyHoldRing(Graphics g, int centerX, int centerY, float progress)
    {
        if (progress <= 0f) return;
        int ringSize = 72;
        Rectangle ringRect = new Rectangle(centerX - ringSize / 2, centerY - ringSize / 2, ringSize, ringSize);
        using (Pen bgRing = new Pen(Color.FromArgb(90, 255, 255, 255), 4f))
            g.DrawArc(bgRing, ringRect, -90, 360);
        using (Pen progressRing = new Pen(Color.FromArgb(255, 98, 0, 238), 5f))
            g.DrawArc(progressRing, ringRect, -90, 360f * progress);
    }

    private void DrawLaserPointerOverlay(Graphics g, int mx, int my)
    {
        using (SolidBrush glow = new SolidBrush(Color.FromArgb(70, 255, 60, 60)))
            g.FillEllipse(glow, mx - 22, my - 22, 44, 44);
        using (SolidBrush core = new SolidBrush(Color.FromArgb(255, 255, 40, 40)))
            g.FillEllipse(core, mx - 8, my - 8, 16, 16);
        using (Pen ring = new Pen(Color.FromArgb(220, 255, 255, 255), 2f))
            g.DrawEllipse(ring, mx - 14, my - 14, 28, 28);
    }

    private void ApplySignupKeyboardAction(string action)
    {
        if (string.IsNullOrEmpty(action) || action == "noop" || action == "tab" || action == "char") return;
        if (action == "backspace")
        {
            string n = GetKeyboardNameValue();
            if (n.Length > 0)
                SetKeyboardNameValue(n.Substring(0, n.Length - 1));
            return;
        }
        if (action == "caps")
        {
            signupKeyboardCapsLock = !signupKeyboardCapsLock;
            return;
        }
        if (action == "space")
        {
            AppendSignupNameChar(' ');
            return;
        }
        if (action == "enter")
        {
            if (screenState == ScreenAdminEditName)
                FinishAdminNameKeyboard();
            else
                CompleteSignupWithName();
        }
    }

    private void ApplySignupKeyboardKey(SignupKeyboardKey key)
    {
        if (key == null) return;
        if (key.Action == "char")
        {
            string use = key.Primary ?? "";
            if (use.Length == 0) return;
            if (signupKeyboardCapsLock && use.Length == 1 && char.IsLetter(use[0]))
                use = use.ToUpperInvariant();
            else if (!signupKeyboardCapsLock && use.Length == 1 && char.IsLetter(use[0]))
                use = use.ToLowerInvariant();
            AppendSignupNameChar(use[0]);
            return;
        }
        ApplySignupKeyboardAction(key.Action);
    }

    private void AppendSignupNameChar(char ch)
    {
        string n = GetKeyboardNameValue();
        if (n.Length >= SignupNameMaxLength) return;
        SetKeyboardNameValue(n + ch);
    }

    private void DrawSignupNameScreen(Graphics g)
    {
        EnsureSignupKeyboardLayout();
        UpdateSignupKeyboardHover();

        int cx = width / 2;
        using (Font tf = new Font("Arial", theme.TitleFontSize, FontStyle.Bold))
        using (SolidBrush tb = new SolidBrush(theme.Accent))
        {
            string title = screenState == ScreenAdminEditName
                ? ("Edit name — user " + adminSelectedTuioId)
                : "Enter Your Name";
            SizeF ts = g.MeasureString(title, tf);
            g.DrawString(title, tf, tb, cx - ts.Width / 2f, 8);
        }

        using (SolidBrush boxFill = new SolidBrush(Color.White))
        using (Pen boxPen = new Pen(Color.FromArgb(255, 200, 205, 210), 1.5f))
        {
            DrawRoundedRect(g, boxFill, boxPen, signupNameTextBoxRect, 4);
        }

        string nameVal = GetKeyboardNameValue();
        string display = string.IsNullOrEmpty(nameVal) ? " " : nameVal;
        using (Font nameFont = new Font("Consolas", Math.Max(16f, theme.BodyFontSize + 4), FontStyle.Regular))
        using (SolidBrush nameBr = new SolidBrush(Color.Black))
        {
            Rectangle textInner = signupNameTextBoxRect;
            textInner.Inflate(-12, -8);
            g.DrawString(display, nameFont, nameBr, textInner.Location);
        }

        using (SolidBrush panelFill = new SolidBrush(SignupKbPanelBg))
            DrawRoundedRect(g, panelFill, null, signupKeyboardPanelRect, 8);

        for (int i = 0; i < signupKeyboardKeys.Count; i++)
        {
            SignupKeyboardKey key = signupKeyboardKeys[i];
            if (key.Action == "noop") continue;
            bool hovered = (i == signupKeyboardHoveredIndex);
            bool isCaps = key.Action == "caps";
            Color bg = isCaps && signupKeyboardCapsLock ? SignupKbCapsBg
                : (hovered ? SignupKbKeyHoverBg : (isCaps ? SignupKbCapsBg : SignupKbKeyBg));
            using (SolidBrush kb = new SolidBrush(bg))
                DrawRoundedRect(g, kb, null, key.Bounds, 4);

            using (Font kf = new Font("Segoe UI", Math.Max(9f, key.Bounds.Height * 0.28f), FontStyle.Regular))
            using (SolidBrush kt = new SolidBrush(SignupKbKeyText))
            {
                if (!string.IsNullOrEmpty(key.Secondary) && key.Action == "char")
                {
                    using (Font sf = new Font(kf.FontFamily, kf.Size * 0.72f, FontStyle.Regular))
                    {
                        SizeF ss = g.MeasureString(key.Secondary, sf);
                        g.DrawString(key.Secondary, sf, kt,
                            key.Bounds.X + (key.Bounds.Width - ss.Width) / 2f,
                            key.Bounds.Y + 4);
                    }
                    string bottom = key.Primary ?? "";
                    SizeF bs = g.MeasureString(bottom, kf);
                    g.DrawString(bottom, kf, kt,
                        key.Bounds.X + (key.Bounds.Width - bs.Width) / 2f,
                        key.Bounds.Bottom - bs.Height - 5);
                }
                else
                {
                    string label = key.Primary ?? "";
                    if (key.Action == "backspace") label = "⌫";
                    if (key.Action == "space" && string.IsNullOrEmpty(label)) label = "Space";
                    DrawCenteredString(g, label, kf, kt, key.Bounds);
                }
            }

            if (i == signupKeyHoldTargetIndex)
            {
                float holdProgress = GetSignupKeyHoldProgress();
                if (holdProgress > 0f)
                {
                    int kcx = key.Bounds.X + key.Bounds.Width / 2;
                    int kcy = key.Bounds.Y + key.Bounds.Height / 2;
                    DrawSignupKeyHoldRing(g, kcx, kcy, holdProgress);
                }
            }
        }

        using (Font inf = new Font("Arial", theme.SmallFontSize))
        using (SolidBrush iBr = new SolidBrush(theme.TextSecondary))
        {
            int instY = signupKeyboardPanelRect.Bottom + 8;
            string[] lines;
            if (screenState == ScreenAdminEditName)
            {
                lines = new[]
                {
                    "Aim laser on a key and hold 4 seconds to type  ·  Hold Done when finished",
                    "Laser only on this screen — MediaPipe hand tracking is off"
                };
            }
            else
            {
                lines = new[]
                {
                    "Aim laser on a key and hold 4 seconds to type  ·  TUIO 6 — backspace  ·  TUIO 7 — done",
                    "Laser only on this screen — MediaPipe hand tracking is off"
                };
            }
            for (int li = 0; li < lines.Length; li++)
                g.DrawString(lines[li], inf, iBr, signupKeyboardPanelRect.X, instY + li * 18);
        }

        if (!string.IsNullOrEmpty(signupError))
        {
            using (Font ef = new Font("Arial", theme.BodyFontSize))
            using (SolidBrush eb = new SolidBrush(theme.ErrorColor))
            {
                SizeF es = g.MeasureString(signupError, ef);
                g.DrawString(signupError, ef, eb, cx - es.Width / 2f, height - 28);
            }
        }
    }

    private void DrawSignupScreen(Graphics g)
    {
        int cx = width / 2;
        int y = 28;

        // Title
        using (Font tf = new Font("Arial", theme.TitleFontSize, FontStyle.Bold))
        using (SolidBrush tb = new SolidBrush(theme.Accent))
        {
            string title = "Sign Up — TUIO " + signupTuioId;
            SizeF s = g.MeasureString(title, tf);
            g.DrawString(title, tf, tb, cx - s.Width / 2f, y);
            y += (int)s.Height + 18;
        }

        // Current selections
        using (Font lf = new Font("Arial", theme.BodyFontSize))
        using (SolidBrush activeBr = new SolidBrush(theme.Accent))
        using (SolidBrush dimBr = new SolidBrush(theme.TextSecondary))
        {
            string genderLabel = signupField == 0 ? ">> Gender:" : "   Gender:";
            string ageLabel = signupField == 1 ? ">> Age Group:" : "   Age Group:";
            int fieldX = cx - 180;

            g.DrawString(genderLabel, lf, signupField == 0 ? activeBr : dimBr, fieldX, y);
            using (Font vf = new Font("Arial", theme.BodyFontSize + 2, FontStyle.Bold))
            using (SolidBrush valBr = new SolidBrush(theme.TextPrimary))
            {
                string genderDisp = !string.IsNullOrEmpty(signupGender) ? signupGender : "(not selected)";
                g.DrawString(genderDisp, vf, valBr, fieldX + 130, y);
            }
            y += 40;

            g.DrawString(ageLabel, lf, signupField == 1 ? activeBr : dimBr, fieldX, y);
            using (Font vf = new Font("Arial", theme.BodyFontSize + 2, FontStyle.Bold))
            using (SolidBrush valBr = new SolidBrush(theme.TextPrimary))
            {
                string ageDisp = !string.IsNullOrEmpty(signupAgeGroup) ? signupAgeGroup : "(not selected)";
                g.DrawString(ageDisp, vf, valBr, fieldX + 130, y);
            }
            y += 46;
        }

        string[] dialOptions = GetCurrentDialOptions();
        if (dialOptions.Length > 0)
        {
            int dialCenterX = cx;
            int dialCenterY = y + 130;
            int dialRadius = 100;
            int visibleCount = Math.Min(7, Math.Max(3, dialOptions.Length));
            int half = visibleCount / 2;

            using (Pen circlePen = new Pen(theme.BorderColor, 2))
            {
                g.DrawEllipse(circlePen, dialCenterX - dialRadius - 10, dialCenterY - dialRadius - 10,
                    (dialRadius + 10) * 2, (dialRadius + 10) * 2);
            }

            if (signupField == 0 && dialOptions.Length == 2)
            {
                // Gender step: keep options fixed left/right of the dial.
                bool maleSelected = (dialIndex % 2 == 0);
                bool femaleSelected = !maleSelected;

                using (Font opt = new Font("Arial", theme.BodyFontSize + 4, FontStyle.Bold))
                using (SolidBrush maleBr = new SolidBrush(maleSelected ? theme.Accent : theme.TextSecondary))
                using (SolidBrush femaleBr = new SolidBrush(femaleSelected ? theme.Accent : theme.TextSecondary))
                using (Pen selPen = new Pen(theme.Accent, 3))
                {
                    string male = "Male";
                    string female = "Female";
                    SizeF ms = g.MeasureString(male, opt);
                    SizeF fs = g.MeasureString(female, opt);

                    float maleX = dialCenterX - dialRadius - 70 - ms.Width;
                    float femaleX = dialCenterX + dialRadius + 70;
                    float txtY = dialCenterY - ms.Height / 2f;

                    g.DrawString(male, opt, maleBr, maleX, txtY);
                    g.DrawString(female, opt, femaleBr, femaleX, txtY);

                    if (maleSelected)
                        DrawRoundedRect(g, null, selPen, new Rectangle((int)maleX - 8, (int)txtY - 4, (int)ms.Width + 16, (int)ms.Height + 8), 6);
                    if (femaleSelected)
                        DrawRoundedRect(g, null, selPen, new Rectangle((int)femaleX - 8, (int)txtY - 4, (int)fs.Width + 16, (int)fs.Height + 8), 6);
                }
            }
            else if (signupField == 1 && dialOptions.Length == 3)
            {
                // Age-group step: place the 3 choices around the circle.
                int selectedIdx = ((dialIndex % dialOptions.Length) + dialOptions.Length) % dialOptions.Length;
                double[] anglesDeg = { -90.0, 30.0, 150.0 }; // top, bottom-right, bottom-left

                for (int i = 0; i < dialOptions.Length; i++)
                {
                    string option = dialOptions[i];
                    double angleRad = anglesDeg[i] * Math.PI / 180.0;
                    float px = dialCenterX + (float)(dialRadius * Math.Cos(angleRad));
                    float py = dialCenterY + (float)(dialRadius * Math.Sin(angleRad));

                    bool isSelected = (i == selectedIdx);
                    float fontSize = isSelected ? theme.BodyFontSize + 4 : theme.SmallFontSize + 1;
                    FontStyle style = isSelected ? FontStyle.Bold : FontStyle.Regular;

                    using (Font cf = new Font("Arial", fontSize, style))
                    using (SolidBrush cb = new SolidBrush(isSelected ? theme.Accent : theme.TextSecondary))
                    {
                        SizeF cs = g.MeasureString(option, cf);
                        g.DrawString(option, cf, cb, px - cs.Width / 2f, py - cs.Height / 2f);
                    }

                    if (isSelected)
                    {
                        using (Font cf = new Font("Arial", fontSize, style))
                        using (Pen selPen = new Pen(theme.Accent, 3))
                        {
                            SizeF cs = g.MeasureString(option, cf);
                            Rectangle selRect = new Rectangle(
                                (int)(px - cs.Width / 2f - 6), (int)(py - cs.Height / 2f - 4),
                                (int)(cs.Width + 12), (int)(cs.Height + 8));
                            DrawRoundedRect(g, null, selPen, selRect, 6);
                        }
                    }
                }
            }
            else
            {
                for (int i = -half; i <= half; i++)
                {
                    int idx = ((dialIndex + i) % dialOptions.Length + dialOptions.Length) % dialOptions.Length;
                    string option = dialOptions[idx];
                    double angleDeg = -90.0 + i * 24.0;
                    double angleRad = angleDeg * Math.PI / 180.0;
                    float px = dialCenterX + (float)(dialRadius * Math.Cos(angleRad));
                    float py = dialCenterY + (float)(dialRadius * Math.Sin(angleRad));

                    bool isSelected = (i == 0);
                    float fontSize = isSelected ? theme.BodyFontSize + 4 : theme.SmallFontSize + 1;
                    FontStyle style = isSelected ? FontStyle.Bold : FontStyle.Regular;

                    using (Font cf = new Font("Arial", fontSize, style))
                    using (SolidBrush cb = new SolidBrush(isSelected ? theme.Accent : theme.TextSecondary))
                    {
                        SizeF cs = g.MeasureString(option, cf);
                        g.DrawString(option, cf, cb, px - cs.Width / 2f, py - cs.Height / 2f);
                    }

                    if (isSelected)
                    {
                        using (Font cf = new Font("Arial", fontSize, style))
                        using (Pen selPen = new Pen(theme.Accent, 3))
                        {
                            SizeF cs = g.MeasureString(option, cf);
                            Rectangle selRect = new Rectangle(
                                (int)(px - cs.Width / 2f - 6), (int)(py - cs.Height / 2f - 4),
                                (int)(cs.Width + 12), (int)(cs.Height + 8));
                            DrawRoundedRect(g, null, selPen, selRect, 6);
                        }
                    }
                }
            }

            using (SolidBrush arrBr = new SolidBrush(theme.Accent))
            {
                Point[] arrow = {
                    new Point(dialCenterX, dialCenterY - dialRadius - 22),
                    new Point(dialCenterX - 8, dialCenterY - dialRadius - 34),
                    new Point(dialCenterX + 8, dialCenterY - dialRadius - 34)
                };
                g.FillPolygon(arrBr, arrow);
            }

            y = dialCenterY + dialRadius + 36;

            using (Font inf = new Font("Arial", theme.SmallFontSize + 1))
            using (SolidBrush iBr = new SolidBrush(theme.TextSecondary))
            {
                int instX = 20;
                int instY = y;
                int lineH = 20;

                string[] instructions = new[] {
                    "TUIO 4  Rotate = Browse options",
                    "TUIO 5  Place  = Choose highlighted option",
                    "TUIO 6  Place  = Go back to previous field",
                    "TUIO 7  Place  = " + (signupField == 0 ? "Switch to Age Group (after choosing gender)" : "Continue to name keyboard (both required)"),
                    "Choose each field before switching; both required to continue",
                    "Keyboard is disabled in sign-up"
                };
                if (mediapipeSignupHybrid)
                {
                    instructions = new[] {
                        "TUIO 4–7 (reacTIVision markers): rotate / select / back / next — OR bottle+hand below",
                        "Slide bottle left/right — rotate dial (or swipe empty hand)",
                        "Hold bottle still near your palm — select option",
                        "Lower bottle below your palm (~1 sec) — next field or submit",
                        "Fist — select | Peace (V) — next field or submit (hand backup)",
                        "Both gender and age must be selected before the name keyboard",
                        "Keyboard is disabled in sign-up"
                    };
                }

                foreach (string inst in instructions)
                {
                    Color dotColor = theme.AccentSecondary;
                    using (SolidBrush dotBr = new SolidBrush(dotColor))
                        g.FillEllipse(dotBr, instX, instY + 4, 10, 10);
                    g.DrawString(inst, inf, iBr, instX + 16, instY);
                    instY += lineH;
                }
            }
        }

        if (!string.IsNullOrEmpty(signupPhysicalTuioStatus))
        {
            using (Font pf = new Font("Arial", theme.SmallFontSize))
            using (SolidBrush pb = new SolidBrush(
                signupPhysicalTuioStatus.StartsWith("reacTIVision: no", StringComparison.OrdinalIgnoreCase)
                    ? theme.ErrorColor
                    : theme.AccentSecondary))
            {
                SizeF ps = g.MeasureString(signupPhysicalTuioStatus, pf);
                g.DrawString(signupPhysicalTuioStatus, pf, pb, cx - ps.Width / 2f, height - 72);
            }
        }

        if (!string.IsNullOrEmpty(signupError))
        {
            using (Font ef = new Font("Arial", theme.BodyFontSize))
            using (SolidBrush eb = new SolidBrush(theme.ErrorColor))
            {
                SizeF es = g.MeasureString(signupError, ef);
                g.DrawString(signupError, ef, eb, cx - es.Width / 2f, height - 30);
            }
        }

        if (!string.IsNullOrEmpty(signupFaceStatus))
        {
            using (Font sf = new Font("Arial", theme.SmallFontSize + 1))
            using (SolidBrush sb = new SolidBrush(signupFaceReady ? theme.SuccessColor : theme.TextSecondary))
            {
                SizeF ss = g.MeasureString(signupFaceStatus, sf);
                g.DrawString(signupFaceStatus, sf, sb, cx - ss.Width / 2f, height - 54);
            }
        }
    }

    //  Screen 2: Greeting 
    private void DrawGreetingScreen(Graphics g)
    {
        string msg = "Hello, " + currentUser.Name + "!";
        string sub = currentUser.AgeGroup == "child" ? "Let's play a fun game!"
                   : currentUser.AgeGroup == "teen" ? "Ready for a challenge?"
                   : "Welcome. Let's begin.";
        if (IsFacialEmotionVisualFresh() && (facialEmotionMood == "happy" || facialEmotionMood == "sad") && !string.IsNullOrEmpty(facialEmotionContext))
            sub = sub + "\n" + facialEmotionContext;
        string ageInfo = "Age group: " + currentUser.AgeGroup;

        Color primaryC = theme.Accent;
        Color secondaryC = theme.TextPrimary;
        Color hintC = theme.TextSecondary;
        if (IsFacialEmotionVisualFresh() && facialEmotionMood == "happy")
        {
            primaryC = Color.FromArgb(255, 255, 150, 30);
            secondaryC = Color.FromArgb(255, 85, 45, 5);
            hintC = Color.FromArgb(255, 120, 70, 20);
        }
        else if (IsFacialEmotionVisualFresh() && facialEmotionMood == "sad")
        {
            primaryC = Color.FromArgb(255, 175, 205, 255);
            secondaryC = Color.FromArgb(255, 225, 232, 252);
            hintC = Color.FromArgb(255, 185, 200, 235);
        }

        using (Font hf = new Font("Arial", theme.TitleFontSize + 12, FontStyle.Bold))
        using (Font sf = new Font("Arial", theme.BodyFontSize + 2))
        using (Font af = new Font("Arial", theme.SmallFontSize + 1))
        using (SolidBrush primary = new SolidBrush(primaryC))
        using (SolidBrush secondary = new SolidBrush(secondaryC))
        using (SolidBrush hint = new SolidBrush(hintC))
        {
            SizeF ms = g.MeasureString(msg, hf);
            g.DrawString(msg, hf, primary, (width - ms.Width) / 2f, height / 2f - 50);
            SizeF ss = g.MeasureString(sub, sf);
            g.DrawString(sub, sf, secondary, (width - ss.Width) / 2f, height / 2f + 10);
            SizeF as2 = g.MeasureString(ageInfo, af);
            g.DrawString(ageInfo, af, hint, (width - as2.Width) / 2f, height / 2f + 50);
        }
    }

    // Screen 4: Level Transition
    private void DrawLevelTransitionScreen(Graphics g)
    {
        int cx = width / 2;
        int cy = height / 2;

        string mainText;
        string subText = "";
        float mainSize;
        Color mainColor;

        switch (transitionPhase)
        {
            case 0:
                mainText = "Level 1 Accomplished!";
                mainSize = theme.TitleFontSize + 10;
                mainColor = theme.SuccessColor;
                break;
            case 1:
                mainText = "Ready for Level 2";
                mainSize = theme.TitleFontSize + 6;
                mainColor = theme.Accent;
                subText = "Match landmarks to their flags!";
                break;
            case 2:
                mainText = "1";
                mainSize = theme.TitleFontSize + 40;
                mainColor = theme.Accent;
                break;
            case 3:
                mainText = "2";
                mainSize = theme.TitleFontSize + 40;
                mainColor = theme.Accent;
                break;
            case 4:
                mainText = "3";
                mainSize = theme.TitleFontSize + 40;
                mainColor = theme.Accent;
                break;
            default:
                mainText = "Go!";
                mainSize = theme.TitleFontSize + 40;
                mainColor = theme.SuccessColor;
                break;
        }

        using (Font mf = new Font("Arial", mainSize, FontStyle.Bold))
        using (SolidBrush mb = new SolidBrush(mainColor))
        {
            SizeF ms = g.MeasureString(mainText, mf);
            g.DrawString(mainText, mf, mb, cx - ms.Width / 2f, cy - ms.Height / 2f);
        }

        if (!string.IsNullOrEmpty(subText))
        {
            using (Font sf = new Font("Arial", theme.BodyFontSize + 2))
            using (SolidBrush sb = new SolidBrush(theme.TextSecondary))
            {
                SizeF ss = g.MeasureString(subText, sf);
                g.DrawString(subText, sf, sb, cx - ss.Width / 2f, cy + 40);
            }
        }

        using (Font nf = new Font("Arial", theme.SmallFontSize))
        using (SolidBrush nb = new SolidBrush(theme.TextSecondary))
        {
            string name = currentUser != null ? currentUser.Name : "";
            string info = name + " — " + (currentUser != null ? currentUser.AgeGroup : "");
            SizeF ns = g.MeasureString(info, nf);
            g.DrawString(info, nf, nb, cx - ns.Width / 2f, height - 30);
        }
    }

    private void DrawHeart(Graphics g, float cx, float cy, float size, Color color)
    {
        using (GraphicsPath path = new GraphicsPath())
        {
            float s = size;
            float top = cy - s * 0.4f;
            path.AddArc(cx - s, top, s, s * 0.8f, 135, 225);
            path.AddArc(cx, top, s, s * 0.8f, 180, 225);
            path.AddLine(cx + s, cy + s * 0.1f, cx, cy + s * 0.7f);
            path.CloseFigure();
            using (SolidBrush br = new SolidBrush(color))
                g.FillPath(br, path);
        }
    }

    private void DrawBrokenHeart(Graphics g, float cx, float cy, float size)
    {
        Color brokenColor = Color.FromArgb(80, 80, 80);
        DrawHeart(g, cx, cy, size, brokenColor);
        using (Pen crack = new Pen(theme.Background, 2f))
        {
            float s = size;
            float top = cy - s * 0.15f;
            g.DrawLine(crack, cx, top - s * 0.15f, cx - s * 0.12f, top + s * 0.15f);
            g.DrawLine(crack, cx - s * 0.12f, top + s * 0.15f, cx + s * 0.1f, top + s * 0.35f);
            g.DrawLine(crack, cx + s * 0.1f, top + s * 0.35f, cx - s * 0.05f, top + s * 0.6f);
        }
    }

    private void DrawLives(Graphics g, int x, int y, int heartSize)
    {
        int spacing = heartSize + 6;
        for (int i = 0; i < maxLives; i++)
        {
            float hx = x + i * spacing + heartSize / 2f;
            float hy = y + heartSize / 2f;
            if (i < currentLives)
                DrawHeart(g, hx, hy, heartSize / 2f, Color.FromArgb(220, 30, 30));
            else
                DrawBrokenHeart(g, hx, hy, heartSize / 2f);
        }
    }

    // Screen 3: Game 
    private void DrawGameScreen(Graphics g)
    {
        if (IsFacialEmotionVisualFresh() && facialEmotionMood == "happy")
        {
            Rectangle full = new Rectangle(0, 0, width, height);
            using (LinearGradientBrush gb = new LinearGradientBrush(full,
                Color.FromArgb(255, 255, 230, 110),
                Color.FromArgb(255, 40, 190, 140),
                LinearGradientMode.Vertical))
                g.FillRectangle(gb, full);
        }
        else if (IsFacialEmotionVisualFresh() && facialEmotionMood == "sad")
        {
            Rectangle full = new Rectangle(0, 0, width, height);
            using (LinearGradientBrush gb = new LinearGradientBrush(full,
                Color.FromArgb(255, 38, 52, 88),
                Color.FromArgb(255, 14, 22, 48),
                LinearGradientMode.Vertical))
                g.FillRectangle(gb, full);
        }
        else
        {
            using (SolidBrush blueBg = new SolidBrush(Color.FromArgb(35, 110, 220)))
                g.FillRectangle(blueBg, 0, 0, width, height);
        }

        Rectangle randomArea = new Rectangle(16, 72, width - 32, height - 120);

        int heartSize = 28;
        int heartsWidth = maxLives * (heartSize + 6);
        DrawLives(g, width - heartsWidth - 20, 16, heartSize);

        if (IsFacialEmotionVisualFresh() && !string.IsNullOrEmpty(facialEmotionModeLabel) &&
            (facialEmotionMood == "happy" || facialEmotionMood == "sad"))
        {
            using (Font bf = new Font("Arial", theme.SmallFontSize + 4, FontStyle.Bold))
            {
                SizeF sz = g.MeasureString(facialEmotionModeLabel, bf);
                int padX = 14;
                int padY = 8;
                int rw = (int)sz.Width + padX * 2;
                int rh = (int)sz.Height + padY * 2;
                int rx = 18;
                int ry = 10;
                Rectangle badge = new Rectangle(rx, ry, rw, rh);
                Color fill = facialEmotionMood == "happy"
                    ? Color.FromArgb(228, 255, 210, 70)
                    : Color.FromArgb(215, 55, 72, 125);
                using (SolidBrush sb = new SolidBrush(fill))
                using (Pen edge = new Pen(Color.FromArgb(180, 255, 255, 255), 1.2f))
                {
                    DrawRoundedRect(g, sb, edge, badge, 10);
                }
                Color tc = facialEmotionMood == "happy"
                    ? Color.FromArgb(255, 115, 45, 0)
                    : Color.FromArgb(255, 235, 240, 255);
                using (SolidBrush tb = new SolidBrush(tc))
                    g.DrawString(facialEmotionModeLabel, bf, tb, rx + padX, ry + padY - 1f);
            }
        }

        if (currentLevel == 1)
        {
            EnsureChildFlagPositions(randomArea);

            List<string> flagsSnapshot = new List<string>(remainingFlags);
            if (flagsSnapshot.Count > 0)
            {
                var oldMode = g.InterpolationMode;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                foreach (string flagName in flagsSnapshot)
                {
                    Image img = GetFlagImageByPath(flagName);
                    if (img == null) continue;
                    Point p;
                    if (!childFlagRandomPositions.TryGetValue(flagName, out p)) continue;
                    g.DrawImage(img, p.X, p.Y, ChildFlagWidth, ChildFlagHeight);
                }
                g.InterpolationMode = oldMode;
            }
        }
        else
        {
            EnsureLevel2LandmarkPositions(randomArea);

            List<string> landmarksSnapshot = new List<string>(remainingLandmarks);
            if (landmarksSnapshot.Count > 0)
            {
                var oldMode = g.InterpolationMode;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                foreach (string lmName in landmarksSnapshot)
                {
                    Image img = GetFlagImageByPath(lmName);
                    if (img == null) continue;
                    Point p;
                    if (!level2LandmarkRandomPositions.TryGetValue(lmName, out p)) continue;
                    g.DrawImage(img, p.X, p.Y, ChildFlagWidth, ChildFlagHeight);
                }
                g.InterpolationMode = oldMode;
            }
        }

        using (Font title = new Font("Arial", theme.TitleFontSize, FontStyle.Bold))
        {
            Color titleCol = Color.White;
            if (IsFacialEmotionVisualFresh() && facialEmotionMood == "happy")
                titleCol = Color.FromArgb(255, 52, 38, 18);
            else if (IsFacialEmotionVisualFresh() && facialEmotionMood == "sad")
                titleCol = Color.FromArgb(255, 230, 236, 252);
            using (SolidBrush white = new SolidBrush(titleCol))
            {
                string text = currentLevel == 1
                    ? "Match the country with the correct flag"
                    : "Match the landmark with the correct flag";
                SizeF ts = g.MeasureString(text, title);
                g.DrawString(text, title, white, (width - ts.Width) / 2f, 18);
            }
        }

        if (IsFacialEmotionVisualFresh() && !string.IsNullOrEmpty(facialEmotionContext))
        {
            using (Font mf = new Font("Arial", theme.SmallFontSize + 2, FontStyle.Italic))
            {
                Color mc;
                if (facialEmotionMood == "happy")
                    mc = Color.FromArgb(245, 65, 40, 8);
                else if (facialEmotionMood == "sad")
                    mc = Color.FromArgb(235, 210, 220, 255);
                else
                    mc = theme.TextSecondary;
                using (SolidBrush mb = new SolidBrush(mc))
                {
                    SizeF ms = g.MeasureString(facialEmotionContext, mf);
                    g.DrawString(facialEmotionContext, mf, mb, (width - ms.Width) / 2f, 18 + theme.TitleFontSize + 8f);
                }
            }
        }

        int planePx, planePy;
        float planeAngle;
        bool hasPlane = TryGetPlaneScreenPoint(out planePx, out planePy, out planeAngle);
        // Country name is drawn only on the plane overlay.

        // Hover loading ring around plane (fills over 3 seconds).
        if (hasPlane && !string.IsNullOrEmpty(hoveredFlagForPlane) && hoveredFlagSince != DateTime.MinValue)
        {
            double elapsedMs = (DateTime.Now - hoveredFlagSince).TotalMilliseconds;
            float progress = (float)Math.Max(0.0, Math.Min(1.0, elapsedMs / PlaneHoverSelectMs));
            int px = planePx;
            int py = planePy;
            int ringSize = 86;
            Rectangle ringRect = new Rectangle(px - ringSize / 2, py - ringSize / 2, ringSize, ringSize);

            using (Pen bgRing = new Pen(Color.FromArgb(120, 255, 255, 255), 5f))
                g.DrawArc(bgRing, ringRect, -90, 360);

            using (Pen progressRing = new Pen(Color.FromArgb(255, 255, 215, 0), 6f))
                g.DrawArc(progressRing, ringRect, -90, 360f * progress);
        }

        DrawGazeSoftMarker(g);
        DrawCountrySmartHint(g);

        if (cursorList.Count > 0)
        {
            lock (cursorSync)
            {
                foreach (TuioCursor tcur in cursorList.Values)
                {
                    using (SolidBrush cb = new SolidBrush(Color.White))
                        g.FillEllipse(cb, tcur.getScreenX(width) - 8, tcur.getScreenY(height) - 8, 16, 16);
                }
            }
        }

        if (gameOver)
        {
            using (SolidBrush overlay = new SolidBrush(Color.FromArgb(185, 0, 0, 0)))
                g.FillRectangle(overlay, 0, 0, width, height);

            using (Font overTitle = new Font("Arial", theme.TitleFontSize + 12, FontStyle.Bold))
            using (Font overText = new Font("Arial", theme.BodyFontSize + 4, FontStyle.Regular))
            using (SolidBrush titleBr = new SolidBrush(Color.FromArgb(255, 245, 90, 90)))
            using (SolidBrush textBr = new SolidBrush(Color.White))
            {
                string title = "GAME OVER";
                string text = "You lost all lives";
                SizeF t1 = g.MeasureString(title, overTitle);
                SizeF t2 = g.MeasureString(text, overText);
                g.DrawString(title, overTitle, titleBr, (width - t1.Width) / 2f, (height / 2f) - 60);
                g.DrawString(text, overText, textBr, (width - t2.Width) / 2f, (height / 2f) + 10);
            }
        }
    }

    private void DrawSideBar(Graphics g, int x, int barId, bool selected, string label, Image flagImg)
    {
        using (SolidBrush barBg = new SolidBrush(selected ? theme.BarSelectedBackground : theme.BarBackground))
        using (Pen borderPen = new Pen(selected ? theme.SelectedBorder : theme.BorderColor, selected ? 3 : 1))
        using (Pen arrowPen = new Pen(Color.White, 3))
        using (Font barFont = new Font("Arial", theme.GameItemFontSize, FontStyle.Bold))
        using (Font selFont = new Font("Arial", theme.SmallFontSize))
        using (SolidBrush textBr = new SolidBrush(Color.White))
        {
            g.FillRectangle(barBg, x, 0, barWidth, height);
            if (barId == 0) g.DrawLine(borderPen, x + barWidth - 1, 0, x + barWidth - 1, height);
            else g.DrawLine(borderPen, x, 0, x, height);

            if (selected)
                g.DrawString("Selected", selFont, textBr, x + 4, 4);

            if (flagImg != null)
            {
                int fSize = Math.Min(barWidth - 40, height / 3);
                int fX = x + (barWidth - fSize) / 2;
                int fY = (height - fSize) / 2;
                var old = g.InterpolationMode;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(flagImg, fX, fY, fSize, fSize);
                g.InterpolationMode = old;
            }
            else
            {
                SizeF ls = g.MeasureString(label, barFont);
                g.DrawString(label, barFont, textBr, x + (barWidth - ls.Width) / 2f, (height - ls.Height) / 2f);
            }

            // Arrows
            int ay = height / 2, aSize = 12;
            g.DrawLine(arrowPen, x + 8, ay, x + 8 + aSize, ay - aSize);
            g.DrawLine(arrowPen, x + 8, ay, x + 8 + aSize, ay + aSize);
            int rx = x + barWidth - 8;
            g.DrawLine(arrowPen, rx, ay, rx - aSize, ay - aSize);
            g.DrawLine(arrowPen, rx, ay, rx - aSize, ay + aSize);
        }
    }

    private void DrawTuioMouseOverlay(Graphics g)
    {
        if (screenState == ScreenSignup)
            return;

        int mx, my;
        float angleRad;
        if (!TryGetPlaneScreenPoint(out mx, out my, out angleRad)) return;

        if (IsVirtualKeyboardScreen() || (screenState == ScreenAdminUsers && keyboardPointerIsLaser))
        {
            if (signupKeyHoldTargetIndex >= 0)
            {
                float holdProgress = GetSignupKeyHoldProgress();
                if (holdProgress > 0f)
                    DrawSignupKeyHoldRing(g, mx, my, holdProgress);
            }
            else if (screenState == ScreenAdminUsers && !string.IsNullOrEmpty(adminPointerHoldTarget))
            {
                float holdProgress = GetAdminHoldProgress();
                if (holdProgress > 0f)
                    DrawSignupKeyHoldRing(g, mx, my, holdProgress);
            }
            DrawLaserPointerOverlay(g, mx, my);
            return;
        }

        string planeImagePath = TuioMouseImagePath;
        if (screenState == ScreenAdminUsers)
            planeImagePath = "adult_pointer.png";
        else if (screenState == ScreenGame && currentLevel == 2 && !string.IsNullOrEmpty(level2PlaneFlagImagePath))
            planeImagePath = level2PlaneFlagImagePath;

        // Reuse the flag loader because level-2 cursor images live under bin/Debug/net48/Flages.
        Image mouseImage = GetFlagImageByPath(planeImagePath);

        if (mouseImage != null)
        {
            int size = 82;
            var old = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            var oldTransform = g.Transform;
            g.TranslateTransform(mx, my);
            g.RotateTransform((float)(angleRad * 180.0 / Math.PI));
            g.DrawImage(mouseImage, -size / 2, -size / 2, size, size);
            g.Transform = oldTransform;

            if (IsVirtualKeyboardScreen() && signupKeyHoldTargetIndex >= 0)
            {
                float holdProgress = GetSignupKeyHoldProgress();
                if (holdProgress > 0f)
                    DrawSignupKeyHoldRing(g, mx, my, holdProgress);
            }
            else if (screenState == ScreenAdminUsers && !string.IsNullOrEmpty(adminPointerHoldTarget))
            {
                float holdProgress = GetAdminHoldProgress();
                if (holdProgress > 0f)
                    DrawSignupKeyHoldRing(g, mx, my, holdProgress);
            }

            if (screenState == ScreenGame && currentLevel == 1 && remainingCountries.Count > 0)
            {
                string country = remainingCountries[countryNameIndex];
                using (Font onPlane = new Font("Arial", Math.Max(10f, theme.SmallFontSize + 2f), FontStyle.Bold))
                using (SolidBrush textBr = new SolidBrush(Color.FromArgb(255, 255, 245, 120)))
                {
                    SizeF ts = g.MeasureString(country, onPlane);
                    g.DrawString(country, onPlane, textBr, mx - ts.Width / 2f, my - ts.Height / 2f);
                }
            }

            g.InterpolationMode = old;
            return;
        }

        if (!tuioMouseImageMissingLogged)
        {
            tuioMouseImageMissingLogged = true;
            Console.WriteLine("[TuioDemo] Could not load plane image: " + planeImagePath);
        }

        using (SolidBrush fallback = new SolidBrush(theme.Accent))
            g.FillEllipse(fallback, mx - 12, my - 12, 24, 24);
    }

    public static void Main(String[] argv)
    {
        int port = 0;
        switch (argv.Length)
        {
            case 1:
                port = int.Parse(argv[0], null);
                if (port == 0) goto default;
                break;
            case 0:
                string envPort = System.Environment.GetEnvironmentVariable("TUIO_PORT");
                port = (envPort != null && int.TryParse(envPort, out int ep) && ep > 0) ? ep : 3333;
                break;
            default:
                Console.WriteLine("usage: TuioDemo [port]");
                System.Environment.Exit(0);
                break;
        }
        Console.WriteLine("[TuioDemo] Listening on UDP port " + port + "  (set TUIO_PORT to change; Python bridge must use same port)");
        TuioDemo app = new TuioDemo(port);
        Application.Run(app);
    }
}