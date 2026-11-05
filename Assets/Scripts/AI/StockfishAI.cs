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
    /// 현재는 Windows(에디터, Quest Link로 PC 실행)에서만 동작한다.
    /// Quest 단독 실행(안드로이드)은 나중에 별도 작업이 필요하다.
    ///
    /// 준비: stockfish.exe를 Assets/StreamingAssets/Stockfish/stockfish.exe 경로에 넣는다.
    /// </summary>
    public class StockfishAI : ChessAIBase
    {
        [Tooltip("StreamingAssets 폴더 기준 실행 파일 경로")]
        [SerializeField] string enginePath = "Stockfish/stockfish.exe";

        [Tooltip("탐색 깊이. 낮을수록 약하고 빠르다. (난이도 조절 1)")]
        [Range(1, 30)] [SerializeField] int searchDepth = 8;

        [Tooltip("Stockfish Skill Level. 0이 가장 약하고 20이 최강. (난이도 조절 2)")]
        [Range(0, 20)] [SerializeField] int skillLevel = 5;

        Process process;
        readonly object sync = new object();

        public override Task<Move?> ChooseMoveAsync(Board board)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            // Unity API는 메인 스레드에서만 읽고, 엔진 통신은 백그라운드 스레드에서 한다 (게임이 멈추지 않도록)
            string fullPath = Path.Combine(Application.streamingAssetsPath, enginePath);
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
#else
            Debug.LogWarning("StockfishAI는 아직 Windows에서만 동작합니다. 랜덤 AI로 대신 둡니다.");
            return Task.FromResult<Move?>(null);
#endif
        }

        void EnsureStarted(string path)
        {
            if (process != null && !process.HasExited) return;
            if (!File.Exists(path)) throw new FileNotFoundException("Stockfish 실행 파일을 찾을 수 없습니다: " + path);

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
