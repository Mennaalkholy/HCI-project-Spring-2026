using System;
using System.IO;
using System.Windows.Forms;
using System.Windows.Media;

/// <summary>
/// Plays MP3s from <c>sounds</c> or <c>sound</c> next to the executable (e.g. bin\Debug\net48\sound).
/// </summary>
public sealed class GameSoundManager : IDisposable
{
    private readonly Control _invokeTarget;
    private readonly string _soundsDir;
    private readonly string _soundDirAlt;
    private readonly MediaPlayer _heartPlayer = new MediaPlayer();
    private readonly MediaPlayer _gameOverPlayer = new MediaPlayer();
    private readonly MediaPlayer _signupPlayer = new MediaPlayer();
    private readonly MediaPlayer _transitionPlayer = new MediaPlayer();
    private System.Windows.Forms.Timer _gameOverDelayTimer;
    private bool _pendingGameOver;
    private string _pendingAgeGroup = "adult";

    public GameSoundManager(Control invokeTarget)
    {
        _invokeTarget = invokeTarget ?? throw new ArgumentNullException(nameof(invokeTarget));
        string baseDir = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath) ?? ".";
        _soundsDir = Path.Combine(baseDir, "sounds");
        _soundDirAlt = Path.Combine(baseDir, "sound");
    }

    public void Dispose()
    {
        if (_gameOverDelayTimer != null)
        {
            _gameOverDelayTimer.Stop();
            _gameOverDelayTimer.Dispose();
            _gameOverDelayTimer = null;
        }
        _heartPlayer.Close();
        _gameOverPlayer.Close();
        _signupPlayer.Close();
        _transitionPlayer.Close();
    }

    /// <summary>Plays after a successful sign-up submit, before the greeting / game.</summary>
    public void PlaySignupSuccess()
    {
        void Run()
        {
            string path = FindSoundFile(new[] { "signedupp", "signed_up", "signup", "signup_success" });
            if (!string.IsNullOrEmpty(path))
                PlayFile(_signupPlayer, path);
        }

        if (_invokeTarget.InvokeRequired)
            _invokeTarget.BeginInvoke(new Action(Run));
        else
            Run();
    }

    /// <summary>
    /// Loops while the level 1 → 2 transition countdown runs. Looks for transition.mp3 (etc.) in <c>sound</c> or <c>sounds</c>.
    /// </summary>
    public void PlayLevelTransition()
    {
        void Run()
        {
            string path = FindSoundFile(new[]
            {
                "transition",
                "level_transition",
                "level2_transition",
                "between_levels",
                "countdown",
                "level_countdown",
            });
            if (string.IsNullOrEmpty(path) && Directory.Exists(_soundDirAlt))
            {
                try
                {
                    string[] mp3 = Directory.GetFiles(_soundDirAlt, "*.mp3", SearchOption.TopDirectoryOnly);
                    string[] wav = Directory.GetFiles(_soundDirAlt, "*.wav", SearchOption.TopDirectoryOnly);
                    if (mp3.Length == 1)
                        path = Path.GetFullPath(mp3[0]);
                    else if (mp3.Length == 0 && wav.Length == 1)
                        path = Path.GetFullPath(wav[0]);
                }
                catch
                {
                    // ignore
                }
            }
            if (string.IsNullOrEmpty(path))
                return;
            try
            {
                _transitionPlayer.MediaEnded -= TransitionPlayer_MediaEnded;
                _transitionPlayer.Stop();
                _transitionPlayer.Open(new Uri(path, UriKind.Absolute));
                _transitionPlayer.MediaEnded += TransitionPlayer_MediaEnded;
                _transitionPlayer.Play();
            }
            catch
            {
                // Missing codec or bad path
            }
        }

        if (_invokeTarget.InvokeRequired)
            _invokeTarget.BeginInvoke(new Action(Run));
        else
            Run();
    }

    private void TransitionPlayer_MediaEnded(object sender, EventArgs e)
    {
        try
        {
            _transitionPlayer.MediaEnded -= TransitionPlayer_MediaEnded;
            _transitionPlayer.Position = TimeSpan.Zero;
            _transitionPlayer.Play();
        }
        catch
        {
            // ignore
        }
    }

    public void StopLevelTransition()
    {
        void Run()
        {
            try
            {
                _transitionPlayer.MediaEnded -= TransitionPlayer_MediaEnded;
                _transitionPlayer.Stop();
            }
            catch
            {
                // ignore
            }
        }

        if (_invokeTarget.InvokeRequired)
            _invokeTarget.BeginInvoke(new Action(Run));
        else
            Run();
    }

    /// <summary>After a wrong match: play heart break; if that was the last life, queue age-specific game over.</summary>
    public void OnLifeLost(bool isGameOver, string ageGroup)
    {
        void Run()
        {
            string heartPath = FindSoundFile(new[] { "heart_break", "heartbreak", "heart-break" });
            if (!string.IsNullOrEmpty(heartPath))
                PlayFile(_heartPlayer, heartPath);

            if (isGameOver)
            {
                _pendingGameOver = true;
                _pendingAgeGroup = string.IsNullOrEmpty(ageGroup) ? "adult" : ageGroup;
                if (_gameOverDelayTimer == null)
                {
                    _gameOverDelayTimer = new System.Windows.Forms.Timer();
                    _gameOverDelayTimer.Interval = 450;
                    _gameOverDelayTimer.Tick += GameOverDelayTimer_Tick;
                }
                _gameOverDelayTimer.Stop();
                _gameOverDelayTimer.Start();
            }
        }

        if (_invokeTarget.InvokeRequired)
            _invokeTarget.BeginInvoke(new Action(Run));
        else
            Run();
    }

    private void GameOverDelayTimer_Tick(object sender, EventArgs e)
    {
        _gameOverDelayTimer.Stop();
        if (!_pendingGameOver) return;
        _pendingGameOver = false;
        PlayGameOverInternal(_pendingAgeGroup);
    }

    private void PlayGameOverInternal(string ageGroup)
    {
        string[] names;
        switch ((ageGroup ?? "adult").ToLowerInvariant())
        {
            case "child":
                names = new[] { "gameover_child", "game_over_child", "child_gameover" };
                break;
            case "teen":
                names = new[] { "gameover_teen", "game_over_teen", "teen_gameover" };
                break;
            default:
                names = new[] { "gameover_adult", "game_over_adult", "adult_gameover", "gameover" };
                break;
        }
        string path = FindSoundFile(names);
        if (!string.IsNullOrEmpty(path))
            PlayFile(_gameOverPlayer, path);
    }

    private static void PlayFile(MediaPlayer player, string fullPath)
    {
        try
        {
            player.Stop();
            player.Open(new Uri(fullPath, UriKind.Absolute));
            player.Play();
        }
        catch
        {
            // Missing codec or bad path — ignore so gameplay continues
        }
    }

    private string FindSoundFile(string[] baseNamesWithoutExtension)
    {
        foreach (string dir in new[] { _soundDirAlt, _soundsDir })
        {
            string found = FindSoundFileInDirectory(dir, baseNamesWithoutExtension);
            if (!string.IsNullOrEmpty(found))
                return found;
        }
        return null;
    }

    private static string FindSoundFileInDirectory(string dir, string[] baseNamesWithoutExtension)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
        string[] files;
        try
        {
            files = Directory.GetFiles(dir, "*.*");
        }
        catch
        {
            return null;
        }

        foreach (string baseName in baseNamesWithoutExtension)
        {
            foreach (string ext in new[] { ".mp3", ".MP3", ".wav", ".WAV" })
            {
                string exact = Path.Combine(dir, baseName + ext);
                if (File.Exists(exact)) return Path.GetFullPath(exact);
            }
        }

        foreach (string f in files)
        {
            string name = Path.GetFileNameWithoutExtension(f);
            string lower = name.ToLowerInvariant();
            foreach (string bn in baseNamesWithoutExtension)
            {
                if (lower == bn.ToLowerInvariant())
                    return Path.GetFullPath(f);
            }
        }

        return null;
    }
}
