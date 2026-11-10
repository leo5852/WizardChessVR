using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using WizardChess.Core;
using Debug = UnityEngine.Debug;

namespace WizardChess.AI
{
    /// <summary>
    /// Stockfish 엔진과 UCI 프로토콜로 통신하는 AI.
    /// Supported platforms:
    ///  - Quest standalone (Android): Assets/Plugins/Android/libs/arm64-v8a/libstockfish.so
    ///    is packaged as a native lib, extracted at install time, and executed from nativeLibraryDir.
    ///  - Windows editor: Engines/Windows/stockfish.exe (outside Assets so it never ships in the APK).
    /// Other platforms (e.g. macOS editor) return null, and BoardView falls back to RandomAI.
    /// </summary>
    public class StockfishAI : ChessAIBase
    {
        [Tooltip("Windows 에디터용 실행 파일 경로 (프로젝트 루트 기준)")]
#pragma warning disable 0414   // Only read in the Windows editor; unused on other platforms
        [SerializeField] string windowsEnginePath = "Engines/Windows/stockfish.exe";
#pragma warning restore 0414

        [Tooltip("탐색 깊이. 낮을수록 약하고 빠르다. (난이도 조절 1)")]
        [Range(1, 30)] [SerializeField] int searchDepth = 8;

        [Tooltip("Stockfish Skill Level. 0이 가장 약하고 20이 최강. (난이도 조절 2)")]
        [Range(0, 20)] [SerializeField] int skillLevel = 5;

        // File name must start with "lib" and end with ".so" so the package manager extracts it.
        const string AndroidEngineFileName = "libstockfish.so";

        Process process;
        readonly object sync = new object();

        public override Task<Move?> ChooseMoveAsync(Board board)
        {
            // Read Unity / Java APIs on the main thread; engine I/O runs on a background thread so the game never stalls
            string fullPath;
            if (!TryGetEnginePath(out fullPath))
            {
                Debug.LogWarning("StockfishAI는 이 플랫폼에서 지원되지 않습니다. 랜덤 AI로 대신 둡니다.");
                return Task.FromResult<Move?>(null);
            }

            string fen = board.ToFen();
            int depth = searchDepth, skill = skillLevel;

            return Task.Run(() =>
            {
                try
                {
                    string bestLine;
                    lock (sync)
                    {
                        EnsureStarted(fullPath);
                        Send("setoption name Skill Level value " + skill);
                        Send("position fen " + fen);
                        Send("go depth " + depth);
                        bestLine = ReadUntil("bestmove");   // 예: "bestmove e2e4 ponder e7e5"
                    }

                    string[] parts = bestLine.Split(' ');
                    if (parts.Length < 2 || parts[1] == "(none)") return (Move?)null;

                    Move move;
                    if (board.TryParseUci(parts[1], out move)) return move;
                    Debug.LogWarning("Stockfish가 알 수 없는 수를 반환: " + parts[1]);
                    return (Move?)null;
                }
                catch (Exception e)
                {
                    Debug.LogError("Stockfish 오류: " + e.Message);
                    return (Move?)null;
                }
            });
        }

        string cachedEnginePath;

        /// <summary>Resolves the engine executable path for the current platform. Must be called on the main thread.</summary>
        bool TryGetEnginePath(out string path)
        {
            if (cachedEnginePath == null) cachedEnginePath = ResolveEnginePath();
            path = cachedEnginePath;
            return path != null;
        }

        /// <summary>Returns null when the current platform has no engine binary.</summary>
        string ResolveEnginePath()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var appInfo = activity.Call<AndroidJavaObject>("getApplicationInfo"))
                {
                    return Path.Combine(appInfo.Get<string>("nativeLibraryDir"), AndroidEngineFileName);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("Stockfish 경로 조회 실패: " + e.Message);
                return null;
            }
#elif UNITY_EDITOR_WIN
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", windowsEnginePath));
#else
            return null;
#endif
        }

        void EnsureStarted(string path)
        {
            if (process != null && !process.HasExited) return;
            if (!File.Exists(path)) throw new FileNotFoundException("Stockfish 실행 파일을 찾을 수 없습니다: " + path
                + " (Android: useLegacyPackaging이 꺼져 있으면 네이티브 라이브러리가 추출되지 않음)");

            process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            process.Start();

            Send("uci");
            ReadUntil("uciok");
            Send("ucinewgame");
            Send("isready");
            ReadUntil("readyok");
            Debug.Log("Stockfish 시작됨");
        }

        void Send(string command)
        {
            process.StandardInput.WriteLine(command);
            process.StandardInput.Flush();
        }

        string ReadUntil(string prefix)
        {
            while (true)
            {
                string line = process.StandardOutput.ReadLine();
                if (line == null) throw new IOException("Stockfish 프로세스가 응답 없이 종료되었습니다.");
                if (line.StartsWith(prefix)) return line;
            }
        }

        void OnDestroy()
        {
            try
            {
                if (process != null && !process.HasExited)
                {
                    Send("quit");
                    if (!process.WaitForExit(500)) process.Kill();
                }
            }
            catch (Exception) { /* 종료 중 오류는 무시 */ }
        }
    }
}
