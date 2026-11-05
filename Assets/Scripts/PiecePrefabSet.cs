using UnityEngine;
using WizardChess.Core;

namespace WizardChess
{
    /// <summary>한 진영(백 또는 흑)의 기물 프리팹 6종을 인스펙터에서 묶어 지정하기 위한 클래스.</summary>
    [System.Serializable]
    public class PiecePrefabSet
    {
        public GameObject pawn;
        public GameObject knight;
        public GameObject bishop;
        public GameObject rook;
        public GameObject queen;
        public GameObject king;

        public GameObject Get(PieceType type)
        {
            switch (type)
            {
                case PieceType.Pawn: return pawn;
                case PieceType.Knight: return knight;
                case PieceType.Bishop: return bishop;
                case PieceType.Rook: return rook;
                case PieceType.Queen: return queen;
                case PieceType.King: return king;
                default: return null;
            }
        }
    }
}